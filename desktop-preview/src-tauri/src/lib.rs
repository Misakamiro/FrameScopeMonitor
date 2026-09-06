#[cfg(test)]
mod tests;
mod overlay_panel;
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
        ;
    if let (Some(x), Some(y)) = (settings.vertical_position_x, settings.vertical_position_y) {
        builder = builder.position(x as f64, y as f64);
    }
    let window = builder.build().map_err(|e| e.to_string())?;
    window.set_ignore_cursor_events(true).map_err(|e| e.to_string())
}
fn stop_windows(app: &tauri::AppHandle) -> Result<(), String> {
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
// creating a webview inside this handler would deadlock the main thread on Windows
// (WebView2 controller init needs the message pump that the handler is blocking).
#[tauri::command]
fn preview_native(app: tauri::AppHandle, command: String, args: Value) -> Result<Value, String> {
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
        "preview_set_hardware" => {
            let data = decode_fixture(args.get("data").cloned().unwrap_or_else(|| json!({})))?;
            overlay_panel::publish_fixture(data.clone());
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
        "start_overlay_panel" => spawn_job(&app, |app| {
            let _lock = OVERLAY_OPERATION.lock().unwrap();
            if let Err(e) = start_selected(app) { eprintln!("start_overlay_panel: {e}"); }
            let _ = app.emit("overlay-status-changed", true);
        }),
        "stop_overlay_panel" | "stop_vertical_overlay" => {
            let _lock = OVERLAY_OPERATION.lock().unwrap();
            stop_windows(&app)?;
            app.emit("overlay-status-changed", false).map_err(|e| e.to_string())?;
        }
        "toggle_overlay_panel" => spawn_job(&app, |app| {
            let _lock = OVERLAY_OPERATION.lock().unwrap();
            if active(app) { let _ = stop_windows(app); } else { let _ = start_selected(app); }
            let _ = app.emit("overlay-status-changed", active(app));
        }),
        "get_overlay_panel_status" => return Ok(json!(active(&app))),
        "get_overlay_current_settings" => return serde_json::to_value(overlay_panel::get_or_init_settings()).map_err(|e| e.to_string()),
        "update_overlay_settings" => {
            let settings: overlay_panel::OverlaySettings = serde_json::from_value(args["settings"].clone()).map_err(|e| e.to_string())?;
            if settings.font_size < 8 || settings.font_size > 72 || settings.item_width < 30 || settings.item_width > 1000 {
                return Err("Overlay size outside preview limits".into());
            }
            let was_active = active(&app);
            stop_windows(&app)?;
            overlay_panel::update_settings(settings.clone())?;
            save_native_settings(&app)?;
            if was_active {
                spawn_job(&app, |app| {
                    let _lock = OVERLAY_OPERATION.lock().unwrap();
                    if let Err(e) = start_selected(app) { eprintln!("update_overlay_settings restart: {e}"); }
                });
            }
            app.emit("vertical-overlay-settings", settings).map_err(|e| e.to_string())?;
        }
        "set_overlay_drag_mode" => {
            let enabled = args["enabled"].as_bool().ok_or("enabled must be boolean")?;
            overlay_panel::set_drag_mode(enabled);
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                window.set_ignore_cursor_events(!enabled).map_err(|e| e.to_string())?;
                app.emit("vertical-overlay-drag-mode", enabled).map_err(|e| e.to_string())?;
            }
        }
        "set_vertical_overlay_click_through" => {
            if let Some(window) = app.get_webview_window("vertical-overlay") {
                window.set_ignore_cursor_events(args["enabled"].as_bool().ok_or("enabled must be boolean")?).map_err(|e| e.to_string())?;
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
            let content = args["content"].as_str().ok_or("content required")?;
            std::fs::write(path, content).map_err(|e| e.to_string())?;
            return Ok(json!(path));
        }
        _ => return Err("Unimplemented preview command".into()),
    }
    Ok(json!({"success": true, "message": "预览操作完成"}))
}
pub fn run() {
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
            let show = MenuItem::with_id(app, "show", "打开预览", true, None::<&str>)?;
            let quit = MenuItem::with_id(app, "quit", "退出预览", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show, &quit])?;
            let icon = tauri::image::Image::new_owned(vec![45, 155, 150, 255].repeat(16 * 16), 16, 16);
            tauri::tray::TrayIconBuilder::new().icon(icon).tooltip("FrameScope UI Preview").menu(&menu)
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" => { if let Some(window) = app.get_webview_window("main") { let _ = window.show(); let _ = window.set_focus(); } }
                    "quit" => { let _ = stop_windows(app); app.exit(0); }
                    _ => {}
                }).build(app)?;
            Ok(())
        })
        .on_window_event(|window, event| {
            if window.label() == "main" && matches!(event, tauri::WindowEvent::CloseRequested { .. }) {
                let _ = stop_windows(window.app_handle());
                window.app_handle().exit(0);
            }
        })
        .build(tauri::generate_context!()).expect("Unable to build isolated preview");
    app.run(|app, event| {
        if matches!(event, tauri::RunEvent::Exit | tauri::RunEvent::ExitRequested { .. }) { let _ = stop_windows(app); }
    });
}
