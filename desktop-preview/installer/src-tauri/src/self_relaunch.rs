//! Move an installed installer out of its target before either UI or silent
//! dispatch. Handshake binds the copied binary, original PID/start/image and args.
use crate::install_transaction::{assert_path, hash, Result};
use serde::{Deserialize, Serialize};
use std::os::windows::process::CommandExt;
use std::{
    fs,
    path::{Path, PathBuf},
    process::Command,
    time::{Duration, Instant},
};
const INTERNAL: &str = "--framescope-internal-relaunch";
static ORIGINAL_ROOT: std::sync::OnceLock<PathBuf> = std::sync::OnceLock::new();
static CLEANUP: std::sync::OnceLock<(PathBuf, PathBuf, String)> = std::sync::OnceLock::new();
pub fn original_root() -> Option<PathBuf> {
    ORIGINAL_ROOT.get().cloned()
}
pub fn select_root(
    original: Option<PathBuf>,
    registered: impl FnOnce() -> Option<PathBuf>,
) -> Option<PathBuf> {
    original.or_else(registered)
}
#[derive(Serialize, Deserialize)]
struct Handoff {
    parent: u32,
    start: u64,
    source: PathBuf,
    target: PathBuf,
    copy: PathBuf,
    sha256: String,
    args: Vec<String>,
}
#[repr(C)]
struct FileTime {
    low: u32,
    high: u32,
}
extern "system" {
    fn OpenProcess(access: u32, inherit: i32, pid: u32) -> *mut std::ffi::c_void;
    fn CloseHandle(handle: *mut std::ffi::c_void) -> i32;
    fn GetProcessTimes(
        handle: *mut std::ffi::c_void,
        creation: *mut FileTime,
        exit: *mut FileTime,
        kernel: *mut FileTime,
        user: *mut FileTime,
    ) -> i32;
    fn QueryFullProcessImageNameW(
        handle: *mut std::ffi::c_void,
        flags: u32,
        path: *mut u16,
        size: *mut u32,
    ) -> i32;
    fn WaitForSingleObject(handle: *mut std::ffi::c_void, milliseconds: u32) -> u32;
}
struct Process(*mut std::ffi::c_void);
impl Drop for Process {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}
fn process(pid: u32) -> Result<Process> {
    let handle = unsafe { OpenProcess(0x1000 | 0x00100000, 0, pid) };
    if handle.is_null() {
        Err(std::io::Error::last_os_error().to_string())
    } else {
        Ok(Process(handle))
    }
}
fn identity(process: &Process) -> Result<(u64, PathBuf)> {
    let mut creation = FileTime { low: 0, high: 0 };
    let mut exit = FileTime { low: 0, high: 0 };
    let mut kernel = FileTime { low: 0, high: 0 };
    let mut user = FileTime { low: 0, high: 0 };
    if unsafe { GetProcessTimes(process.0, &mut creation, &mut exit, &mut kernel, &mut user) } == 0
    {
        return Err(std::io::Error::last_os_error().to_string());
    }
    let mut buffer = vec![0u16; 32768];
    let mut size = buffer.len() as u32;
    if unsafe { QueryFullProcessImageNameW(process.0, 0, buffer.as_mut_ptr(), &mut size) } == 0 {
        return Err(std::io::Error::last_os_error().to_string());
    }
    let image =
        PathBuf::from(String::from_utf16(&buffer[..size as usize]).map_err(|e| e.to_string())?);
    Ok(((creation.high as u64) << 32 | creation.low as u64, image))
}
fn validate_handoff(
    handoff: &Handoff,
    current: &Path,
    args: &[String],
    descriptor: &Path,
) -> Result<()> {
    assert_path(current)?;
    assert_path(&handoff.source)?;
    assert_path(descriptor)?;
    if current != handoff.copy
        || descriptor.parent() != current.parent()
        || handoff.args != args
        || handoff.source.parent() != Some(handoff.target.as_path())
        || current
            .parent()
            .and_then(|p| p.file_name())
            .is_none_or(|n| !n.to_string_lossy().starts_with("framescope-relaunch-"))
        || hash(current)? != handoff.sha256
        || hash(&handoff.source)? != handoff.sha256
    {
        return Err("Installer relaunch identity/options mismatch".into());
    }
    Ok(())
}
/// true means the validated child has taken over and the original must exit.
pub fn bootstrap() -> Result<bool> {
    let current = std::env::current_exe().map_err(|e| e.to_string())?;
    let args = std::env::args().skip(1).collect::<Vec<_>>();
    if let Some(index) = args.iter().position(|a| a == INTERNAL) {
        if index + 2 != args.len() {
            return Err("Malformed internal relaunch options".into());
        }
        let descriptor = PathBuf::from(&args[index + 1]);
        let handoff: Handoff =
            serde_json::from_slice(&fs::read(&descriptor).map_err(|e| e.to_string())?)
                .map_err(|e| e.to_string())?;
        validate_handoff(&handoff, &current, &args[..index], &descriptor)?;
        let parent = process(handoff.parent)?;
        let (start, image) = identity(&parent)?;
        if start != handoff.start
            || fs::canonicalize(image).ok() != fs::canonicalize(&handoff.source).ok()
        {
            return Err("Relaunch parent identity changed".into());
        }
        fs::write(
            descriptor.with_extension("ready"),
            handoff.sha256.as_bytes(),
        )
        .map_err(|e| e.to_string())?;
        if unsafe { WaitForSingleObject(parent.0, 30000) } != 0 {
            return Err("Relaunch parent did not exit; no target changes allowed".into());
        }
        ORIGINAL_ROOT
            .set(handoff.target)
            .map_err(|_| "Original installation root already initialized")?;
        CLEANUP
            .set((current, descriptor, handoff.sha256))
            .map_err(|_| "Relaunch cleanup already initialized")?;
        return Ok(false);
    }
    if !current
        .file_name()
        .is_some_and(|name| name == "FrameScope Installer.exe")
    {
        return Ok(false);
    }
    let target = current
        .parent()
        .ok_or("Installed installer lacks target directory")?;
    // A downloaded installer with this filename is not an installed uninstaller.
    if !target.join("FrameScope.exe").is_file()
        && !target.join("FrameScopePayloadManifest.json").is_file()
        && !crate::install_transaction::is_transaction_target(target)?
    {
        return Ok(false);
    }
    let target = target.to_path_buf();
    assert_path(&current)?;
    let id = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_err(|e| e.to_string())?
        .as_nanos();
    let directory =
        std::env::temp_dir().join(format!("framescope-relaunch-{}-{id}", std::process::id()));
    assert_path(&directory)?;
    fs::create_dir(&directory).map_err(|e| e.to_string())?;
    let copy = directory.join("FrameScope Installer.exe");
    fs::copy(&current, &copy).map_err(|e| e.to_string())?;
    let sha256 = hash(&current)?;
    if hash(&copy)? != sha256 {
        return Err("Relaunch copy verification failed".into());
    }
    let parent = process(std::process::id())?;
    let (start, _) = identity(&parent)?;
    let handoff = Handoff {
        parent: std::process::id(),
        start,
        source: current,
        target,
        copy: copy.clone(),
        sha256,
        args: args.clone(),
    };
    let descriptor = directory.join("handoff.json");
    use std::io::Write;
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&descriptor)
        .map_err(|e| e.to_string())?;
    file.write_all(&serde_json::to_vec(&handoff).map_err(|e| e.to_string())?)
        .map_err(|e| e.to_string())?;
    file.sync_all().map_err(|e| e.to_string())?;
    drop(file);
    let mut child = Command::new(&copy)
        .args(&args)
        .arg(INTERNAL)
        .arg(&descriptor)
        .creation_flags(0x08000000)
        .spawn()
        .map_err(|e| e.to_string())?;
    let deadline = Instant::now() + Duration::from_secs(15);
    loop {
        if descriptor.with_extension("ready").exists() {
            if fs::read(descriptor.with_extension("ready")).map_err(|e| e.to_string())?
                != handoff.sha256.as_bytes()
            {
                return Err("Relaunch handshake identity mismatch".into());
            }
            return Ok(true);
        }
        if child.try_wait().map_err(|e| e.to_string())?.is_some() {
            return Err("Relaunch child exited before readiness".into());
        }
        if Instant::now() >= deadline {
            return Err("Relaunch child handshake timeout; no target changes".into());
        }
        std::thread::sleep(Duration::from_millis(25));
    }
}

pub fn schedule_cleanup() -> Result<()> {
    use std::os::windows::process::CommandExt;
    let Some((copy, descriptor, sha)) = CLEANUP.get() else {
        return Ok(());
    };
    let directory = copy.parent().ok_or("Missing relaunch cleanup directory")?;
    assert_path(copy)?;
    assert_path(descriptor)?;
    if !directory
        .file_name()
        .is_some_and(|n| n.to_string_lossy().starts_with("framescope-relaunch-"))
        || descriptor.parent() != Some(directory)
        || hash(copy)? != *sha
    {
        return Err("Relaunch cleanup identity changed".into());
    }
    let owner = process(std::process::id())?;
    let (start, _) = identity(&owner)?;
    let quote = |p: &Path| p.to_string_lossy().replace('\'', "''");
    let script = format!(
        r#"$ErrorActionPreference = 'Stop'
function Assert-Plain([string]$path) {{
  for ($part = $path; $part; $part = [IO.Path]::GetDirectoryName($part)) {{
    if ([IO.File]::Exists($part) -or [IO.Directory]::Exists($part)) {{
      if ([IO.File]::GetAttributes($part) -band [IO.FileAttributes]::ReparsePoint) {{ throw 'Reparse cleanup path' }}
    }}
  }}
}}
$copy = '{copy}'
$descriptor = '{descriptor}'
$directory = '{directory}'
try {{
  $owner = [Diagnostics.Process]::GetProcessById({pid})
  $handle = $owner.Handle
  if ($owner.StartTime.ToUniversalTime().ToFileTimeUtc() -ne {start}) {{ throw 'Cleanup owner start identity changed' }}
  if (-not $owner.WaitForExit(30000)) {{ throw 'Cleanup owner did not exit' }}
}} catch [ArgumentException] {{ }}
Assert-Plain $copy
Assert-Plain $descriptor
Assert-Plain $directory
if ([IO.File]::Exists($copy)) {{
  $algorithm = [Security.Cryptography.SHA256]::Create()
  $stream = [IO.File]::OpenRead($copy)
  try {{ $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }}
  finally {{ $stream.Dispose(); $algorithm.Dispose() }}
  if ($actual -ne '{sha}') {{ throw 'Cleanup copy hash changed' }}
}}
foreach ($path in @($copy, $descriptor, [IO.Path]::ChangeExtension($descriptor, 'ready'))) {{
  Assert-Plain $path
  for ($attempt = 0; [IO.File]::Exists($path) -and $attempt -lt 20; $attempt++) {{
    try {{ [IO.File]::Delete($path) }} catch [IO.IOException] {{ [Threading.Thread]::Sleep(100) }}
  }}
  if ([IO.File]::Exists($path)) {{ throw 'Temporary copy deletion remained locked' }}
}}
[IO.File]::Delete($PSCommandPath)
try {{ [IO.Directory]::Delete($directory, $false) }} catch [IO.IOException] {{ }}
"#,
        copy = quote(copy),
        descriptor = quote(descriptor),
        directory = quote(directory),
        pid = std::process::id()
    );
    let path = directory.join("cleanup.ps1");
    assert_path(&path)?;
    use std::io::Write;
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&path)
        .map_err(|e| e.to_string())?;
    file.write_all(format!("\u{feff}{script}").as_bytes())
        .map_err(|e| e.to_string())?;
    file.sync_all().map_err(|e| e.to_string())?;
    drop(file);
    Command::new("powershell.exe")
        .args([
            "-NoProfile",
            "-NonInteractive",
            "-WindowStyle",
            "Hidden",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
        ])
        .arg(path)
        .creation_flags(0x08000000)
        .spawn()
        .map_err(|e| e.to_string())?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn relaunch_preserves_unicode_silent_options_and_rejects_wrong_copy() {
        let root =
            std::env::temp_dir().join(format!("framescope-relaunch-test-{}", std::process::id()));
        let _ = fs::create_dir(&root);
        let source = root.join("source.exe");
        let copy = root.join("copy.exe");
        fs::write(&source, b"synthetic").unwrap();
        fs::write(&copy, b"synthetic").unwrap();
        let args = vec!["--uninstall".into(), "/S".into(), "/D=C:\\中文 空格".into()];
        let handoff = Handoff {
            parent: 0,
            start: 0,
            target: root.clone(),
            source,
            copy: copy.clone(),
            sha256: hash(&copy).unwrap(),
            args: args.clone(),
        };
        validate_handoff(&handoff, &copy, &args, &root.join("handoff.json")).unwrap();
        assert!(validate_handoff(&handoff, &copy, &[], &root.join("handoff.json")).is_err());
        fs::write(&copy, b"changed").unwrap();
        assert!(validate_handoff(&handoff, &copy, &args, &root.join("handoff.json")).is_err());
    }
    #[test]
    fn resumed_gui_and_silent_uninstall_or_default_upgrade_bind_original_root() {
        for mode in [
            "gui-uninstall",
            "silent-uninstall",
            "gui-default-install",
            "silent-default-install",
        ] {
            let original = PathBuf::from(r"C:\Fixture\Original");
            let external = PathBuf::from(r"C:\Fixture\OtherInstalled");
            let chosen = select_root(Some(original.clone()), || {
                panic!("{mode}: global ARP must not redirect resumed transaction")
            });
            assert_eq!(chosen, Some(original));
            assert_eq!(select_root(None, || Some(external.clone())), Some(external));
        }
    }
}
