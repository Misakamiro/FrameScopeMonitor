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

/// Everything the main app chain runs as; all of them lock files in the
/// install directory and must be dead before extraction.
const APP_PROCESS_NAMES: &[&str] = &[
    "FrameScope.exe",
    "FrameScopeHardwareService.exe",
    "FrameScopeHardwareProvider.exe",
    "FrameScopeMonitor.exe",
    "FrameScopeProcessSampler.exe",
    "FrameScopeSystemSampler.exe",
    "FrameScopeReportGenerator.exe",
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

/// Force-stop every FrameScope process so payload files are not locked.
pub fn kill_running_app() {
    for name in APP_PROCESS_NAMES {
        let _ = Command::new("taskkill")
            .args(["/F", "/T", "/IM", name])
            .creation_flags(CREATE_NO_WINDOW)
            .status();
    }
    // Let handles drain before extraction starts.
    std::thread::sleep(std::time::Duration::from_millis(600));
}

fn is_app_running() -> bool {
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("tasklist")
            .args(["/FI", "IMAGENAME eq FrameScope.exe", "/NH"])
            .creation_flags(CREATE_NO_WINDOW)
            .output();
        if let Ok(out) = output {
            return String::from_utf8_lossy(&out.stdout).contains("FrameScope.exe");
        }
    }
    false
}

#[tauri::command]
pub fn is_app_running_cmd() -> bool {
    is_app_running()
}

// === Install ===

/// The ARP UninstallString points at the installer exe INSIDE the install
/// directory (single-binary dual mode), so every install must place a copy of
/// itself there - the running installer's own file is the source.
fn place_uninstaller(target: &Path) -> Result<(), String> {
    let self_exe = std::env::current_exe().map_err(|e| format!("无法获取安装器自身路径: {e}"))?;
    fs::copy(&self_exe, target.join("FrameScope Installer.exe"))
        .map_err(|e| format!("无法放置卸载器: {e}"))?;
    Ok(())
}

#[tauri::command]
pub fn install(
    app: tauri::AppHandle,
    target_dir: String,
    create_desktop_shortcut: bool,
    autostart: bool,
) -> Result<(), String> {
    let target = PathBuf::from(&target_dir);
    if !target.is_absolute() {
        return Err("安装路径必须是绝对路径".into());
    }

    kill_running_app();

    // Migrate away from a legacy NSIS install: its uninstaller exe/registry
    // entry would survive beside ours as a ghost entry.
    {
        let state = get_state();
        if state.installed && state.legacy_nsis {
            cleanup_legacy_nsis(&state.install_path);
        }
    }

    fs::create_dir_all(&target).map_err(|e| format!("无法创建目标目录: {}", e))?;

    let total = payload_file_count()?;
    let app2 = app.clone();
    extract_payload(&target, &move |index, name| {
        use tauri::Emitter;
        let _ = app2.emit(
            "install-progress",
            ProgressEvent { index, total, name: name.to_string() },
        );
    })?;

    place_uninstaller(&target)?;

    register_uninstall(&target_dir)
        .map_err(|e| format!("无法注册卸载信息: {}", e))?;

    // Start menu shortcut (always) + desktop shortcut (optional).
    let exe_path = target.join("FrameScope.exe");
    delete_existing_shortcuts();
    if let Err(e) = create_lnk_shortcut(PRODUCT_NAME, &exe_path, "StartMenu") {
        eprintln!("创建开始菜单快捷方式失败 (非致命): {e}");
    }
    if create_desktop_shortcut {
        if let Err(e) = create_lnk_shortcut(PRODUCT_NAME, &exe_path, "Desktop") {
            eprintln!("创建桌面快捷方式失败 (非致命): {e}");
        }
    }

    // Autostart is a per-user HKCU Run entry, matching the main app's own
    // toggles that also live in HKCU.
    if autostart {
        set_autostart(&exe_path.display().to_string());
    }

    Ok(())
}

/// Silent-mode install used by `/S` (no window; called before Tauri starts).
pub fn install_headless(target_dir: String, desktop_shortcut: bool, autostart: bool) -> Result<(), String> {
    let target = PathBuf::from(&target_dir);
    kill_running_app();
    {
        let state = get_state();
        if state.installed && state.legacy_nsis {
            cleanup_legacy_nsis(&state.install_path);
        }
    }
    fs::create_dir_all(&target).map_err(|e| format!("无法创建目标目录: {e}"))?;
    extract_payload(&target, &|_, _| {})?;
    place_uninstaller(&target)?;
    register_uninstall(&target_dir).map_err(|e| format!("无法注册卸载信息: {e}"))?;
    let exe_path = target.join("FrameScope.exe");
    delete_existing_shortcuts();
    let _ = create_lnk_shortcut(PRODUCT_NAME, &exe_path, "StartMenu");
    if desktop_shortcut {
        let _ = create_lnk_shortcut(PRODUCT_NAME, &exe_path, "Desktop");
    }
    if autostart {
        set_autostart(&exe_path.display().to_string());
    }
    Ok(())
}

// === Uninstall ===

#[tauri::command]
pub fn uninstall(delete_data: bool) -> Result<(), String> {
    let (install_dir, _, _) = find_existing_install()
        .ok_or("未找到已安装的 FrameScope")?;
    perform_uninstall(Path::new(&install_dir), delete_data)
}

/// Headless uninstall for `--uninstall /S`.
pub fn uninstall_headless(delete_data: bool) -> Result<(), String> {
    let (install_dir, _, _) = find_existing_install().ok_or("未找到已安装的 FrameScope")?;
    perform_uninstall(Path::new(&install_dir), delete_data)
}

/// Shared uninstall body. The installer exe usually LIVES in the install
/// directory, so "delete everything" is done as: delete all children except
/// the running installer, then hand the final file + directory removal to a
/// hidden PowerShell that waits for us to exit.
fn perform_uninstall(install_dir: &Path, delete_data: bool) -> Result<(), String> {
    kill_running_app();

    let self_exe = std::env::current_exe().map_err(|e| e.to_string())?;

    // Remove directory children, sparing the running installer binary (and
    // its own trace files). Anything locked is retried by the cleanup script.
    if let Ok(entries) = fs::read_dir(install_dir) {
        for entry in entries.flatten() {
            let p = entry.path();
            if p == self_exe {
                continue;
            }
            if p.is_dir() {
                let _ = fs::remove_dir_all(&p);
            } else {
                let _ = fs::remove_file(&p);
            }
        }
    }

    // Shortcuts + autostart.
    delete_existing_shortcuts();
    remove_autostart();

    // ARP registration.
    #[cfg(target_os = "windows")]
    {
        let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
        let _ = hklm.delete_subkey_all(arp_key());
        // Remove the legacy NSIS key too, if this uninstall is servicing it.
        for view in [
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ] {
            if let Ok(root) = hklm.open_subkey_with_flags(view, KEY_ALL_ACCESS) {
                let ghosts: Vec<String> = root
                    .enum_keys()
                    .filter_map(|k| k.ok())
                    .filter(|k| {
                        if let Ok(sub) = root.open_subkey_with_flags(k, KEY_READ) {
                            return sub.get_value::<String, _>("DisplayName").unwrap_or_default() == PRODUCT_NAME;
                        }
                        false
                    })
                    .collect();
                for k in ghosts {
                    let _ = root.delete_subkey_all(&k);
                }
            }
        }
    }

    // Optional data wipe: the app keeps config/history next to its exe
    // (self-contained layout) plus traces under the identifier dir.
    if delete_data {
        for rel in ["framescope-config.json", "history", "state", "reports", "tools"] {
            let p = install_dir.join(rel);
            if p.is_dir() {
                let _ = fs::remove_dir_all(&p);
            } else {
                let _ = fs::remove_file(&p);
            }
        }
        for base in [dirs::data_dir(), dirs::config_dir(), dirs::cache_dir()] {
            if let Some(base) = base {
                let p = base.join("com.framescope.ui-preview");
                if p.exists() {
                    let _ = fs::remove_dir_all(&p);
                }
            }
        }
    }

    schedule_self_cleanup(&self_exe, install_dir);
    Ok(())
}

/// Hidden PowerShell that waits for the installer to exit, deletes its exe and
/// the leftover install directory, then deletes itself (nexbox-proven method).
fn schedule_self_cleanup(self_exe: &Path, install_dir: &Path) {
    let exe_str = self_exe.display().to_string();
    let dir_str = install_dir.display().to_string();
    let script = format!(
        "$exe = '{}'\r\n$dir = '{}'\r\nwhile (Test-Path -LiteralPath $exe) {{\r\n  try {{ Remove-Item -Force -LiteralPath $exe -Recurse -ErrorAction Stop }} catch {{ Start-Sleep -Milliseconds 500 }}\r\n}}\r\nif (Test-Path -LiteralPath $dir) {{\r\n  try {{ Remove-Item -Force -LiteralPath $dir -Recurse -ErrorAction Stop }} catch {{ }}\r\n}}\r\nRemove-Item -Force -LiteralPath $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue\r\n",
        exe_str.replace('\'', "''"),
        dir_str.replace('\'', "''"),
    );
    let script_path = std::env::temp_dir().join("framescope_uninstall_cleanup.ps1");
    if fs::write(&script_path, script).is_ok() {
        let _ = Command::new("powershell.exe")
            .args([
                "-NoProfile",
                "-NonInteractive",
                "-WindowStyle", "Hidden",
                "-ExecutionPolicy", "Bypass",
                "-File",
                script_path.to_str().unwrap_or_default(),
            ])
            .creation_flags(CREATE_NO_WINDOW)
            .spawn();
    }
}

// === Payload extraction ===

fn payload_file_count() -> Result<u32, String> {
    let cursor = std::io::Cursor::new(PAYLOAD_ZIP);
    let mut archive = zip::ZipArchive::new(cursor).map_err(|e| format!("无法读取安装包: {e}"))?;
    Ok(archive.len() as u32)
}

/// Extract the embedded payload, reporting each finished entry through the
/// callback (windowed mode forwards it to the UI as an event; silent mode
/// passes a no-op).
fn extract_payload(target_dir: &Path, on_entry: &dyn Fn(u32, &str)) -> Result<(), String> {
    let cursor = std::io::Cursor::new(PAYLOAD_ZIP);
    let mut archive = zip::ZipArchive::new(cursor).map_err(|e| format!("无法读取安装包: {e}"))?;
    for i in 0..archive.len() {
        let mut file = archive.by_index(i).map_err(|e| format!("读取文件失败: {e}"))?;
        let name = file.name().to_string();
        let outpath = target_dir.join(&name);
        if name.ends_with('/') {
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

// === Registry ===

fn register_uninstall(install_dir: &str) -> Result<(), String> {
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

/// Remove a legacy NSIS install's uninstaller artifacts so the two packages
/// don't leave ghost entries behind. File cleanup happens via kill+extract.
fn cleanup_legacy_nsis(legacy_install_dir: &str) {
    kill_running_app();
    let dir = Path::new(legacy_install_dir);
    for name in ["uninstall.exe"] {
        let _ = fs::remove_file(dir.join(name));
    }
    #[cfg(target_os = "windows")]
    {
        let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
        for view in [
            r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            r"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ] {
            if let Ok(root) = hklm.open_subkey_with_flags(view, KEY_ALL_ACCESS) {
                let ghosts: Vec<String> = root
                    .enum_keys()
                    .filter_map(|k| k.ok())
                    .filter(|k| k != REG_KEY_NAME)
                    .filter(|k| {
                        if let Ok(sub) = root.open_subkey_with_flags(k, KEY_READ) {
                            let display = sub.get_value::<String, _>("DisplayName").unwrap_or_default();
                            let loc = sub.get_value::<String, _>("InstallLocation").unwrap_or_default();
                            return display == PRODUCT_NAME || (loc == legacy_install_dir && !loc.is_empty());
                        }
                        false
                    })
                    .collect();
                for k in ghosts {
                    let _ = root.delete_subkey_all(&k);
                }
            }
        }
    }
}

// === Shortcuts ===

fn create_lnk_shortcut(name: &str, target_exe: &Path, location: &str) -> Result<(), String> {
    let folder = get_special_folder_path(location).ok_or("无法获取系统目录路径")?;
    let shortcut_path = Path::new(&folder).join(format!("{name}.lnk"));
    let workdir = target_exe.parent().unwrap_or(Path::new(""));
    let mut sl = mslnk::ShellLink::new(target_exe).map_err(|e| format!("创建快捷方式失败: {e}"))?;
    sl.set_working_dir(Some(workdir.to_string_lossy().to_string()));
    sl.create_lnk(&shortcut_path).map_err(|e| format!("保存快捷方式失败: {e}"))?;
    Ok(())
}

fn get_special_folder_path(folder: &str) -> Option<String> {
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

fn delete_existing_shortcuts() {
    for location in ["Desktop", "StartMenu"] {
        if let Some(folder) = get_special_folder_path(location) {
            let path = Path::new(&folder).join(format!("{PRODUCT_NAME}.lnk"));
            if path.exists() {
                let _ = fs::remove_file(&path);
            }
        }
    }
}

// === Autostart ===

fn set_autostart(exe_path: &str) {
    #[cfg(target_os = "windows")]
    {
        if let Ok(hkcu) = RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags(r"Software\Microsoft\Windows\CurrentVersion\Run", KEY_SET_VALUE) {
            let _ = hkcu.set_value(PRODUCT_NAME, &format!("\"{exe_path}\""));
        }
    }
}

fn remove_autostart() {
    #[cfg(target_os = "windows")]
    {
        if let Ok(hkcu) = RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags(r"Software\Microsoft\Windows\CurrentVersion\Run", KEY_SET_VALUE) {
            let _ = hkcu.delete_value(PRODUCT_NAME);
        }
    }
}

// === Disk space ===

fn available_space(path: &Path) -> Result<u64, String> {    let path_str = path.display().to_string();
    let drive_letter = path_str
        .chars()
        .next()
        .filter(|c| c.is_ascii_alphabetic())
        .ok_or("无效的路径")?;
    #[cfg(target_os = "windows")]
    {
        let root_path = format!("{drive_letter}:\\");
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
