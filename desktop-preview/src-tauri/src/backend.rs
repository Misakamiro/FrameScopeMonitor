//! Bridge to the real FrameScope capture pipeline (C# FrameScopeMonitor.exe
//! family). Reads and writes the exact same file contracts as the C# WebView2
//! bridge: `framescope-config.json` (PascalCase, FrameScopeConfigStore
//! schema), `framescope-watcher-state.json`, `framescope-history.jsonl`, and
//! the run-directory layout (`<dataRoot>\<Target>\<run>\charts\...`).

use serde_json::{json, Value};
use std::collections::BTreeMap;
use std::path::{Path, PathBuf};
#[cfg(test)]
#[path = "report_operations_tests.rs"]
mod report_operations_tests;
static REPORT_OPERATION: std::sync::Mutex<()> = std::sync::Mutex::new(());
static CONFIG_OPERATION: std::sync::Mutex<()> = std::sync::Mutex::new(());
#[cfg(all(test, windows))]
#[path = "config_lock_tests.rs"]
mod config_lock_tests;

struct ConfigFileMutex {
    #[cfg(windows)] handle: windows_sys::Win32::Foundation::HANDLE,
}
#[cfg(windows)]
impl Drop for ConfigFileMutex {
    fn drop(&mut self) { unsafe { windows_sys::Win32::System::Threading::ReleaseMutex(self.handle); windows_sys::Win32::Foundation::CloseHandle(self.handle); } }
}

fn acquire_config_file_mutex(path: &Path) -> Result<ConfigFileMutex, String> {
    acquire_config_file_mutex_with_timeout(path, 30_000)
}

fn acquire_config_file_mutex_with_timeout(path: &Path, timeout_ms: u32) -> Result<ConfigFileMutex, String> {
    #[cfg(windows)]
    {
        use sha2::{Digest, Sha256};
        use std::os::windows::ffi::OsStrExt;
        use windows_sys::Win32::Foundation::WAIT_OBJECT_0;
        use windows_sys::Win32::System::Threading::{CreateMutexW, WaitForSingleObject};
        let filename = path.file_name().ok_or("configuration filename missing")?;
        let parent = path.parent().filter(|p| !p.as_os_str().is_empty()).unwrap_or(Path::new("."));
        std::fs::create_dir_all(parent).map_err(|error| error.to_string())?;
        let parent = parent.canonicalize().map_err(|error| error.to_string())?;
        let full = parent.join(filename).to_string_lossy().replace('/', "\\");
        let full = if let Some(unc) = full.strip_prefix(r"\\?\UNC\") { format!(r"\\{}", unc) }
            else { full.strip_prefix(r"\\?\").unwrap_or(&full).to_string() };
        let input: Vec<u16> = full.encode_utf16().collect();
        use windows_sys::Win32::Globalization::{LCMapStringEx, LCMAP_UPPERCASE};
        let locale = [0u16];
        let size = unsafe { LCMapStringEx(locale.as_ptr(), LCMAP_UPPERCASE, input.as_ptr(), input.len() as i32,
            std::ptr::null_mut(), 0, std::ptr::null_mut(), std::ptr::null_mut(), 0) };
        if size == 0 { return Err(std::io::Error::last_os_error().to_string()); }
        let mut mapped = vec![0u16; size as usize];
        if unsafe { LCMapStringEx(locale.as_ptr(), LCMAP_UPPERCASE, input.as_ptr(), input.len() as i32,
            mapped.as_mut_ptr(), size, std::ptr::null_mut(), std::ptr::null_mut(), 0) } != size {
            return Err(std::io::Error::last_os_error().to_string());
        }
        let normalized = String::from_utf16(&mapped).map_err(|error| error.to_string())?;
        let digest = Sha256::digest(normalized.as_bytes());
        let name = format!("Local\\FrameScopeConfig-{}", digest.iter().map(|b| format!("{:02X}", b)).collect::<String>());
        let wide: Vec<u16> = std::ffi::OsStr::new(&name).encode_wide().chain(std::iter::once(0)).collect();
        let handle = unsafe { CreateMutexW(std::ptr::null(), 0, wide.as_ptr()) };
        if handle.is_null() { return Err("configuration mutex creation failed".into()); }
        let result = unsafe { WaitForSingleObject(handle, timeout_ms) };
        if result != WAIT_OBJECT_0 && result != 0x0000_0080 { unsafe { windows_sys::Win32::Foundation::CloseHandle(handle); } return Err("configuration mutex timeout".into()); }
        return Ok(ConfigFileMutex { handle });
    }
    #[cfg(not(windows))]
    { let _ = path; Ok(ConfigFileMutex {}) }
}
static REGENERATING_RUN: std::sync::Mutex<Option<PathBuf>> = std::sync::Mutex::new(None);
static OWNED_WATCHER: std::sync::Mutex<Option<OwnedWatcher>> = std::sync::Mutex::new(None);
static HOST_CLOSING: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);
static UPDATE_PENDING: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

pub fn ensure_update_idle() -> Result<(),String> {
    ensure_not_closing()?;
    let status=monitor_status()?;
    match status["phase"].as_str() {
        Some(phase) if framescope_update::installation_allowed(phase)=>Ok(()),
        _=>Err("请先正常停止监控，等待采集和报告收尾，再安装或恢复版本".into()),
    }
}
pub fn reserve_update()->Result<(),String>{
    let owned=OWNED_WATCHER.lock().unwrap_or_else(|p|p.into_inner());
    let report=REPORT_OPERATION.try_lock().map_err(|_|"报告正在处理，不能安装更新")?;
    if UPDATE_PENDING.swap(true,std::sync::atomic::Ordering::SeqCst){return Err("更新已在进行".into());}
    drop(report);drop(owned);
    let status=monitor_status();
    if !status.is_ok_and(|s|s["phase"].as_str().is_some_and(framescope_update::installation_allowed)){release_update();return Err("监控正在运行或收尾，请先正常停止监控".into());}
    Ok(())
}
pub fn release_update(){UPDATE_PENDING.store(false,std::sync::atomic::Ordering::SeqCst);}

struct OwnedWatcher {
    child: std::process::Child,
    root: PathBuf,
    identity: WatcherOwner,
    token: Option<String>,
}

pub fn begin_shutdown() -> bool {
    !HOST_CLOSING.swap(true, std::sync::atomic::Ordering::SeqCst)
}

pub fn cancel_shutdown() {
    HOST_CLOSING.store(false, std::sync::atomic::Ordering::SeqCst);
}

pub fn ensure_not_closing() -> Result<(), String> {
    if UPDATE_PENDING.load(std::sync::atomic::Ordering::SeqCst){return Err("正在安装或恢复版本，不能开始新操作".into());}
    if HOST_CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
        Err("正在等待监控和报告收尾，不能启动新窗口或监控".into())
    } else { Ok(()) }
}

struct ReportRegeneration;
impl Drop for ReportRegeneration {
    fn drop(&mut self) { *REGENERATING_RUN.lock().unwrap_or_else(|p| p.into_inner()) = None; }
}

// ---------------------------------------------------------------- root discovery

/// Locates the directory holding the C# pipeline executables:
/// `FRAMESCOPE_BACKEND_DIR`, then the preview exe's own directory, then up to
/// four ancestors that contain `FrameScopeMonitor.exe` (dev-tree layout:
/// desktop-preview/src-tauri/target/release -> repo root).
pub fn backend_root() -> Result<PathBuf, String> {
    if let Ok(dir) = std::env::var("FRAMESCOPE_BACKEND_DIR") {
        if !dir.trim().is_empty() && Path::new(dir.trim()).join("FrameScopeMonitor.exe").exists() {
            return Ok(PathBuf::from(dir.trim()));
        }
    }
    let exe = std::env::current_exe().map_err(|_| "cannot resolve executable directory")?;
    let mut dir = exe.parent().ok_or("exe has no parent")?.to_path_buf();
    for depth in 0..=4 {
        if depth > 0 {
            dir = dir.parent().ok_or("no ancestor directory")?.to_path_buf();
        }
        if dir.join("FrameScopeMonitor.exe").exists() {
            return Ok(dir);
        }
    }
    Err("FrameScope backend executables are not deployed (FrameScopeMonitor.exe not found)".into())
}

fn config_path() -> Result<PathBuf, String> {
    Ok(backend_root()?.join("framescope-config.json"))
}

fn watcher_state_path() -> Result<PathBuf, String> {
    Ok(backend_root()?.join("framescope-watcher-state.json"))
}

fn history_path() -> Result<PathBuf, String> {
    Ok(backend_root()?.join("framescope-history.jsonl"))
}

// ---------------------------------------------------------------- config / targets

/// Default targets, verbatim from FrameScopeConfigStore (the C# bridge writes
/// this file on first load; the preview host mirrors that behavior).
fn default_config() -> Value {
    let target = |name: &str, process: &str, enabled: bool| {
        json!({
            "Enabled": enabled, "Name": name, "ProcessName": process,
            "SampleIntervalMs": 1000, "ProcessSamplingMode": "normal",
            "ProcessSampleIntervalMs": 1000, "SlowSampleIntervalMs": 1000,
            "OpenReportOnComplete": true
        })
    };
    json!({
        "PollIntervalMs": 1000, "TelemetrySampleIntervalMs": 1000, "DataRoot": "",
        "OpenReportOnComplete": true, "AutoStartMonitoring": true,
        "AutoStartOverlay": false, "StartMinimized": false, "EnableVerboseLogs": false,
        "EnablePerformanceDiagnosticsLogs": false, "AutoGenerateDiagnosticReport": false,
        "LogRetentionDays": 14, "MaxLogDiskMb": 100, "MonitorScript": "native-csharp",
        "ThemeMode": "system", "VisualEffectStrength": "high",
        "CloseWindowBehavior": "minimize-to-tray", "TrayEnabled": true,
        "CpuTelemetry": {
            "CollectPerCoreFrequency": true, "CollectCpuVoltage": false,
            "PerCoreSampleIntervalMs": 1000, "PerCoreVoltageSampleIntervalMs": 1000,
            "VoltageProvider": "auto"
        },
        "Targets": [
            target("Counter-Strike 2", "cs2.exe", true),
            target("PUBG: BATTLEGROUNDS", "TslGame.exe", true),
            target("Delta Force", "DeltaForceClient-Win64-Shipping.exe", true),
            target("Neverness To Everness", "HTGame.exe", true),
            target("Valorant", "VALORANT-Win64-Shipping.exe", false),
            target("Cyberpunk 2077", "Cyberpunk2077.exe", false),
            target("Battlefield 6", "bf6.exe", false),
            target("Hogwarts Legacy", "HogwartsLegacy.exe", false),
            target("OPUS Prism Peak", "OPUS_ Prism Peak.exe", false)
        ]
    })
}

fn load_config() -> Result<(PathBuf, Value), String> {
    let path = config_path()?;
    let _file_guard = acquire_config_file_mutex(&path)?;
    if !path.exists() {
        let config = default_config();
        write_json_atomic(&path, &config)?;
        return Ok((path, config));
    }
    let text = std::fs::read_to_string(&path).map_err(|e| e.to_string())?;
    match serde_json::from_str::<Value>(&text) {
        Ok(value) => Ok((path, value)),
        // Mirror the C# loader: parse errors fall back to defaults without
        // overwriting the broken file.
        Err(_) => Ok((path, default_config())),
    }
}

fn write_json_atomic(path: &Path, value: &Value) -> Result<(), String> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
    }
    use std::sync::atomic::{AtomicU64, Ordering};
    static TMP_SEQ: AtomicU64 = AtomicU64::new(0);
    let seq = TMP_SEQ.fetch_add(1, Ordering::Relaxed);
    let temp = path.with_extension(format!("fsj-{}-{}", std::process::id(), seq));
    std::fs::write(&temp, serde_json::to_string_pretty(value).unwrap_or_default())
        .map_err(|e| e.to_string())?;
    std::fs::rename(&temp, path).map_err(|e| e.to_string())
}

/// Read-modify-write that only replaces the Targets array; every other config
/// field keeps its current value (PascalCase contract preserved verbatim).
pub fn targets_save(targets: &[Value]) -> Result<Value, String> {
    let _guard = CONFIG_OPERATION.lock().unwrap_or_else(|p| p.into_inner());
    let lock_path = config_path()?;
    let _file_guard = acquire_config_file_mutex(&lock_path)?;
    let (path, mut config) = load_config()?;
    let normalized: Vec<Value> = targets
        .iter()
        .filter_map(|target| {
            let executable = target.get("executable").and_then(Value::as_str)?.trim();
            if executable.is_empty() {
                return None;
            }
            let executable = normalize_process_name(executable);
            if executable.is_empty() {
                return None;
            }
            let name = target
                .get("name")
                .and_then(Value::as_str)
                .map(str::trim)
                .filter(|value| !value.is_empty())
                .map(str::to_string)
                .unwrap_or_else(|| executable.trim_end_matches(".exe").to_string());
            let enabled = target.get("enabled").and_then(Value::as_bool).unwrap_or(false);
            let mut row = config.get("Targets").and_then(Value::as_array)
                .and_then(|existing| existing.iter().find(|row| row.is_object() && row.get("ProcessName")
                    .and_then(Value::as_str).is_some_and(|process| normalize_process_name(process).eq_ignore_ascii_case(&executable))))
                .cloned().unwrap_or_else(|| json!({
                "SampleIntervalMs": 1000, "ProcessSamplingMode": "normal",
                "ProcessSampleIntervalMs": 1000, "SlowSampleIntervalMs": 1000,
                "OpenReportOnComplete": true
            }));
            row["Enabled"] = json!(enabled);
            row["Name"] = json!(name);
            row["ProcessName"] = json!(executable);
            Some(row)
        })
        .collect();
    config["Targets"] = Value::Array(normalized);
    write_json_atomic(&path, &config)?;
    targets_payload("saved")
}

/// M6: persists the preview settings page. Only fields the capture chain
/// actually honors are mapped into the PascalCase config contract - the page
/// pretends to save more, but silently ignoring those here beats lying in the
/// file. Unknown/unmapped keys never touch the config (M7 stays intact).
pub fn config_save(update: &Value) -> Result<Value, String> {
    let _guard = CONFIG_OPERATION.lock().unwrap_or_else(|p| p.into_inner());
    let lock_path = config_path()?;
    let _file_guard = acquire_config_file_mutex(&lock_path)?;
    let (path, mut config) = load_config()?;
    // The C# side reads these with JavaScriptSerializer into Int32 fields,
    // and a float literal ("1000.0") makes the whole-config deserialize
    // throw - which ConfigStore.Load swallows into the default config,
    // losing every user target. Always write whole numbers.
    let mut map_number = |key: &str, target: &str| {
        if let Some(value) = update.get(key).and_then(Value::as_f64) {
            config[target] = json!(value.round() as i64);
        }
    };
    map_number("sampleIntervalMs", "TelemetrySampleIntervalMs");
    map_number("pollIntervalMs", "PollIntervalMs");
    if let Some(value) = update.get("autoStart").and_then(Value::as_bool) {
        config["AutoStartMonitoring"] = json!(value);
    }
    if let Some(value) = update.get("autoStartOverlay").and_then(Value::as_bool) {
        config["AutoStartOverlay"] = json!(value);
    }
    if let Some(value) = update.get("startMinimized").and_then(Value::as_bool) {
        config["StartMinimized"] = json!(value);
    }
    if let Some(value) = update.get("autoOpenReport").and_then(Value::as_bool) {
        config["OpenReportOnComplete"] = json!(value);
    }
    if let Some(value) = update.get("detailedLogging").and_then(Value::as_bool) {
        config["EnableVerboseLogs"] = json!(value);
    }
    if let Some(value) = update.get("diagnosticLogging").and_then(Value::as_bool) {
        config["EnablePerformanceDiagnosticsLogs"] = json!(value);
    }
    if let Some(value) = update.get("closeWindowBehavior").and_then(Value::as_str) {
        // Same vocabulary the C# NormalizeChoice allows; unknown values are
        // ignored so a stale client cannot write garbage.
        if matches!(value, "ask" | "minimize-to-tray" | "exit") {
            config["CloseWindowBehavior"] = json!(value);
        }
    }
    write_json_atomic(&path, &config)?;
    Ok(json!({ "status": "saved" }))
}

/// Host-side close policy for the main window's CloseRequested. Fail-closed
/// to "exit": a broken config must never leave the user unable to quit.
pub fn close_window_behavior() -> String {
    load_config()
        .ok()
        .and_then(|(_, config)| config.get("CloseWindowBehavior").and_then(Value::as_str).map(str::to_string))
        .unwrap_or_else(|| "exit".into())
}

/// Frontend-facing settings read. The settings page used to initialize from
/// hardcoded preview defaults and never saw the real file, so values saved on
/// desktop looked lost after a restart. Maps the backend-owned keys back to
/// the camelCase preview contract; missing/invalid values fall back to the
/// same defaults config_save writes (fail-closed, same spirit as the C#
/// loader, which silently falls back on a broken file).
pub fn config_read() -> Result<Value, String> {
    let (_, config) = load_config()?;
    let whole_number = |key: &str, min: i64, max: i64, fallback: i64| -> i64 {
        config
            .get(key)
            .and_then(Value::as_f64)
            .map(|value| value.round() as i64)
            .filter(|value| *value > 0)
            .map(|value| value.clamp(min, max))
            .unwrap_or(fallback)
    };
    let boolean = |key: &str, fallback: bool| config.get(key).and_then(Value::as_bool).unwrap_or(fallback);
    Ok(json!({
        "status": "loaded",
        "config": {
            "sampleIntervalMs": whole_number("TelemetrySampleIntervalMs", 500, 5000, 1000),
            "pollIntervalMs": whole_number("PollIntervalMs", 250, 10000, 1000),
            "autoOpenReport": boolean("OpenReportOnComplete", true),
            "autoStart": boolean("AutoStartMonitoring", true),
            "autoStartOverlay": boolean("AutoStartOverlay", false),
            "startMinimized": boolean("StartMinimized", false),
            "detailedLogging": boolean("EnableVerboseLogs", false),
            "diagnosticLogging": boolean("EnablePerformanceDiagnosticsLogs", false),
            "closeWindowBehavior": config
                .get("CloseWindowBehavior")
                .and_then(Value::as_str)
                .unwrap_or("minimize-to-tray")
        }
    }))
}

fn normalize_process_name(value: &str) -> String {
    let parts: Vec<String> = value
        .split(';')
        .map(str::trim)
        .filter(|part| !part.is_empty())
        .map(|part| {
            // Single alias without an extension gets ".exe" (target edit rules).
            if !part.contains(';') && !part.to_ascii_lowercase().ends_with(".exe") {
                format!("{part}.exe")
            } else {
                part.to_string()
            }
        })
        .collect();
    parts.join(";")
}

/// Frontend-facing target list (camelCase PreviewTarget contract; the
/// process name doubles as the stable id, same role ProcessName plays in C#).
pub fn targets_payload(status: &str) -> Result<Value, String> {
    let (path, config) = load_config()?;
    let targets: Vec<Value> = config
        .get("Targets")
        .and_then(Value::as_array)
        .map(|list| {
            list.iter()
                .map(|target| {
                    let process = target.get("ProcessName").and_then(Value::as_str).unwrap_or("");
                    json!({
                        "id": process,
                        "name": target.get("Name").and_then(Value::as_str).unwrap_or(""),
                        "executable": process,
                        "enabled": target.get("Enabled").and_then(Value::as_bool).unwrap_or(false)
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    let enabled = targets.iter().filter(|t| t["enabled"] == true).count();
    Ok(json!({
        "status": status, "configPath": path.to_string_lossy(),
        "dataRoot": config.get("DataRoot").cloned().unwrap_or(Value::Null),
        "targets": targets, "targetCount": targets.len(), "enabledTargetCount": enabled
    }))
}

// ---------------------------------------------------------------- data root

pub fn resolved_data_root() -> Result<PathBuf, String> {
    let (_, config) = load_config()?;
    let raw = config.get("DataRoot").and_then(Value::as_str).unwrap_or("");
    let root = if raw.trim().is_empty() {
        let local = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".into());
        Path::new(&local)
            .join("FrameScopeMonitorData")
            .join("framescope-runs")
    } else {
        let candidate = PathBuf::from(raw);
        if candidate.is_absolute() {
            candidate
        } else {
            backend_root()?.join(candidate)
        }
    };
    Ok(root)
}

// ---------------------------------------------------------------- processes

/// Full process enumeration with main-window titles (the v1.2.2 bridge
/// semantics: front windows first, 250 entries max).
pub fn processes_refresh(query: &str) -> Result<Value, String> {
    use sysinfo::System;
    let mut system = System::new();
    system.refresh_processes();

    let titles = window_titles();
    let mut rows: Vec<(String, u32, String)> = Vec::new();
    for (pid, process) in system.processes() {
        let name = process.name().to_string();
        if name.is_empty() {
            continue;
        }
        let name = if name.to_ascii_lowercase().ends_with(".exe") {
            name
        } else {
            format!("{name}.exe")
        };
        let title = titles.get(&pid.as_u32()).cloned().unwrap_or_default();
        rows.push((name, pid.as_u32(), title));
    }
    let needle = query.to_ascii_lowercase();
    let mut items: Vec<Value> = rows
        .into_iter()
        .filter(|(name, _, title)| {
            needle.is_empty()
                || name.to_ascii_lowercase().contains(&needle)
                || title.to_ascii_lowercase().contains(&needle)
        })
        .map(|(name, pid, title)| {
            let display = if title.is_empty() {
                name.clone()
            } else {
                format!("{title} ({name})")
            };
            json!({"processName": name, "processId": pid, "windowTitle": title, "displayText": display})
        })
        .collect();
    // Windows-bearing entries first, then by display text (v1.2.2 ordering).
    items.sort_by_key(|item| {
        let title = item["windowTitle"].as_str().unwrap_or("");
        (
            !title.is_empty(),
            item["displayText"].as_str().unwrap_or("").to_string(),
        )
    });
    items.truncate(250);
    Ok(json!({"status": "completed", "query": query, "count": items.len(), "processes": items}))
}

#[cfg(windows)]
fn window_titles() -> BTreeMap<u32, String> {
    type HWND = *mut core::ffi::c_void;
    #[link(name = "user32")]
    extern "system" {
        fn EnumWindows(callback: usize, lparam: isize) -> i32;
        fn GetWindowTextW(hwnd: HWND, buffer: *mut u16, max: i32) -> i32;
        fn IsWindowVisible(hwnd: HWND) -> i32;
        fn GetWindowThreadProcessId(hwnd: HWND, pid: *mut u32) -> u32;
    }
    struct Sink {
        map: BTreeMap<u32, String>,
    }
    unsafe extern "system" fn on_window(hwnd: *mut core::ffi::c_void, lparam: isize) -> i32 {
        let sink = unsafe { &mut *(lparam as *mut Sink) };
        if unsafe { IsWindowVisible(hwnd) } == 0 {
            return 1;
        }
        let mut buffer = [0u16; 512];
        let len = unsafe { GetWindowTextW(hwnd, buffer.as_mut_ptr(), 512) };
        if len <= 0 {
            return 1;
        }
        let mut pid = 0u32;
        unsafe { GetWindowThreadProcessId(hwnd, &mut pid) };
        if pid != 0 {
            let title = String::from_utf16_lossy(&buffer[..len as usize]);
            sink.map.entry(pid).or_insert(title);
        }
        1
    }
    let mut sink = Sink {
        map: BTreeMap::new(),
    };
    unsafe { EnumWindows(on_window as usize, &mut sink as *mut Sink as isize) };
    sink.map
}

#[cfg(not(windows))]
fn window_titles() -> BTreeMap<u32, String> {
    BTreeMap::new()
}

// ---------------------------------------------------------------- monitor control

pub fn monitor_start() -> Result<Value, String> {
    if HOST_CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
        return Err("正在等待监控和报告收尾，不能启动新监控".into());
    }
    let mut owned = OWNED_WATCHER.lock().unwrap_or_else(|p| p.into_inner());
    ensure_not_closing()?;
    if HOST_CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
        return Err("正在等待监控和报告收尾，不能启动新监控".into());
    }
    if let Some(watcher) = owned.as_mut() {
        if watcher.child.try_wait().map_err(|e| e.to_string())?.is_none() {
            return Ok(json!({"status":"started", "alreadyRunning":true}));
        }
    }
    if watcher_running() {
        return Ok(json!({"status": "started", "alreadyRunning": true}));
    }
    let root = backend_root()?;
    let (config_path, _) = load_config()?;
    let mut command = std::process::Command::new(root.join("FrameScopeMonitor.exe"));
    command.arg("--watcher").arg("--config").arg(&config_path);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    }
    let child = command
        .spawn()
        .map_err(|error| format!("watcher spawn failed: {error}"))?;
    let pid = child.id();
    let identity = WatcherOwner { pid, exe: root.join("FrameScopeMonitor.exe"),
        start_time: child_start_time(&child),
        args: vec!["--watcher".into(), "--config".into(), config_path.to_string_lossy().into_owned()] };
    *owned = Some(OwnedWatcher { child, root, identity, token: None });
    if let Some(watcher) = owned.as_mut() { let _ = owned_state(watcher); }
    *AUTO_START_ERROR.lock().unwrap_or_else(|p| p.into_inner()) = None;
    Ok(json!({"status": "started", "alreadyRunning": false, "watcherPid": pid}))
}

static AUTO_START_ERROR: std::sync::Mutex<Option<String>> = std::sync::Mutex::new(None);

pub fn startup_preferences() -> Result<(bool, bool), String> {
    let (_, config) = load_config()?;
    Ok((config["AutoStartOverlay"].as_bool().unwrap_or(false),
        config["StartMinimized"].as_bool().unwrap_or(false)))
}

pub fn auto_start_monitor() -> Result<(), String> {
    if HOST_CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
        return Err("正在退出，不能启动监控".into());
    }
    let (_, config) = load_config()?;
    if config["AutoStartMonitoring"].as_bool().unwrap_or(true) {
        if let Err(error) = monitor_start() {
            *AUTO_START_ERROR.lock().unwrap_or_else(|p| p.into_inner()) = Some(error.clone());
            return Err(error);
        }
    }
    Ok(())
}

#[cfg(test)]
mod report_live_lows_tests {
    use super::*;
    #[test]
    fn fresh_frame_fps_fills_missing_resident_fps_for_the_verified_target_only() {
        let data = json!({"target_pid":0,"fps":null,"fps_1low":null,"fps_01low":null});
        let frames = json!({"target_pid":99,"fps":144.5,"fps_1low":100.0,"fps_01low":90.0});
        let filled = apply_frame_snapshot(data.clone(), 99, Some(&frames));
        assert_eq!(filled["fps"], 144.5);
        assert_eq!(filled["target_pid"], 99);
        assert_eq!(filled["fps_1low"], 100.0);
        assert!(apply_frame_snapshot(data.clone(), 100, Some(&frames))["fps"].is_null());
        assert!(apply_frame_snapshot(data, 99, None)["fps"].is_null());
        let resident = json!({"target_pid":99,"fps":120.0});
        assert_eq!(apply_frame_snapshot(resident, 99, Some(&frames))["fps"], 120.0);
    }
    #[test]
    fn cached_lows_expire_using_source_age_and_monotonic_cache_age() {
        let value = json!({"source_age_ms": 2200});
        assert!(live_lows_source_fresh(&value, 799));
        assert!(!live_lows_source_fresh(&value, 800));
        assert!(!live_lows_source_fresh(&json!({}), 0));
        assert!(!live_lows_source_fresh(&json!({"source_age_ms": u64::MAX}), 1));
    }
    #[test]
    fn refreshes_before_an_aging_low_snapshot_would_blank_the_overlay() {
        let aging = json!({"source_age_ms":2200});
        assert!(!live_lows_refresh_due(Some(&aging), 799));
        assert!(live_lows_refresh_due(Some(&aging), 800),
            "read the new publication instead of showing an expired cached value");
        assert!(!live_lows_refresh_due(None, 99));
        assert!(live_lows_refresh_due(None, 100), "a transient missing snapshot must not blank a whole second");
        assert!(!live_lows_refresh_due(Some(&json!({"source_age_ms":0})), 999));
    }
    #[test]
    fn rejects_stale_wrong_target_finished_and_wrong_worker_snapshots() {
        let root = std::env::temp_dir().join(format!("framescope-low-validation-{}", std::process::id()));
        std::fs::create_dir_all(&root).unwrap();
        let good = json!({"source":"presentmon","phase":"capturing","caught_up":true,"error":null,
            "frame_count":1,"worker_pid":42,"target_pid":99,"timestamp_ms":10000,"source_age_ms":0,"run_dir":root});
        assert!(validate_live_lows(&good, &root, 42, 99, 10001));
        let mut no_new_frames = good.clone(); no_new_frames["source_age_ms"] = json!(2999);
        assert!(!validate_live_lows(&no_new_frames, &root, 42, 99, 10001),
            "a fresh publication heartbeat cannot renew a three-second-old frame");
        assert!(!validate_live_lows(&good, &root, 43, 99, 10001));
        assert!(!validate_live_lows(&good, &root, 42, 100, 10001));
        assert!(!validate_live_lows(&good, &root, 42, 99, 16000));
        assert!(!validate_live_lows(&good, &root, 42, 99, 9999));
        let mut completed = good.clone(); completed["phase"] = json!("completed");
        assert!(!validate_live_lows(&completed, &root, 42, 99, 10001));
        let mut incomplete = good.clone(); incomplete["caught_up"] = json!(false);
        assert!(!validate_live_lows(&incomplete, &root, 42, 99, 10001));
        let mut sampled = good; sampled["source"] = json!("etw-fps-sampled");
        assert!(!validate_live_lows(&sampled, &root, 42, 99, 10001));
        std::fs::remove_dir(root).unwrap();
    }
}

fn validate_live_lows(value: &Value, run: &Path, worker_pid: u32, target_pid: u32, now: u64) -> bool {
    let timestamp = value["timestamp_ms"].as_u64().unwrap_or(0);
    value["source"] == "presentmon" && value["phase"] == "capturing" && value["caught_up"] == true
        && value["error"].is_null() && value["frame_count"].as_u64().unwrap_or(0) > 0
        && value["worker_pid"].as_u64() == Some(worker_pid as u64)
        && value["target_pid"].as_u64() == Some(target_pid as u64)
        && timestamp > 0 && timestamp <= now && now - timestamp <= 5000
        && live_lows_source_fresh(value, now.saturating_sub(timestamp))
        && same_existing_path(Path::new(value["run_dir"].as_str().unwrap_or("")), run)
}

fn live_lows_source_fresh(value: &Value, elapsed_ms: u64) -> bool {
    value["source_age_ms"].as_u64().and_then(|age| age.checked_add(elapsed_ms))
        .map(|age| age < 3000).unwrap_or(false)
}

fn live_lows_refresh_due(value: Option<&Value>, elapsed_ms: u64) -> bool {
    elapsed_ms >= if value.is_some() { 1000 } else { 100 }
        || value.is_some_and(|value| !live_lows_source_fresh(value, elapsed_ms))
}

fn read_report_lows(target_pid: u32) -> Option<Value> {
    let state: Value = serde_json::from_str(&std::fs::read_to_string(watcher_state_path().ok()?).ok()?).ok()?;
    if !watcher_running() || state["StopRequested"] == true { return None; }
    let root = backend_root().ok()?;
    let now = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).ok()?.as_millis() as u64;
    for monitor in state["ActiveMonitors"].as_array()? {
        let Some(worker_pid) = monitor["MonitorPid"].as_u64().and_then(|pid| u32::try_from(pid).ok()) else { continue };
        let Some(run) = monitor["RunDir"].as_str().map(Path::new) else { continue };
        let path = run.join("live-frame-stats.json");
        if path.metadata().ok().map(|metadata| metadata.len() > 65536).unwrap_or(true) { continue; }
        let Some(mut value) = std::fs::read_to_string(&path).ok().and_then(|text| serde_json::from_str::<Value>(&text).ok()) else { continue };
        if !validate_live_lows(&value, run, worker_pid, target_pid, now) { continue; }
        let processes = watcher_process_snapshot(worker_pid);
        let Some(worker) = processes.process(sysinfo::Pid::from_u32(worker_pid)) else { continue };
        if value["worker_start_time_unix"].as_u64() != Some(worker.start_time())
            || !worker.exe().map(|exe| same_existing_path(exe, &root.join("FrameScopeMonitor.exe"))).unwrap_or(false)
            || !worker.cmd().iter().any(|arg| arg == "--monitor-session") { continue; }
        value["source_age_ms"] = json!(value["source_age_ms"].as_u64()?.saturating_add(
            now.saturating_sub(value["timestamp_ms"].as_u64()?)));
        return Some(value);
    }
    None
}

pub fn apply_report_lows(data: Value) -> Value {
    use std::sync::Mutex;
    use std::time::Instant;
    static CACHE: Mutex<Option<(Instant, u32, Option<Value>)>> = Mutex::new(None);
    let mut pid = data["target_pid"].as_u64().and_then(|pid| u32::try_from(pid).ok()).unwrap_or(0);
    // A missing resident FPS target must not hide a valid worker stream.
    // Use the foreground PID; read_report_lows still verifies run, worker,
    // process path/start time, target identity and actual frame freshness.
    #[cfg(windows)]
    if pid == 0 {
        unsafe {
            let window = windows_sys::Win32::UI::WindowsAndMessaging::GetForegroundWindow();
            if !window.is_null() {
                windows_sys::Win32::UI::WindowsAndMessaging::GetWindowThreadProcessId(window, &mut pid);
            }
        }
    }
    let mut cache = CACHE.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    if cache.as_ref().map(|(at, cached_pid, value)| *cached_pid != pid ||
        live_lows_refresh_due(value.as_ref(), at.elapsed().as_millis().min(u64::MAX as u128) as u64)).unwrap_or(true) {
        *cache = Some((Instant::now(), pid, if pid > 0 { read_report_lows(pid) } else { None }));
    }
    let value = cache.as_ref().and_then(|(at, _, value)| value.as_ref()
        .filter(|value| live_lows_source_fresh(value, at.elapsed().as_millis().min(u64::MAX as u128) as u64)));
    apply_frame_snapshot(data, pid, value)
}

fn apply_frame_snapshot(mut data: Value, pid: u32, value: Option<&Value>) -> Value {
    let value = value.filter(|value| pid > 0 && value["target_pid"].as_u64() == Some(pid as u64));
    for field in ["fps_1low", "fps_01low"] {
        data[field] = value.and_then(|value| value[field].as_f64()).filter(|low| low.is_finite() && *low > 0.0)
            .map(|low| json!(low)).unwrap_or(Value::Null);
    }
    if data["fps"].as_f64().filter(|fps| fps.is_finite() && *fps > 0.0).is_none() {
        if let Some(fps) = value.and_then(|value| value["fps"].as_f64()).filter(|fps| fps.is_finite() && *fps > 0.0) {
            data["fps"] = json!(fps);
            data["target_pid"] = json!(pid);
        }
    }
    data
}

pub fn monitor_stop() -> Result<Value, String> {
    let mut owned = OWNED_WATCHER.lock().unwrap_or_else(|p| p.into_inner());
    let owned = owned.as_mut().ok_or("当前监控由其他宿主启动，不能停止借用的监控")?;
    let state = owned_state(owned)?;
    request_watcher_stop(&owned.root, &state, &owned.identity)
}

fn owned_state(owned: &mut OwnedWatcher) -> Result<Value, String> {
    let state: Value = serde_json::from_str(&std::fs::read_to_string(
        owned.root.join("framescope-watcher-state.json")).map_err(|e|e.to_string())?)
        .map_err(|e|e.to_string())?;
    let config = owned.root.join("framescope-config.json");
    let command_config = owned.identity.args.windows(2)
        .find(|args|args[0].eq_ignore_ascii_case("--config")).map(|args|Path::new(&args[1]));
    if !same_existing_path(Path::new(state["ConfigPath"].as_str().unwrap_or("")), &config) ||
        !command_config.map(|path|same_existing_path(path, &config)).unwrap_or(false) {
        return Err("监控配置身份不匹配，已取消退出".into());
    }
    if !same_existing_path(&owned.identity.exe, &owned.root.join("FrameScopeMonitor.exe")) ||
        owned.identity.start_time == 0 || !owned.identity.args.iter().any(|arg|arg.eq_ignore_ascii_case("--watcher")) {
        return Err("保留的监控启动身份不匹配，已取消退出".into());
    }
    // An exited retained Child is authoritative. sysinfo can still return an
    // exited process shell without exe metadata while its handle is retained.
    if owned.child.try_wait().map_err(|e|e.to_string())?.is_none() {
        let snapshot = watcher_process_snapshot(owned.identity.pid);
        if let Some(process) = snapshot.process(sysinfo::Pid::from_u32(owned.identity.pid)) {
            if process.start_time() != owned.identity.start_time ||
                !process.exe().map(|p|same_existing_path(p, &owned.identity.exe)).unwrap_or(false) {
                return Err("监控进程身份已改变，已取消退出".into());
            }
        }
    }
    if watcher_pid(&state)? != owned.identity.pid ||
        state["WatcherStartTimeUnix"].as_u64() != Some(owned.identity.start_time) {
        return Err("监控所有权尚未确认，已取消退出，请稍后重试".into());
    }
    watcher_stop_path(&owned.root, &state)?;
    let token = state["WatcherInstanceId"].as_str().unwrap().to_string();
    if owned.token.as_ref().map(|saved|saved != &token).unwrap_or(false) {
        return Err("监控实例已改变，已取消退出".into());
    }
    owned.token = Some(token);
    Ok(state)
}

pub fn shutdown_owned_monitor() -> Result<(), String> {
    let mut slot = OWNED_WATCHER.lock().unwrap_or_else(|p|p.into_inner());
    if let Some(owned) = slot.as_mut() {
        let started = std::time::Instant::now();
        let mut requested = false;
        let mut budget = std::time::Duration::from_secs(45);
        loop {
            let exited = owned.child.try_wait().map_err(|e|e.to_string())?.is_some();
            match owned_state(owned) {
                Ok(state) => {
                    if !requested && !exited {
                        let reports = state["ActiveMonitors"].as_array().map(|v|v.len()).unwrap_or(0)
                            + usize::from(state["GeneratingRunDir"].as_str().map(|s|!s.is_empty()).unwrap_or(false));
                        budget = crate::host_shutdown::budget(reports);
                        request_watcher_stop(&owned.root, &state, &owned.identity)?;
                        requested = true;
                    }
                    if crate::host_shutdown::completed(exited, &state, owned.token.as_deref())? { break; }
                }
                Err(error) if started.elapsed() < std::time::Duration::from_secs(15) && !exited => {
                    log::debug!("等待监控所有权发布: {error}");
                }
                Err(error) => return Err(error),
            }
            if started.elapsed() >= budget {
                return Err("监控或报告收尾超时，已取消此次退出；程序和数据已保留，请稍后重试".into());
            }
            std::thread::sleep(std::time::Duration::from_millis(100));
        }
        *slot = None;
    }
    drop(slot);
    let started = std::time::Instant::now();
    while REGENERATING_RUN.lock().unwrap_or_else(|p|p.into_inner()).is_some() {
        if started.elapsed() >= crate::host_shutdown::budget(1) {
            return Err("手动报告生成尚未完成，已取消此次退出，请稍后重试".into());
        }
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    Ok(())
}

struct WatcherOwner {
    pid: u32,
    exe: PathBuf,
    start_time: u64,
    args: Vec<String>,
}

fn child_start_time(child: &std::process::Child) -> u64 {
    #[cfg(windows)] {
        use std::os::windows::io::AsRawHandle;
        use windows_sys::Win32::Foundation::FILETIME;
        let mut created: FILETIME = unsafe { std::mem::zeroed() };
        let mut exited = created;
        let mut kernel = created;
        let mut user = created;
        if unsafe { windows_sys::Win32::System::Threading::GetProcessTimes(
            child.as_raw_handle() as _, &mut created, &mut exited, &mut kernel, &mut user) } != 0 {
            let ticks = ((created.dwHighDateTime as u64) << 32) | created.dwLowDateTime as u64;
            return ticks / 10_000_000 - 11_644_473_600;
        }
        0
    }
    #[cfg(not(windows))] {
        watcher_process_snapshot(child.id()).process(sysinfo::Pid::from_u32(child.id()))
            .map(|p|p.start_time()).unwrap_or(0)
    }
}

fn watcher_process_snapshot(pid: u32) -> sysinfo::System {
    let mut system = sysinfo::System::new();
    // sysinfo 0.30's default refresh does not load the command line.
    system.refresh_process_specifics(sysinfo::Pid::from_u32(pid),
        sysinfo::ProcessRefreshKind::new()
            .with_cmd(sysinfo::UpdateKind::Always)
            .with_exe(sysinfo::UpdateKind::Always));
    system
}

fn watcher_pid(state: &Value) -> Result<u32, String> {
    state["WatcherPid"].as_u64().and_then(|p| u32::try_from(p).ok())
        .filter(|p| *p > 0).ok_or_else(|| "invalid watcher PID".into())
}

fn same_existing_path(a: &Path, b: &Path) -> bool {
    match (a.canonicalize(), b.canonicalize()) {
        (Ok(a), Ok(b)) => a.to_string_lossy().eq_ignore_ascii_case(&b.to_string_lossy()),
        _ => false,
    }
}

fn watcher_stop_path(root: &Path, state: &Value) -> Result<PathBuf, String> {
    let token = state["WatcherInstanceId"].as_str().unwrap_or("");
    if state["StopProtocolVersion"] != 1 || token.len() != 32 ||
        !token.bytes().all(|b| b.is_ascii_hexdigit())
    {
        return Err("watcher does not support the owned stop protocol; refusing force-stop".into());
    }
    Ok(root.join(format!("framescope-watcher-stop-{token}.json")))
}

fn stop_request_payload(state: &Value) -> Value {
    json!({
        "Action": "stop", "WatcherPid": state["WatcherPid"],
        "WatcherStartTimeUnix": state["WatcherStartTimeUnix"],
        "WatcherInstanceId": state["WatcherInstanceId"], "ConfigPath": state["ConfigPath"]
    })
}

fn request_watcher_stop(root: &Path, state: &Value, owner: &WatcherOwner) -> Result<Value, String> {
    let path = watcher_stop_path(root, state)?;
    let config = root.join("framescope-config.json");
    let command_config = owner.args.windows(2)
        .find(|a| a[0].eq_ignore_ascii_case("--config")).map(|a| Path::new(&a[1]));
    if watcher_pid(state)? != owner.pid ||
        state["WatcherStartTimeUnix"].as_u64() != Some(owner.start_time) || owner.start_time == 0 ||
        !same_existing_path(&owner.exe, &root.join("FrameScopeMonitor.exe")) ||
        !same_existing_path(Path::new(state["ConfigPath"].as_str().unwrap_or("")), &config) ||
        !owner.args.iter().any(|a| a.eq_ignore_ascii_case("--watcher")) ||
        !command_config.map(|p| same_existing_path(p, &config)).unwrap_or(false)
    {
        return Err("watcher ownership mismatch; no process was stopped".into());
    }
    // A per-instance request cannot stop a recycled PID or a later watcher.
    // Keep the watcher state: only the watcher may publish terminal completion.
    write_json_atomic(&path, &stop_request_payload(state))?;
    Ok(json!({"status": "stopping", "completedRuns": state["CompletedRuns"],
        "watcherInstanceId": state["WatcherInstanceId"]}))
}

fn watcher_stop_pending(root: &Path, state: &Value) -> bool {
    watcher_stop_path(root, state).ok()
        .and_then(|p| std::fs::read_to_string(p).ok())
        .and_then(|s| serde_json::from_str::<Value>(&s).ok())
        .map(|request| request == stop_request_payload(state)).unwrap_or(false)
}

fn watcher_running() -> bool {
    let Ok(path) = watcher_state_path() else { return false };
    let Ok(state) = std::fs::read_to_string(&path) else { return false };
    let Ok(value) = serde_json::from_str::<Value>(&state) else { return false };
    watcher_running_from_state(&path, &value)
}

fn watcher_running_from_state(path: &Path, value: &Value) -> bool {
    let pid = value.get("WatcherPid").and_then(Value::as_u64).unwrap_or(0);
    if pid == 0 {
        return false;
    }
    let Some(pid) = u32::try_from(pid).ok() else { return false };
    let system = watcher_process_snapshot(pid);
    let Some(process) = system.process(sysinfo::Pid::from_u32(pid)) else { return false };
    let Some(root) = path.parent() else { return false };
    let Some(exe) = process.exe() else { return false };
    if !same_existing_path(exe, &root.join("FrameScopeMonitor.exe")) ||
        !process.cmd().iter().any(|arg| arg.eq_ignore_ascii_case("--watcher"))
    {
        return false;
    }
    // Older watchers remain visible, but monitor_stop rejects their missing
    // handshake metadata rather than falling back to termination.
    value["WatcherStartTimeUnix"].as_u64()
        .map(|start| start == process.start_time()).unwrap_or(true)
}

/// 读取一个 runDir 的活跃报告进度（问题 4）。字段由 C#
/// `FrameScopeReportProgress.CreateFields` 落盘到 `report-progress.json`。
/// 仅接受「活跃中」的进度（0 ≤ percent < 100 且 10 分钟内更新过，对齐 C#
/// ActiveProgressFreshness）；完成（=100）与失败不转发 —— UI 的 completed/error
/// 边缘由既有的 CompletedRuns 逻辑负责。
fn read_active_report_progress(run_dir: &Path) -> Option<(i64, Option<i64>, String)> {
    let path = run_dir.join("report-progress.json");
    let text = std::fs::read_to_string(&path).ok()?;
    let value = serde_json::from_str::<Value>(&text).ok()?;
    let percent = value.get("ReportProgressPercent").and_then(Value::as_i64)?;
    if !(0..100).contains(&percent) {
        return None;
    }
    let eta = value
        .get("ReportProgressEtaSeconds")
        .and_then(Value::as_i64)
        .filter(|eta| *eta > 0);
    let message = value
        .get("ReportProgressPhase")
        .and_then(Value::as_str)
        .unwrap_or("")
        .to_string();
    // 新鲜度：优先解析 UpdatedAt（本地 ISO），失败回退文件 mtime
    let updated = value
        .get("ReportProgressUpdatedAt")
        .and_then(Value::as_str)
        .and_then(|s| chrono::DateTime::parse_from_rfc3339(s).ok())
        .map(|dt| dt.timestamp())
        .or_else(|| {
            let mtime = std::fs::metadata(&path).ok()?.modified().ok()?;
            mtime.duration_since(std::time::UNIX_EPOCH).ok().map(|d| d.as_secs() as i64)
        });
    match updated {
        Some(updated) if updated <= now_unix() && now_unix() - updated <= 600
            && value["ReportProgressError"].as_str().map(str::is_empty).unwrap_or(true) => Some((percent, eta, message)),
        _ => None,
    }
}

/// Watcher/monitor phase for the home page, mapped onto the frontend
/// PreviewState machine: nothing -> idle; watcher without monitors ->
/// waiting; active monitors -> capturing (elapsed from the run-directory
/// timestamp); a fresh report-progress.json -> generating (the real C#
/// report pipeline progress with an ETA, not a time-driven estimate).
pub fn monitor_status() -> Result<Value, String> {
    if let Some(run) = REGENERATING_RUN.lock().unwrap_or_else(|p| p.into_inner()).clone() {
        let progress = read_active_report_progress(&run);
        let target_names = watcher_state_path().and_then(|path| monitor_status_at_path(&path))
            .ok().and_then(|status| status.get("targetNames").cloned()).unwrap_or_else(|| json!([]));
        return Ok(json!({"running":true,"phase":"generating","runDir":run,
            "targetNames":target_names,
            "reportPercent":progress.as_ref().map(|p|p.0),
            "reportEtaSeconds":progress.as_ref().and_then(|p|p.1)}));
    }
    let state_path = watcher_state_path()?;
    let mut status = monitor_status_at_path(&state_path)?;
    if status["running"] != true {
        if let Some(error) = AUTO_START_ERROR.lock().unwrap_or_else(|p| p.into_inner()).clone() {
            status["phase"] = json!("error");
            status["error"] = json!(error);
        }
    }
    Ok(status)
}

fn monitor_status_at_path(state_path: &Path) -> Result<Value, String> {
    let state = match std::fs::read_to_string(state_path) {
        Ok(state) => state,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return Ok(json!({"running": false, "phase": "idle", "completedRuns": 0, "targetNames": []}));
        }
        Err(error) => return Err(format!("读取监控状态失败: {error}")),
    };
    let value = serde_json::from_str::<Value>(&state)
        .map_err(|error| format!("解析监控状态失败: {error}"))?;
    // Use the same snapshot for phase/count and process identity; a second
    // file read could fail during replacement and fabricate a watcher exit.
    monitor_status_from_state(state_path, &value, watcher_running_from_state(state_path, &value))
}

fn monitor_status_from_state(state_path: &Path, value: &Value, running: bool) -> Result<Value, String> {
    let completed_runs = value
        .get("CompletedRuns")
        .and_then(Value::as_u64)
        .unwrap_or(0);
    if value["Phase"] == "stopped" {
        return Ok(json!({"running": false, "phase": "stopped", "completedRuns": completed_runs, "targetNames": [],
            "error": value["StopError"]}));
    }
    if !running {
        return Ok(json!({"running": false, "phase": "idle", "completedRuns": completed_runs, "targetNames": []}));
    }
    let monitors = value
        .get("ActiveMonitors")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let stopping = value["StopRequested"] == true ||
        watcher_stop_pending(state_path.parent().ok_or("invalid watcher state path")?, &value);
    let live_monitors: Vec<_> = monitors.iter().filter(|monitor| {
        monitor["MonitorPid"].as_u64().unwrap_or(0) > 0 &&
        monitor
            .get("RunDir")
            .and_then(Value::as_str)
            .map(str::len)
            .unwrap_or(0)
            > 0
    }).collect();
    let active = live_monitors.first().copied();
    let mut target_names: Vec<&str> = Vec::new();
    for monitor in &live_monitors {
        if let Some(name) = monitor["Game"].as_str().filter(|name| !name.trim().is_empty()) {
            if !target_names.contains(&name) { target_names.push(name); }
        }
    }
    if value["Phase"] == "generating" {
        let run_dir = value["GeneratingRunDir"].as_str().or_else(|| active.and_then(|monitor| monitor["RunDir"].as_str()));
        let progress = run_dir.and_then(|run| read_active_report_progress(Path::new(run)));
        return Ok(json!({"running":true,"phase":"generating","runDir":run_dir,"targetNames":target_names,
            "reportPercent":progress.as_ref().map(|p|p.0),
            "reportEtaSeconds":progress.as_ref().and_then(|p|p.1),
            "reportMessage":progress.as_ref().map(|p|&p.2),"completedRuns":completed_runs}));
    }
    if let Some(monitor) = active {
        let run_dir = monitor.get("RunDir").and_then(Value::as_str).unwrap_or("");
        let started_unix = run_dir
            .rsplit(['\\', '/'])
            .next()
            .and_then(parse_run_timestamp);
        let elapsed = started_unix
            .map(|start| (now_unix() - start as i64).max(0) as u64)
            .unwrap_or(0);
        return Ok(json!({
            "running": true, "phase": if stopping { "stopping" } else { "capturing" },
            "targetName": monitor.get("Game").cloned().unwrap_or(Value::Null),
            "targetNames": target_names,
            "runDir": run_dir,
            "elapsedSeconds": elapsed,
            "reportPercent": null,
            "reportEtaSeconds": null,
            "completedRuns": completed_runs
        }));
    }
    if stopping {
        return Ok(json!({"running": true, "phase": "stopping", "completedRuns": completed_runs, "targetNames": []}));
    }
    // A progress file from another run must never change a waiting watcher to generating.
    Ok(json!({"running": true, "phase": "waiting", "completedRuns": completed_runs, "targetNames": []}))
}

/// `Counter-Strike-2-20260907-142530-p1234-ab12` -> unix seconds. The C#
/// pipeline encodes *local* wall-clock time; convert it with the actual local
/// offset so the result compares against real file mtimes.
fn parse_run_timestamp(name: &str) -> Option<u64> {
    let segments: Vec<&str> = name.split('-').collect();
    if segments.len() < 4 {
        return None;
    }
    // yyyyMMdd-HHmmss appears right after the safe-name prefix.
    let date = segments[segments.len() - 4];
    let time = segments[segments.len() - 3];
    if date.len() != 8 || time.len() != 6 {
        return None;
    }
    let naive = chrono::NaiveDateTime::parse_from_str(
        &format!("{} {}", date, time),
        "%Y%m%d %H%M%S",
    )
    .ok()?;
    use chrono::TimeZone;
    chrono::Local
        .from_local_datetime(&naive)
        // DST fall-back ambiguity: take the earlier instant instead of
        // dropping the run entirely (which zeroed its duration).
        .earliest()
        .map(|datetime| datetime.timestamp().max(0) as u64)
}

fn now_unix() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

// ---------------------------------------------------------------- reports

/// reportId = SHA256 of the normalized uppercase run directory, Base64 with
/// URL-safe alphabet (verbatim from the C# bridge ReportId algorithm).
fn report_id(run_dir: &str) -> String {
    let normalized = run_dir.trim_end_matches(['\\', '/']).to_ascii_uppercase();
    let bytes = sha256_bytes(normalized.as_bytes());
    base64_url(&bytes)
}

fn sha256_bytes(input: &[u8]) -> [u8; 32] {
    use sha2::{Digest, Sha256};
    let mut hasher = Sha256::new();
    hasher.update(input);
    hasher.finalize().into()
}

fn base64_url(input: &[u8]) -> String {
    const ALPHABET: &[u8] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    let mut output = String::new();
    for chunk in input.chunks(3) {
        let b = [
            chunk[0],
            chunk.get(1).copied().unwrap_or(0),
            chunk.get(2).copied().unwrap_or(0),
        ];
        let triple = ((b[0] as u32) << 16) | ((b[1] as u32) << 8) | b[2] as u32;
        output.push(ALPHABET[(triple >> 18) as usize & 63] as char);
        output.push(ALPHABET[(triple >> 12) as usize & 63] as char);
        if chunk.len() > 1 {
            output.push(ALPHABET[(triple >> 6) as usize & 63] as char);
        }
        if chunk.len() > 2 {
            output.push(ALPHABET[triple as usize & 63] as char);
        }
    }
    output
}

/// Extracts frameStats from a report's `framescope-interactive-data.js`
/// (`window.FRAMESCOPE_DATA = { ... }`): first `{` to last `}` is the JSON.
fn frame_stats_cache() -> std::sync::MutexGuard<'static, std::collections::HashMap<std::path::PathBuf, (std::time::SystemTime, Value)>> {
    static CACHE: std::sync::OnceLock<std::sync::Mutex<std::collections::HashMap<std::path::PathBuf, (std::time::SystemTime, Value)>>> =
        std::sync::OnceLock::new();
    CACHE.get_or_init(|| std::sync::Mutex::new(std::collections::HashMap::new())).lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

fn frame_stats(data_js: &Path) -> Option<Value> {
    let mtime = std::fs::metadata(data_js).ok()?.modified().ok()?;
    {
        let mut cache = frame_stats_cache();
        if let Some((cached_mtime, cached_value)) = cache.get(data_js) {
            if *cached_mtime == mtime {
                return Some(cached_value.clone());
            }
        }
    }
    let parsed = frame_stats_uncached(data_js)?;
    frame_stats_cache().insert(data_js.to_path_buf(), (mtime, parsed.clone()));
    Some(parsed)
}

fn frame_stats_uncached(data_js: &Path) -> Option<Value> {
    let text = std::fs::read_to_string(data_js).ok()?;
    let start = text.find('{')?;
    let end = text.rfind('}')?;
    let value: Value = serde_json::from_str(&text[start..=end]).ok()?;
    let stats = value.get("frameStats")?;
    let number = |key: &str| stats.get(key).and_then(Value::as_f64);
    let average = number("average")?;
    Some(json!({
        "averageFps": (average * 10.0).round() / 10.0,
        "low1Fps": number("low1").map(|v| (v * 10.0).round() / 10.0),
        "low01Fps": number("low01").map(|v| (v * 10.0).round() / 10.0),
        "frameTimeMs": if average > 0.0 {
            Some((1000.0 / average * 100.0).round() / 100.0)
        } else {
            None
        }
    }))
}

fn run_timestamp_unix(run_dir: &Path) -> Option<u64> {
    parse_run_timestamp(run_dir.file_name()?.to_str()?)
}

fn read_status_json(run_dir: &Path) -> Option<Value> {
    let text = std::fs::read_to_string(run_dir.join("status.json")).ok()?;
    serde_json::from_str(&text).ok()
}

/// Report list: history.jsonl first (newest first), then a bounded scan of
/// the data root as fallback (v1.2.2 semantics; expected layout
/// <dataRoot>/<Target>/<run>/charts/framescope-interactive-report.html).
pub fn reports_list() -> Result<Value, String> {
    let data_root = resolved_data_root()?;
    let mut seen: Vec<String> = Vec::new();
    let mut reports: Vec<Value> = Vec::new();

    let history = std::fs::read_to_string(history_path()?).unwrap_or_default();
    for line in history.lines().rev() {
        let Ok(entry) = serde_json::from_str::<Value>(line) else { continue };
        let run_dir = entry.get("RunDir").and_then(Value::as_str).unwrap_or("");
        if run_dir.is_empty() {
            continue;
        }
        let html = entry
            .get("ReportHtml")
            .and_then(Value::as_str)
            .map(str::to_string)
            .unwrap_or_else(|| {
                Path::new(run_dir)
                    .join("charts")
                    .join("framescope-interactive-report.html")
                    .to_string_lossy()
                    .to_string()
            });
        if let Some(mut report) = build_report_entry(run_dir, &html, &mut seen) {
            if let Some(game) = entry.get("Game").and_then(Value::as_str) {
                report["targetName"] = json!(game);
            }
            if let Some(time) = entry.get("Time").and_then(Value::as_str) {
                report["createdAt"] = json!(time);
            }
            reports.push(report);
        }
        if reports.len() >= 50 {
            break;
        }
    }

    if reports.len() < 50 && data_root.is_dir() {
        let mut targets: Vec<PathBuf> = std::fs::read_dir(&data_root)
            .into_iter()
            .flatten()
            .flatten()
            .map(|entry| entry.path())
            .filter(|path| path.is_dir())
            .collect();
        targets.sort();
        for target_dir in targets {
            let mut runs: Vec<PathBuf> = std::fs::read_dir(&target_dir)
                .into_iter()
                .flatten()
                .flatten()
                .map(|entry| entry.path())
                .filter(|path| path.is_dir())
                .collect();
            runs.sort_by_key(|path| {
                std::cmp::Reverse(path.metadata().and_then(|m| m.modified()).ok())
            });
            for run_dir in runs {
                let html = run_dir
                    .join("charts")
                    .join("framescope-interactive-report.html");
                if let Some(report) = build_report_entry(
                    &run_dir.to_string_lossy(),
                    &html.to_string_lossy(),
                    &mut seen,
                ) {
                    reports.push(report);
                }
                if reports.len() >= 50 {
                    break;
                }
            }
            if reports.len() >= 50 {
                break;
            }
        }
    }

    for report in &mut reports {
        if let Some(run) = report["runDir"].as_str() {
            if let Err(error) = checked_report_run(&data_root, Path::new(run)) {
                report["artifactError"] = json!(error);
            }
        }
    }
    if let Err(error) = inspect_report_entries(&mut reports) {
        for report in &mut reports { report["artifactError"] = json!(error); }
    }
    Ok(json!({
        "status": "loaded",
        "dataRoot": data_root.to_string_lossy(),
        "count": reports.len(),
        "reports": reports
    }))
}

fn build_report_entry(run_dir: &str, html: &str, seen: &mut Vec<String>) -> Option<Value> {
    let run_path = Path::new(run_dir);
    if !run_path.is_dir() { return None; }
    let id = report_id(run_dir);
    if seen.iter().any(|existing| existing.eq_ignore_ascii_case(&id)) {
        return None;
    }
    seen.push(id.clone());

    let status = read_status_json(run_path);
    let kind_raw = status
        .as_ref()
        .and_then(|value| value.get("ReportKind"))
        .and_then(Value::as_str)
        .unwrap_or("")
        .to_ascii_lowercase();
    let mut kind = match kind_raw.as_str() {
        "full" | "partial" | "diagnostic" | "error" => kind_raw,
        _ => "full".to_string(),
    };
    // The generator's manifest reflects what was actually built: a regenerate
    // can upgrade a worker-preclassified diagnostic stub into a full report
    // (anti-cheat fallback series from ETW FPS samples). Prefer it.
    if let Ok(manifest_text) = std::fs::read_to_string(run_path.join("charts").join("framescope-interactive-manifest.json")) {
        if let Ok(manifest) = serde_json::from_str::<Value>(&manifest_text) {
            if let Some(manifest_kind) = manifest.get("reportKind").and_then(Value::as_str) {
                let manifest_kind = manifest_kind.to_ascii_lowercase();
                if matches!(manifest_kind.as_str(), "full" | "partial" | "diagnostic" | "error") {
                    kind = manifest_kind;
                }
            }
        }
    }
    let stats = frame_stats(&run_path.join("charts").join("framescope-interactive-data.js"))
        .unwrap_or_else(|| {
            json!({"averageFps": null, "low1Fps": null, "low01Fps": null, "frameTimeMs": null})
        });
    let run_start = run_timestamp_unix(run_path);
    let created = run_start.map(format_unix_local).unwrap_or_default();
    let target_name = run_path
        .parent()
        .and_then(|parent| parent.file_name())
        .map(|name| name.to_string_lossy().to_string())
        .unwrap_or_default();

    Some(json!({
        "id": id,
        "targetName": target_name,
        "processName": "",
        "createdAt": created,
        "durationSeconds": null,
        "kind": kind,
        "runDir": run_dir,
        "reportHtml": html,
        "averageFps": stats["averageFps"], "low1Fps": stats["low1Fps"],
        "low01Fps": stats["low01Fps"], "frameTimeMs": stats["frameTimeMs"],
        "frameCount": status.as_ref().and_then(|value| value.get("ReportFrameCount")).cloned().unwrap_or(Value::Null),
        "artifactStatus": "incomplete",
        "canOpen": false, "canOpenReport": false,
        "canRegenerate": false
    }))
}


const REPORT_INSPECTION_PROTOCOL: &str = "framescope-report-inspection/1";

fn require_report_inspection_protocol(generator: &Path) -> Result<(), String> {
    if generator.metadata().map_err(|e| e.to_string())?.len() > 64 * 1024 * 1024 {
        return Err("report inspection executable exceeds size limit".into());
    }
    let bytes = std::fs::read(generator).map_err(|e| e.to_string())?;
    let marker: Vec<u8> = REPORT_INSPECTION_PROTOCOL.encode_utf16().flat_map(u16::to_le_bytes).collect();
    if !bytes.starts_with(b"MZ") || !bytes.windows(marker.len()).any(|v| v == marker)
        || !bytes.windows(b"ReportInspectionProtocol".len()).any(|v| v == b"ReportInspectionProtocol") {
        return Err("report generator does not declare the read-only inspection protocol; rebuild required".into());
    }
    Ok(())
}

fn apply_report_inspections(reports: &mut [Value], response: &Value) -> Result<(), String> {
    if response["protocol"] != REPORT_INSPECTION_PROTOCOL || response["status"] != "inspected" {
        return Err("invalid report inspection protocol".into());
    }
    let items = response["reports"].as_array().ok_or("report inspection array missing")?;
    if items.len() > 50 { return Err("report inspection exceeds list limit".into()); }
    for report in reports {
        if report.get("artifactError").is_some() { continue; }
        let run = Path::new(report["runDir"].as_str().ok_or("report run missing")?);
        let matches: Vec<_> = items.iter().filter(|item| item["runDir"].as_str()
            .map(|item| same_existing_path(Path::new(item), run)).unwrap_or(false)).collect();
        if matches.len() != 1 { report["artifactError"] = json!("report inspection result missing or ambiguous"); continue; }
        let item = matches[0];
        let status = item["artifactStatus"].as_str().unwrap_or("");
        let kind = item["reportKind"].as_str().unwrap_or("");
        if !matches!(status, "complete" | "legacy-readable" | "incomplete" | "inputs-changed")
            || !matches!(kind, "full" | "partial" | "diagnostic" | "error")
            || !item["canOpen"].is_boolean() || !item["canRegenerate"].is_boolean() {
            report["artifactError"] = json!("report inspection fields invalid"); continue;
        }
        report["artifactStatus"] = json!(status);
        report["kind"] = json!(kind);
        report["durationSeconds"] = item["durationSeconds"].as_f64().filter(|v| v.is_finite() && *v >= 0.0).map(|v| json!(v)).unwrap_or(Value::Null);
        let can_open = matches!(status, "complete" | "legacy-readable") && item["canOpen"] == true;
        report["canOpen"] = json!(can_open); report["canOpenReport"] = json!(can_open);
        report["canRegenerate"] = item["canRegenerate"].clone();
        report["artifactError"] = item["error"].clone();
    }
    Ok(())
}

struct InspectionListFile(PathBuf);
impl Drop for InspectionListFile { fn drop(&mut self) { let _ = std::fs::remove_file(&self.0); } }

fn inspect_report_entries(reports: &mut [Value]) -> Result<(), String> {
    inspect_report_entries_with_root(reports, &backend_root()?)
}

fn inspect_report_entries_with_root(reports: &mut [Value], root: &Path) -> Result<(), String> {
    let runs: Vec<_> = reports.iter().filter(|report| report.get("artifactError").is_none())
        .filter_map(|report| report["runDir"].as_str()).collect();
    if runs.is_empty() { return Ok(()); }
    let response = inspect_report_runs(root, &runs, std::time::Duration::from_secs(30))?;
    apply_report_inspections(reports, &response)
}

fn inspect_report_runs(root: &Path, runs: &[&str], timeout: std::time::Duration) -> Result<Value, String> {
    use std::io::Write;
    if runs.len() > 50 { return Err("report list exceeds inspection limit".into()); }
    let generator = root.join("FrameScopeReportGenerator.exe");
    require_report_inspection_protocol(&generator)?;
    static SEQUENCE: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(0);
    let path = std::env::temp_dir().join(format!("framescope-report-inspection-{}-{}-{}.txt", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_err(|e| e.to_string())?.as_nanos(),
        SEQUENCE.fetch_add(1, std::sync::atomic::Ordering::Relaxed)));
    let mut file = std::fs::OpenOptions::new().write(true).create_new(true).open(&path).map_err(|e| e.to_string())?;
    let temporary = InspectionListFile(path);
    for run in runs { writeln!(file, "{run}").map_err(|e| e.to_string())?; }
    drop(file);
    let mut command = std::process::Command::new(generator);
    command.arg("--inspect-reports").arg("--run-list").arg(&temporary.0).current_dir(root);
    let (status, output, error) = run_report_generator(command, timeout)?;
    if !status.success() { return Err(format!("report inspection failed: {}", String::from_utf8_lossy(&error))); }
    let response: Value = serde_json::from_slice(&output).map_err(|e| format!("report inspection response invalid: {e}"))?;
    if response["protocol"] != REPORT_INSPECTION_PROTOCOL || response["status"] != "inspected" {
        return Err("invalid report inspection protocol".into());
    }
    Ok(response)
}

fn format_unix_local(seconds: u64) -> String {
    // The old hand-rolled conversion formatted the UTC instant as if it were
    // local wall-clock time, putting fallback timestamps 8 hours behind.
    match chrono::DateTime::from_timestamp(seconds as i64, 0) {
        Some(utc) => utc
            .with_timezone(&chrono::Local)
            .format("%Y-%m-%d %H:%M:%S")
            .to_string(),
        None => String::new(),
    }
}

fn find_report(id: &str) -> Result<Value, String> {
    let list = reports_list()?;
    list.get("reports")
        .and_then(Value::as_array)
        .and_then(|reports| {
            reports
                .iter()
                .find(|report| {
                    report
                        .get("id")
                        .and_then(Value::as_str)
                        .map(|value| value.eq_ignore_ascii_case(id))
                        .unwrap_or(false)
                })
                .cloned()
        })
        .ok_or_else(|| format!("report not found: {id}"))
}

fn shell_open(path: &str) -> Result<(), String> {
    #[cfg(windows)]
    {
        use std::os::windows::ffi::OsStrExt;
        use windows::core::PCWSTR;
        use windows::Win32::UI::Shell::ShellExecuteW;
        let operation: Vec<u16> = std::ffi::OsStr::new("open").encode_wide().chain(std::iter::once(0)).collect();
        let target: Vec<u16> = std::ffi::OsStr::new(path).encode_wide().chain(std::iter::once(0)).collect();
        let result = unsafe {
            ShellExecuteW(
                None,
                PCWSTR(operation.as_ptr()),
                PCWSTR(target.as_ptr()),
                PCWSTR::null(),
                PCWSTR::null(),
                windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL,
            )
        };
        if (result.0 as isize) <= 32 { return Err(format!("open failed with ShellExecuteW status {}", result.0 as isize)); }
        return Ok(());
    }
    #[cfg(not(windows))]
    {
        let _ = path;
        Err("报告打开仅支持 Windows".into())
    }
}

fn run_report_generator(command: std::process::Command, timeout: std::time::Duration) -> Result<(std::process::ExitStatus, Vec<u8>, Vec<u8>), String> {
    crate::bounded_process::run(command, timeout)
}

pub fn report_open(id: &str) -> Result<Value, String> {
    let report = find_report(id)?;
    if report["canOpenReport"] != true { return Err("报告不完整或输入已变化，请重新生成".into()); }
    let run = Path::new(report["runDir"].as_str().ok_or("report run missing")?);
    checked_report_run(&resolved_data_root()?, run)?;
    let html = report
        .get("reportHtml")
        .and_then(Value::as_str)
        .ok_or("report path missing")?;
    if !same_existing_path(Path::new(html), &run.join("charts").join("framescope-interactive-report.html")) {
        return Err("报告路径不属于当前记录".into());
    }
    shell_open(html)?;
    Ok(json!({"status": "opened", "reportHtml": html}))
}

pub fn report_open_directory(id: &str) -> Result<Value, String> {
    let report = find_report(id)?;
    let run_dir = report
        .get("runDir")
        .and_then(Value::as_str)
        .ok_or("run directory missing")?;
    checked_report_run(&resolved_data_root()?, Path::new(run_dir))?;
    shell_open(run_dir)?;
    Ok(json!({"status": "directory_opened", "runDir": run_dir}))
}

fn checked_report_run(root: &Path, run: &Path) -> Result<PathBuf, String> {
    let root = root.canonicalize().map_err(|e| e.to_string())?;
    let canonical = run.canonicalize().map_err(|e| e.to_string())?;
    let relative = canonical.strip_prefix(&root).map_err(|_| "报告目录不属于当前记录目录")?;
    if relative.components().count() != 2 || !canonical.is_dir() {
        return Err("只能删除单次记录目录".into());
    }
    #[cfg(windows)] {
        use std::os::windows::fs::MetadataExt;
        let mut pending = vec![run.to_path_buf(), run.parent().ok_or("invalid run parent")?.to_path_buf()];
        while let Some(path) = pending.pop() {
            let metadata = std::fs::symlink_metadata(&path).map_err(|e| e.to_string())?;
            if metadata.file_attributes() & 0x400 != 0 { return Err("记录包含链接目录，不能移入回收站".into()); }
            if path != run.parent().unwrap() && metadata.is_dir() {
                for entry in std::fs::read_dir(&path).map_err(|e| e.to_string())? {
                    pending.push(entry.map_err(|e| e.to_string())?.path());
                }
            }
        }
    }
    Ok(canonical)
}

fn watcher_uses_run(state: &Value, run: &Path) -> bool {
    state["Phase"] == "starting" || state["GeneratingRunDir"].as_str().map(|path| same_existing_path(Path::new(path), run)).unwrap_or(false)
        || state["ActiveMonitors"].as_array().map(|monitors| monitors.iter().any(|monitor|
            monitor["RunDir"].as_str().map(|path| same_existing_path(Path::new(path), run)).unwrap_or(false))).unwrap_or(false)
}

#[cfg(windows)]
struct ReportRunLock(windows_sys::Win32::Foundation::HANDLE);
#[cfg(windows)]
impl Drop for ReportRunLock {
    fn drop(&mut self) { unsafe {
        windows_sys::Win32::System::Threading::ReleaseMutex(self.0);
        windows_sys::Win32::Foundation::CloseHandle(self.0);
    } }
}
#[cfg(windows)]
fn lock_report_for_delete(run: &Path) -> Result<ReportRunLock, String> {
    use windows_sys::Win32::System::Threading::{CreateMutexW, WaitForSingleObject};
    let canonical = run.canonicalize().map_err(|e| e.to_string())?;
    let path = canonical.to_string_lossy();
    let normalized = path.strip_prefix(r"\\?\").unwrap_or(&path).trim_end_matches(['\\','/']).to_ascii_uppercase();
    let hash = sha256_bytes(normalized.as_bytes()).iter().map(|byte| format!("{byte:02x}")).collect::<String>();
    let name: Vec<u16> = format!("Local\\FrameScopeReportOperation-{hash}").encode_utf16().chain(Some(0)).collect();
    unsafe {
        let handle = CreateMutexW(std::ptr::null(), 0, name.as_ptr());
        if handle.is_null() { return Err("无法建立记录操作锁".into()); }
        match WaitForSingleObject(handle, 0) {
            0 | 128 => Ok(ReportRunLock(handle)),
            _ => { windows_sys::Win32::Foundation::CloseHandle(handle); Err("记录正在处理，不能删除".into()) }
        }
    }
}
#[cfg(not(windows))]
fn lock_report_for_delete(_: &Path) -> Result<(), String> { Ok(()) }

#[cfg(windows)]
fn recycle_run(run: &Path) -> Result<(), String> {
    let run = run.to_path_buf();
    std::thread::spawn(move || {
        use windows::core::PCWSTR;
        use windows::Win32::System::Com::{CoInitializeEx, CoUninitialize, CoCreateInstance, COINIT_APARTMENTTHREADED, CLSCTX_INPROC_SERVER};
        use windows::Win32::UI::Shell::*;
        use std::os::windows::ffi::OsStrExt;
        unsafe {
            CoInitializeEx(None, COINIT_APARTMENTTHREADED).ok().map_err(|e| e.to_string())?;
            let result = (|| -> windows::core::Result<()> {
                let operation: IFileOperation = CoCreateInstance(&FileOperation, None, CLSCTX_INPROC_SERVER)?;
                operation.SetOperationFlags(FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD | FOFX_EARLYFAILURE | FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI)?;
                // Shell parsing expects a normal absolute DOS path, while
                // Rust canonicalize returns the Win32 verbatim (\\?\) prefix.
                let mut wide: Vec<u16> = run.as_os_str().encode_wide().chain(Some(0)).collect();
                if wide.starts_with(&[92,92,63,92]) { wide.drain(..4); }
                let item: IShellItem = SHCreateItemFromParsingName(PCWSTR(wide.as_ptr()), None)?;
                operation.DeleteItem(&item, None)?;
                operation.PerformOperations()?;
                if operation.GetAnyOperationsAborted()?.as_bool() || run.exists() {
                    return Err(windows::core::Error::new(windows::core::HRESULT(0x80004004u32 as i32), "移入回收站已取消或失败"));
                }
                Ok(())
            })();
            CoUninitialize();
            result.map_err(|e| format!("无法将记录移入回收站：{e}"))
        }
    }).join().map_err(|_| "回收站操作线程异常".to_string())?
}

#[cfg(not(windows))]
fn recycle_run(_: &Path) -> Result<(), String> { Err("回收站操作仅支持 Windows".into()) }

pub fn report_delete(id: &str) -> Result<Value, String> {
    let _operation = REPORT_OPERATION.try_lock().map_err(|_| "报告正在处理，请稍后再删除")?;
    let report = find_report(id)?;
    let run = checked_report_run(&resolved_data_root()?, Path::new(report["runDir"].as_str().ok_or("run directory missing")?))?;
    if watcher_running() {
        let state = std::fs::read_to_string(watcher_state_path()?).ok().and_then(|text| serde_json::from_str::<Value>(&text).ok())
            .ok_or("无法确认监控状态，请停止监控后再删除")?;
        if watcher_uses_run(&state, &run) { return Err("记录正在采集或生成报告，不能删除".into()); }
    }
    let _run_lock = lock_report_for_delete(&run)?;
    recycle_run(&run)?;
    Ok(json!({"status":"recycled","id":id}))
}

/// Spawns FrameScopeReportGenerator.exe for the run directory and waits
/// (bounded, same as the C# host). The generator computes and validates the
/// input fingerprint through the shared C# report-artifact contract.
pub fn report_regenerate(id: &str) -> Result<Value, String> {
    let started = std::time::Instant::now();
    report_regeneration_scope(|| {
    if HOST_CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
        return Err("正在退出，不能开始生成新报告".into());
    }
    let start_guard = OWNED_WATCHER.lock().unwrap_or_else(|p|p.into_inner());
    ensure_not_closing()?;
    if HOST_CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
        return Err("正在退出，不能开始生成新报告".into());
    }
    let report = find_report(id)?;
    let run_dir = report
        .get("runDir")
        .and_then(Value::as_str)
        .ok_or("run directory missing")?;
    if report["canRegenerate"] != true { return Err("missing_monitor_data: report has no valid raw inputs".into()); }
    let run_path = checked_report_run(&resolved_data_root()?, Path::new(run_dir))?;
    let run_path = run_path.as_path();
    *REGENERATING_RUN.lock().unwrap_or_else(|p|p.into_inner()) = Some(run_path.to_path_buf());
    let _regeneration = ReportRegeneration;
    drop(start_guard);
    let root = backend_root()?;
    regenerate_report_with_root(&root, run_path, std::time::Duration::from_secs(120).saturating_sub(started.elapsed()))
    })
}

fn report_regeneration_scope<T>(operation: impl FnOnce() -> Result<T, String>) -> Result<T, String> {
    let _operation = REPORT_OPERATION.try_lock().map_err(|_| "报告正在处理，请稍后重试")?;
    operation()
}

fn regenerate_report_with_root(root: &Path, run_path: &Path, budget: std::time::Duration) -> Result<Value, String> {
    *REGENERATING_RUN.lock().unwrap_or_else(|p|p.into_inner()) = Some(run_path.to_path_buf());
    let _regeneration = ReportRegeneration;
    let started = std::time::Instant::now();
    // Reserve terminal publication time inside the same business budget.
    let process_budget = budget.saturating_sub(std::time::Duration::from_millis(250).min(budget / 4));
    let remaining = || process_budget.checked_sub(started.elapsed()).filter(|d| !d.is_zero())
        .ok_or_else(|| "报告生成超时（总期限包含检查与状态发布）".to_string());
    let inspect = || -> Result<Value, String> {
        let response = inspect_report_runs(root, &[run_path.to_str().ok_or("run directory is not UTF-8")?],
            remaining()?.min(std::time::Duration::from_secs(30)))?;
        let items = response["reports"].as_array().ok_or("report inspection array missing")?;
        if items.len() != 1 || !items[0]["runDir"].as_str().map(|p| same_existing_path(Path::new(p),run_path)).unwrap_or(false) {
            return Err("report inspection result missing or ambiguous".into());
        }
        let item = items[0].clone();
        if item["currentInputFingerprintVersion"] != 2 || item["currentInputFingerprintStable"] != true
            || item["currentInputFingerprint"].as_str().filter(|s| !s.is_empty()).is_none() {
            return Err("report current v2 inputs cannot be verified".into());
        }
        Ok(item)
    };
    let result = (|| -> Result<Value, String> {
        let before = inspect()?;
        if before["canRegenerate"] != true { return Err("missing_monitor_data: report has no valid raw inputs".into()); }
        publish_regeneration_state(run_path,"generating",None)?;
        let mut command = std::process::Command::new(root.join("FrameScopeReportGenerator.exe"));
        command.arg(run_path).arg("--progress").arg(run_path.join("report-progress.json"))
            .arg("--input-fingerprint").arg(before["currentInputFingerprint"].as_str().unwrap()).current_dir(root);
        let (status, _, stderr) = run_report_generator(command, remaining()?)?;
        if !status.success() { return Err(format!("report generation failed with status {status}: {}",
            String::from_utf8_lossy(&stderr).chars().take(4096).collect::<String>())); }
        let after = inspect()?;
        if after["artifactStatus"] != "complete" || after["isComplete"] != true || after["canOpen"] != true
            || after["fingerprintVersion"] != 2 || after["inputFingerprintMatches"] != true
            || after["currentInputFingerprint"] != before["currentInputFingerprint"]
            || after["manifestInputFingerprint"] != before["currentInputFingerprint"] {
            return Err("report generator exited successfully but complete unchanged-input artifacts were not verified".into());
        }
        remaining()?;
        publish_regeneration_state(run_path,"completed",None)?;
        let status = read_status_json(run_path).unwrap_or(Value::Null);
        Ok(json!({"status":"regenerated", "runDir":run_path,"reportKind":after["reportKind"],
            "frameCount":status["ReportFrameCount"],"hasFrameData":status["ReportHasFrameData"],"canRetry":false}))
    })();
    if let Err(error) = &result {
        let phase = if error.contains("超时") { "timeout" } else { "failed" };
        if let Err(publication) = publish_regeneration_state(run_path,phase,Some(error)) {
            return Err(format!("{error}; failure evidence publication failed: {publication}"));
        }
    }
    result
}

fn publish_regeneration_state(run: &Path, phase: &str, error: Option<&str>) -> Result<(), String> {
    let timestamp = chrono::Utc::now().to_rfc3339();
    let retry = error.is_some();
    if let Some(error) = error {
        write_json_atomic(&run.join("report-regeneration-failure.json"),&json!({"phase":phase,"error":error,
            "canRetry":true,"updatedAt":timestamp,"runDir":run}))?;
    }
    let mut status = read_status_json(run).filter(Value::is_object).unwrap_or_else(|| json!({}));
    status["ReportProgressPhase"] = json!(phase);
    status["ReportCanRetry"] = json!(retry);
    status["ReportGenerationAttempted"] = json!(true);
    status["ReportGenerationTimedOut"] = json!(phase == "timeout");
    status["ReportError"] = error.map(|e|json!(e)).unwrap_or(Value::Null);
    status["ReportArtifactsComplete"] = json!(phase == "completed");
    status["ReportInputFingerprintMatches"] = json!(phase == "completed");
    status[if phase == "generating" { "ReportGenerationStartedAt" } else { "ReportGenerationEndedAt" }] = json!(timestamp);
    status["ReportProgressError"] = error.map(|e|json!(e)).unwrap_or(Value::Null);
    status["ReportProgressUpdatedAt"] = json!(timestamp);
    write_json_atomic(&run.join("status.json"),&status)?;
    write_json_atomic(&run.join("report-progress.json"),&json!({"ReportProgressPhase":phase,
        "ReportProgressPercent":if phase == "completed" {100} else {0},"ReportProgressEtaSeconds":null,
        "ReportCanRetry":retry,"ReportProgressError":error,"ReportProgressUpdatedAt":timestamp}))
}

fn csv_has_rows(path: &Path) -> bool {
    let Ok(meta) = std::fs::metadata(path) else { return false };
    if meta.len() == 0 {
        return false;
    }
    std::fs::read_to_string(path)
        .map(|text| text.lines().count() > 1)
        .unwrap_or(false)
}

#[cfg(test)]
mod graceful_stop_tests {
    use super::*;

    #[test]
    #[ignore = "read-only live sysinfo diagnostic; never sends a stop request"]
    fn watcher_owner_readonly_sysinfo_dump() {
        let pid = sysinfo::Pid::from_u32(std::process::id());
        let mut default = sysinfo::System::new();
        default.refresh_processes();
        let mut explicit = sysinfo::System::new();
        explicit.refresh_process_specifics(pid, sysinfo::ProcessRefreshKind::new()
            .with_cmd(sysinfo::UpdateKind::Always)
            .with_exe(sysinfo::UpdateKind::Always));
        for (mode, system) in [("default", default), ("explicit", explicit)] {
            let process = system.process(pid).expect("diagnostic process must exist");
            eprintln!("{}", json!({
                "mode": mode, "pid": pid.as_u32(), "exe": process.exe(),
                "start_time": process.start_time(), "cmd": process.cmd(),
                "expected_exe": std::env::current_exe().unwrap(),
                "expected_cmd": std::env::args().collect::<Vec<_>>(),
            }));
        }
    }

    #[test]
    fn watcher_owner_snapshot_loads_real_child_identity_before_stop_validation() {
        use std::io::{BufRead, BufReader};
        use std::os::windows::io::AsRawHandle;
        use std::process::{Child, Command, Stdio};
        use windows_sys::Win32::Foundation::FILETIME;
        use windows_sys::Win32::System::Threading::GetProcessTimes;

        struct OwnedFixture(Child);
        impl Drop for OwnedFixture {
            fn drop(&mut self) {
                drop(self.0.stdin.take());
                self.0.wait().unwrap();
            }
        }

        let (root, mut state, _) = fixture();
        let exe = root.join("FrameScopeMonitor.exe");
        let config = root.join("framescope-config.json");
        let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("src/backend_watcher_owner_fixture.rs");
        assert!(Command::new("rustc").arg(source).arg("-o").arg(&exe).status().unwrap().success());
        let mut child = OwnedFixture(Command::new(&exe)
            .arg("--watcher").arg("--config").arg(&config)
            .stdin(Stdio::piped()).stdout(Stdio::piped()).spawn().unwrap());
        let mut ready = String::new();
        BufReader::new(child.0.stdout.take().unwrap()).read_line(&mut ready).unwrap();
        assert_eq!(ready.trim(), "ready");
        let mut created: FILETIME = unsafe { std::mem::zeroed() };
        let mut exit = created;
        let mut kernel = created;
        let mut user = created;
        assert_ne!(unsafe { GetProcessTimes(child.0.as_raw_handle(), &mut created, &mut exit, &mut kernel, &mut user) }, 0);
        let native_start = ((((created.dwHighDateTime as u64) << 32) | created.dwLowDateTime as u64)
            - 116_444_736_000_000_000) / 10_000_000;
        let pid = child.0.id();
        state["WatcherPid"] = json!(pid);
        state["WatcherStartTimeUnix"] = json!(native_start);
        let system = watcher_process_snapshot(pid);
        let process = system.process(sysinfo::Pid::from_u32(pid)).unwrap();
        let owner = WatcherOwner {
            pid, exe: process.exe().unwrap().to_path_buf(),
            start_time: process.start_time(), args: process.cmd().to_vec(),
        };
        assert_eq!(owner.start_time, native_start, "start time must match Windows exactly");
        assert_eq!(child_start_time(&child.0), native_start, "retained child handle must bind creation time");
        eprintln!("fixture identity pid={pid} start={} args={:?} root={}", owner.start_time, owner.args, root.display());
        // The fixture only waits on stdin. It never consumes this test-only request.
        let result = request_watcher_stop(&root, &state, &owner);
        assert!(result.is_ok(), "real matching child identity rejected: {result:?}");
        assert!(watcher_stop_path(&root, &state).unwrap().is_file());
    }

    fn fixture() -> (PathBuf, Value, WatcherOwner) {
        use std::sync::atomic::{AtomicU64, Ordering};
        static SEQ: AtomicU64 = AtomicU64::new(0);
        let root = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../artifacts/20260918-repair/stop-rust")
            .join(format!("fs-owned-stop-{}-{}-{}",
            std::process::id(), now_unix(), SEQ.fetch_add(1, Ordering::Relaxed)));
        std::fs::create_dir_all(&root).unwrap();
        let exe = root.join("FrameScopeMonitor.exe");
        let config = root.join("framescope-config.json");
        std::fs::write(&exe, b"test fixture; never executed").unwrap();
        std::fs::write(&config, b"{}").unwrap();
        let state = json!({"WatcherPid": 999999, "WatcherStartTimeUnix": 123456,
            "WatcherInstanceId": "0123456789abcdef0123456789abcdef", "StopProtocolVersion": 1,
            "ConfigPath": config, "Phase": "monitoring", "CompletedRuns": 4});
        std::fs::write(root.join("framescope-watcher-state.json"), state.to_string()).unwrap();
        let owner = WatcherOwner { pid: 999999, exe, start_time: 123456,
            args: vec!["FrameScopeMonitor.exe".into(), "--watcher".into(),
                "--config".into(), config.to_string_lossy().into_owned()] };
        (root, state, owner)
    }

    #[test]
    fn owned_stop_writes_request_without_removing_state_and_is_idempotent() {
        let (root, state, owner) = fixture();
        let state_path = root.join("framescope-watcher-state.json");
        let before = std::fs::read(&state_path).unwrap();
        for _ in 0..2 {
            let result = request_watcher_stop(&root, &state, &owner).unwrap();
            assert_eq!(result["status"], "stopping");
            assert_eq!(result["completedRuns"], 4);
            assert_eq!(std::fs::read(&state_path).unwrap(), before);
            assert!(watcher_stop_pending(&root, &state));
        }
    }

    #[test]
    fn recycled_pid_wrong_executable_role_and_config_cannot_issue_stop() {
        for mismatch in 0..6 {
            let (root, state, mut owner) = fixture();
            match mismatch {
                0 => owner.pid += 1,
                1 => owner.start_time += 1,
                2 => owner.exe = root.join("other.exe"),
                3 => owner.args[1] = "--monitor-session".into(),
                4 => owner.args[3] = root.join("other-config.json").to_string_lossy().into_owned(),
                _ => owner.args.clear(),
            }
            assert!(request_watcher_stop(&root, &state, &owner).is_err(), "case {mismatch}");
            assert!(!watcher_stop_path(&root, &state).unwrap().exists());
            assert!(root.join("framescope-watcher-state.json").exists());
        }
    }

    #[test]
    fn legacy_or_path_traversal_tokens_fail_closed() {
        for token in ["", "../state", "0123456789abcdef0123456789abcdeg"] {
            let (root, mut state, owner) = fixture();
            state["WatcherInstanceId"] = json!(token);
            assert!(request_watcher_stop(&root, &state, &owner).is_err());
        }
        let (root, mut state, owner) = fixture();
        state["StopProtocolVersion"] = Value::Null;
        assert!(request_watcher_stop(&root, &state, &owner).is_err());
    }

    #[test]
    fn another_watcher_instance_does_not_inherit_stop_request() {
        let (root, mut state, owner) = fixture();
        request_watcher_stop(&root, &state, &owner).unwrap();
        state["WatcherInstanceId"] = json!("abcdef0123456789abcdef0123456789");
        assert!(!watcher_stop_pending(&root, &state));
    }
    #[test]
    fn exited_owned_child_with_wrong_config_cannot_complete_shutdown() {
        let (root, mut state, identity) = fixture();
        use std::io::{BufRead, BufReader};
        use std::process::{Command, Stdio};
        let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("src/backend_watcher_owner_fixture.rs");
        assert!(Command::new("rustc").arg(source).arg("-o").arg(&identity.exe).status().unwrap().success());
        let mut child = Command::new(&identity.exe).args(&identity.args[1..])
            .stdin(Stdio::piped()).stdout(Stdio::piped()).spawn().unwrap();
        let mut ready = String::new();
        BufReader::new(child.stdout.take().unwrap()).read_line(&mut ready).unwrap();
        assert_eq!(ready.trim(), "ready");
        let start_time = child_start_time(&child);
        let pid = child.id();
        drop(child.stdin.take());
        child.wait().unwrap();
        state["WatcherPid"] = json!(pid);
        state["WatcherStartTimeUnix"] = json!(start_time);
        state["Phase"] = json!("stopped");
        state["ActiveMonitors"] = json!([]);
        let wrong_config = root.join("wrong-config.json");
        std::fs::write(&wrong_config, "{}").unwrap();
        state["ConfigPath"] = json!(wrong_config);
        std::fs::write(root.join("framescope-watcher-state.json"), state.to_string()).unwrap();
        let mut owned = OwnedWatcher { child, root, identity: WatcherOwner {pid, start_time, ..identity},
            token: state["WatcherInstanceId"].as_str().map(str::to_string) };
        assert!(owned.child.try_wait().unwrap().is_some());
        assert!(crate::host_shutdown::completed(true, &state, owned.token.as_deref()).unwrap());
        let error = owned_state(&mut owned).expect_err("terminal state must still match retained startup config");
        assert!(error.contains("配置"), "wrong config must be rejected specifically, got: {error}");
        state["ConfigPath"] = json!(owned.root.join("framescope-config.json"));
        std::fs::write(owned.root.join("framescope-watcher-state.json"), state.to_string()).unwrap();
        assert!(owned_state(&mut owned).is_ok(), "matching config terminal state must work after child exit");
        let wrong_startup_config = owned.root.join("wrong-config.json").to_string_lossy().into_owned();
        *owned.identity.args.last_mut().unwrap() = wrong_startup_config;
        assert!(owned_state(&mut owned).unwrap_err().contains("配置"));
    }

    #[test]
    fn status_waits_for_report_and_retains_terminal_acknowledgement() {
        let (root, mut state, owner) = fixture();
        let run = root.join("test-run");
        std::fs::create_dir_all(&run).unwrap();
        state["ActiveMonitors"] = json!([{"RunDir": run, "Game": "Test", "MonitorPid": 12}]);
        let state_path = root.join("framescope-watcher-state.json");
        request_watcher_stop(&root, &state, &owner).unwrap();
        assert_eq!(monitor_status_from_state(&state_path, &state, true).unwrap()["phase"], "stopping");
        state["Phase"] = json!("generating");
        assert_eq!(monitor_status_from_state(&state_path, &state, true).unwrap()["phase"], "generating");
        std::fs::write(run.join("report-progress.json"),
            r#"{"ReportProgressPercent":42,"ReportProgressEtaSeconds":3}"#).unwrap();
        let status = monitor_status_from_state(&state_path, &state, true).unwrap();
        assert_eq!(status["reportPercent"], 42);
        assert_eq!(status["reportEtaSeconds"], 3);
        state["Phase"] = json!("stopped");
        state["CompletedRuns"] = json!(5);
        state["ActiveMonitors"] = json!([]);
        let status = monitor_status_from_state(&state_path, &state, false).unwrap();
        assert_eq!(status["phase"], "stopped");
        assert_eq!(status["completedRuns"], 5);
        assert_eq!(status["running"], false);
    }

    #[test]
    fn dead_watcher_without_terminal_ack_is_not_reported_as_stopped() {
        let (root, mut state, owner) = fixture();
        request_watcher_stop(&root, &state, &owner).unwrap();
        state["Phase"] = json!("stopping");
        let status = monitor_status_from_state(&root.join("framescope-watcher-state.json"), &state, false).unwrap();
        assert_eq!(status["phase"], "idle");
        assert_eq!(status["running"], false);
    }

    #[test]
    fn unreadable_monitor_state_is_not_a_zero_count_exit() {
        let (root, _, _) = fixture();
        let path = root.join("framescope-watcher-state.json");
        std::fs::write(&path, "{unfinished").unwrap();
        assert!(monitor_status_at_path(&path).is_err());
    }

    #[cfg(windows)]
    #[test]
    fn locked_monitor_state_is_not_a_zero_count_exit() {
        let (root, _, _) = fixture();
        let path = root.join("framescope-watcher-state.json");
        use std::os::windows::fs::OpenOptionsExt;
        std::fs::write(&path, r#"{"Phase":"stopped","CompletedRuns":1}"#).unwrap();
        let held = std::fs::OpenOptions::new().read(true).share_mode(0).open(&path).unwrap();
        assert!(monitor_status_at_path(&path).is_err());
        drop(held);
        assert_eq!(monitor_status_at_path(&path).unwrap()["completedRuns"], 1);
    }

    #[test]
    fn three_segment_run_name_is_rejected_without_panicking() {
        assert_eq!(parse_run_timestamp("game-20260918-010000"), None);
    }
}
#[cfg(test)]
mod host_shutdown_binding_tests {
    #[test]
    fn monitor_startup_default_and_overlay_are_independent() {
        assert_eq!(super::default_config()["AutoStartMonitoring"], true);
        let source = include_str!("lib.rs");
        let start = source.split("fn start_selected(").nth(1).unwrap().split("fn save_native_settings").next().unwrap();
        assert!(!start.contains("start_overlay_recording"), "showing an overlay cannot start report monitoring");
        assert!(source.contains("backend::auto_start_monitor()"));
        assert_eq!(source.matches("backend::auto_start_monitor()").count(), 1, "auto-start belongs only to process setup");
        let stop = source.split("fn stop_windows(").nth(1).unwrap().split("fn active(").next().unwrap();
        assert!(!stop.contains("monitor_stop"));
    }

    #[test]
    fn status_lists_only_live_monitor_workers_without_config_selection() {
        let state = serde_json::json!({"ActiveMonitors":[
            {"RunDir":"test-cs2","MonitorPid":12,"Game":"Counter-Strike 2"},
            {"RunDir":"old-run","MonitorPid":null,"Game":"Exited game"},
            {"RunDir":"test-delta","MonitorPid":13,"Game":"Delta Force"}
        ]});
        let status = super::monitor_status_from_state(std::path::Path::new("test-state.json"), &state, true).unwrap();
        assert_eq!(status["targetNames"], serde_json::json!(["Counter-Strike 2","Delta Force"]));
        let idle = super::monitor_status_from_state(std::path::Path::new("test-state.json"), &serde_json::json!({}), true).unwrap();
        assert_eq!(idle["targetNames"], serde_json::json!([]));
    }
    #[test]
    fn every_exit_uses_async_owned_shutdown() {
        let source = include_str!("lib.rs");
        assert!(source.contains("mod host_shutdown;"));
        assert!(source.contains("api.prevent_exit()"));
        assert!(!source.contains("backend::stop_overlay_recording(pid, start_time)"));
    }
    #[test]
    fn starts_retain_the_owned_child() {
        let source = include_str!("backend.rs");
        let start = source.split("pub fn monitor_start(").nth(1).unwrap().split("pub fn auto_start_monitor").next().unwrap();
        assert!(start.contains("OWNED_WATCHER.lock()"));
        assert!(start.contains("Some(OwnedWatcher { child, root, identity, token: None })"));
        let shutdown = source.split("pub fn shutdown_owned_monitor()").nth(1).unwrap().split("struct WatcherOwner").next().unwrap();
        assert!(shutdown.contains("owned.child.try_wait()"));
    }
    #[test]
    fn borrowed_watcher_is_never_adopted_and_closing_starts_are_rejected() {
        let source = include_str!("backend.rs");
        let stop = source.split("pub fn monitor_stop()").nth(1).unwrap().split("fn owned_state").next().unwrap();
        assert!(stop.contains("OWNED_WATCHER.lock()"));
        assert!(!stop.contains("backend_root()?"));
        let start = source.split("pub fn monitor_start(").nth(1).unwrap().split("pub fn auto_start_monitor").next().unwrap();
        assert!(start.find("HOST_CLOSING.load").unwrap() < start.find("OWNED_WATCHER.lock()").unwrap());
        assert!(start.find("OWNED_WATCHER.lock()").unwrap() < start.find(".spawn()").unwrap());
        let close = source.split("pub fn begin_shutdown()").nth(1).unwrap().split("pub fn cancel_shutdown").next().unwrap();
        assert!(!close.contains(".lock()"), "message-pump exit handler must never wait on the worker lock");
    }
    #[test]
    fn closing_gate_rejects_start_without_touching_backend_then_allows_retry() {
        assert!(super::begin_shutdown());
        assert!(!super::begin_shutdown(), "duplicate exit cannot enqueue another worker");
        assert!(super::monitor_start().unwrap_err().contains("不能启动"));
        assert!(super::auto_start_monitor().unwrap_err().contains("不能启动"));
        assert!(super::report_regenerate("fake-no-backend").unwrap_err().contains("不能开始"));
        super::cancel_shutdown();
        assert!(super::begin_shutdown(), "timeout cancellation must permit retry");
        super::cancel_shutdown();
    }
}
