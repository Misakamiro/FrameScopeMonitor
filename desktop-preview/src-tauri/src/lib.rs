#[cfg(test)]
mod tests;
mod backend;
mod bounded_process;
mod host_shutdown;
mod hardware_service;
mod runtime_uiaccess;
mod updater;
mod delta_password;
mod overlay_panel;
mod pawnio;
#[cfg(windows)]
mod window_icons;
use serde_json::{json, Value};
use std::sync::Mutex;
use tauri::{Emitter, Manager, WebviewUrl, WebviewWindowBuilder};

static OVERLAY_OPERATION: Mutex<()> = Mutex::new(());
static SHUTDOWN_COMPLETE: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

fn request_shutdown(app: &tauri::AppHandle) {
    if !backend::begin_shutdown() { return; }
    let app = app.clone();
    std::thread::spawn(move || {
        let _overlay = OVERLAY_OPERATION.lock().unwrap_or_else(|p|p.into_inner());
        let result = backend::shutdown_owned_monitor()
            .map_err(|error| format!("停止监控和报告收尾失败：{error}"))
            .and_then(|_| stop_windows(&app).map_err(|error| format!("保存悬浮窗设置或关闭悬浮窗失败：{error}")))
            .and_then(|_| hardware_service::stop_connected_service().map_err(|error| format!("停止硬件服务失败：{error}")));
        match result {
            Ok(_) => {
                SHUTDOWN_COMPLETE.store(true, std::sync::atomic::Ordering::SeqCst);
                app.exit(0);
            }
            Err(error) => {
                backend::cancel_shutdown();
                log::warn!("退出已取消: {error}");
                let _ = app.emit("app-exit-failed", &error);
                if let Some(window) = app.get_webview_window("main") { let _ = window.show(); }
                use tauri_plugin_dialog::DialogExt;
                app.dialog().message(error).title("FrameScope 退出已取消").show(|_| {});
            }
        }
    });
}
const COMMANDS: &[&str] = &[
    "build_info", "update_status", "update_ready", "update_preferences", "update_check", "update_download", "update_cancel", "update_install", "update_restore",
    "preview_set_hardware", "open_sensor_monitor", "start_overlay_panel", "stop_overlay_panel",
    "toggle_overlay_panel", "get_overlay_panel_status", "update_overlay_settings", "get_overlay_current_settings",
    "set_overlay_drag_mode", "reset_overlay_position", "open_vertical_overlay", "stop_vertical_overlay",
    "set_vertical_overlay_click_through", "resize_vertical_overlay", "save_vertical_overlay_position",
    "reset_vertical_overlay_position", "vertical_overlay_ready", "minimize_to_tray", "exit_app",
    "get_overlay_hotkey", "set_overlay_hotkey", "export_hardware_report", "open_report_sample",
    // Real hardware data, served by FrameScopeHardwareService over the named pipe.
    "get_hardware", "get_hardware_info", "get_all_sensors", "get_overlay_hardware_data",
    "get_memory_status", "get_disk_status", "is_nvidia_gpu", "set_active_gpu_index",
    "clear_hardware_data", "get_system_locale", "get_system_username",
    // Real capture pipeline (FrameScopeMonitor.exe chain, same file contracts
    // as the C# WebView2 bridge).
    "targets_get", "targets_save", "processes_refresh", "config_save", "config_read",
    "monitor_start", "monitor_stop", "monitor_status",
    "reports_list", "report_open", "report_open_directory", "report_regenerate", "report_delete",
    // PawnIO driver detection / install / uninstall.
    "check_pawnio_status", "install_pawnio_driver", "uninstall_pawnio_driver",
    "restart_monitor_process",
    // Delta Force password doors (community API, 60s cache).
    "get_delta_passwords",
];
fn ensure_allowed(command: &str) -> Result<(), String> {
    if COMMANDS.contains(&command) { Ok(()) } else { Err(format!("Unsupported preview command: {command}")) }
}
fn sensor_response_error<'a>(response: &'a Value, fallback: &'a str) -> &'a str {
    response["errors"].as_array().and_then(|errors| errors.first()).and_then(Value::as_str).unwrap_or(fallback)
}

fn decode_fixture(value: Value) -> Result<overlay_panel::OverlayHardwareData, String> {
    let object = value.as_object().ok_or("Fixture must be an object")?;
    let defaults = serde_json::to_value(overlay_panel::OverlayHardwareData::default()).unwrap();
    for key in object.keys() {
        if defaults.get(key).is_none() { return Err(format!("Unknown fixture field: {key}")); }
    }
    let mut merged = defaults;
    for (key, value) in object { merged[key] = value.clone(); }
    if let Some(gpus) = merged["gpu_sensors"].as_array_mut() {
        for gpu in gpus {
            let mut base = serde_json::to_value(overlay_panel::GpuSensorData::default()).unwrap();
            for (key, value) in gpu.as_object().ok_or("GPU must be an object")? { base[key] = value.clone(); }
            *gpu = base;
        }
    }
    let parsed: overlay_panel::OverlayHardwareData = serde_json::from_value(merged).map_err(|e| e.to_string())?;
    if parsed.active_gpu_index >= parsed.gpu_sensors.len().max(1) { return Err("GPU index out of range".into()); }
    Ok(parsed)
}
fn open_sensor(app: &tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("sensor-monitor") {
        window.show().map_err(|e| e.to_string())?;
        return window.set_focus().map_err(|e| e.to_string());
    }
    let mut builder = WebviewWindowBuilder::new(app, "sensor-monitor", WebviewUrl::App("sensor-monitor".into()))
        .title("FrameScope UI Preview - Sensors").inner_size(1000.0, 720.0)
        .min_inner_size(760.0, 480.0).resizable(true).always_on_top(true);
    if let Some(args) = app.config().app.windows.iter().find(|w| w.label == "main")
        .and_then(|w| w.additional_browser_args.as_deref())
    {
        builder = builder.additional_browser_args(args);
    }
    builder.build().map(|_| ()).map_err(|e| e.to_string())
}
fn open_vertical(app: &tauri::AppHandle) -> Result<(), String> {
    backend::ensure_not_closing()?;
    overlay_panel::stop_overlay()?;
    if let Some(window) = app.get_webview_window("vertical-overlay") {
        if VERTICAL_ACTIVE.load(std::sync::atomic::Ordering::SeqCst) {
            return window.show().map_err(|e| e.to_string());
        }
        let _ = window.destroy();
        for _ in 0..100 {
            if app.get_webview_window("vertical-overlay").is_none() { break; }
            std::thread::sleep(std::time::Duration::from_millis(10));
        }
        if app.get_webview_window("vertical-overlay").is_some() {
            return Err("旧竖排悬浮窗尚未销毁".into());
        }
    }
    let settings = overlay_panel::get_or_init_settings();
    let mut builder = WebviewWindowBuilder::new(app, "vertical-overlay", WebviewUrl::App("vertical-overlay".into()))
        .title("FrameScope UI Preview - Overlay").inner_size(240.0, 420.0)
        .decorations(false).transparent(true).always_on_top(true).skip_taskbar(true).resizable(false)
        // Windows: undecorated windows default to a DWM shadow, and that shadow
        // forces an opaque compositor backdrop - the transparency slider never
        // reaches the screen without shadow(false) (NexBox upstream does the same).
        .shadow(false).focused(false)
        ;
    // Windows sharing one WebView2 profile must use identical environment arguments.
    if let Some(args) = app.config().app.windows.iter().find(|w| w.label == "main")
        .and_then(|w| w.additional_browser_args.as_deref())
    {
        builder = builder.additional_browser_args(args);
    }
    if let (Some(x), Some(y)) = (settings.vertical_position_x, settings.vertical_position_y) {
        builder = builder.position(x as f64, y as f64);
    }
    let built = builder.build();
    log::info!("UIAccess acceptance: original vertical build {:?}", built.as_ref().map(|window| window.label()));
    let window = built.map_err(|e| e.to_string())?;
    let mut ready = false;
    for _ in 0..200 {
        if window.hwnd().is_ok() && window.is_visible().unwrap_or(false) { ready = true; break; }
        std::thread::sleep(std::time::Duration::from_millis(10));
    }
    if !ready {
        let _ = window.destroy();
        return Err("竖排悬浮窗初始化失败，请检查 WebView2 日志".into());
    }
    overlay_set_hit_test_transparent(&window, true);
    #[cfg(windows)]
    apply_vertical_backdrop_kill(&window);
    VERTICAL_ACTIVE.store(true, std::sync::atomic::Ordering::SeqCst);
    Ok(())
}

/// The tao patch (vendor/tao) gives this window WS_EX_NOREDIRECTIONBITMAP for
/// clean per-pixel alpha. Without the attribute below, DWM still composes a
/// default system backdrop UNDER such a window: a uniform ~11% black veil
/// (measured 227/255 over white with DOM alpha 0; opaque pixels stay exact).
/// DWMSBT_NONE removes it - plate pixels then match the DOM rgba() math
/// exactly (0% -> 255, 50% -> 136, 100% -> 17 over a white backdrop).
#[cfg(windows)]
fn apply_vertical_backdrop_kill(window: &tauri::WebviewWindow) {
    use windows_sys::Win32::Graphics::Dwm::{DwmSetWindowAttribute, DWMWA_SYSTEMBACKDROP_TYPE};
    if let Ok(hwnd) = window.hwnd() {
        unsafe {
            let dwmsbt_none: u32 = 1;
            let _ = DwmSetWindowAttribute(
                hwnd.0 as _,
                DWMWA_SYSTEMBACKDROP_TYPE as u32,
                &dwmsbt_none as *const u32 as *const core::ffi::c_void,
                4,
            );
        }
    }
}

/// Click-through for the vertical overlay. Two facts drive this design:
/// 1. WS_EX_TRANSPARENT alone (non-layered) does NOT skip mouse input - the
///    WebView2 child HWND still receives clicks (measured; matches Wails
///    wailsapp/wails#5812). True pass-through needs the layered pair:
///    WS_EX_LAYERED|WS_EX_TRANSPARENT + SetLayeredWindowAttributes(LWA_ALPHA,255).
/// 2. LAYERED used to paint a frosted base over the per-pixel webview (the
///    old frosted-sand bug), but that was the blur-behind-era rendering
///    stack. With WS_EX_NOREDIRECTIONBITMAP (the vendored-tao patch) the
///    layered pair composes cleanly - pixel samples are unchanged.
/// Interactive (drag) mode strips both bits so the panel takes input.
#[cfg(windows)]
fn overlay_set_hit_test_transparent(window: &tauri::WebviewWindow, transparent: bool) {
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        GetWindowLongW, SetLayeredWindowAttributes, SetWindowLongW, GWL_EXSTYLE, LWA_ALPHA,
        WS_EX_LAYERED, WS_EX_TRANSPARENT,
    };
    if let Ok(hwnd) = window.hwnd() {
        unsafe {
            let ex = GetWindowLongW(hwnd.0 as _, GWL_EXSTYLE);
            let next = if transparent {
                // alpha 255 = no visual fade, pass-through only
                SetLayeredWindowAttributes(hwnd.0 as _, 0, 255, LWA_ALPHA);
                ex | WS_EX_LAYERED as i32 | WS_EX_TRANSPARENT as i32
            } else {
                ex & !(WS_EX_LAYERED as i32) & !(WS_EX_TRANSPARENT as i32)
            };
            SetWindowLongW(hwnd.0 as _, GWL_EXSTYLE, next);
        }
    }
}

/// Window-layer effects (SetLayeredWindowAttributes colorkey/alpha, DWM
/// blur-behind) are intentionally NOT used for the vertical overlay:
/// - LWA_COLORKEY cannot key the WebView2's pixels (separate child HWND),
///   but adding WS_EX_LAYERED for it switches DWM to the layered-composition
///   path, which paints a permanent frosted base over the per-pixel-alpha
///   webview - the "0% still frosted" regression.
/// - LWA_ALPHA fades the whole window including the text.
/// - DWM blur-behind draws frost even at zero tint on Win11 24H2.
/// The plate therefore lives entirely in the DOM: the page paints
/// rgba(17,17,17,opacity/255) and the transparent window lets it through
/// per-pixel. Only WS_EX_TRANSPARENT is ever toggled on the HWND.

static VERTICAL_ACTIVE: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

fn stop_windows(app: &tauri::AppHandle) -> Result<(), String> {
    save_overlay_position(app)?;
    VERTICAL_ACTIVE.store(false, std::sync::atomic::Ordering::SeqCst);
    overlay_panel::stop_overlay()?;
    if let Some(window) = app.get_webview_window("vertical-overlay") {
        let _ = window.hide();
        window.destroy().map_err(|e| e.to_string())?;
    }
    Ok(())
}
fn active(app: &tauri::AppHandle) -> bool {
    overlay_panel::is_overlay_active() || (VERTICAL_ACTIVE.load(std::sync::atomic::Ordering::SeqCst)
        && app.get_webview_window("vertical-overlay").map(|window| window.is_visible().unwrap_or(false)).unwrap_or(false))
}
fn start_selected(app: &tauri::AppHandle) -> Result<(), String> {
    let settings = overlay_panel::get_or_init_settings();
    if settings.style == "vertical_panel" { open_vertical(app) }
    else {
        VERTICAL_ACTIVE.store(false, std::sync::atomic::Ordering::SeqCst);
        if let Some(window) = app.get_webview_window("vertical-overlay") { let _ = window.close(); }
        overlay_panel::start_overlay(settings).map(|_| ())
    }
}
fn save_native_settings(app: &tauri::AppHandle) -> Result<(), String> {
    let dir = app.path().app_data_dir().map_err(|e| e.to_string())?;
    let state = app.state::<std::sync::Mutex<NativeHotkeyState>>();
    let hotkey = state.lock().map_err(|e| e.to_string())?;
    write_native_settings(&dir.join("preview-native-settings.json"), &hotkey.shortcut,
        serde_json::to_value(overlay_panel::get_or_init_settings()).map_err(|e| e.to_string())?)
}

fn settings_with_native_positions(mut settings: overlay_panel::OverlaySettings,
    current: &overlay_panel::OverlaySettings) -> overlay_panel::OverlaySettings {
    settings.vertical_position_x = current.vertical_position_x;
    settings.vertical_position_y = current.vertical_position_y;
    settings.position_x = current.position_x;
    settings.position_y = current.position_y;
    settings
}

fn record_vertical_position(position: tauri::PhysicalPosition<i32>) {
    let mut settings = overlay_panel::CURRENT_SETTINGS.lock().unwrap_or_else(|p|p.into_inner());
    let settings = settings.get_or_insert_with(Default::default);
    settings.vertical_position_x = Some(position.x);
    settings.vertical_position_y = Some(position.y);
}

fn save_overlay_position(app: &tauri::AppHandle) -> Result<(), String> {
    // Native move events already captured the coordinates. Querying a closing
    // WebView here can fail and incorrectly cancel the entire application exit.
    save_native_settings(app)?;
    // Do not acknowledge a pending move until its write has succeeded.
    let _ = overlay_panel::take_position_changed();
    Ok(())
}

fn write_native_settings(path: &std::path::Path, hotkey: &str, settings: Value) -> Result<(), String> {
    use std::io::Write;
    static NEXT_WRITE: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
    let dir = path.parent().ok_or("native settings parent missing")?;
    std::fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    let temporary = dir.join(format!(".native-settings-{}-{}.tmp", std::process::id(),
        NEXT_WRITE.fetch_add(1, std::sync::atomic::Ordering::Relaxed)));
    let result = (|| {
        let mut file = std::fs::OpenOptions::new().write(true).create_new(true)
            .open(&temporary).map_err(|e| e.to_string())?;
        let data = json!({"overlay-settings": settings, "overlay-hotkey": hotkey});
        file.write_all(data.to_string().as_bytes()).map_err(|e| e.to_string())?;
        file.sync_all().map_err(|e| e.to_string())?;
        drop(file);
        std::fs::rename(&temporary, path).map_err(|e| e.to_string())
    })();
    if result.is_err() { let _ = std::fs::remove_file(&temporary); }
    result
}

struct NativeHotkeyState {
    shortcut: String,
    registration_error: Option<String>,
}

fn initialize_native_hotkey<FRegister>(path: &std::path::Path, mut register: FRegister) -> Result<NativeHotkeyState, String>
where FRegister: FnMut(&str) -> Result<(), String> {
    let persisted = match std::fs::read_to_string(path) {
        Ok(text) => serde_json::from_str::<Value>(&text).map_err(|e| e.to_string())?
            .get("overlay-hotkey").and_then(Value::as_str).filter(|key| !key.trim().is_empty())
            .unwrap_or("Shift+F10").to_string(),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => "Shift+F10".to_string(),
        Err(error) => return Err(error.to_string()),
    };
    let registration_error = register(&persisted).err()
        .map(|error| format!("Failed to restore overlay shortcut {persisted}: {error}"));
    Ok(NativeHotkeyState { shortcut: persisted, registration_error })
}

#[cfg(test)]
fn restore_native_hotkey<FRegister>(path: &std::path::Path, register: FRegister) -> Result<String, String>
where FRegister: FnMut(&str) -> Result<(), String> {
    let state = initialize_native_hotkey(path, register)?;
    match state.registration_error { Some(error) => Err(error), None => Ok(state.shortcut) }
}

fn apply_hotkey_change<FRegister, FUnregister, FSave>(
    previous: &str,
    candidate: &str,
    previous_registered: bool,
    mut register: FRegister,
    mut unregister: FUnregister,
    mut save: FSave,
) -> Result<String, String>
where
    FRegister: FnMut(&str) -> Result<(), String>,
    FUnregister: FnMut(&str) -> Result<(), String>,
    FSave: FnMut(&str) -> Result<(), String>,
{
    if candidate.trim().is_empty() { return Err("shortcut required".into()); }
    if previous == candidate && previous_registered { return Ok(previous.to_string()); }
    register(candidate)?;
    if let Err(error) = if previous_registered { unregister(previous) } else { Ok(()) } {
        return match unregister(candidate) {
            Ok(()) => Err(error),
            Err(rollback) => Err(format!("{error}; candidate shortcut cleanup failed: {rollback}")),
        };
    }
    if let Err(error) = save(candidate) {
        let restored = if previous_registered { register(previous) } else { Ok(()) };
        let removed = unregister(candidate);
        return match (restored, removed) {
            (Ok(()), Ok(())) => Err(error),
            (restored, removed) => Err(format!("{error}; shortcut rollback failed: restore={restored:?}, cleanup={removed:?}")),
        };
    }
    Ok(candidate.to_string())
}

fn change_native_hotkey<FRegister, FUnregister, FSave>(state: &mut NativeHotkeyState, candidate: &str,
    mut register: FRegister, mut unregister: FUnregister, save: FSave) -> Result<(), String>
where FRegister: FnMut(&str) -> Result<(), String>, FUnregister: FnMut(&str) -> Result<(), String>,
    FSave: FnMut(&str) -> Result<(), String> {
    let registered = std::cell::Cell::new(state.registration_error.is_none());
    let result = apply_hotkey_change(&state.shortcut, candidate, registered.get(),
        |key| { let result = register(key); if result.is_ok() && key == state.shortcut { registered.set(true); } result },
        |key| { let result = unregister(key); if result.is_ok() && key == state.shortcut { registered.set(false); } result }, save);
    match result {
        Ok(saved) => { state.shortcut = saved; state.registration_error = None; Ok(()) },
        Err(error) => { if !registered.get() { state.registration_error = Some(error.clone()); } Err(error) },
    }
}
// Sync command. Window-creating flows are dispatched to worker threads below:
// creating a webview inside this handler would deadlock the main thread
// (WebView2 controller init needs the message pump that the handler is blocking).
// The command itself is async so long-running calls (report generation,
// PawnIO installer, hardware queries) occupy the async runtime's thread pool
// instead of freezing the window's message pump.
#[tauri::command]
async fn preview_native(app: tauri::AppHandle, command: String, args: Value) -> Result<Value, String> {
    #[cfg(feature = "uiaccess-acceptance")]
    if command == "uiaccess_acceptance_gdi" { return Ok(overlay_panel::render_diagnostics()); }
    if command == "uiaccess_acceptance_window" && cfg!(feature = "uiaccess-acceptance") {
        #[cfg(windows)]
        {
            let window = app.get_webview_window("vertical-overlay").ok_or("original vertical overlay missing")?;
            let hwnd = window.hwnd().map_err(|e| e.to_string())?;
            let mut band = 0;
            let mut rect = windows_sys::Win32::Foundation::RECT { left: 0, top: 0, right: 0, bottom: 0 };
            unsafe {
                let module = windows_sys::Win32::System::LibraryLoader::GetModuleHandleW(windows_sys::core::w!("user32.dll"));
                let address = windows_sys::Win32::System::LibraryLoader::GetProcAddress(module, b"GetWindowBand\0".as_ptr())
                    .ok_or("GetWindowBand export unavailable")?;
                let get_band: unsafe extern "system" fn(*mut std::ffi::c_void, *mut u32) -> i32 = std::mem::transmute(address);
                let ok = get_band(hwnd.0 as _, &mut band);
                windows_sys::Win32::UI::WindowsAndMessaging::GetWindowRect(hwnd.0 as _, &mut rect);
                return Ok(json!({"hwnd":hwnd.0 as usize,"band":band,"band_ok":ok != 0,
                    "visible":window.is_visible().unwrap_or(false),"rect":[rect.left,rect.top,rect.right,rect.bottom]}));
            }
        }
    }
    ensure_allowed(&command)?;
    if command == "build_info" || command.starts_with("update_") && command != "update_overlay_settings" {
        return updater::command(app,command,args).await;
    }
    // Creating a webview window from inside the command handler deadlocks the main
    // thread (WebView2 controller init needs the message pump that is currently
    // blocked in this handler). All window-creating flows therefore run on a worker
    // thread, leaving the main thread free to pump.
    fn spawn_job(app: &tauri::AppHandle, job: impl FnOnce(&tauri::AppHandle) + Send + 'static) {
        let app = app.clone();
        std::thread::spawn(move || job(&app));
    }
    match command.as_str() {
        // ---- Real hardware data (FrameScopeHardwareService named pipe) ----
        "get_hardware" | "get_hardware_info" => {
            let response = hardware_service::call("get_static_hardware", json!({}))?;
            let hardware = response["hardware"].clone();
            if hardware.is_null() {
                let error = sensor_response_error(&response, "硬件信息不可用");
                return Err(error.to_string());
            }
            return Ok(hardware);
        }
        "get_all_sensors" => {
            let response = hardware_service::call("get_sensors", json!({}))?;
            if response["availability"] == "unavailable" && response["sensors"].as_array().map(|s| s.is_empty()).unwrap_or(true) {
                let error = sensor_response_error(&response, "传感器数据不可用");
                return Err(error.to_string());
            }
            return Ok(json!({
                "updatedAt": response["updatedAt"],
                "sensors": response["sensors"],
                "availability": response["availability"],
                "errors": response["errors"],
                "pawnIo": response["pawnIo"],
            }));
        }
        "get_overlay_hardware_data" => return hardware_service::call("get_overlay_data", json!({})).map(backend::apply_report_lows),
        "get_memory_status" => return hardware_service::call("get_memory_status", json!({})),
        "get_disk_status" => return hardware_service::call("get_disk_status", json!({})),
        "is_nvidia_gpu" => {
            let overlay = hardware_service::call("get_overlay_data", json!({}))?;
            let index = overlay["active_gpu_index"].as_u64().unwrap_or(0) as usize;
            let name = overlay["gpu_sensors"].as_array().and_then(|gpus| gpus.get(index))
                .and_then(|gpu| gpu["name"].as_str()).unwrap_or("");
            return Ok(json!(name.to_ascii_lowercase().contains("nvidia")));
        }
        "set_active_gpu_index" => {
            let index = args["index"].as_u64().ok_or("GPU index required")?;
            hardware_service::call("set_active_gpu", json!({"index": index}))?;
        }
        "clear_hardware_data" => { hardware_service::call("clear_cache", json!({}))?; },
        "get_system_locale" => return Ok(json!(system_locale())),
        "get_system_username" => return Ok(json!(std::env::var("USERNAME").unwrap_or_default())),
        // ---- Real capture pipeline ----
        "targets_get" => return backend::targets_payload("loaded"),
        "targets_save" => {
            let targets = args["targets"].as_array().ok_or("targets array required")?;
            return backend::targets_save(targets);
        }
        "processes_refresh" => {
            let query = args["query"].as_str().unwrap_or("");
            return backend::processes_refresh(query);
        }
        "monitor_start" => {
            return backend::monitor_start();
        }
        "monitor_stop" => return backend::monitor_stop(),
        "monitor_status" => return backend::monitor_status(),
        "reports_list" => return backend::reports_list(),
        "report_delete" => {
            let id = args["id"].as_str().ok_or("id required")?.to_string();
            return std::thread::spawn(move || backend::report_delete(&id)).join().map_err(|_| "report delete worker failed".to_string())?;
        }
        "report_open" => {
            let id = args["id"].as_str().ok_or("id required")?;
            return backend::report_open(id);
        }
        "report_open_directory" => {
            let id = args["id"].as_str().ok_or("id required")?;
            return backend::report_open_directory(id);
        }
        "report_regenerate" => {
            let id = args["id"].as_str().ok_or("id required")?;
            // Long-running (generator can take a while); run on a worker
            // thread so the webview IPC is not stalled.
            let id = id.to_string();
            let (sender, receiver) = std::sync::mpsc::channel();
            std::thread::spawn(move || {
                let _ = sender.send(backend::report_regenerate(&id));
            });
            return receiver.recv().map_err(|_| "report generation worker died".to_string())?;
        }
        // ---- PawnIO driver ----
        "check_pawnio_status" => return Ok(pawnio::status()),
        "install_pawnio_driver" => {
            // The installer runs elevated (UAC prompt) and blocks; worker thread.
            // The modal expects the plain strings "success"/"already_installed".
            // recv() yields the inner Result<String, String> (the worker sends
            // the fallible result) — unwrap THAT, not the recv envelope, or the
            // serialized {"Ok":...} envelope leaks to the UI and crashes React.
            let (sender, receiver) = std::sync::mpsc::channel();
            std::thread::spawn(move || {
                let result = pawnio::install().and_then(|value| {
                    value["result"]
                        .as_str()
                        .map(str::to_string)
                        .ok_or_else(|| "unexpected installer result".to_string())
                });
                let _ = sender.send(result);
            });
            let inner: Result<String, String> =
                receiver.recv().map_err(|_| "pawnio worker died".to_string())?;
            return inner.map(|s| json!(s));
        }
        "uninstall_pawnio_driver" => {
            let (sender, receiver) = std::sync::mpsc::channel();
            std::thread::spawn(move || {
                let _ = sender.send(pawnio::uninstall());
            });
            return receiver.recv().map_err(|_| "pawnio worker died".to_string())?;
        }
        "restart_monitor_process" => { hardware_service::call("restart_provider", json!({}))?; }

        // ---- Preview-side-effect commands ----
        "preview_set_hardware" => {
            let data = decode_fixture(args.get("data").cloned().unwrap_or_else(|| json!({})))?;
            overlay_panel::publish_hardware_data(data.clone());
            app.emit("vertical-overlay-data", data).map_err(|e| e.to_string())?;
        }
        "open_sensor_monitor" => spawn_job(&app, |app| {
            if let Err(e) = open_sensor(app) { eprintln!("open_sensor_monitor: {e}"); }
        }),
        "open_report_sample" => spawn_job(&app, |app| {
            if let Some(window) = app.get_webview_window("report-sample") {
                let _ = window.show();
                let _ = window.set_focus();
            } else if let Err(e) = WebviewWindowBuilder::new(app, "report-sample", WebviewUrl::App("samples/charts/framescope-interactive-report.html".into()))
                .title("FrameScope - 模拟报告样例").inner_size(1200.0, 780.0)
                
                .build()
            {
                eprintln!("open_report_sample: {e}");
            }
        }),
        "open_vertical_overlay" => spawn_job(&app, |app| {
            if let Err(e) = open_vertical(app) { eprintln!("open_vertical_overlay: {e}"); }
        }),
        "start_overlay_panel" => {
            // The window-creating flow must run OFF the command thread (iron
            // rule 2), but the frontend awaits this invoke and reads
            // result.success to flip the toggle - the old fire-and-forget
            // spawn_job always resolved to null, so the toggle never moved
            // even though the panel came up. Bridge the worker's outcome back
            // through a channel and wait for it.
            let (tx, rx) = std::sync::mpsc::channel::<Value>();
            let worker_app = app.clone();
            std::thread::spawn(move || {
                let _lock = OVERLAY_OPERATION.lock().unwrap_or_else(|p| p.into_inner());
                let payload = match start_selected(&worker_app) {
                    Ok(_) => json!({ "success": true, "message": "悬浮框已启动" }),
                    Err(e) => json!({ "success": false, "message": e }),
                };
                let _ = worker_app.emit("overlay-status-changed", active(&worker_app));
                let _ = tx.send(payload);
            });
            match rx.recv_timeout(std::time::Duration::from_secs(10)) {
                Ok(payload) => return Ok(payload),
                Err(_) => return Err("启动悬浮框超时".into()),
            }
        }
        "stop_overlay_panel" | "stop_vertical_overlay" => {
            let _lock = OVERLAY_OPERATION.lock().unwrap_or_else(|p| p.into_inner());
            stop_windows(&app)?;
            app.emit("overlay-status-changed", false).map_err(|e| e.to_string())?;
            return Ok(json!({ "success": true, "message": "悬浮框已关闭" }));
        }
        "toggle_overlay_panel" => spawn_job(&app, |app| {
            let _lock = OVERLAY_OPERATION.lock().unwrap();
            if active(app) { let _ = stop_windows(app); } else { let _ = start_selected(app); }
            let _ = app.emit("overlay-status-changed", active(app));
        }),
        "get_overlay_panel_status" => return Ok(json!(active(&app))),
        "get_overlay_current_settings" => return serde_json::to_value(overlay_panel::get_or_init_settings()).map_err(|e| e.to_string()),
        "get_delta_passwords" => {
            let items = delta_password::get_delta_passwords().await?;
            return serde_json::to_value(items).map_err(|e| e.to_string());
        }
        "config_save" => {
            let update = args
                .get("config")
                .cloned()
                .ok_or("config_save requires a config object")?;
            return backend::config_save(&update);
        }
        "config_read" => return backend::config_read(),
        "update_overlay_settings" => {
            let settings: overlay_panel::OverlaySettings = serde_json::from_value(args["settings"].clone()).map_err(|e| e.to_string())?;
            if settings.font_size < 8 || settings.font_size > 72 || settings.item_width < 30 || settings.item_width > 1000 {
                return Err("Overlay size outside preview limits".into());
            }
            // Hold the same operation lock every other start/stop path uses:
            // update_settings tears the overlay down and rebuilds it, and an
            // unserialized rebuild could double-create windows (R-A8).
            let _operation_lock = OVERLAY_OPERATION.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
            // Position changes belong to drag/reset commands. A settings page
            // can carry old non-null coordinates, including the default 40/80.
            let mut settings = settings_with_native_positions(settings, &overlay_panel::get_or_init_settings());
            let was_active = active(&app);
            if settings.style == "vertical_panel" {
                // The vertical panel is a webview that applies the new settings
                // live from the emitted event (and re-measures its size via
                // resize_vertical_overlay). Tearing the window down here would
                // race its async destroy and the rebuild would fail with a
                // duplicate-label error — the panel would just vanish.
                let was_vertical = overlay_panel::get_or_init_settings().style == "vertical_panel";
                overlay_panel::update_settings(settings.clone())?;
                save_native_settings(&app)?;
                // Plate opacity is pure DOM: the vertical webview applies the
                // new settings live from the emitted event; the HWND never
                // needs restyling (see window-layer note above).
                if was_active && !was_vertical {
                    // Style switched GDI → vertical: retire the GDI panel and
                    // open the webview panel (open_vertical stops the GDI one).
                    spawn_job(&app, |app| {
                        let _lock = OVERLAY_OPERATION.lock().unwrap();
                        if let Err(e) = start_selected(app) { eprintln!("update_overlay_settings switch: {e}"); }
                    });
                }
            } else {
                // Hot path: same GDI style, panel running, and the ONLY
                // changed field is opacity - repaint in place instead of the
                // stop+rebuild cycle that made the bar blink on every
                // slider tick. Any other change (style metrics, items, fonts)
                // still goes through the full rebuild below.
                let old = overlay_panel::get_or_init_settings();
                let same_style = old.style == settings.style;
                let only_opacity = same_style && {
                    let mut a = serde_json::to_value(&old).unwrap_or(Value::Null);
                    let b = serde_json::to_value(&settings).unwrap_or(Value::Null);
                    if let (Some(a), Some(b)) = (a.as_object_mut(), b.as_object()) {
                        a.insert("opacity".into(), b.get("opacity").cloned().unwrap_or(Value::Null));
                        a == b
                    } else {
                        false
                    }
                };
                if only_opacity && overlay_panel::is_overlay_active() {
                    // Persist into the live settings FIRST - the repaint reads
                    // CURRENT_SETTINGS, and this was the missed half of the hot
                    // path (opacity slider appeared dead on GDI styles).
                    overlay_panel::store_settings(settings.clone());
                    if overlay_panel::request_overlay_repaint() {
                        save_native_settings(&app)?;
                        app.emit("vertical-overlay-settings", settings).map_err(|e| e.to_string())?;
                        return Ok(Value::Null);
                    }
                    // repaint post failed (window gone?) - fall through to rebuild;
                    // store_settings already applied, update_settings below is a
                    // harmless re-store plus a window rebuild.
                }
                stop_windows(&app)?;
                settings = settings_with_native_positions(settings, &overlay_panel::get_or_init_settings());
                overlay_panel::update_settings(settings.clone())?;
                save_native_settings(&app)?;
                if was_active {
                    spawn_job(&app, |app| {
                        let _lock = OVERLAY_OPERATION.lock().unwrap();
                        if let Err(e) = start_selected(app) { eprintln!("update_overlay_settings restart: {e}"); }
                    });
                }
            }
            app.emit("vertical-overlay-settings", settings).map_err(|e| e.to_string())?;
        }
        "set_overlay_drag_mode" => {
            let enabled = args["enabled"].as_bool().ok_or("enabled must be boolean")?;
            let _lock = OVERLAY_OPERATION.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
            if !enabled { save_overlay_position(&app)?; }
            overlay_panel::set_drag_mode(enabled);
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                overlay_set_hit_test_transparent(&window, !enabled);
                app.emit("vertical-overlay-drag-mode", enabled).map_err(|e| e.to_string())?;
            }
        }
        "set_vertical_overlay_click_through" => {
            // Contract (matching the 05606fe/tao-era semantics the frontend is
            // written against): enabled = click-through ON. Dragging sends
            // enabled:false to take input, the panel's mouseup sends
            // enabled:true to give it back. (e1aedfd inverted this and drag
            // mode enabled the pass-through instead.)
            let enabled = args["enabled"].as_bool().ok_or("enabled must be boolean")?;
            let _lock = OVERLAY_OPERATION.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                if enabled { save_overlay_position(&app)?; }
                overlay_set_hit_test_transparent(&window, enabled);
            }
        }
        "resize_vertical_overlay" => {
            let height = args["height"].as_f64().ok_or("height must be a number")?.clamp(40.0, 1600.0);
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                window.set_size(tauri::LogicalSize::new(overlay_panel::get_or_init_settings().item_width as f64, height)).map_err(|e| e.to_string())?;
            }
        }
        "save_vertical_overlay_position" | "reset_vertical_overlay_position" | "reset_overlay_position" => {
            let _lock = OVERLAY_OPERATION.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
            if command == "save_vertical_overlay_position" {
                // JS move callbacks may be delayed or arrive after destruction;
                // persist the latest native event, not their stale coordinates.
                save_overlay_position(&app)?;
                return Ok(Value::Null);
            }
            let mut settings = overlay_panel::get_or_init_settings();
            let reset = command.starts_with("reset");
            let x = if reset { 40 } else { args["x"].as_i64().ok_or("x required")?.clamp(-32000, 32000) as i32 };
            let y = if reset { 80 } else { args["y"].as_i64().ok_or("y required")?.clamp(-32000, 32000) as i32 };
            if command == "reset_overlay_position" {
                settings.position_x = Some(x); settings.position_y = Some(y);
            } else {
                settings.vertical_position_x = Some(x); settings.vertical_position_y = Some(y);
                if reset {
                    if let Some(window) = app.get_webview_window("vertical-overlay") {
                        window.set_position(tauri::PhysicalPosition::new(x, y)).map_err(|e| e.to_string())?;
                    }
                }
            }
            overlay_panel::update_settings(settings)?;
            save_native_settings(&app)?;
        }
        "vertical_overlay_ready" => {
            app.emit("vertical-overlay-data", overlay_panel::collect_hardware_data()).map_err(|e| e.to_string())?;
            app.emit("vertical-overlay-settings", overlay_panel::get_or_init_settings()).map_err(|e| e.to_string())?;
        }
        "set_overlay_hotkey" => {
            use tauri_plugin_global_shortcut::GlobalShortcutExt;
            let shortcut = args["shortcut"].as_str().ok_or("shortcut required")?;
            let state = app.state::<std::sync::Mutex<NativeHotkeyState>>();
            let mut previous = state.lock().map_err(|e| e.to_string())?;
            let path = app.path().app_data_dir().map_err(|e| e.to_string())?.join("preview-native-settings.json");
            change_native_hotkey(
                &mut previous,
                shortcut,
                |key| app.global_shortcut().register(key).map_err(|e| e.to_string()),
                |key| app.global_shortcut().unregister(key).map_err(|e| e.to_string()),
                |candidate| write_native_settings(&path, candidate,
                    serde_json::to_value(overlay_panel::get_or_init_settings()).map_err(|e| e.to_string())?),
            )?;
        }
        "get_overlay_hotkey" => {
            return Ok(Value::String(app.state::<std::sync::Mutex<NativeHotkeyState>>().lock().map_err(|error| error.to_string())?.shortcut.clone()));
        }
        "minimize_to_tray" => app.get_webview_window("main").ok_or("main missing")?.hide().map_err(|e| e.to_string())?,
        "exit_app" => { request_shutdown(&app); }
        "export_hardware_report" => {
            let path = args["path"].as_str().ok_or("export path required")?;
            if !path.to_ascii_lowercase().ends_with(".html") && !path.to_ascii_lowercase().ends_with(".json") {
                return Err("Export must be HTML or JSON".into());
            }
            // R-A9: the host ALWAYS assembles the snapshot itself - a
            // compromised webview must not be able to plant arbitrary file
            // content through the content channel. (The path itself comes
            // from the user's save dialog in the frontend.)
            let overlay = hardware_service::call("get_overlay_data", json!({}))?;
            let hardware = hardware_service::call("get_static_hardware", json!({}))
                .ok()
                .and_then(|response| response["hardware"].clone().as_object().cloned());
            let content = serde_json::to_string_pretty(&json!({
                "brand": "FrameScope",
                "generatedAt": chrono::Utc::now().to_rfc3339(),
                "overlay": overlay,
                "hardware": hardware,
            })).map_err(|e| e.to_string())?;
            std::fs::write(path, content).map_err(|e| e.to_string())?;
            return Ok(json!(path));
        }
        _ => return Err("Unimplemented preview command".into()),
    }
    Ok(json!({"success": true, "message": "预览操作完成"}))
}

fn system_locale() -> String {
    #[cfg(windows)]
    unsafe {
        use windows_sys::Win32::Globalization::GetUserDefaultLocaleName;
        let mut buffer = [0u16; 85]; // LOCALE_NAME_MAX_LENGTH
        let len = GetUserDefaultLocaleName(buffer.as_mut_ptr(), buffer.len() as i32);
        if len > 1 {
            return String::from_utf16_lossy(&buffer[..(len - 1) as usize]);
        }
    }
    "zh-CN".into()
}
pub fn run() {
    #[cfg(windows)]
    {
        let mut runtime = runtime_uiaccess::windows::NativeRuntime::default();
        match runtime_uiaccess::bootstrap(&mut runtime, &std::env::args().collect::<Vec<_>>()) {
            Ok(runtime_uiaccess::Action::Continue) => (),
            Ok(runtime_uiaccess::Action::Relaunched) => return,
            Err(error) => {
                eprintln!("UIAccess startup failed: {error}");
                use windows_sys::Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK};
                let message: Vec<u16> = format!("UIAccess 启动失败：{error}\n程序没有进入采集或窗口初始化。")
                    .encode_utf16().chain(Some(0)).collect();
                let title: Vec<u16> = "FrameScope 启动失败".encode_utf16().chain(Some(0)).collect();
                unsafe { MessageBoxW(std::ptr::null_mut(), message.as_ptr(), title.as_ptr(), MB_OK | MB_ICONERROR); }
                std::process::exit(1);
            }
        }
    }
    // Diagnostics (ETW/permission/pipe failures) used to vanish because no
    // logger was installed; stderr is enough for a desktop app and costs
    // nothing in release.
    struct StderrLogger;
    impl log::Log for StderrLogger {
        fn enabled(&self, metadata: &log::Metadata) -> bool {
            metadata.level() <= log::Level::Info
        }
        fn log(&self, record: &log::Record) {
            #[cfg(feature = "uiaccess-acceptance")]
            {
                use std::io::Write;
                if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true)
                    .open(concat!(env!("CARGO_MANIFEST_DIR"), "/../../artifacts/20261010-uiaccess-resume/runtime.log")) {
                    let _ = writeln!(file, "[{}] {}", record.level(), record.args());
                }
            }
            eprintln!("[{}] {}", record.level(), record.args());
        }
        fn flush(&self) {}
    }
    static LOGGER: StderrLogger = StderrLogger;
    let _ = log::set_logger(&LOGGER).map(|_| log::set_max_level(log::LevelFilter::Info));

    let request_exit_only = std::env::args().any(|a|a == "--request-exit");
    let mut context = tauri::generate_context!();
    if request_exit_only { context.config_mut().app.windows.clear(); }
    let app = tauri::Builder::default()
        // Must register before other plugins (single-instance docs): a second
        // launch forwards here and dies, so the tray-hidden window comes back
        // to the foreground instead of spawning a twin process (audit F-1).
        .plugin(tauri_plugin_single_instance::init(|app, args, _cwd| {
            if args.iter().any(|a|a == "--request-exit") {
                if let (Ok(exe), Ok(root)) = (std::env::current_exe(), backend::backend_root()) {
                    if host_shutdown::same_installation_request(&args, &exe, &root) {
                        request_shutdown(app);
                    } else { log::warn!("拒绝来自其他安装目录的退出请求"); }
                }
                return;
            }
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.show();
                let _ = window.unminimize();
                let _ = window.set_focus();
            }
        }))
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_global_shortcut::Builder::new().with_handler(|app, _, event| {
            if event.state == tauri_plugin_global_shortcut::ShortcutState::Pressed {
                let app = app.clone();
                std::thread::spawn(move || {
                    let _lock = OVERLAY_OPERATION.lock().unwrap();
                    if active(&app) { let _ = stop_windows(&app); } else { let _ = start_selected(&app); }
                    let _ = app.emit("overlay-status-changed", active(&app));
                });
            }
        }).build())
        .invoke_handler(tauri::generate_handler![preview_native])
        .setup(move |app| {
            app.manage(std::sync::Arc::new(updater::Service::new(app.path().app_data_dir()?.join("updates"))
                .map_err(std::io::Error::other)?));
            // Single-instance forwarding runs before this setup. No matching
            // host: return a distinct code without creating GUI or providers.
            if request_exit_only { std::process::exit(3); }
            use tauri::menu::{Menu, MenuItem};
            #[cfg(windows)]
            if let Some(window) = app.get_webview_window("main") {
                window_icons::apply(window.hwnd()?.0)?;
            }
            // Restore persisted overlay settings (style/items/positions) BEFORE
            // anything can start the panel: this was defined but never called,
            // so every launch reset the overlay to the GDI default style even
            // though the settings file said vertical_panel.
            overlay_panel::try_load_persisted_settings(app.handle());
            use tauri_plugin_global_shortcut::GlobalShortcutExt;
            let persisted_hotkey = initialize_native_hotkey(&app.path().app_data_dir()?.join("preview-native-settings.json"),
                |key| app.global_shortcut().register(key).map_err(|error| error.to_string()))
                .map_err(std::io::Error::other)?;
            if let Some(error) = persisted_hotkey.registration_error.as_ref() {
                log::error!("{error}");
                let app_handle = app.handle().clone();
                let message = format!("悬浮窗快捷键注册失败：{error}\n请在设置中选择可用的快捷键。");
                app.handle().run_on_main_thread(move || {
                    use tauri_plugin_dialog::DialogExt;
                    app_handle.dialog().message(message).title("FrameScope 快捷键不可用").show(|_| {});
                })?;
            }
            app.manage(std::sync::Mutex::new(persisted_hotkey));
            let show = MenuItem::with_id(app, "show", "打开 FrameScope", true, None::<&str>)?;
            let quit = MenuItem::with_id(app, "quit", "退出 FrameScope", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show, &quit])?;
            // Real FrameScope icon instead of the old 16x16 solid-color square.
            // The shell draws the tray at SM_CXSMICON (16-32px by DPI); feeding
            // it the 256px app icon made the shell downscale 16x and the glyph
            // turned to mush. Pre-rendered tray-{16,20,24,32,48}.png are
            // HighQualityBicubic downscales of the 256px source - pick the
            // closest to what the shell will actually draw.
            let icon = {
                #[cfg(windows)]
                let tray_px = unsafe {
                    windows_sys::Win32::UI::WindowsAndMessaging::GetSystemMetrics(
                        windows_sys::Win32::UI::WindowsAndMessaging::SM_CXSMICON,
                    )
                } as u32;
                #[cfg(not(windows))]
                let tray_px = 24u32;
                let bytes: &[u8] = match tray_px {
                    0..=17 => include_bytes!("../icons/tray-16.png"),
                    18..=22 => include_bytes!("../icons/tray-20.png"),
                    23..=27 => include_bytes!("../icons/tray-24.png"),
                    28..=39 => include_bytes!("../icons/tray-32.png"),
                    _ => include_bytes!("../icons/tray-48.png"),
                };
                tauri::image::Image::from_bytes(bytes)
                    .map_err(|e| e.to_string())?
                    .to_owned()
            };
            tauri::tray::TrayIconBuilder::new().icon(icon).tooltip("FrameScope").menu(&menu)
                .on_tray_icon_event(|tray, event| {
                    // Left-click restores the main window (the old tray had no
                    // click behavior at all, which made "minimize to tray"
                    // feel like the app had vanished).
                    if let tauri::tray::TrayIconEvent::Click {
                        button: tauri::tray::MouseButton::Left,
                        button_state: tauri::tray::MouseButtonState::Up,
                        ..
                    } = event {
                        if let Some(window) = tray.app_handle().get_webview_window("main") {
                            let _ = window.unminimize();
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                })
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" => {
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.unminimize();
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                    "quit" => { request_shutdown(app); }
                    _ => {}
                }).build(app)?;
            // Window startup preferences run once, after persisted overlay
            // settings and tray restoration are ready, independently of capture.
            match backend::startup_preferences() {
                Ok((auto_overlay, start_minimized)) => {
                    if start_minimized {
                        if let Some(window) = app.get_webview_window("main") {
                            if let Err(error) = window.minimize() { eprintln!("Startup minimize failed: {error}"); }
                        }
                    }
                    if auto_overlay {
                        let handle = app.handle().clone();
                        std::thread::spawn(move || {
                            let _lock = OVERLAY_OPERATION.lock().unwrap_or_else(|p|p.into_inner());
                            if let Err(error) = backend::ensure_not_closing().and_then(|_| start_selected(&handle)) {
                                eprintln!("Automatic overlay start failed: {error}");
                            }
                            let _ = handle.emit("overlay-status-changed", active(&handle));
                        });
                    }
                }
                Err(error) => eprintln!("Window startup preferences unavailable: {error}"),
            }
            // Per main-process startup, never a page mount or overlay toggle.
            std::thread::spawn(|| {
                if let Err(error) = backend::auto_start_monitor() {
                    eprintln!("Automatic monitoring start failed: {error}");
                }
            });
            // Resident hardware poller (NexBox start_hardware_poller pattern):
            // 10 Hz service reads feed the GDI overlay cache and the vertical
            // overlay event stream, so both overlays show real data without
            // any frontend fixture push. 10Hz matches the service-side
            // throttle (instant FPS window is ~200ms at 300FPS); the GDI
            // panel itself repaints on its own slower timer.
            let handle = app.handle().clone();
            std::thread::spawn(move || {
                let mut error_streak: u32 = 0;
                loop {
                    std::thread::sleep(std::time::Duration::from_millis(100));
                    // R-A7: PawnIO setup kills and re-creates the sampling
                    // service; stay completely hands-off until it finishes.
                    if hardware_service::poller_paused() {
                        continue;
                    }
                    match hardware_service::call("get_overlay_data", json!({})) {
                        Ok(data) => {
                            let data = backend::apply_report_lows(data);
                            error_streak = 0;
                            // The GDI panel stores clocks/percent as integers; feed
                            // it a rounded copy while the frontend event keeps the
                            // full-precision service snapshot.
                            match decode_fixture(hardware_service::sanitize_for_gdi(data.clone())) {
                                Ok(parsed) => overlay_panel::publish_hardware_data(parsed),
                                Err(e) => eprintln!("hardware poller: decode failed: {e}"),
                            }
                            let _ = handle.emit("vertical-overlay-data", data);
                        }
                        Err(e) => {
                            error_streak = error_streak.saturating_add(1);
                            eprintln!("hardware poller: {e}");
                            // R-C5: with the service unreachable, the overlays
                            // would freeze on the last snapshot forever (the
                            // host-side cousin of the stale-cache failure).
                            // Broadcast an all-null snapshot so they show
                            // "--" while down.
                            if error_streak == 3 {
                                let empty = hardware_service::empty_overlay_value();
                                match decode_fixture(hardware_service::sanitize_for_gdi(empty.clone())) {
                                    Ok(parsed) => overlay_panel::publish_hardware_data(parsed),
                                    Err(e2) => eprintln!("hardware poller: decode failed: {e2}"),
                                }
                                let _ = handle.emit("vertical-overlay-data", empty);
                            }
                        }
                    }
                }
            });
            // 问题 6 方案 A（竖排 WebView OSD）：周期性重申 TOPMOST。创建时的
            // always_on_top 在游戏抢占前台后可能被永久压到下层；flip model（独立
            // 翻转）下 DWM 仍在链路中，TOPMOST 本该有效。SWP_NOACTIVATE 不抢焦点。
            let topmost_handle = app.handle().clone();
            std::thread::spawn(move || loop {
                std::thread::sleep(std::time::Duration::from_millis(500));
                if let Some(window) = topmost_handle.get_webview_window("vertical-overlay") {
                    if window.is_visible().unwrap_or(false) {
                        #[cfg(windows)]
                        if let Ok(hwnd) = window.hwnd() {
                            use windows_sys::Win32::UI::WindowsAndMessaging::{
                                SetWindowPos, HWND_TOPMOST, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE,
                            };
                            unsafe {
                                SetWindowPos(
                                    hwnd.0 as _,
                                    HWND_TOPMOST,
                                    0,
                                    0,
                                    0,
                                    0,
                                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
                                );
                            }
                        }
                    }
                }
            });
            Ok(())
        })
        .on_window_event(|window, event| {
            if window.label() == "vertical-overlay" {
                match event {
                    tauri::WindowEvent::Moved(position) => record_vertical_position(*position),
                    tauri::WindowEvent::Destroyed => VERTICAL_ACTIVE.store(false, std::sync::atomic::Ordering::SeqCst),
                    _ => {}
                }
            }
            if window.label() == "main" {
                #[cfg(windows)]
                if matches!(event, tauri::WindowEvent::ScaleFactorChanged { .. }) {
                    if let Ok(hwnd) = window.hwnd() {
                        if let Err(error) = window_icons::apply(hwnd.0) {
                            log::warn!("Window icon update failed: {error}");
                        }
                    }
                }
                if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                    // The X button and Alt+F4/taskbar closes must behave the
                    // same; the choice lives in framescope-config.json
                    // (CloseWindowBehavior, mirrored from the UI setting).
                    match backend::close_window_behavior().as_str() {
                        // Tray-style close: the hardware chain and the OSD
                        // overlays keep running, only the window hides.
                        // prevent_close is REQUIRED — without it the default
                        // close still proceeds after hide() and a hidden-
                        // then-destroyed window takes the whole app down.
                        "minimize-to-tray" => {
                            api.prevent_close();
                            let _ = window.hide();
                        }
                        // Hand the question to the UI's close-confirm dialog.
                        "ask" => {
                            api.prevent_close();
                            let _ = window.app_handle().emit("main-close-requested", ());
                        }
                        _ => { api.prevent_close(); request_shutdown(window.app_handle()); }
                    }
                }
            }
        })
        .build(context).expect("Unable to build isolated preview");
    app.run(|app, event| {
        if let tauri::RunEvent::ExitRequested { api, .. } = event {
            if !SHUTDOWN_COMPLETE.load(std::sync::atomic::Ordering::SeqCst) {
                api.prevent_exit();
                request_shutdown(app);
            }
        }
    });
}
