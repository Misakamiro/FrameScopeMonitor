use crate::{backend, report_data};
use serde_json::{json, Value};
use std::{
    collections::{HashMap, HashSet},
    path::PathBuf,
    sync::{Mutex, OnceLock},
};
use tauri::{Emitter, Manager, WebviewUrl, WebviewWindowBuilder};
#[path = "report_material.rs"]
mod material;

static OPENING: OnceLock<Mutex<HashSet<String>>> = OnceLock::new();
static PENDING: OnceLock<Mutex<HashMap<String, Vec<String>>>> = OnceLock::new();
static LOADED: OnceLock<Mutex<HashSet<String>>> = OnceLock::new();
static NORMAL_SIZE: OnceLock<Mutex<HashMap<String, (f64, f64)>>> = OnceLock::new();
pub fn resized(window: &tauri::Window) {
    if window.is_maximized().unwrap_or(true) || window.is_minimized().unwrap_or(true) {
        return;
    }
    if let (Ok(size), Ok(scale), Ok(mut sizes)) = (
        window.inner_size(),
        window.scale_factor(),
        NORMAL_SIZE.get_or_init(Default::default).lock(),
    ) {
        sizes.insert(
            window.label().into(),
            (size.width as f64 / scale, size.height as f64 / scale),
        );
    }
}
pub fn invalidate(id: &str) {
    if let Ok(mut loaded) = LOADED.get_or_init(Default::default).lock() {
        loaded.remove(id);
    }
}
pub fn closed(name: &str) {
    if let Ok(mut sizes) = NORMAL_SIZE.get_or_init(Default::default).lock() {
        sizes.remove(name);
    }
    if let Some(id) = name.strip_prefix("report-") {
        invalidate(id);
        if let Ok(mut pending) = PENDING.get_or_init(Default::default).lock() {
            pending.remove(id);
        }
    }
}
fn acknowledge(id: &str) -> Result<(), String> {
    if id == "sample" {
        return Ok(());
    }
    let tokens = PENDING
        .get_or_init(Default::default)
        .lock()
        .map_err(|e| e.to_string())?
        .remove(id)
        .unwrap_or_default();
    let mut failure = None;
    for token in tokens {
        match backend::report_acknowledge(id, &token) {
            Ok(()) => failure = None,
            Err(e) => {
                log::warn!("报告确认失败: {e}");
                failure = Some(e);
            }
        }
    }
    if let Some(e) = failure {
        return Err(e);
    }
    Ok(())
}
fn label(id: &str) -> Result<String, String> {
    if id != "sample"
        && (id.len() != 43
            || !id
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_'))
    {
        return Err("无效报告 ID".into());
    }
    Ok(format!("report-{id}"))
}
fn state_path(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    Ok(app
        .path()
        .app_data_dir()
        .map_err(|e| e.to_string())?
        .join("report-window.json"))
}
pub fn open(app: &tauri::AppHandle, id: &str, request: Option<String>) -> Result<Value, String> {
    let name = label(id)?;
    if let Some(token) = request {
        if token.len() != 32 || !token.bytes().all(|b| b.is_ascii_hexdigit()) {
            return Err("无效报告请求".into());
        }
        let mut pending = PENDING
            .get_or_init(Default::default)
            .lock()
            .map_err(|e| e.to_string())?;
        let tokens = pending.entry(id.into()).or_default();
        if !tokens.contains(&token) {
            tokens.push(token);
        }
    }
    if let Some(window) = app.get_webview_window(&name) {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        let loaded = LOADED
            .get_or_init(Default::default)
            .lock()
            .map_err(|e| e.to_string())?
            .contains(id);
        if loaded {
            acknowledge(id)?;
        } else {
            let _ = window.emit("report-changed", json!({"id":id,"kind":"reload"}));
        }
        return Ok(json!({"status":"focused"}));
    }
    {
        let mut opening = OPENING
            .get_or_init(Default::default)
            .lock()
            .map_err(|e| e.to_string())?;
        if !opening.insert(name.clone()) {
            return Ok(json!({"status":"opening"}));
        }
    }
    let result = (|| -> Result<Value, String> {
        let state = state_path(app)
            .ok()
            .and_then(|p| std::fs::read(p).ok())
            .and_then(|b| serde_json::from_slice::<Value>(&b).ok())
            .unwrap_or(Value::Null);
        let monitor = app
            .get_webview_window("main")
            .and_then(|w| w.current_monitor().ok().flatten())
            .or_else(|| app.primary_monitor().ok().flatten());
        let (available_w, available_h) = monitor
            .as_ref()
            .map(|m| {
                (
                    m.work_area().size.width as f64 / m.scale_factor(),
                    m.work_area().size.height as f64 / m.scale_factor(),
                )
            })
            .unwrap_or((1440., 900.));
        let width = state["width"]
            .as_f64()
            .filter(|v| v.is_finite())
            .unwrap_or(1440.)
            .clamp(640., 4000.)
            .min((available_w - 24.).max(320.));
        let height = state["height"]
            .as_f64()
            .filter(|v| v.is_finite())
            .unwrap_or(900.)
            .clamp(480., 2400.)
            .min((available_h - 24.).max(240.));
        let window = WebviewWindowBuilder::new(
            app,
            &name,
            WebviewUrl::App(format!("report-analysis?id={id}").into()),
        )
        .title("FrameScope 报告")
        .decorations(false)
        .transparent(true)
        .inner_size(width, height)
        .min_inner_size(640_f64.min(width), 480_f64.min(height))
        .visible(false)
        .build()
        .map_err(|e| e.to_string())?;
        material::refresh(&window);
        if let Some(m) = monitor {
            let area = m.work_area();
            window
                .set_position(tauri::PhysicalPosition::new(
                    area.position.x
                        + ((area.size.width as f64 - width * m.scale_factor()) / 2.) as i32,
                    area.position.y
                        + ((area.size.height as f64 - height * m.scale_factor()) / 2.) as i32,
                ))
                .map_err(|e| e.to_string())?;
        }
        if state["maximized"] == true {
            let _ = window.maximize();
        }
        window.show().map_err(|e| e.to_string())?;
        let _ = window.set_focus();
        Ok(json!({"status":"opening"}))
    })();
    if let Ok(mut opening) = OPENING.get_or_init(Default::default).lock() {
        opening.remove(&name);
    }
    if result.is_err() {
        closed(&name);
    }
    result
}
pub fn persist(window: &tauri::Window) {
    if window.is_minimized().unwrap_or(false) {
        return;
    }
    let Ok(path) = state_path(window.app_handle()) else {
        return;
    };
    let previous = std::fs::read(&path)
        .ok()
        .and_then(|b| serde_json::from_slice::<Value>(&b).ok())
        .unwrap_or(json!({"width":1440,"height":900}));
    let maximized = window.is_maximized().unwrap_or(false);
    let mut state = previous;
    if let Ok(sizes) = NORMAL_SIZE.get_or_init(Default::default).lock() {
        if let Some((width, height)) = sizes.get(window.label()) {
            state["width"] = json!(width);
            state["height"] = json!(height);
        }
    }
    if !maximized {
        if let (Ok(size), Ok(scale)) = (window.inner_size(), window.scale_factor()) {
            state["width"] = json!(size.width as f64 / scale);
            state["height"] = json!(size.height as f64 / scale);
        }
    }
    state["maximized"] = json!(maximized);
    if let Some(parent) = path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    let _ = std::fs::write(path, serde_json::to_vec(&state).unwrap_or_default());
}
pub fn ready(window: &tauri::WebviewWindow, id: &str) -> Result<Value, String> {
    if window.label() != label(id)? {
        return Err("报告窗口与记录不匹配".into());
    }
    LOADED
        .get_or_init(Default::default)
        .lock()
        .map_err(|e| e.to_string())?
        .insert(id.into());
    acknowledge(id)?;
    Ok(json!({"ready":true}))
}
pub fn startup_request(args: &[String]) -> Option<(String, Option<String>)> {
    let index = args.iter().position(|a| a == "--open-report")?;
    let id = args.get(index + 1)?.clone();
    let request = args
        .iter()
        .position(|a| a == "--report-request")
        .and_then(|i| args.get(i + 1))
        .cloned();
    Some((id, request))
}
pub fn dispatch(app: &tauri::AppHandle, args: &[String]) -> bool {
    let Some((id, request)) = startup_request(args) else {
        return false;
    };
    let app = app.clone();
    std::thread::spawn(move || {
        if let Err(e) = open(&app, &id, request) {
            log::warn!("报告打开失败: {e}");
        }
    });
    true
}
pub async fn command(
    app: tauri::AppHandle,
    window: tauri::WebviewWindow,
    command: String,
    args: Value,
) -> Result<Value, String> {
    let id = args["id"].as_str().ok_or("id required")?.to_owned();
    if window.label() != "main" && window.label() != label(&id)? {
        return Err("报告窗口与记录不匹配".into());
    }
    tauri::async_runtime::spawn_blocking(move || match command.as_str() {
        "report_section" => backend::report_section(&id, args["section"].as_str().ok_or("报告分区不存在")?),
        "report_inspection" => backend::report_inspection(&id, args["retry"] == true),
        "report_data" | "report_overview" => {
            let value = if command == "report_overview" {backend::report_overview(&id)?} else {backend::report_data(&id)?};
            if let Some(title) = value["title"].as_str() {
                let _ = window.set_title(title);
            }
            Ok(value)
        }
        "report_ready" => ready(&window, &id),
        "report_progress" => Ok(backend::report_progress(&id)),
        "report_material" => if args["inspect"].as_bool() == Some(true) {
            material::inspect(&window)
        } else { Ok(material::refresh(&window)) },
        "report_appearance_set" => material::set(&window, args["preference"].as_str().ok_or("preference required")?),
        "report_export_html" | "report_save_png" => {
            use tauri_plugin_dialog::DialogExt;
            let report = backend::report_data(&id)?;
            let (name, extension, bytes) = if command == "report_export_html" {
                (
                    "FrameScope-report.html",
                    "html",
                    report_data::standalone(
                        include_str!("../../report-dist/report.html"),
                        &report["data"],
                    )?
                    .into_bytes(),
                )
            } else {
                use base64::Engine;
                let raw = args["data"]
                    .as_str()
                    .and_then(|s| s.strip_prefix("data:image/png;base64,"))
                    .ok_or("无效 PNG")?;
                if raw.len() > 64 * 1024 * 1024 {
                    return Err("PNG 超过大小限制".into());
                }
                let bytes = base64::engine::general_purpose::STANDARD
                    .decode(raw)
                    .map_err(|_| "无效 PNG")?;
                if !bytes.starts_with(b"\x89PNG\r\n\x1a\n") {
                    return Err("无效 PNG".into());
                }
                ("FrameScope-chart.png", "png", bytes)
            };
            let Some(path) = app
                .dialog()
                .file()
                .set_file_name(name)
                .add_filter("报告", &[extension])
                .blocking_save_file()
            else {
                return Ok(json!({"cancelled":true}));
            };
            let path = path.into_path().map_err(|_| "请选择本地文件")?;
            std::fs::write(&path, bytes).map_err(|e| e.to_string())?;
            Ok(json!({"saved":true,"path":path}))
        }
        _ => Err("未知报告操作".into()),
    })
    .await
    .map_err(|e| e.to_string())?
}
