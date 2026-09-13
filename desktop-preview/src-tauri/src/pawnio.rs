//! PawnIO driver detection / install / uninstall (NexBox pawnio_driver.rs
//! semantics). Detection reads the uninstall registry key; install expects
//! the official `PawnIO_setup.exe` (dash arguments: -install/-uninstall
//! -silent) to sit in `dependencies\PawnIO\` next to the deployed
//! executables and runs it directly — the requireAdministrator manifest
//! makes the child inherit the elevated token. The setup package is staged
//! from `packaging\PawnIO_setup.exe` by build.ps1 and Package-Installer.ps1
//! into both installer payloads.

use serde_json::{json, Value};

const UNINSTALL_KEY: &str = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

/// `{installed, version}` from the PawnIO uninstall registry entry.
pub fn status() -> Value {
    let (installed, version) = read_registry();
    json!({"installed": installed, "version": version})
}

#[cfg(windows)]
fn read_registry() -> (bool, String) {
    use windows_sys::Win32::System::Registry::{
        RegCloseKey, RegGetValueA, RegOpenKeyExA, HKEY, HKEY_LOCAL_MACHINE, KEY_READ,
        RRF_RT_REG_SZ,
    };
    const ERROR_SUCCESS: u32 = 0;
    unsafe {
        let full = concat!(
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
            "\0"
        );
        let mut key: HKEY = std::ptr::null_mut();
        if RegOpenKeyExA(HKEY_LOCAL_MACHINE, full.as_ptr(), 0, KEY_READ, &mut key)
            != ERROR_SUCCESS
        {
            return (false, String::new());
        }
        let mut buffer = [0u8; 256];
        let mut size = buffer.len() as u32;
        let mut value_type = 0u32;
        let version = if RegGetValueA(
            key,
            std::ptr::null(),
            concat!("DisplayVersion", "\0").as_ptr(),
            RRF_RT_REG_SZ,
            &mut value_type,
            buffer.as_mut_ptr() as *mut core::ffi::c_void,
            &mut size,
        ) == ERROR_SUCCESS
            && size > 0
        {
            String::from_utf8_lossy(&buffer[..(size as usize - 1).min(255)])
                .trim_end_matches('\0')
                .to_string()
        } else {
            String::new()
        };
        let _ = RegCloseKey(key);
        (true, version)
    }
}

#[cfg(not(windows))]
fn read_registry() -> (bool, String) {
    (false, String::new())
}

/// Locates the official setup package next to the deployed executables.
fn setup_package() -> Option<std::path::PathBuf> {
    let root = crate::backend::backend_root().ok()?;
    let candidate = root.join("dependencies").join("PawnIO").join("PawnIO_setup.exe");
    candidate.exists().then_some(candidate)
}

pub fn install() -> Result<Value, String> {
    // Stop the sampling service first: the driver load while LibreHardwareMonitor
    // is polling full sensor sets is what crashed the WebView2 renderer (the
    // "black window" report). The next service call respawns it fresh.
    stop_sampling_service();
    let (installed, version) = read_registry();
    if installed {
        return Ok(json!({"result": "already_installed", "version": version}));
    }
    let package = setup_package().ok_or_else(|| {
        "PawnIO 安装包未随当前构建分发：请将官方 PawnIO_setup.exe 放置到程序目录 dependencies\\PawnIO\\ 后重试".to_string()
    })?;
    run_elevated(&package, &["-install", "-silent"])?;
    let (installed, version) = read_registry();
    if !installed {
        return Err("PawnIO 安装程序已运行，但注册表中未检测到安装记录".into());
    }
    Ok(json!({"result": "success", "version": version}))
}

pub fn uninstall() -> Result<Value, String> {
    stop_sampling_service();
    let (installed, version) = read_registry();
    if !installed {
        return Ok(json!({"result": "already_uninstalled"}));
    }
    let _ = version;
    let package = setup_package().ok_or_else(|| {
        "PawnIO 安装包未随当前构建分发：无法定位卸载程序".to_string()
    })?;
    run_elevated(&package, &["-uninstall", "-silent"])?;
    Ok(json!({"result": "success"}))
}

/// Kills the hardware service and its provider child so the driver load does
/// not race the 1 Hz sensor polling. Callers reconnect lazily afterwards.
fn stop_sampling_service() {
    use std::process::{Command, Stdio};
    for name in ["FrameScopeHardwareService.exe", "FrameScopeHardwareProvider.exe"] {
        let mut command = Command::new("taskkill");
        command.arg("/F").arg("/IM").arg(name);
        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            command.creation_flags(0x0800_0000);
        }
        let _ = command.stdout(Stdio::null()).stderr(Stdio::null()).status();
    }
}

/// Runs the driver setup and waits for it to finish. The main app carries a
/// requireAdministrator manifest, so the child inherits the already-elevated
/// token — spawn the setup directly instead of going through a PowerShell
/// command string (no shell parsing, no quoting/injection surface).
#[cfg(windows)]
fn run_elevated(package: &std::path::Path, args: &[&str]) -> Result<(), String> {
    let mut command = std::process::Command::new(package);
    command.args(args);
    use std::os::windows::process::CommandExt;
    command.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    let status = command
        .status()
        .map_err(|error| format!("failed to launch the installer: {error}"))?;
    if !status.success() {
        return Err(format!("installer exited with {}", status.code().unwrap_or(-1)));
    }
    Ok(())
}

#[cfg(not(windows))]
fn run_elevated(_package: &std::path::Path, _args: &[&str]) -> Result<(), String> {
    Err("unsupported platform".into())
}
