//! PawnIO driver detection / install / uninstall (NexBox pawnio_driver.rs
//! semantics). Detection reads the uninstall registry key; install expects
//! the official `PawnIO_setup.exe` to sit in `dependencies\PawnIO\` next to
//! the deployed executables and runs it elevated via PowerShell. The setup
//! package is NOT redistributed in the current build — install/uninstall
//! fail with a clear message until it is deployed.

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
    let (installed, version) = read_registry();
    if installed {
        return Ok(json!({"result": "already_installed", "version": version}));
    }
    let package = setup_package().ok_or_else(|| {
        "PawnIO 安装包未随当前构建分发：请将官方 PawnIO_setup.exe 放置到程序目录 dependencies\\PawnIO\\ 后重试".to_string()
    })?;
    run_elevated(&package, &["/install", "/silent"])?;
    let (installed, version) = read_registry();
    if !installed {
        return Err("PawnIO 安装程序已运行，但注册表中未检测到安装记录".into());
    }
    Ok(json!({"result": "success", "version": version}))
}

pub fn uninstall() -> Result<Value, String> {
    let (installed, version) = read_registry();
    if !installed {
        return Ok(json!({"result": "already_uninstalled"}));
    }
    let _ = version;
    let package = setup_package().ok_or_else(|| {
        "PawnIO 安装包未随当前构建分发：无法定位卸载程序".to_string()
    })?;
    run_elevated(&package, &["/uninstall", "/silent"])?;
    Ok(json!({"result": "success"}))
}

/// Runs the installer elevated and waits (UAC prompt goes to the user).
#[cfg(windows)]
fn run_elevated(package: &std::path::Path, args: &[&str]) -> Result<(), String> {
    let mut ps = std::process::Command::new("powershell");
    let argument_list = args
        .iter()
        .map(|arg| format!("'{arg}'"))
        .collect::<Vec<_>>()
        .join(" ");
    ps.arg("-NoProfile")
        .arg("-Command")
        .arg(format!(
            "Start-Process -FilePath '{}' -ArgumentList {} -Verb RunAs -Wait",
            package.display(),
            argument_list
        ));
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        ps.creation_flags(0x0800_0000);
    }
    let status = ps
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
