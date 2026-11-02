//! Windows side effects for the shared transaction. Snapshots contain raw registry
//! value kinds/bytes and shortcut bytes, and are persisted by the core before apply.
use super::install_transaction::{self as transaction, Result, Runtime};
use serde::{Deserialize, Serialize};
use std::os::windows::process::CommandExt;
use std::{
    fs,
    path::{Path, PathBuf},
    process::{Command, Stdio},
    time::{Duration, Instant},
};
use winreg::{enums::*, RegKey, RegValue};

const ARP: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FrameScope";
const RUN: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";

#[derive(Serialize, Deserialize)]
struct Raw {
    name: String,
    kind: u32,
    bytes: Vec<u8>,
}
#[derive(Serialize, Deserialize)]
struct Key {
    path: String,
    exists: bool,
    values: Vec<Raw>,
    children: Vec<Key>,
}
#[derive(Serialize, Deserialize)]
struct Link {
    path: PathBuf,
    temporary: PathBuf,
    bytes: Option<Vec<u8>>,
}
#[derive(Serialize, Deserialize)]
struct Snapshot {
    keys: Vec<Key>,
    run: Option<Raw>,
    run_exists: bool,
    links: Vec<Link>,
    #[serde(default)]
    logon_task: Option<framescope_update::autostart::State>,
}

pub struct WindowsRuntime {
    pub startup_build:Option<framescope_update::BuildInfo>,
    pub keep_previous:bool,
    pub preserve_settings:bool,
    pub started_new:bool,
    pub target: PathBuf,
    pub uninstall: bool,
    pub desktop: bool,
    pub autostart: bool,
    pub links: Vec<PathBuf>,
    pub legacy: Vec<String>,
    link_temps: Vec<PathBuf>,
}

fn io<T>(value: std::io::Result<T>) -> Result<T> {
    value.map_err(|e| e.to_string())
}
fn missing(error: &std::io::Error) -> bool {
    error.raw_os_error() == Some(2)
}
fn raw(name: String, value: RegValue) -> Raw {
    Raw {
        name,
        kind: value.vtype as u32,
        bytes: value.bytes,
    }
}
fn value(raw: &Raw) -> Result<RegValue> {
    let kind = match raw.kind {
        0 => REG_NONE,
        1 => REG_SZ,
        2 => REG_EXPAND_SZ,
        3 => REG_BINARY,
        4 => REG_DWORD,
        5 => REG_DWORD_BIG_ENDIAN,
        6 => REG_LINK,
        7 => REG_MULTI_SZ,
        8 => REG_RESOURCE_LIST,
        9 => REG_FULL_RESOURCE_DESCRIPTOR,
        10 => REG_RESOURCE_REQUIREMENTS_LIST,
        11 => REG_QWORD,
        _ => return Err("Unsupported registry value type".into()),
    };
    Ok(RegValue {
        bytes: raw.bytes.clone(),
        vtype: kind,
    })
}
fn capture(root: &RegKey, path: &str) -> Result<Key> {
    let key = match root.open_subkey_with_flags(path, KEY_READ) {
        Ok(key) => key,
        Err(error) if missing(&error) => {
            return Ok(Key {
                path: path.into(),
                exists: false,
                values: vec![],
                children: vec![],
            })
        }
        Err(error) => return Err(error.to_string()),
    };
    let mut values = Vec::new();
    for entry in key.enum_values() {
        let (name, v) = io(entry)?;
        values.push(raw(name, v));
    }
    let mut children = Vec::new();
    for entry in key.enum_keys() {
        let name = io(entry)?;
        children.push(capture(root, &format!("{path}\\{name}"))?);
    }
    Ok(Key {
        path: path.into(),
        exists: true,
        values,
        children,
    })
}
fn restore_key(root: &RegKey, key: &Key) -> Result<()> {
    match root.delete_subkey_all(&key.path) {
        Ok(()) => (),
        Err(e) if missing(&e) => (),
        Err(e) => return Err(e.to_string()),
    }
    if key.exists {
        let (destination, _) = io(root.create_subkey(&key.path))?;
        for raw in &key.values {
            io(destination.set_raw_value(&raw.name, &value(raw)?))?;
        }
        for child in &key.children {
            restore_key(root, child)?;
        }
    }
    Ok(())
}
fn registry_text(raw: &Raw) -> Result<String> {
    if !matches!(raw.kind, 1 | 2) || raw.bytes.len() % 2 != 0 {
        return Err("Registry identity is not a string".into());
    }
    String::from_utf16(
        &raw.bytes
            .chunks_exact(2)
            .map(|b| u16::from_le_bytes([b[0], b[1]]))
            .take_while(|c| *c != 0)
            .collect::<Vec<_>>(),
    )
    .map_err(|e| e.to_string())
}
fn validate_tree(key: &Key) -> Result<()> {
    if !key.exists && (!key.values.is_empty() || !key.children.is_empty()) {
        return Err("Absent registry key contains recovery children".into());
    }
    for raw in &key.values {
        value(raw)?;
    }
    let mut names = std::collections::BTreeSet::new();
    for child in &key.children {
        let name = child
            .path
            .strip_prefix(&format!("{}\\", key.path))
            .ok_or("Registry child escapes parent")?;
        if name.is_empty()
            || name.contains(['\\', '/'])
            || name == "."
            || name == ".."
            || !names.insert(name.to_lowercase())
        {
            return Err("Invalid/duplicate registry child path".into());
        }
        validate_tree(child)?;
    }
    Ok(())
}
fn validate_snapshot(snapshot: &Snapshot, target: &Path, links: &[PathBuf]) -> Result<()> {
    if snapshot.logon_task.as_ref().is_some_and(|task| task.enabled && !task.exists) {
        return Err("Invalid logon task recovery state".into());
    }
    let mut keys = std::collections::BTreeSet::new();
    for key in &snapshot.keys {
        if !keys.insert(key.path.to_lowercase()) {
            return Err("Duplicate registry recovery key".into());
        }
        if key.path != ARP {
            let name = [
                r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\",
                r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\",
            ]
            .iter()
            .find_map(|prefix| key.path.strip_prefix(prefix))
            .ok_or("Registry recovery escapes product uninstall views")?;
            if name.is_empty() || name.contains(['\\', '/']) || name == "." || name == ".." {
                return Err("Legacy registry recovery is not a direct uninstall child".into());
            }
            let display = key
                .values
                .iter()
                .find(|r| r.name == "DisplayName")
                .ok_or("Missing legacy product identity")?;
            let location = key
                .values
                .iter()
                .find(|r| r.name == "InstallLocation")
                .ok_or("Missing legacy root identity")?;
            if registry_text(display)? != "FrameScope"
                || transaction::normalize(Path::new(&registry_text(location)?))?
                    != transaction::normalize(target)?
            {
                return Err("Legacy recovery identity is outside target".into());
            }
        }
        validate_tree(key)?;
    }
    if !keys.contains(&ARP.to_lowercase()) {
        return Err("Snapshot lacks product ARP identity".into());
    }
    if let Some(run) = &snapshot.run {
        if run.name != "FrameScope" {
            return Err("Autostart recovery value is outside product".into());
        }
        value(run)?;
    }
    let mut found = std::collections::BTreeSet::new();
    for link in &snapshot.links {
        if !links.contains(&link.path) || !found.insert(link.path.clone()) {
            return Err("Shortcut recovery escapes explicit KnownFolder plan".into());
        }
        transaction::assert_path(&link.path)?;
        transaction::assert_path(&link.temporary)?;
        let name = link
            .temporary
            .file_name()
            .ok_or("Shortcut temporary name missing")?
            .to_string_lossy();
        if link.temporary.parent() != link.path.parent()
            || !name
                .strip_prefix(".FrameScope-transaction-")
                .and_then(|n| n.strip_suffix(".lnk"))
                .is_some_and(|n| {
                    !n.is_empty() && n.bytes().all(|b| b.is_ascii_digit() || b == b'-')
                })
        {
            return Err("Shortcut temporary recovery path is outside product plan".into());
        }
    }
    if found.len() != links.len() {
        return Err("Incomplete shortcut recovery snapshot".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn first_1x_upgrade_preserves_existing_startup_and_shortcut_policy() {
        let root=std::env::temp_dir().join(format!("framescope-startup-migration-{}-{}",std::process::id(),std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        fs::create_dir(&root).unwrap();
        assert!(!WindowsRuntime::new(&root,false,true,true).unwrap().preserve_settings);
        fs::write(root.join("FrameScope.exe"),b"legacy 1.x program fixture").unwrap();
        assert!(!root.join("FrameScopeBuildInfo.json").exists());
        assert!(WindowsRuntime::new(&root,false,false,false).unwrap().preserve_settings);
        assert!(!WindowsRuntime::new(&root,true,false,false).unwrap().preserve_settings);
    }
    fn snapshot() -> Snapshot {
        Snapshot {
            keys: vec![Key {
                path: ARP.into(),
                exists: true,
                values: vec![Raw {
                    name: "DisplayName".into(),
                    kind: REG_SZ as u32,
                    bytes: vec![70, 0, 0, 0],
                }],
                children: vec![],
            }],
            run: None,
            run_exists: true,
            links: vec![],
            logon_task: None,
        }
    }
    #[test]
    fn registry_snapshot_types_and_bytes_round_trip_without_registry_access() {
        for kind in 0..=11 {
            let raw = Raw {
                name: "value".into(),
                kind,
                bytes: vec![0, 7, 255, 8],
            };
            let v = value(&raw).unwrap();
            assert_eq!(v.bytes, raw.bytes);
            assert_eq!(v.vtype as u32, kind);
        }
    }
    #[test]
    fn snapshot_escape_is_rejected_before_any_registry_mutation() {
        let target = std::env::temp_dir().join("framescope-snapshot-validation");
        let mut state = snapshot();
        state.keys[0].children.push(Key {
            path: r"SOFTWARE\ExternalProduct".into(),
            exists: true,
            values: vec![],
            children: vec![],
        });
        assert!(validate_snapshot(&state, &target, &[]).is_err());
        let mut state = snapshot();
        state.keys[0].path = r"SOFTWARE\ExternalProduct".into();
        assert!(validate_snapshot(&state, &target, &[]).is_err());
        let mut state = snapshot();
        state.keys[0].values[0].kind = 100;
        assert!(validate_snapshot(&state, &target, &[]).is_err());
        validate_snapshot(&snapshot(), &target, &[]).unwrap();
    }
}
fn legacy_keys(target: &Path) -> Result<Vec<String>> {
    let root = RegKey::predef(HKEY_LOCAL_MACHINE);
    let mut keys = Vec::new();
    for view in [
        r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ] {
        let parent = match root.open_subkey_with_flags(view, KEY_READ) {
            Ok(k) => k,
            Err(e) if missing(&e) => continue,
            Err(e) => return Err(e.to_string()),
        };
        for entry in parent.enum_keys() {
            let name = io(entry)?;
            let path = format!("{view}\\{name}");
            if path == ARP {
                continue;
            }
            let key = io(parent.open_subkey_with_flags(&name, KEY_READ))?;
            let display = key
                .get_value::<String, _>("DisplayName")
                .unwrap_or_default();
            let location = key
                .get_value::<String, _>("InstallLocation")
                .unwrap_or_default();
            if display == "FrameScope"
                && Path::new(&location).is_absolute()
                && target.exists()
                && fs::canonicalize(target)
                    .is_ok_and(|actual| fs::canonicalize(&location).is_ok_and(|loc| loc == actual))
            {
                keys.push(path);
            }
        }
    }
    Ok(keys)
}
impl WindowsRuntime {
    pub fn new(target: &Path, uninstall: bool, desktop: bool, autostart: bool) -> Result<Self> {
        let mut links = Vec::new();
        for folder in ["StartMenu", "Desktop"] {
            let base = crate::installer::get_special_folder_path(folder)
                .ok_or("Cannot locate shortcut KnownFolder")?;
            let path = PathBuf::from(base).join("FrameScope.lnk");
            transaction::assert_path(&path)?;
            links.push(path);
        }
        let id = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map_err(|e| e.to_string())?
            .as_nanos();
        let link_temps = links
            .iter()
            .map(|link| {
                link.with_file_name(format!(
                    ".FrameScope-transaction-{}-{id}.lnk",
                    std::process::id()
                ))
            })
            .collect();
        Ok(Self {
            startup_build:None,keep_previous:false,
            preserve_settings:!uninstall && target.join("FrameScope.exe").is_file(),started_new:false,
            target: target.into(),
            uninstall,
            desktop,
            autostart,
            links,
            link_temps,
            legacy: legacy_keys(target)?,
        })
    }
}

/// Child execution never falls back to Kill, including a helper timeout.
fn helper(helper: &Path, args: &[&std::ffi::OsStr], seconds: u64) -> Result<String> {
    let nonce = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_err(|e| e.to_string())?
        .as_nanos();
    let output = helper
        .parent()
        .ok_or("Missing helper parent")?
        .join(format!("shutdown-output-{nonce}.log"));
    let file = io(fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&output))?;
    let error_output = output.with_extension("stderr.log");
    let error_file = io(fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&error_output))?;
    let mut child = Command::new(helper)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::from(file))
        .stderr(Stdio::from(error_file))
        .creation_flags(0x08000000)
        .spawn()
        .map_err(|e| e.to_string())?;
    let deadline = Instant::now() + Duration::from_secs(seconds);
    loop {
        if let Some(status) = io(child.try_wait())? {
            if io(fs::metadata(&output))?.len() > 1024 * 1024
                || io(fs::metadata(&error_output))?.len() > 1024 * 1024
            {
                return Err("Shutdown helper output exceeded limit".into());
            }
            let text = io(fs::read_to_string(&output))?;
            if !status.success() {
                return Err(format!(
                    "Normal shutdown rejected ({}): {text}; {}",
                    status,
                    io(fs::read_to_string(&error_output))?
                ));
            }
            return Ok(text);
        }
        if Instant::now() >= deadline {
            return Err(format!(
                "Normal shutdown helper timed out; target preserved; output {}",
                output.display()
            ));
        }
        std::thread::sleep(Duration::from_millis(50));
    }
}

impl Runtime for WindowsRuntime {
    fn retain_previous(&self)->bool {self.keep_previous}
    fn confirm_startup(&mut self)->Result<()> {
        if let Some(build)=self.startup_build.clone(){crate::update_install::confirm_startup(self,build)}else{Ok(())}
    }
    fn abort_startup(&mut self)->Result<()> {
        if self.started_new {let target=self.target.clone();self.stop(&target,&target.join("FrameScopeUninstaller.exe"))?;self.started_new=false;}Ok(())
    }
    fn allowed_data_roots(&self) -> Vec<PathBuf> {
        let mut roots: Vec<_> = [dirs::data_dir(), dirs::config_dir(), dirs::cache_dir()]
            .into_iter()
            .flatten()
            .map(|r| r.join("com.framescope.ui-preview"))
            .collect();
        if let Some(local) = dirs::data_local_dir() {
            roots.push(local.join("FrameScopeMonitorData"));
        }
        roots
    }
    fn space(&mut self, path: &Path) -> Result<u64> {
        crate::installer::available_space(path)
    }
    fn stop(&mut self, target: &Path, executable: &Path) -> Result<()> {
        if !target.exists() {
            return Ok(());
        }
        if !executable.is_file() {
            return Err("Payload lacks verified normal-stop helper".into());
        }
        let capability = helper(
            executable,
            &[std::ffi::OsStr::new("--shutdown-capability")],
            10,
        )?;
        if capability.trim() != "framescope-installation-shutdown-v1" {
            return Err("Unknown normal-stop helper capability; target untouched".into());
        }
        let plan = helper(
            executable,
            &[std::ffi::OsStr::new("--shutdown-plan"), target.as_os_str()],
            10,
        )?;
        let plan: serde_json::Value =
            serde_json::from_str(plan.trim()).map_err(|e| e.to_string())?;
        if plan["capability"] != "framescope-installation-shutdown-v1" {
            return Err("Shutdown plan capability mismatch".into());
        }
        let budget = plan["maximumBudgetMs"]
            .as_u64()
            .filter(|n| *n > 0 && *n <= 86_400_000)
            .ok_or("Shutdown plan has invalid/excessive budget")?;
        let output = helper(
            executable,
            &[
                std::ffi::OsStr::new("--stop-installation"),
                target.as_os_str(),
            ],
            budget.div_ceil(1000),
        )?;
        let result: serde_json::Value =
            serde_json::from_str(output.trim()).map_err(|e| e.to_string())?;
        if !matches!(
            result["status"].as_str(),
            Some("stopped" | "stopped-with-warning")
        ) {
            return Err("Shutdown helper did not confirm completion".into());
        }
        eprintln!("{output}");
        Ok(())
    }
    fn snapshot(&mut self) -> Result<serde_json::Value> {
        let root = RegKey::predef(HKEY_LOCAL_MACHINE);
        let mut keys = vec![capture(&root, ARP)?];
        for key in &self.legacy {
            keys.push(capture(&root, key)?);
        }
        let user = RegKey::predef(HKEY_CURRENT_USER);
        let (run, run_exists) = match user.open_subkey_with_flags(RUN, KEY_READ) {
            Ok(key) => (
                match key.get_raw_value("FrameScope") {
                    Ok(v) => Some(raw("FrameScope".into(), v)),
                    Err(e) if missing(&e) => None,
                    Err(e) => return Err(e.to_string()),
                },
                true,
            ),
            Err(e) if missing(&e) => (None, false),
            Err(e) => return Err(e.to_string()),
        };
        let mut links = Vec::new();
        for (path, temp) in self.links.iter().zip(&self.link_temps) {
            transaction::assert_path(path)?;
            transaction::assert_path(temp)?;
            if temp.exists() {
                return Err("Shortcut temporary path already exists".into());
            }
            let bytes = if path.exists() {
                Some(io(fs::read(path))?)
            } else {
                None
            };
            links.push(Link {
                path: path.clone(),
                temporary: temp.clone(),
                bytes,
            });
        }
        serde_json::to_value(Snapshot {
            keys,
            run,
            run_exists,
            links,
            logon_task: Some(framescope_update::autostart::state(&self.target.join("FrameScope.exe"))?),
        })
        .map_err(|e| e.to_string())
    }
    fn apply(&mut self) -> Result<()> {
        let root = RegKey::predef(HKEY_LOCAL_MACHINE);
        if self.uninstall {
            match root.delete_subkey_all(ARP) {
                Ok(()) => (),
                Err(e) if missing(&e) => (),
                Err(e) => return Err(e.to_string()),
            }
        } else {
            crate::installer::register_uninstall(&self.target.to_string_lossy())?;
        }
        for path in &self.legacy {
            io(root.delete_subkey_all(path))?;
        }
        if self.preserve_settings {
            let exe = self.target.join("FrameScope.exe");
            if framescope_update::autostart::legacy_enabled(&exe)?
                && !framescope_update::autostart::state(&exe)?.exists {
                framescope_update::autostart::set(&exe, true)?;
            }
            if let Some(build)=&self.startup_build {
                let key=io(root.open_subkey_with_flags(ARP,KEY_SET_VALUE))?;
                io(key.set_value("DisplayVersion",&build.version))?;
            }
            return Ok(());
        }
        for (index, path) in self.links.iter().enumerate() {
            transaction::assert_path(path)?;
            if self.uninstall || (index == 1 && !self.desktop) {
                if path.exists() {
                    io(fs::remove_file(path))?;
                }
            } else {
                let temp = &self.link_temps[index];
                transaction::assert_path(temp)?;
                let placeholder = io(fs::OpenOptions::new()
                    .write(true)
                    .create_new(true)
                    .open(temp))?;
                drop(placeholder);
                let mut link = mslnk::ShellLink::new(self.target.join("FrameScope.exe"))
                    .map_err(|e| e.to_string())?;
                link.set_working_dir(Some(self.target.to_string_lossy().to_string()));
                link.create_lnk(&temp).map_err(|e| e.to_string())?;
                if path.exists() {
                    io(fs::remove_file(path))?;
                }
                io(fs::rename(&temp, path))?;
            }
        }
        let user = RegKey::predef(HKEY_CURRENT_USER);
        framescope_update::autostart::set(&self.target.join("FrameScope.exe"), !self.uninstall && self.autostart)?;
        {
            match user.open_subkey_with_flags(RUN, KEY_SET_VALUE) {
                Ok(key) => match key.delete_value("FrameScope") {
                    Ok(()) => (),
                    Err(e) if missing(&e) => (),
                    Err(e) => return Err(e.to_string()),
                },
                Err(e) if missing(&e) => (),
                Err(e) => return Err(e.to_string()),
            }
        }
        Ok(())
    }
    fn restore(&mut self, snapshot: &serde_json::Value) -> Result<()> {
        let snapshot: Snapshot =
            serde_json::from_value(snapshot.clone()).map_err(|e| e.to_string())?;
        validate_snapshot(&snapshot, &self.target, &self.links)?;
        if let Some(task) = &snapshot.logon_task {
            framescope_update::autostart::restore(&self.target.join("FrameScope.exe"), task)?;
        }
        let root = RegKey::predef(HKEY_LOCAL_MACHINE);
        // Restore only the exact product key and legacy keys captured for this target.
        for key in &snapshot.keys {
            restore_key(&root, key)?;
        }
        let user = RegKey::predef(HKEY_CURRENT_USER);
        if snapshot.run_exists || snapshot.run.is_some() {
            let (key, _) = io(user.create_subkey(RUN))?;
            if let Some(raw) = &snapshot.run {
                io(key.set_raw_value("FrameScope", &value(raw)?))?;
            } else {
                match key.delete_value("FrameScope") {
                    Ok(()) => (),
                    Err(e) if missing(&e) => (),
                    Err(e) => return Err(e.to_string()),
                }
            }
        } else {
            match user.open_subkey_with_flags(RUN, KEY_READ | KEY_WRITE) {
                Ok(key) => {
                    match key.delete_value("FrameScope") {
                        Ok(()) => (),
                        Err(e) if missing(&e) => (),
                        Err(e) => return Err(e.to_string()),
                    }
                    if key.enum_values().next().is_none() && key.enum_keys().next().is_none() {
                        drop(key);
                        io(user.delete_subkey(RUN))?;
                    }
                }
                Err(e) if missing(&e) => (),
                Err(e) => return Err(e.to_string()),
            }
        }
        for link in snapshot.links {
            if !self.links.contains(&link.path) {
                return Err("Shortcut recovery escapes KnownFolder plan".into());
            }
            transaction::assert_path(&link.path)?;
            if let Some(bytes) = link.bytes {
                io(fs::write(link.path, bytes))?;
            } else if link.path.exists() {
                io(fs::remove_file(link.path))?;
            }
            if link.temporary.exists() {
                io(fs::remove_file(link.temporary))?;
            }
        }
        Ok(())
    }
}
