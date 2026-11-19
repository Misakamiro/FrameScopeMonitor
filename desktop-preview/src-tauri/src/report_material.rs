use serde_json::{json, Value};
use std::{path::{Path, PathBuf}, sync::Mutex};
use tauri::{Emitter, Manager};


static APPEARANCE_LOCK: Mutex<()> = Mutex::new(());

fn preference_path(window: &tauri::WebviewWindow) -> Result<PathBuf, String> {
    Ok(window.app_handle().path().app_data_dir().map_err(|e| e.to_string())?.join("report-appearance.json"))
}
fn read_preference(path: &Path) -> Result<String, String> {
    let bytes = match std::fs::read(path) {
        Ok(bytes) => bytes,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok("glass".into()),
        Err(e) => return Err(e.to_string()),
    };
    let value: Value = serde_json::from_slice(&bytes).map_err(|e| e.to_string())?;
    match value["appearance"].as_str() {
        Some("glass") => Ok("glass".into()),
        Some("normal") => Ok("normal".into()),
        _ => Err("报告外观配置无效".into()),
    }
}
fn save_preference(path: &Path, preference: &str) -> Result<(), String> {
    if !matches!(preference, "glass" | "normal") { return Err("无效报告外观".into()); }
    if let Some(parent) = path.parent() { std::fs::create_dir_all(parent).map_err(|e| e.to_string())?; }
    framescope_update::atomic_json(path, &json!({"appearance":preference}))
}
pub fn set(window: &tauri::WebviewWindow, preference: &str) -> Result<Value, String> {
    let _guard = APPEARANCE_LOCK.lock().map_err(|e| e.to_string())?;
    save_preference(&preference_path(window)?, preference)?;
    for (label, report) in window.app_handle().webview_windows() {
        if label.starts_with("report-") {
            let _ = report.emit("report-appearance-changed", ());
        }
    }
    Ok(json!({"preference":preference}))
}
pub fn inspect(window: &tauri::WebviewWindow) -> Result<Value, String> {
    let _guard = APPEARANCE_LOCK.lock().map_err(|e| e.to_string())?;
    Ok(json!({"preference":preference_path(window).and_then(|p| read_preference(&p)).unwrap_or_else(|_| "glass".into())}))
}
pub fn refresh(window: &tauri::WebviewWindow) -> Value {
    let Ok(_guard) = APPEARANCE_LOCK.lock() else {
        return json!({"mode":"fallback","preference":"glass","reason":"报告外观状态不可用"});
    };
    match preference_path(window).and_then(|p| read_preference(&p)) {
        Ok(preference) => apply(window, &preference),
        Err(_) => {
            let mut state = apply(window, "normal");
            state["mode"] = json!("fallback");
            state["preference"] = json!("glass");
            state["reason"] = json!("报告外观配置无法读取，暂用普通界面；可重新选择外观");
            state
        }
    }
}
fn apply(window: &tauri::WebviewWindow, preference: &str) -> Value {
    let mut state = apply_native(window, preference);
    state["preference"] = json!(preference);
    state
}

/// Only report windows use this backdrop. Overlay composition is intentionally separate.
fn apply_native(window: &tauri::WebviewWindow, preference: &str) -> Value {
    #[cfg(windows)]
    {
        use std::{ffi::c_void, mem::size_of, ptr::null_mut};
        use windows_sys::Win32::{
            Graphics::Dwm::{DwmGetWindowAttribute, DwmSetWindowAttribute, DWMWA_SYSTEMBACKDROP_TYPE},
            System::Registry::{RegGetValueW, HKEY_CURRENT_USER, RRF_RT_REG_DWORD},
            UI::WindowsAndMessaging::SystemParametersInfoW,
        };
        #[repr(C)]
        struct Contrast { size: u32, flags: u32, scheme: *mut u16 }
        let mut contrast = Contrast { size: size_of::<Contrast>() as u32, flags: 0, scheme: null_mut() };
        let contrast_read = unsafe { SystemParametersInfoW(0x0042, contrast.size, &mut contrast as *mut _ as *mut c_void, 0) } != 0;
        let key: Vec<u16> = "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize\0".encode_utf16().collect();
        let name: Vec<u16> = "EnableTransparency\0".encode_utf16().collect();
        let mut enabled = 1u32;
        let mut size = 4u32;
        let read = unsafe { RegGetValueW(HKEY_CURRENT_USER, key.as_ptr(), name.as_ptr(), RRF_RT_REG_DWORD, null_mut(), &mut enabled as *mut _ as *mut c_void, &mut size) };
        let reason = if preference == "normal" { Some("普通界面 · 深色背景") }
            else if !contrast_read { Some("系统辅助功能状态无法读取") }
            else if contrast.flags & 1 != 0 { Some("Windows 高对比度已开启") }
            else if read == 0 && enabled == 0 { Some("Windows 透明效果已关闭") }
            else { None };
        let Ok(hwnd) = window.hwnd() else { return json!({"mode":"fallback","reason":"窗口材质不可用"}); };
        let backdrop: u32 = if reason.is_some() { 1 } else { 3 };
        let mut current = 0u32;
        let read_backdrop = unsafe { DwmGetWindowAttribute(hwnd.0 as _, DWMWA_SYSTEMBACKDROP_TYPE as u32, &mut current as *mut _ as *mut c_void, 4) };
        let result = if read_backdrop >= 0 && current == backdrop { 0 } else {
            unsafe { DwmSetWindowAttribute(hwnd.0 as _, DWMWA_SYSTEMBACKDROP_TYPE as u32, &backdrop as *const _ as *const c_void, 4) }
        };
        if let Some(reason) = reason { return json!({"mode":if preference == "normal" {"normal"} else {"fallback"},"reason":reason}); }
        if result < 0 { return json!({"mode":"fallback","reason":"当前系统不支持报告 Acrylic 材质"}); }
        json!({"mode":"glass","reason":"Windows Acrylic · 实时窗口背景"})
    }
    #[cfg(not(windows))]
    json!({"mode":"fallback","reason":"当前平台使用深色报告材质"})
}
