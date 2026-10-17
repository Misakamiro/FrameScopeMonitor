use serde::Serialize;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;
#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;
#[cfg(target_os = "windows")]
use std::os::windows::ffi::OsStrExt;
use winreg::enums::*;
use winreg::RegKey;

const CREATE_NO_WINDOW: u32 = 0x08000000;
pub const PRODUCT_NAME: &str = "FrameScope";
pub const REG_KEY_NAME: &str = "FrameScope";

/// Runtime roles for the read-only running query. Installation uses the
/// ownership-verified normal-stop helper after complete payload staging.
const APP_PROCESS_NAMES: &[&str] = &[
    "FrameScope.exe",
    "FrameScopeHardwareService.exe",
    "FrameScopeHardwareProvider.exe",
    "FrameScopeMonitor.exe",
    "FrameScopeProcessSampler.exe",
    "FrameScopeSystemSampler.exe",
    "FrameScopeReportGenerator.exe",
    "framescope-ui-preview.exe",
    "PresentMon-2.4.1-x64.exe",
];

/// Embedded app payload (app exe + backend chain + DLLs + PresentMon + fonts),
/// packed by pack-payload.ps1 before this crate is built.
pub const PAYLOAD_ZIP: &[u8] = include_bytes!("../payload.zip");

#[derive(Serialize)]
pub struct InstallState {
    pub installed: bool,
    pub version: String,
    pub install_path: String,
    /// True when the existing ARP entry came from the legacy NSIS package.
    pub legacy_nsis: bool,
}

#[derive(Serialize, Clone)]
pub struct ProgressEvent {
    pub index: u32,
    pub total: u32,
    pub name: String,
}

#[cfg(target_os = "windows")]
extern "system" {
    fn GetDiskFreeSpaceExW(
        lpDirectoryName: *const u16,
        lpFreeBytesAvailableToCaller: *mut u64,
        lpTotalNumberOfBytes: *mut u64,
        lpTotalNumberOfFreeBytes: *mut u64,
    ) -> i32;
}

// === Existing-install detection ===

fn arp_key() -> String {
    format!(r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{REG_KEY_NAME}")
}

/// Locate the currently installed app. Prefers our own ARP key; falls back to
/// the legacy NSIS entry (identified by DisplayName) so upgrades from the old
/// package keep working and get migrated on the next install.
fn find_existing_install() -> Option<(String, String, bool)> {
    #[cfg(target_os = "windows")]
    {
        let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
        // 64-bit view first, then WOW6432Node (NSIS perMachine writes 64-bit,
        // but scan both to be safe).
        for view in [
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ] {
            if let Ok(root) = hklm.open_subkey_with_flags(view, KEY_READ) {
                // Our own key first.
                if let Ok(key) = root.open_subkey_with_flags(REG_KEY_NAME, KEY_READ) {
                    if let (Ok(loc), Ok(ver)) = (
                        key.get_value::<String, _>("InstallLocation"),
                        key.get_value::<String, _>("DisplayVersion"),
                    ) {
                        if Path::new(&loc).join("FrameScope.exe").exists() {
                            return Some((loc, ver, false));
                        }
                    }
                }
                // Legacy NSIS entry: tauri's NSIS names the key after the
                // product; match by DisplayName to stay robust.
                for name in root.enum_keys().filter_map(|k| k.ok()) {
                    if name == REG_KEY_NAME {
                        continue;
                    }
                    if let Ok(sub) = root.open_subkey_with_flags(&name, KEY_READ) {
                        let display = sub.get_value::<String, _>("DisplayName").unwrap_or_default();
                        if display == PRODUCT_NAME {
                            if let Ok(loc) = sub.get_value::<String, _>("InstallLocation") {
                                let ver = sub
                                    .get_value::<String, _>("DisplayVersion")
                                    .unwrap_or_else(|_| "1.0.0".into());
                                if Path::new(&loc).join("FrameScope.exe").exists() {
                                    return Some((loc, ver, true));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    None
}

#[tauri::command]
pub fn get_state() -> InstallState {
    if let Some(root)=crate::self_relaunch::original_root(){return InstallState{installed:root.is_dir(),version:String::new(),install_path:root.to_string_lossy().to_string(),legacy_nsis:false};}
    match find_existing_install() {
        Some((path, version, legacy)) => InstallState {
            installed: true,
            version,
            install_path: path,
            legacy_nsis: legacy,
        },
        None => InstallState {
            installed: false,
            version: String::new(),
            install_path: String::new(),
            legacy_nsis: false,
        },
    }
}

#[tauri::command]
pub fn get_default_install_path() -> String {
    if let Some(root)=crate::self_relaunch::original_root(){return root.to_string_lossy().to_string();}
    if let Some((path, _, _)) = find_existing_install() {
        return path;
    }
    let program_files = std::env::var("ProgramFiles").unwrap_or_else(|_| "C:\\Program Files".into());
    Path::new(&program_files)
        .join(PRODUCT_NAME)
        .display()
        .to_string()
}

#[tauri::command]
pub fn get_app_version() -> String {
    env!("FRAMESCOPE_APP_VERSION").to_string()
}

/// "install" or "uninstall" — the same exe serves both skins; the UI picks
/// its layout from this (the uninstall skin only needs the delete-data
/// choice, no path/shortcut options).
#[tauri::command]
pub fn get_launch_mode() -> String {
    let args: Vec<String> = std::env::args().collect();
    if args.iter().any(|a| a == "--uninstall") {
        "uninstall".into()
    } else {
        "install".into()
    }
}

#[tauri::command]
pub fn launch_app(install_dir: String) -> Result<(), String> {
    let exe = Path::new(&install_dir).join("FrameScope.exe");
    if !exe.exists() {
        return Err("未找到 FrameScope.exe".into());
    }
    Command::new(&exe)
        .spawn()
        .map_err(|e| format!("无法启动应用: {e}"))?;
    Ok(())
}

#[tauri::command]
pub fn check_disk_space(path: String) -> Result<u64, String> {
    available_space(Path::new(&path))
}

/// WebView2 runtime presence (same key chain the NSIS template checks).
#[tauri::command]
pub fn check_webview2() -> bool {
    #[cfg(target_os = "windows")]
    {
        let keys = [
            r"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            r"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            r"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}0000",
        ];
        for k in keys {
            if let Ok(hklm) = RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey_with_flags(k, KEY_READ) {
                // The NSIS template treats pv >= "0.0.0.1" as installed.
                if let Ok(pv) = hklm.get_value::<String, _>("pv") {
                    if pv.as_str() >= "0.0.0.1" {
                        return true;
                    }
                }
            }
        }
        // Per-user install location.
        if let Ok(hkcu) = RegKey::predef(HKEY_CURRENT_USER)
            .open_subkey_with_flags(r"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", KEY_READ)
        {
            if let Ok(pv) = hkcu.get_value::<String, _>("pv") {
                if pv.as_str() >= "0.0.0.1" {
                    return true;
                }
            }
        }
    }
    false
}

// === Process handling ===

fn scoped_process_script(target: &Path, stop: bool) -> String {
    let names = APP_PROCESS_NAMES.iter()
        .map(|name| format!("'{}'", name.trim_end_matches(".exe"))).collect::<Vec<_>>().join(",");
    format!(r#"$ErrorActionPreference = 'Stop'
function Assert-PlainPath([string]$path) {{
    $full = [IO.Path]::GetFullPath($path)
    $part = $full
    while ($part) {{
        $item = Get-Item -LiteralPath $part -Force -ErrorAction Stop
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {{ throw "Reparse path: $part" }}
        $part = [IO.Path]::GetDirectoryName($part)
    }}
    return $full
}}
$root = (Assert-PlainPath '{root}').TrimEnd('\') + '\'
foreach ($p in @(Get-Process -Name {names} -ErrorAction SilentlyContinue)) {{
    try {{
        # Retain the process handle before inspecting its image; never kill by name/PID.
        $handle = $p.Handle
        $image = $p.MainModule.FileName
        if (-not $image -or -not [IO.Path]::IsPathRooted($image)) {{ continue }}
        $image = [IO.Path]::GetFullPath($image)
        if (-not $image.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {{ continue }}
        $null = Assert-PlainPath $image
        if ({stop}) {{
            throw 'Force termination is forbidden; use validated shutdown helper'
            if (-not $p.WaitForExit(5000)) {{ throw 'Process did not exit within 5 seconds' }}
        }} else {{ Write-Output 'RUNNING' }}
    }} catch {{
        if (-not $p.HasExited) {{ throw }}
    }} finally {{ $p.Dispose() }}
}}
exit 0
"#, root = target.display().to_string().trim_start_matches(r"\\?\").replace('\'', "''"),
        stop = if stop { "$true" } else { "$false" })
}

fn is_app_running() -> bool {
    #[cfg(target_os = "windows")]
    {
        let Some((target, _, _)) = find_existing_install() else { return false };
        let output = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-Command", &scoped_process_script(Path::new(&target), false)])
            .creation_flags(CREATE_NO_WINDOW)
            .output();
        if let Ok(out) = output {
            return out.status.success() && String::from_utf8_lossy(&out.stdout).contains("RUNNING");
        }
    }
    false
}

#[tauri::command]
pub fn is_app_running_cmd() -> bool {
    is_app_running()
}

// === Install ===

#[tauri::command]
pub fn install(app: tauri::AppHandle, target_dir: String, create_desktop_shortcut: bool, autostart: bool) -> Result<(), String> {
    let total = payload_file_count()?;
    install_shared(&target_dir, create_desktop_shortcut, autostart, &move |index, name| {
        use tauri::Emitter;
        let _ = app.emit("install-progress", ProgressEvent { index, total, name: name.to_string() });
    })
}

pub fn install_headless(target_dir: String, desktop_shortcut: bool, autostart: bool) -> Result<(), String> {
    install_shared(&target_dir, desktop_shortcut, autostart, &|_, _| {})
}

pub(crate) fn required_payload_files() -> Result<Vec<String>, String> {
    let mut files = ["FrameScope.exe", "FrameScopeUninstaller.exe", "FrameScopeHostCapabilities.json",
        "FrameScopeHardwareService.exe", "FrameScopeHardwareProvider.exe", "FrameScopeMonitor.exe",
        "FrameScopeProcessSampler.exe", "FrameScopeSystemSampler.exe", "FrameScopeReportGenerator.exe",
        "LICENSE", "LICENSE.EXTRA", "PRIVACY.md", "THIRD-PARTY-NOTICES.md", "README-FrameScopeMonitor.txt",
        "Fonts/MiSansVF.ttf", "dependencies/PawnIO/PawnIO_setup.exe"]
        .iter().map(|s| s.to_string()).collect::<Vec<_>>();
    let lock: serde_json::Value = serde_json::from_str(include_str!("../../../../dependencies.lock.json")).map_err(|e| e.to_string())?;
    let runtimes = lock["runtimeFiles"].as_object().ok_or("Missing runtime dependency contract")?;
    files.extend(runtimes.keys().cloned());
    let presentmon = lock["presentMon"]["file"].as_str().ok_or("Missing PresentMon contract")?.rsplit(['/', '\\']).next().ok_or("Invalid PresentMon path")?;
    files.push(format!("tools/{presentmon}"));
    Ok(files)
}

fn install_shared(target_dir: &str, desktop: bool, autostart: bool, progress: &dyn Fn(u32, &str)) -> Result<(), String> {
    let target = PathBuf::from(target_dir);
    validate_install_target(&target)?;
    let self_exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let mut runtime = crate::installation_system::WindowsRuntime::new(&target, false, desktop, autostart)?;
    let mut deletions = legacy_injected_osd_paths(&target);
    if !runtime.legacy.is_empty() { deletions.push(target.join("uninstall.exe")); }
    crate::install_transaction::execute(crate::install_transaction::Request {
        target: &target, payload: PAYLOAD_ZIP, self_exe: &self_exe, required: &required_payload_files()?,
        uninstall: false, delete_data: false, deletions, data_roots: vec![], progress,
    }, &mut runtime)
}

pub(crate) fn legacy_injected_osd_paths(target: &Path) -> Vec<PathBuf> {
    [
        "FrameScopeInProcessOSD-D3D11.dll",
        "FrameScopeInProcessOSD-D3D12.dll",
        "framescope-osd-inject.exe",
        "THIRD-PARTY-NOTICES-hudhook.md",
    ]
    .into_iter()
    .map(|name| target.join(name))
    .collect()
}
// === Uninstall ===

#[tauri::command]
pub fn uninstall(delete_data: bool) -> Result<(), String> {
    perform_uninstall(&selected_uninstall_root()?, delete_data)
}

/// Headless uninstall for `--uninstall /S`.
pub fn uninstall_headless(delete_data: bool) -> Result<(), String> {
    perform_uninstall(&selected_uninstall_root()?, delete_data)
}
fn selected_uninstall_root()->Result<PathBuf,String>{
    crate::self_relaunch::select_root(crate::self_relaunch::original_root(),||find_existing_install().map(|(path,_,_)|PathBuf::from(path))).ok_or("未找到已安装的 FrameScope".into())
}

/// Remove only owned files; preserved data may keep the install directory alive.
fn perform_uninstall(install_dir: &Path, delete_data: bool) -> Result<(), String> {
    checked_root(install_dir)?;
    let self_exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let mut runtime = crate::installation_system::WindowsRuntime::new(install_dir, true, false, false)?;
    let mut deletions = legacy_injected_osd_paths(install_dir);
    if !runtime.legacy.is_empty() { deletions.push(install_dir.join("uninstall.exe")); }
    let mut data_roots = Vec::new();

    if delete_data {


        for base in [dirs::data_dir(), dirs::config_dir(), dirs::cache_dir()].into_iter().flatten() {
            data_roots.push(base.join("com.framescope.ui-preview"));
        }
        if let Some(local) = dirs::data_local_dir() { data_roots.push(local.join("FrameScopeMonitorData")); }
        data_roots.sort(); data_roots.dedup();

    }
    crate::install_transaction::execute(crate::install_transaction::Request {
        target: install_dir, payload: PAYLOAD_ZIP, self_exe: &self_exe, required: &required_payload_files()?,
        uninstall: true, delete_data, deletions, data_roots, progress: &|_, _| {},
    }, &mut runtime)?;
    // Empty owned directories are optional housekeeping after a durable commit;
    // unknown files, data kept by the user, and reparse paths are never swept.

    let _ = fs::remove_dir(install_dir);
    Ok(())
}
pub(crate) const LOCAL_DATA_FILES: &[&str] = &[
    "framescope-config.json", "framescope-watcher-state.json", "framescope-history.jsonl",
    "framescope-watcher.log", "preview-native-settings.json", "settings.json",
];
pub(crate) const LOCAL_DATA_DIRS: &[&str] = &[
    "framescope-runs", "diagnostic-reports", "history", "state", "reports",
];

fn is_reparse(metadata: &fs::Metadata) -> bool {
    use std::os::windows::fs::MetadataExt;
    metadata.file_attributes() & 0x400 != 0
}

fn checked_root(root: &Path) -> Result<PathBuf, String> {
    if !root.is_absolute() || root.file_name().is_none()
        || root.components().any(|c| matches!(c, std::path::Component::ParentDir))
    {
        return Err(format!("Unsafe uninstall root: {}", root.display()));
    }
    for ancestor in root.ancestors() {
        let metadata = fs::symlink_metadata(ancestor).map_err(|e| e.to_string())?;
        if is_reparse(&metadata) {
            return Err(format!("Refusing reparse point: {}", ancestor.display()));
        }
    }
    if !root.is_dir() {
        return Err(format!("Uninstall root is not a directory: {}", root.display()));
    }
    fs::canonicalize(root).map_err(|e| e.to_string())
}

pub(crate) fn checked_relative(relative: &Path) -> Result<(), String> {
    if relative.as_os_str().is_empty() || relative.components().any(|component| {
        match component {
            std::path::Component::Normal(name) => {
                let name = name.to_string_lossy();
                let stem = name.split('.').next().unwrap_or("").to_ascii_uppercase();
                let device = matches!(stem.as_str(), "CON" | "PRN" | "AUX" | "NUL")
                    || (stem.len() == 4 && (stem.starts_with("COM") || stem.starts_with("LPT"))
                        && matches!(stem.as_bytes()[3], b'1'..=b'9'));
                device || name.chars().any(|c| c < ' ' || "<>:\"|?*".contains(c))
                    || name.ends_with('.') || name.ends_with(' ')
            }
            _ => true,
        }
    }) {
        return Err(format!("Unsafe uninstall entry: {}", relative.display()));
    }
    Ok(())
}

#[cfg(test)]
fn checked_child(root: &Path, relative: &Path) -> Result<PathBuf, String> {
    checked_relative(relative)?;
    let root = checked_root(root)?;
    let mut path = root.clone();
    for component in relative.components() {
        path.push(component);
        match fs::symlink_metadata(&path) {
            Ok(metadata) => {
                if is_reparse(&metadata) || !fs::canonicalize(&path).map_err(|e| e.to_string())?.starts_with(&root) {
                    return Err(format!("Refusing reparse/escaping path: {}", path.display()));
                }
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(e.to_string()),
        }
    }
    Ok(path)
}

// Preflight the complete tree before deleting anything; never follow junctions.
#[cfg(test)]
fn collect_removals(
    root: &Path,
    relative: &Path,
    tree: bool,
    plan: &mut Vec<(PathBuf, bool)>,
) -> Result<(), String> {
    let path = checked_child(root, relative)?;
    let metadata = match fs::symlink_metadata(&path) {
        Ok(metadata) => metadata,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(()),
        Err(e) => return Err(e.to_string()),
    };
    if metadata.is_dir() {
        if !tree {
            return Err(format!("Expected a program/data file, found directory: {}", path.display()));
        }
        for entry in fs::read_dir(&path).map_err(|e| e.to_string())? {
            let entry = entry.map_err(|e| e.to_string())?;
            collect_removals(root, &relative.join(entry.file_name()), true, plan)?;
        }
    }
    plan.push((relative.to_path_buf(), metadata.is_dir()));
    Ok(())
}

#[cfg(test)]
fn execute_removals(root: &Path, plan: &[(PathBuf, bool)]) -> Result<(), String> {
    for (relative, directory) in plan {
        let path = checked_child(root, relative)?;
        let result = if *directory { fs::remove_dir(&path) } else { fs::remove_file(&path) };
        if let Err(e) = result {
            if e.kind() != std::io::ErrorKind::NotFound {
                return Err(format!("Cannot remove {}: {e}", path.display()));
            }
        }
    }
    Ok(())
}

#[cfg(test)]
fn remove_product_data(base: &Path, relative: &Path) -> Result<(), String> {
    let mut plan = Vec::new();
    collect_removals(base, relative, true, &mut plan)?;
    execute_removals(base, &plan)
}

#[cfg(test)]
fn remove_install_files(
    install_dir: &Path,
    self_exe: &Path,
    payload: &[u8],
    delete_data: bool,
) -> Result<(), String> {
    let root = checked_root(install_dir)?;
    let self_exe = fs::canonicalize(self_exe).map_err(|e| e.to_string())?;
    let mut archive = zip::ZipArchive::new(std::io::Cursor::new(payload)).map_err(|e| e.to_string())?;
    let mut files = std::collections::BTreeSet::new();
    for index in 0..archive.len() {
        let entry = archive.by_index(index).map_err(|e| e.to_string())?;
        let relative = PathBuf::from(entry.name());
        checked_relative(&relative)?;
        if entry.unix_mode().map(|mode| mode & 0o170000 == 0o120000).unwrap_or(false) {
            return Err("Payload contains a symbolic link".into());
        }
        let first = relative.components().next().unwrap().as_os_str().to_string_lossy();
        let data = LOCAL_DATA_FILES.iter().chain(LOCAL_DATA_DIRS)
            .any(|name| first.eq_ignore_ascii_case(name));
        if !entry.is_dir() && !data {
            files.insert(relative);
        }
    }
    files.insert(PathBuf::from("FrameScope Installer.exe"));
    let mut plan = Vec::new();
    let mut directories = std::collections::BTreeSet::new();
    for relative in files {
        let path = checked_child(&root, &relative)?;
        if path == self_exe {
            continue;
        }
        collect_removals(&root, &relative, false, &mut plan)?;
        for parent in relative.ancestors().skip(1).filter(|p| !p.as_os_str().is_empty()) {
            directories.insert(parent.to_path_buf());
        }
    }
    if delete_data {
        for name in LOCAL_DATA_FILES {
            collect_removals(&root, Path::new(name), false, &mut plan)?;
        }
        for name in LOCAL_DATA_DIRS {
            collect_removals(&root, Path::new(name), true, &mut plan)?;
        }
    }
    execute_removals(&root, &plan)?;
    // Only prune empty manifest directories; unknown contents are never swept.
    for relative in directories.iter().rev() {
        let path = checked_child(&root, relative)?;
        let _ = fs::remove_dir(path);
    }
    Ok(())
}

/// Only an installed uninstaller may delete itself; downloaded installers stay.
#[cfg(test)]
fn schedule_self_cleanup(self_exe: &Path, install_dir: &Path) -> Result<(), String> {
    use std::io::Write;
    let installed_exe = checked_child(install_dir, Path::new("FrameScope Installer.exe"))?;
    if fs::canonicalize(self_exe).map_err(|e| e.to_string())? != installed_exe {
        let _ = fs::remove_dir(checked_root(install_dir)?);
        return Ok(());
    }
    let script = self_cleanup_script(self_exe, install_dir, std::process::id(), 300_000);
    let nonce = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH)
        .map_err(|e| e.to_string())?.as_nanos();
    let script_path = std::env::temp_dir()
        .join(format!("framescope-uninstall-{}-{nonce}.ps1", std::process::id()));
    fs::OpenOptions::new().write(true).create_new(true).open(&script_path)
        .and_then(|mut file| file.write_all(script.as_bytes())).map_err(|e| e.to_string())?;
    Command::new("powershell.exe")
        .args(["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-ExecutionPolicy", "Bypass", "-File"])
        .arg(&script_path).creation_flags(CREATE_NO_WINDOW).spawn().map_err(|e| e.to_string())?;
    Ok(())
}

#[cfg(test)]
fn self_cleanup_script(self_exe: &Path, install_dir: &Path, pid: u32, wait_ms: u32) -> String {
    let script = format!(r#"$ErrorActionPreference = 'Stop'
function Assert-PlainPath([string]$path) {{
    $full = [IO.Path]::GetFullPath($path)
    $part = $full
    while ($part) {{
        try {{
            $attributes = [IO.File]::GetAttributes($part)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) {{ throw "Reparse point: $part" }}
        }} catch [IO.FileNotFoundException] {{ }} catch [IO.DirectoryNotFoundException] {{ }}
        $parent = [IO.Directory]::GetParent($part)
        $part = if ($parent) {{ $parent.FullName }} else {{ $null }}
    }}
    return $full
}}
try {{
    $owner = $null
    try {{ $owner = [Diagnostics.Process]::GetProcessById({pid}) }} catch [ArgumentException] {{ }}
    if ($owner -and -not $owner.WaitForExit({wait_ms})) {{ return }}
    $dir = Assert-PlainPath '{dir}'
    $exe = Assert-PlainPath '{exe}'
    if ([IO.Path]::GetDirectoryName($exe) -ne $dir -or [IO.Path]::GetFileName($exe) -ne 'FrameScope Installer.exe') {{
        throw 'Uninstaller is outside its installation directory'
    }}
    for ($attempt = 0; $attempt -lt 20; $attempt++) {{
        $null = Assert-PlainPath $exe
        try {{ [IO.File]::Delete($exe); break }} catch [IO.IOException] {{ Start-Sleep -Milliseconds 250 }}
    }}
    $null = Assert-PlainPath $dir
    try {{ [IO.Directory]::Delete($dir, $false) }} catch [IO.IOException] {{ }}
}} finally {{
    [IO.File]::Delete($PSCommandPath)
}}
"#, exe = self_exe.display().to_string().replace('\'', "''"),
        dir = install_dir.display().to_string().replace('\'', "''"));
    // Windows PowerShell 5 otherwise decodes non-ASCII paths using its ANSI code page.
    format!("\u{feff}{script}")
}

// === Payload extraction ===

fn payload_file_count() -> Result<u32, String> {
    let cursor = std::io::Cursor::new(PAYLOAD_ZIP);
    let archive = zip::ZipArchive::new(cursor).map_err(|e| format!("无法读取安装包: {e}"))?;
    Ok(archive.len() as u32)
}

#[cfg(test)]
fn extract_payload_from(target_dir: &Path, payload: &[u8], on_entry: &dyn Fn(u32, &str)) -> Result<(), String> {
    let paths = validate_payload_paths(target_dir, payload)?;
    fs::create_dir_all(target_dir).map_err(|e| e.to_string())?;
    let cursor = std::io::Cursor::new(payload);
    let mut archive = zip::ZipArchive::new(cursor).map_err(|e| format!("无法读取安装包: {e}"))?;
    for i in 0..archive.len() {
        let mut file = archive.by_index(i).map_err(|e| format!("读取文件失败: {e}"))?;
        let name = file.name().to_string();
        let outpath = checked_child(target_dir, &paths[i as usize])?;
        if file.is_dir() {
            fs::create_dir_all(&outpath).map_err(|e| format!("无法创建目录 {}: {e}", outpath.display()))?;
        } else {
            if let Some(parent) = outpath.parent() {
                fs::create_dir_all(parent)
                    .map_err(|e| format!("无法创建目录 {}: {e}", parent.display()))?;
            }
            let mut outfile = fs::File::create(&outpath)
                .map_err(|e| format!("无法写入文件 {}: {e}", outpath.display()))?;
            std::io::copy(&mut file, &mut outfile)
                .map_err(|e| format!("无法写入文件 {}: {e}", outpath.display()))?;
        }
        on_entry((i + 1) as u32, &name);
    }
    Ok(())
}

pub(crate) fn validate_install_target(target: &Path) -> Result<(), String> {
    if !target.is_absolute() || target.file_name().is_none()
        || target.components().any(|c| matches!(c, std::path::Component::ParentDir))
    {
        return Err(format!("Unsafe installation target: {}", target.display()));
    }
    for component in target.components() {
        if let std::path::Component::Normal(name) = component {
            checked_relative(Path::new(name))?;
        }
    }
    for ancestor in target.ancestors() {
        match fs::symlink_metadata(ancestor) {
            Ok(metadata) if is_reparse(&metadata) => return Err(format!("Refusing reparse path: {}", ancestor.display())),
            Ok(_) => {}
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
            Err(e) => return Err(format!("Cannot inspect installation path {}: {e}", ancestor.display())),
        }
    }
    Ok(())
}

pub(crate) fn validate_payload_paths(target: &Path, payload: &[u8]) -> Result<Vec<PathBuf>, String> {
    validate_install_target(target)?;
    let mut archive = zip::ZipArchive::new(std::io::Cursor::new(payload)).map_err(|e| e.to_string())?;
    let mut names = std::collections::HashSet::new();
    let mut files = std::collections::HashSet::new();
    let mut paths = Vec::new();
    for i in 0..archive.len() {
        let file = archive.by_index(i).map_err(|e| e.to_string())?;
        let name = file.name().replace('\\', "/");
        let relative = PathBuf::from(name.trim_end_matches('/'));
        checked_relative(&relative)?;
        let key = relative.to_string_lossy().replace('\\', "/").to_lowercase();
        if !names.insert(key.clone()) {
            return Err(format!("Duplicate payload entry: {name}"));
        }
        if file.unix_mode().map(|m| m & 0o170000 == 0o120000).unwrap_or(false) {
            return Err(format!("Payload symlink is not allowed: {name}"));
        }
        validate_install_target(&target.join(&relative))?;
        if !file.is_dir() { files.insert(key); }
        paths.push(relative);
    }
    for relative in &paths {
        for parent in relative.ancestors().skip(1) {
            if files.contains(&parent.to_string_lossy().replace('\\', "/").to_lowercase()) {
                return Err(format!("Payload file/directory conflict: {}", relative.display()));
            }
        }
    }
    Ok(paths)
}

// === Registry ===

pub(crate) fn register_uninstall(install_dir: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
        let (key, _) = hklm
            .create_subkey(arp_key())
            .map_err(|e| format!("无法创建注册表键: {e}"))?;

        let icon_path = format!("{install_dir}\\FrameScope.exe");
        let uninstaller_path = format!("{install_dir}\\FrameScope Installer.exe");

        key.set_value("DisplayName", &PRODUCT_NAME).map_err(|e| e.to_string())?;
        key.set_value("DisplayVersion", &env!("FRAMESCOPE_APP_VERSION")).map_err(|e| e.to_string())?;
        key.set_value("DisplayIcon", &icon_path).map_err(|e| e.to_string())?;
        key.set_value("Publisher", &"FrameScope").map_err(|e| e.to_string())?;
        key.set_value("InstallLocation", &install_dir).map_err(|e| e.to_string())?;
        key.set_value("UninstallString", &format!("\"{uninstaller_path}\" --uninstall")).map_err(|e| e.to_string())?;
        key.set_value("NoModify", &1u32).map_err(|e| e.to_string())?;
        key.set_value("NoRepair", &1u32).map_err(|e| e.to_string())?;
        // EstimatedSize is in KiB; payload ~90MB unpacked.
        key.set_value("EstimatedSize", &92_000u32).map_err(|e| e.to_string())?;
    }
    Ok(())
}

// === Shortcut KnownFolders ===

pub(crate) fn get_special_folder_path(folder: &str) -> Option<String> {
    if folder == "Desktop" {
        dirs::desktop_dir().map(|p| p.to_string_lossy().to_string())
    } else {
        get_common_programs_path()
    }
}

#[cfg(target_os = "windows")]
fn get_common_programs_path() -> Option<String> {
    use std::os::windows::ffi::OsStringExt;
    extern "system" {
        fn SHGetFolderPathW(
            hwnd: *mut std::ffi::c_void,
            csidl: i32,
            h_token: *mut std::ffi::c_void,
            dw_flags: u32,
            psz_path: *mut u16,
        ) -> i32;
    }
    const CSIDL_COMMON_PROGRAMS: i32 = 0x0017;
    let mut buf = vec![0u16; 260];
    unsafe {
        if SHGetFolderPathW(std::ptr::null_mut(), CSIDL_COMMON_PROGRAMS, std::ptr::null_mut(), 0, buf.as_mut_ptr()) == 0 {
            let len = buf.iter().position(|&c| c == 0).unwrap_or(buf.len());
            return Some(std::ffi::OsString::from_wide(&buf[..len]).to_string_lossy().to_string());
        }
    }
    None
}

// === Disk space ===

pub(crate) fn available_space(path: &Path) -> Result<u64, String> {
    #[cfg(target_os = "windows")]
    {
        let root_path = path.to_string_lossy().to_string();
        let wide_path: Vec<u16> = std::ffi::OsStr::new(&root_path)
            .encode_wide()
            .chain(std::iter::once(0))
            .collect();
        let mut free_bytes: u64 = 0;
        let mut total_bytes: u64 = 0;
        let mut total_free_bytes: u64 = 0;
        unsafe {
            let result = GetDiskFreeSpaceExW(
                wide_path.as_ptr(),
                &mut free_bytes as *mut u64,
                &mut total_bytes as *mut u64,
                &mut total_free_bytes as *mut u64,
            );
            if result != 0 {
                return Ok(free_bytes);
            }
        }
    }
    Err("无法获取磁盘空间".into())
}

#[cfg(test)]
mod install_safety_tests {
    use super::*;
    use std::io::Write;

    fn fixture() -> PathBuf {
        let root = std::env::temp_dir().join(format!("framescope-install-safety-{}-{}",
            std::process::id(), std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        fs::create_dir(&root).unwrap();
        eprintln!("Retained fixture: {}", root.display());
        root
    }

    fn archive(names: &[&str]) -> Vec<u8> {
        let mut zip = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
        for name in names {
            zip.start_file(*name, zip::write::SimpleFileOptions::default()).unwrap();
            zip.write_all(b"new payload").unwrap();
        }
        zip.finish().unwrap().into_inner()
    }

    #[test]
    fn corrupt_manifest_is_rejected_before_old_files_change() {
        let root = fixture();
        fs::write(root.join("FrameScope.exe"), b"old executable").unwrap();
        let payload = archive(&["FrameScope.exe", "FrameScopePayloadManifest.json"]);
        assert!(crate::install_transaction::stage_payload(&root, &root.join("validated-stage"), &payload, &[], &|_, _| {}).is_err(),
            "manifest and SHA must be verified before replacing an old executable");
        assert_eq!(fs::read(root.join("FrameScope.exe")).unwrap(), b"old executable");
    }
    #[test]
    fn extraction_preflights_all_paths_before_writing() {
        for bad in ["../outside.txt", "tools/../../outside.txt", "FrameScope.exe:stream", "alias./file"] {
            let root = fixture();
            let target = root.join("install");
            fs::create_dir(&target).unwrap();
            let exe = target.join("FrameScope.exe");
            fs::write(&exe, b"old executable").unwrap();
            let result = extract_payload_from(&target, &archive(&["FrameScope.exe", bad]), &|_, _| {});
            assert!(result.is_err(), "accepted unsafe payload path: {bad}");
            assert_eq!(fs::read(&exe).unwrap(), b"old executable", "partial mutation before preflight");
            assert!(!root.join("outside.txt").exists());
        }
    }

    #[test]
    fn extraction_rejects_case_aliases_before_writing() {
        let target = fixture();
        let result = extract_payload_from(&target, &archive(&["FrameScope.exe", "framescope.EXE"]), &|_, _| {});
        assert!(result.is_err(), "accepted case-insensitive duplicate payload");
        assert!(!target.join("FrameScope.exe").exists());
    }

    #[test]
    fn extraction_rejects_file_directory_conflicts_and_devices_before_writing() {
        for names in [&["FrameScope.exe", "FrameScope.exe/child"][..], &["FrameScope.exe", "NUL.txt"][..]] {
            let target = fixture();
            assert!(extract_payload_from(&target, &archive(names), &|_, _| {}).is_err());
            assert!(!target.join("FrameScope.exe").exists(), "partially extracted invalid archive");
        }
    }

    #[test]
    fn extraction_keeps_flat_layout_and_existing_user_data() {
        let target = fixture().join("new installation");
        let names = ["FrameScope.exe", "FrameScopeMonitor.exe", "tools/PresentMon.exe", "Fonts/font.ttf"];
        extract_payload_from(&target, &archive(&names), &|_, _| {}).unwrap();
        fs::write(target.join("framescope-config.json"), b"retained configuration").unwrap();
        extract_payload_from(&target, &archive(&names), &|_, _| {}).unwrap();
        for name in names { assert_eq!(fs::read(target.join(name)).unwrap(), b"new payload"); }
        assert_eq!(fs::read(target.join("framescope-config.json")).unwrap(), b"retained configuration");
        assert!(!target.join("backend").exists());
    }

    #[test]
    fn headless_rejects_invalid_targets_before_any_system_side_effect() {
        for target in ["relative-install", r"C:\", r"C:\safe\..\escape", r"C:\safe:stream"] {
            assert!(validate_install_target(Path::new(target)).is_err(), "{target}");
        }
    }

    #[test]
    fn running_query_only_selects_verified_images_under_target() {
        let root = fixture();
        let target = root.join("install 'quoted'");
        let sibling = root.join("install 'quoted'-other");
        fs::create_dir(&target).unwrap();
        fs::create_dir(&sibling).unwrap();
        let inside = target.join("FrameScope.exe");
        let outside = sibling.join("FrameScope.exe");
        fs::write(&inside, b"fixture").unwrap();
        fs::write(&outside, b"fixture").unwrap();
        let setup = format!(r#"
$global:killed = [Collections.Generic.List[string]]::new()
$global:fakeProcesses = @()
foreach ($image in @('{inside}', '{outside}', '')) {{
    $p = [pscustomobject]@{{ Handle = 1; MainModule = [pscustomobject]@{{FileName = $image}}; HasExited = $false }}
    $p | Add-Member ScriptMethod Kill {{ $global:killed.Add($this.MainModule.FileName) }}
    $p | Add-Member ScriptMethod WaitForExit {{ param($timeout) return $true }}
    $p | Add-Member ScriptMethod Dispose {{ }}
    $global:fakeProcesses += $p
}}
function Get-Process {{ return $global:fakeProcesses }}
"#, inside = inside.display().to_string().replace('\'', "''"), outside = outside.display().to_string().replace('\'', "''"));
        let script = root.join("scope-test.ps1");
        fs::write(&script, format!("\u{feff}{setup}\n{}\nif ($global:killed.Count -ne 0) {{ throw 'wrong process selection' }}",
            scoped_process_script(&target, false))).unwrap();
        let output = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"])
            .arg(&script).creation_flags(CREATE_NO_WINDOW).output().unwrap();
        assert!(output.status.success(), "{}", String::from_utf8_lossy(&output.stderr));
    }

    #[test]
    fn isolated_delete_data_true_never_uses_os_profile_roots() {
        let root = fixture();
        let install = root.join("install");
        fs::create_dir(&install).unwrap();
        let exe = install.join("FrameScope Installer.exe");
        fs::write(&exe, b"fixture uninstaller").unwrap();
        fs::write(install.join("FrameScope.exe"), b"fixture application").unwrap();
        fs::write(install.join("framescope-config.json"), b"fixture settings").unwrap();
        fs::write(install.join("personal.txt"), b"retained").unwrap();
        remove_install_files(&install, &exe, &archive(&["FrameScope.exe"]), true).unwrap();
        assert!(!install.join("framescope-config.json").exists());
        assert!(install.join("personal.txt").exists());
        // Explicit fixture roots, never environment overrides or dirs::* discovery.
        for name in ["data", "config", "cache", "local"] {
            let base = root.join(name);
            fs::create_dir(&base).unwrap();
            let relative = if name == "local" { "FrameScopeMonitorData" } else { "com.framescope.ui-preview" };
            fs::create_dir(base.join(relative)).unwrap();
            fs::write(base.join(relative).join("settings.json"), b"fixture settings").unwrap();
            fs::write(base.join("unrelated.txt"), b"retained").unwrap();
            remove_product_data(&base, Path::new(relative)).unwrap();
            assert!(!base.join(relative).exists());
            assert!(base.join("unrelated.txt").exists());
        }
    }
}

#[cfg(test)]
mod uninstall_tests {
    use super::*;
    use std::io::Write;
    static FIXTURE_ID: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);

    struct Fixture {
        root: PathBuf,
        install: PathBuf,
    }

    impl Fixture {
        fn new() -> Self {
            let temp = fs::canonicalize(std::env::temp_dir()).unwrap();
            let nonce = std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos();
            let id = FIXTURE_ID.fetch_add(1, std::sync::atomic::Ordering::Relaxed);
            let root = temp.join(format!("framescope-uninstall-test-{}-{nonce}-{id}", std::process::id()));
            fs::create_dir(&root).unwrap();
            let install = root.join("install");
            fs::create_dir(&install).unwrap();
            eprintln!("Owned TEMP fixture: {}", root.display());
            Self { root, install }
        }

        fn assert_owned(&self, path: &Path) {
            let root = fs::canonicalize(&self.root).unwrap();
            assert!(root.starts_with(fs::canonicalize(std::env::temp_dir()).unwrap()));
            assert!(root.file_name().unwrap().to_str().unwrap().starts_with("framescope-uninstall-test-"));
            assert!(path.starts_with(&root));
            assert!(fs::canonicalize(path).unwrap().starts_with(&root));
        }

        fn file(&self, relative: &str) -> PathBuf {
            let path = self.install.join(relative);
            fs::create_dir_all(path.parent().unwrap()).unwrap();
            fs::write(&path, b"fixture-owned sentinel").unwrap();
            path
        }

        fn cleanup_command(&self, exe: &Path, root: &Path, pid: u32, wait_ms: u32) -> Command {
            self.assert_owned(root);
            self.assert_owned(exe);
            let script = self.root.join("cleanup.ps1");
            fs::write(&script, self_cleanup_script(exe, root, pid, wait_ms)).unwrap();
            self.assert_owned(&script);
            let mut command = Command::new("powershell.exe");
            command.args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"])
                .arg(script).creation_flags(CREATE_NO_WINDOW);
            command
        }

        fn junction(&self, link: &Path, target: &Path) {
            self.assert_owned(link.parent().unwrap());
            self.assert_owned(target);
            // New-Item's junction target parser does not accept the extended prefix.
            let link = link.display().to_string().trim_start_matches(r"\\?\").to_string();
            let target = target.display().to_string().trim_start_matches(r"\\?\").to_string();
            let command = format!("New-Item -ItemType Junction -Path '{}' -Target '{}' | Out-Null",
                link.replace('\'', "''"), target.replace('\'', "''"));
            let output = Command::new("powershell.exe")
                .args(["-NoProfile", "-NonInteractive", "-Command", &command])
                .creation_flags(CREATE_NO_WINDOW).output().unwrap();
            assert!(output.status.success(), "{}", String::from_utf8_lossy(&output.stderr));
            assert!(is_reparse(&fs::symlink_metadata(&link).unwrap()));
        }
    }

    // Fixtures are deliberately retained for inspection; no Drop-time recursive deletion.
    fn payload(names: &[&str]) -> Vec<u8> {
        let mut zip = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
        for name in names {
            zip.start_file(*name, zip::write::SimpleFileOptions::default()).unwrap();
            zip.write_all(b"fixture payload").unwrap();
        }
        zip.finish().unwrap().into_inner()
    }

    #[test]
    fn keep_data_preserves_local_data_and_unknown_files() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        fixture.file("FrameScope.exe");
        let retained: Vec<_> = [
            "framescope-config.json", "framescope-runs/game/report.html",
            "history/session.json", "state/window.json", "reports/report.html",
            "user-notes.txt", "tools/user-script.ps1",
        ].iter().map(|name| fixture.file(name)).collect();
        fixture.assert_owned(&fixture.install);
        remove_install_files(&fixture.install, &exe, &payload(&["FrameScope.exe"]), false).unwrap();
        assert!(!fixture.install.join("FrameScope.exe").exists());
        for path in retained {
            assert!(path.exists(), "keep-data removed {}", path.display());
        }
    }

    #[test]
    fn cleanup_waits_for_parent_and_does_not_remove_retained_data() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let retained = fixture.file("reports/keep.html");
        let script = fixture.root.join("cleanup.ps1");
        fs::write(&script, self_cleanup_script(&exe, &fixture.install, std::process::id(), 50)).unwrap();
        fixture.assert_owned(&fixture.install);
        fixture.assert_owned(&script);
        let output = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File"])
            .arg(&script).creation_flags(CREATE_NO_WINDOW).output().unwrap();
        assert!(output.status.success(), "{}", String::from_utf8_lossy(&output.stderr));
        assert!(exe.exists(), "cleanup deleted installer while its parent was still alive");
        assert!(retained.exists(), "cleanup removed retained report");
    }

    #[test]
    fn delete_data_only_removes_allowlisted_data_and_payload_files() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let program = fixture.file("tools/PresentMon.exe");
        let unknown = fixture.file("tools/user-tool.txt");
        let notes = fixture.file("notes.txt");
        for name in LOCAL_DATA_FILES {
            fixture.file(name);
        }
        for name in LOCAL_DATA_DIRS {
            fixture.file(&format!("{name}/nested/data.json"));
        }
        fixture.assert_owned(&fixture.install);
        remove_install_files(&fixture.install, &exe, &payload(&["tools\\PresentMon.exe"]), true).unwrap();
        assert!(!program.exists());
        assert!(exe.exists() && notes.exists() && unknown.exists());
        for name in LOCAL_DATA_FILES.iter().chain(LOCAL_DATA_DIRS) {
            assert!(!fixture.install.join(name).exists(), "left product data: {name}");
        }
    }

    #[test]
    fn manifest_cannot_override_keep_data_and_empty_program_directories_are_pruned() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let config = fixture.file("framescope-config.json");
        let report = fixture.file("reports/keep.html");
        fixture.file("dependencies/nested/program.dll");
        fixture.assert_owned(&fixture.install);
        let zip = payload(&["framescope-config.json", "reports/keep.html", "dependencies/nested/program.dll"]);
        remove_install_files(&fixture.install, &exe, &zip, false).unwrap();
        assert!(config.exists() && report.exists());
        assert!(!fixture.install.join("dependencies").exists());
    }

    #[test]
    fn unsafe_manifest_entries_fail_before_any_file_is_deleted() {
        for name in ["..\\outside.txt", "C:\\outside.txt", "\\\\server\\share\\outside.txt",
            "FrameScope.exe:stream", "tools\\..\\notes.txt", "tools\\alias."] {
            let fixture = Fixture::new();
            let exe = fixture.file("FrameScope Installer.exe");
            let program = fixture.file("FrameScope.exe");
            let outside = fixture.root.join("outside.txt");
            fs::write(&outside, "retained").unwrap();
            fixture.assert_owned(&fixture.install);
            assert!(remove_install_files(&fixture.install, &exe, &payload(&["FrameScope.exe", name]), false).is_err(), "{name}");
            assert!(program.exists() && outside.exists());
        }
    }

    #[test]
    fn data_junction_aborts_before_deleting_programs_or_outside_data() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let program = fixture.file("FrameScope.exe");
        let outside = fixture.root.join("outside");
        fs::create_dir(&outside).unwrap();
        fs::write(outside.join("sentinel.txt"), "retained").unwrap();
        fixture.junction(&fixture.install.join("reports"), &outside);
        fixture.assert_owned(&fixture.install);
        assert!(remove_install_files(&fixture.install, &exe, &payload(&["FrameScope.exe"]), true).is_err());
        assert!(program.exists() && outside.join("sentinel.txt").exists());
    }

    #[test]
    fn manifest_parent_junction_and_install_root_junction_are_rejected() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let outside = fixture.root.join("outside");
        fs::create_dir(&outside).unwrap();
        fs::write(outside.join("program.dll"), "retained").unwrap();
        fixture.junction(&fixture.install.join("tools"), &outside);
        fixture.assert_owned(&fixture.install);
        assert!(remove_install_files(&fixture.install, &exe, &payload(&["tools/program.dll"]), false).is_err());
        let link = fixture.root.join("install-link");
        fixture.junction(&link, &fixture.install);
        fixture.assert_owned(&link);
        assert!(remove_install_files(&link, &exe, &payload(&[]), false).is_err());
        assert!(outside.join("program.dll").exists());
    }

    #[test]
    fn external_product_data_is_bounded_to_its_named_subdirectory() {
        let fixture = Fixture::new();
        let product = fixture.root.join("FrameScopeMonitorData");
        fs::create_dir(&product).unwrap();
        fs::write(product.join("report.html"), "product data").unwrap();
        let sentinel = fixture.root.join("personal.txt");
        fs::write(&sentinel, "retained").unwrap();
        fixture.assert_owned(&fixture.root);
        assert!(remove_product_data(&fixture.root, Path::new("../escape")).is_err());
        assert!(product.exists());
        remove_product_data(&fixture.root, Path::new("FrameScopeMonitorData")).unwrap();
        assert!(!product.exists());
        assert!(sentinel.exists() && fixture.install.exists());
    }

    #[test]
    fn external_product_data_rejects_nested_junction_without_partial_deletion() {
        let fixture = Fixture::new();
        let product = fixture.root.join("FrameScopeMonitorData");
        fs::create_dir(&product).unwrap();
        fs::write(product.join("report.html"), "product data").unwrap();
        fixture.junction(&product.join("link"), &fixture.install);
        let sentinel = fixture.file("personal.txt");
        fixture.assert_owned(&fixture.root);
        assert!(remove_product_data(&fixture.root, Path::new("FrameScopeMonitorData")).is_err());
        assert!(product.join("report.html").exists() && sentinel.exists());
    }

    fn exited_fixture_process() -> u32 {
        let mut child = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-Command", "exit 0"])
            .creation_flags(CREATE_NO_WINDOW).spawn().unwrap();
        let pid = child.id();
        assert!(child.wait().unwrap().success());
        pid
    }

    #[test]
    fn cleanup_after_exit_keeps_data_and_handles_quoted_unicode_paths() {
        let mut fixture = Fixture::new();
        fixture.install = fixture.root.join("install '引用' [目录]");
        fs::create_dir(&fixture.install).unwrap();
        let exe = fixture.file("FrameScope Installer.exe");
        let data = fixture.file("reports/keep.html");
        let unknown = fixture.file("unknown.txt");
        let mut owner = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Milliseconds 1000"])
            .creation_flags(CREATE_NO_WINDOW).spawn().unwrap();
        let mut cleanup = fixture.cleanup_command(&exe, &fixture.install, owner.id(), 5000).spawn().unwrap();
        std::thread::sleep(std::time::Duration::from_millis(200));
        assert!(owner.try_wait().unwrap().is_none());
        assert!(exe.exists(), "deleted before parent exited");
        assert!(cleanup.wait().unwrap().success());
        assert!(owner.wait().unwrap().success());
        assert!(!exe.exists());
        assert!(data.exists() && unknown.exists() && fixture.install.exists());
        assert!(!fixture.root.join("cleanup.ps1").exists());
    }

    #[test]
    fn cleanup_removes_only_empty_install_root_after_exit() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let output = fixture.cleanup_command(&exe, &fixture.install, exited_fixture_process(), 500)
            .output().unwrap();
        assert!(output.status.success(), "{}", String::from_utf8_lossy(&output.stderr));
        assert!(!fixture.install.exists());
        assert!(fixture.root.exists());
    }

    #[test]
    fn cleanup_rejects_reparse_root_and_outside_executable() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let link = fixture.root.join("install-link");
        fixture.junction(&link, &fixture.install);
        let output = fixture.cleanup_command(&link.join("FrameScope Installer.exe"), &link, exited_fixture_process(), 500)
            .output().unwrap();
        assert!(!output.status.success());
        assert!(exe.exists());
        let external = fixture.root.join("FrameScope Installer.exe");
        fs::write(&external, "external installer").unwrap();
        let output = fixture.cleanup_command(&external, &fixture.install, exited_fixture_process(), 500)
            .output().unwrap();
        assert!(!output.status.success());
        assert!(external.exists() && exe.exists());
    }

    #[test]
    fn cleanup_locked_executable_has_bounded_retries() {
        use std::os::windows::fs::OpenOptionsExt;
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let pid = exited_fixture_process();
        let command = fixture.cleanup_command(&exe, &fixture.install, pid, 500);
        let _locked = fs::OpenOptions::new().read(true).share_mode(0).open(&exe).unwrap();
        let start = std::time::Instant::now();
        let output = { let mut command = command; command.output().unwrap() };
        assert!(output.status.success(), "{}", String::from_utf8_lossy(&output.stderr));
        assert!(start.elapsed() < std::time::Duration::from_secs(10));
        assert!(exe.exists());
    }

    #[test]
    fn embedded_manifest_is_accepted_without_extracting_payload() {
        let fixture = Fixture::new();
        let exe = fixture.file("FrameScope Installer.exe");
        let unknown = fixture.file("my-data.txt");
        fixture.assert_owned(&fixture.install);
        remove_install_files(&fixture.install, &exe, PAYLOAD_ZIP, false).unwrap();
        assert!(exe.exists() && unknown.exists());
    }

    #[test]
    fn downloaded_installer_is_not_scheduled_for_self_deletion() {
        let fixture = Fixture::new();
        let external = fixture.root.join("downloaded-installer.exe");
        fs::write(&external, "external installer").unwrap();
        let installed = fixture.file("FrameScope Installer.exe");
        let retained = fixture.file("reports/keep.html");
        fixture.assert_owned(&fixture.install);
        fixture.assert_owned(&external);
        remove_install_files(&fixture.install, &external, &payload(&[]), false).unwrap();
        assert!(!installed.exists());
        schedule_self_cleanup(&external, &fixture.install).unwrap();
        assert!(external.exists() && retained.exists());
    }
}
