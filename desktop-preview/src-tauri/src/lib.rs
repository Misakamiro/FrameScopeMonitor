#[cfg(test)]
mod tests;
mod backend;
mod hardware_service;
mod delta_password;
mod overlay_panel;
mod pawnio;
use serde_json::{json, Value};
use std::sync::Mutex;
use tauri::{Emitter, Manager, WebviewUrl, WebviewWindowBuilder};

static OVERLAY_OPERATION: Mutex<()> = Mutex::new(());
const COMMANDS: &[&str] = &[
    "preview_set_hardware", "open_sensor_monitor", "start_overlay_panel", "stop_overlay_panel",
    "toggle_overlay_panel", "get_overlay_panel_status", "update_overlay_settings", "get_overlay_current_settings",
    "set_overlay_drag_mode", "reset_overlay_position", "open_vertical_overlay", "stop_vertical_overlay",
    "set_vertical_overlay_click_through", "resize_vertical_overlay", "save_vertical_overlay_position",
    "reset_vertical_overlay_position", "vertical_overlay_ready", "minimize_to_tray", "exit_app",
    "set_overlay_hotkey", "export_hardware_report", "open_report_sample",
    // Real hardware data, served by FrameScopeHardwareService over the named pipe.
    "get_hardware", "get_hardware_info", "get_all_sensors", "get_overlay_hardware_data",
    "get_memory_status", "get_disk_status", "is_nvidia_gpu", "set_active_gpu_index",
    "clear_hardware_data", "get_system_locale", "get_system_username",
    // Real capture pipeline (FrameScopeMonitor.exe chain, same file contracts
    // as the C# WebView2 bridge).
    "targets_get", "targets_save", "processes_refresh", "config_save", "config_read",
    "monitor_start", "monitor_stop", "monitor_status",
    "reports_list", "report_open", "report_open_directory", "report_regenerate",
    // PawnIO driver detection / install / uninstall.
    "check_pawnio_status", "install_pawnio_driver", "uninstall_pawnio_driver",
    "restart_monitor_process",
    // Delta Force password doors (community API, 60s cache).
    "get_delta_passwords",
];
fn ensure_allowed(command: &str) -> Result<(), String> {
    if COMMANDS.contains(&command) { Ok(()) } else { Err(format!("Unsupported preview command: {command}")) }
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
    WebviewWindowBuilder::new(app, "sensor-monitor", WebviewUrl::App("sensor-monitor".into()))
        .title("FrameScope UI Preview - Sensors").inner_size(1000.0, 720.0)
        .min_inner_size(760.0, 480.0).resizable(true).always_on_top(true)
        .build().map(|_| ()).map_err(|e| e.to_string())
}
fn open_vertical(app: &tauri::AppHandle) -> Result<(), String> {
    overlay_panel::stop_overlay()?;
    if let Some(window) = app.get_webview_window("vertical-overlay") {
        return window.show().map_err(|e| e.to_string());
    }
    let settings = overlay_panel::get_or_init_settings();
    let mut builder = WebviewWindowBuilder::new(app, "vertical-overlay", WebviewUrl::App("vertical-overlay".into()))
        .title("FrameScope UI Preview - Overlay").inner_size(240.0, 420.0)
        .decorations(false).transparent(true).always_on_top(true).skip_taskbar(true).resizable(false)
        // Windows: undecorated windows default to a DWM shadow, and that shadow
        // forces an opaque compositor backdrop - the transparency slider never
        // reaches the screen without shadow(false) (NexBox upstream does the same).
        .shadow(false)
        ;
    if let (Some(x), Some(y)) = (settings.vertical_position_x, settings.vertical_position_y) {
        builder = builder.position(x as f64, y as f64);
    }
    let window = builder.build().map_err(|e| e.to_string())?;
    overlay_set_hit_test_transparent(&window, true);
    #[cfg(windows)]
    apply_vertical_backdrop_kill(&window);
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

fn stop_windows(app: &tauri::AppHandle) -> Result<(), String> {
    // The GDI window proc updates CURRENT_SETTINGS on WM_EXITSIZEMOVE but
    // only flags it (POSITION_CHANGED); persist on the next stop so a dragged
    // position survives restarts instead of being dropped.
    if overlay_panel::take_position_changed() {
        let _ = save_native_settings(app);
    }
    overlay_panel::stop_overlay()?;
    if let Some(window) = app.get_webview_window("vertical-overlay") { window.close().map_err(|e| e.to_string())?; }
    Ok(())
}
fn active(app: &tauri::AppHandle) -> bool {
    overlay_panel::is_overlay_active() || app.get_webview_window("vertical-overlay").is_some()
}
fn start_selected(app: &tauri::AppHandle) -> Result<(), String> {
    let settings = overlay_panel::get_or_init_settings();
    if settings.style == "vertical_panel" { open_vertical(app) }
    else {
        if let Some(window) = app.get_webview_window("vertical-overlay") { let _ = window.close(); }
        overlay_panel::start_overlay(settings).map(|_| ())
    }
}
fn save_native_settings(app: &tauri::AppHandle) -> Result<(), String> {
    let dir = app.path().app_data_dir().map_err(|e| e.to_string())?;
    std::fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let data = json!({"overlay-settings": overlay_panel::get_or_init_settings()});
    std::fs::write(dir.join("preview-native-settings.json"), data.to_string()).map_err(|e| e.to_string())
}
// Sync command. Window-creating flows are dispatched to worker threads below:
// creating a webview inside this handler would deadlock the main thread
// (WebView2 controller init needs the message pump that the handler is blocking).
// The command itself is async so long-running calls (report generation,
// PawnIO installer, hardware queries) occupy the async runtime's thread pool
// instead of freezing the window's message pump.
#[tauri::command]
async fn preview_native(app: tauri::AppHandle, command: String, args: Value) -> Result<Value, String> {
    ensure_allowed(&command)?;
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
                let error = response["errors"].as_array().and_then(|e| e[0].as_str()).unwrap_or("硬件信息不可用");
                return Err(error.to_string());
            }
            return Ok(hardware);
        }
        "get_all_sensors" => {
            let response = hardware_service::call("get_sensors", json!({}))?;
            if response["availability"] == "unavailable" && response["sensors"].as_array().map(|s| s.is_empty()).unwrap_or(true) {
                let error = response["errors"].as_array().and_then(|e| e[0].as_str()).unwrap_or("传感器数据不可用");
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
        "get_overlay_hardware_data" => return hardware_service::call("get_overlay_data", json!({})),
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
            let target_id = args["targetId"].as_str().ok_or("targetId required")?;
            return backend::monitor_start(target_id);
        }
        "monitor_stop" => return backend::monitor_stop(),
        "monitor_status" => return backend::monitor_status(),
        "reports_list" => return backend::reports_list(),
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
            // Dragged window positions live in this store, not in the settings
            // page's React state (which snapshots them at page load). A page
            // saved before a drag still carries null positions here - never
            // let those clobber the live ones (measured loss: drag -> move the
            // opacity slider -> position silently reset).
            let mut settings = settings;
            {
                let current = overlay_panel::get_or_init_settings();
                if settings.vertical_position_x.is_none() { settings.vertical_position_x = current.vertical_position_x; }
                if settings.vertical_position_y.is_none() { settings.vertical_position_y = current.vertical_position_y; }
                if settings.position_x.is_none() { settings.position_x = current.position_x; }
                if settings.position_y.is_none() { settings.position_y = current.position_y; }
            }
            // Hold the same operation lock every other start/stop path uses:
            // update_settings tears the overlay down and rebuilds it, and an
            // unserialized rebuild could double-create windows (R-A8).
            let _operation_lock = OVERLAY_OPERATION.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
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
                    if overlay_panel::request_overlay_repaint() {
                        save_native_settings(&app)?;
                        app.emit("vertical-overlay-settings", settings).map_err(|e| e.to_string())?;
                        return Ok(Value::Null);
                    }
                    // repaint post failed (window gone?) - fall through to rebuild
                }
                stop_windows(&app)?;
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
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                overlay_set_hit_test_transparent(&window, args["enabled"].as_bool().ok_or("enabled must be boolean")?);
            }
        }
        "resize_vertical_overlay" => {
            let height = args["height"].as_f64().ok_or("height must be a number")?.clamp(40.0, 1600.0);
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                window.set_size(tauri::LogicalSize::new(overlay_panel::get_or_init_settings().item_width as f64, height)).map_err(|e| e.to_string())?;
            }
        }
        "save_vertical_overlay_position" | "reset_vertical_overlay_position" | "reset_overlay_position" => {
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
            app.global_shortcut().unregister_all().map_err(|e| e.to_string())?;
            app.global_shortcut().register(shortcut).map_err(|e| e.to_string())?;
        }
        "minimize_to_tray" => app.get_webview_window("main").ok_or("main missing")?.hide().map_err(|e| e.to_string())?,
        "exit_app" => { stop_windows(&app)?; app.exit(0); }
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
    // Diagnostics (ETW/permission/pipe failures) used to vanish because no
    // logger was installed; stderr is enough for a desktop app and costs
    // nothing in release.
    struct StderrLogger;
    impl log::Log for StderrLogger {
        fn enabled(&self, metadata: &log::Metadata) -> bool {
            metadata.level() <= log::Level::Info
        }
        fn log(&self, record: &log::Record) {
            eprintln!("[{}] {}", record.level(), record.args());
        }
        fn flush(&self) {}
    }
    static LOGGER: StderrLogger = StderrLogger;
    let _ = log::set_logger(&LOGGER).map(|_| log::set_max_level(log::LevelFilter::Info));

    let app = tauri::Builder::default()
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
        .setup(|app| {
            use tauri::menu::{Menu, MenuItem};
            // Restore persisted overlay settings (style/items/positions) BEFORE
            // anything can start the panel: this was defined but never called,
            // so every launch reset the overlay to the GDI default style even
            // though the settings file said vertical_panel.
            overlay_panel::try_load_persisted_settings(app.handle());
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
                    "quit" => { let _ = stop_windows(app); app.exit(0); }
                    _ => {}
                }).build(app)?;
            // Resident hardware poller (NexBox start_hardware_poller pattern):
            // 1 Hz service reads feed the GDI overlay cache and the vertical
            // overlay event stream, so both overlays show real data without
            // any frontend fixture push.
            let handle = app.handle().clone();
            std::thread::spawn(move || {
                let mut error_streak: u32 = 0;
                loop {
                    std::thread::sleep(std::time::Duration::from_millis(1000));
                    // R-A7: PawnIO setup kills and re-creates the sampling
                    // service; stay completely hands-off until it finishes.
                    if hardware_service::poller_paused() {
                        continue;
                    }
                    match hardware_service::call("get_overlay_data", json!({})) {
                        Ok(data) => {
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
            Ok(())
        })
        .on_window_event(|window, event| {
            if window.label() == "main" {
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
                        _ => { let _ = stop_windows(window.app_handle()); window.app_handle().exit(0); }
                    }
                }
            }
        })
        .build(tauri::generate_context!()).expect("Unable to build isolated preview");
    app.run(|app, event| {
        if matches!(event, tauri::RunEvent::Exit | tauri::RunEvent::ExitRequested { .. }) { let _ = stop_windows(app); }
    });
}
