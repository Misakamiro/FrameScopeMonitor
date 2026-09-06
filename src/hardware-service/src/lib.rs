pub mod overlay;
pub mod provider;

use provider::ResidentProvider;
use serde::{Deserialize, Serialize};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

const MIN_INTERVAL_MS: u64 = 250;
const MAX_INTERVAL_MS: u64 = 5000;
const DEFAULT_THROTTLE_MS: u64 = 1000;

#[derive(Debug, Deserialize)]
pub struct Request {
    pub id: String,
    pub method: String,
    #[serde(default)]
    pub params: serde_json::Value,
}

#[derive(Debug, Serialize)]
pub struct Response<T: Serialize> {
    pub id: String,
    pub ok: bool,
    pub result: Option<T>,
    pub error: Option<String>,
}

#[derive(Debug, Clone)]
pub struct ServiceState {
    pub running: bool,
    pub sequence: u64,
    pub sampling_interval_ms: u64,
    /// Optional sibling executable which hosts the official LibreHardwareMonitor
    /// .NET library. Rust deliberately does not reimplement the vendor driver
    /// protocol; it asks this narrow helper for one structured snapshot.
    pub lhm_helper_path: Option<String>,
}

impl Default for ServiceState {
    fn default() -> Self {
        Self {
            running: true,
            sequence: 0,
            sampling_interval_ms: 1000,
            lhm_helper_path: None,
        }
    }
}

/// Process-wide resident sampling state. Unlike `ServiceState` (per pipe
/// connection) this survives client reconnects so the overlay cache, the
/// throttle window, and the resident provider child outlive any single client.
struct ResidentState {
    provider: Mutex<ResidentProvider>,
    last_payload: Mutex<Option<serde_json::Value>>,
    last_real_read: Mutex<Option<Instant>>,
    overlay_cache: Mutex<Option<overlay::OverlayData>>,
    active_gpu: AtomicUsize,
    throttle: Mutex<Duration>,
}

static RESIDENT: OnceLock<ResidentState> = OnceLock::new();

fn resident() -> &'static ResidentState {
    RESIDENT.get_or_init(|| ResidentState {
        provider: Mutex::new(ResidentProvider::new()),
        last_payload: Mutex::new(None),
        last_real_read: Mutex::new(None),
        overlay_cache: Mutex::new(None),
        active_gpu: AtomicUsize::new(0),
        throttle: Mutex::new(Duration::from_millis(DEFAULT_THROTTLE_MS)),
    })
}

// Wired up by the resident ICMP thread (NexBox game_ping port).
fn ping_sample() -> Option<u32> {
    None
}

// Wired up by the ETW FPS module (NexBox game_fps port).
fn fps_sample() -> (Option<f64>, Option<f64>, Option<f64>) {
    (None, None, None)
}

pub fn dispatch(request: Request) -> serde_json::Value {
    let mut state = ServiceState::default();
    dispatch_with_state(request, &mut state)
}

pub fn dispatch_with_state(request: Request, state: &mut ServiceState) -> serde_json::Value {
    let id = request.id.clone();
    let response = match request.method.as_str() {
        "handshake" => serde_json::json!({
            "protocol": "framescope-hardware-service/1",
            "service": "framescope-hardware-service",
            "capabilities": [
                "get_static_info", "get_snapshot", "get_sensors", "set_sampling_interval",
                "get_overlay_metrics", "get_overlay_data", "get_static_hardware",
                "get_memory_status", "get_disk_status", "set_active_gpu", "clear_cache",
                "restart_provider", "start", "stop"
            ]
        }),
        "get_static_info" => capture_snapshot(state, "get_static_info"),
        "get_snapshot" => {
            state.sequence = state.sequence.saturating_add(1);
            let mut value = capture_snapshot(state, "get_snapshot");
            if let Some(obj) = value.as_object_mut() {
                obj.insert("sequence".into(), serde_json::json!(state.sequence));
                obj.insert(
                    "samplingIntervalMs".into(),
                    serde_json::json!(state.sampling_interval_ms),
                );
                obj.entry("capturedAt")
                    .or_insert_with(|| serde_json::json!(chrono_like_timestamp()));
            }
            value
        }
        "get_sensors" => {
            let snapshot = capture_snapshot(state, "get_sensors");
            serde_json::json!({
                "availability": snapshot.get("availability").cloned().unwrap_or_else(|| serde_json::json!("unavailable")),
                "sensors": snapshot.get("sensors").cloned().unwrap_or_else(|| serde_json::json!([])),
                "errors": snapshot.get("errors").cloned().unwrap_or_else(|| serde_json::json!([])),
                "pawnIo": snapshot.get("pawnIo").cloned().unwrap_or_else(|| serde_json::json!({"installed": false, "version": "", "availability": "unavailable"})),
                "updatedAt": snapshot.get("capturedAt").cloned().unwrap_or_else(|| serde_json::json!(chrono_like_timestamp()))
            })
        }
        "get_overlay_metrics" => {
            let data_root = request
                .params
                .get("dataRoot")
                .and_then(|v| v.as_str())
                .unwrap_or("");
            capture_overlay_metrics(data_root)
        }
        "get_overlay_data" => overlay_data_response(state),
        "get_memory_status" => memory_status_response(state),
        "get_disk_status" => disk_status_response(),
        "get_static_hardware" => static_hardware_response(state),
        "set_sampling_interval" => {
            let interval = request
                .params
                .get("samplingIntervalMs")
                .and_then(|v| v.as_u64());
            match interval {
                Some(value) if (MIN_INTERVAL_MS..=MAX_INTERVAL_MS).contains(&value) => {
                    state.sampling_interval_ms = value;
                    *resident().throttle.lock().unwrap() = Duration::from_millis(value);
                    serde_json::json!({ "status": "accepted", "samplingIntervalMs": value })
                }
                _ => return error_response(id, "invalid_sampling_interval"),
            }
        }
        "set_active_gpu" => {
            let index = request.params.get("index").and_then(|v| v.as_u64());
            match index {
                Some(value) => {
                    resident().active_gpu.store(value as usize, Ordering::Relaxed);
                    serde_json::json!({ "status": "accepted", "activeGpuIndex": value })
                }
                None => return error_response(id, "invalid_gpu_index"),
            }
        }
        "clear_cache" => {
            let res = resident();
            *res.last_payload.lock().unwrap() = None;
            *res.overlay_cache.lock().unwrap() = None;
            res.active_gpu.store(0, Ordering::Relaxed);
            serde_json::json!({ "status": "cleared" })
        }
        "restart_provider" => {
            resident().provider.lock().unwrap().kill();
            serde_json::json!({ "status": "restarting" })
        }
        "start" => {
            state.running = true;
            serde_json::json!({ "status": "started" })
        }
        "stop" => {
            state.running = false;
            serde_json::json!({ "status": "stopped" })
        }
        _ => return error_response(id, "unsupported_method"),
    };

    serde_json::json!({"id": id, "ok": true, "result": response, "error": null})
}

/// One snapshot from the resident provider, throttled: repeated calls inside
/// the sampling interval reuse the last payload instead of re-reading LHM.
fn read_snapshot(state: &ServiceState) -> Result<serde_json::Value, String> {
    let res = resident();
    res.provider
        .lock()
        .unwrap()
        .set_helper(state.lhm_helper_path.clone());

    let throttle = *res.throttle.lock().unwrap();
    {
        let payload = res.last_payload.lock().unwrap();
        if let Some(payload) = payload.as_ref() {
            if let Some(last) = *res.last_real_read.lock().unwrap() {
                if last.elapsed() < throttle {
                    return Ok(payload.clone());
                }
            }
        }
    }

    let payload = res.provider.lock().unwrap().read()?;
    *res.last_payload.lock().unwrap() = Some(payload.clone());
    *res.last_real_read.lock().unwrap() = Some(Instant::now());
    Ok(payload)
}

fn capture_snapshot(state: &ServiceState, method: &str) -> serde_json::Value {
    if state.lhm_helper_path.as_deref().unwrap_or("").trim().is_empty() {
        return unavailable_payload(method);
    }
    match read_snapshot(state) {
        Ok(value) => value,
        Err(error) => unavailable_with_error(method, error),
    }
}

/// Aggregated overlay snapshot. On provider trouble the last good overlay data
/// is served so the overlay does not blink (NexBox behavior); with no data at
/// all the all-null default is returned — never synthetic numbers.
fn overlay_data_response(state: &ServiceState) -> serde_json::Value {
    let res = resident();
    let data = match read_snapshot(state) {
        Ok(payload) => {
            let requested = res.active_gpu.load(Ordering::Relaxed);
            let mut data =
                overlay::build_overlay_data(&payload, fps_sample(), ping_sample(), requested);
            let mut cache = res.overlay_cache.lock().unwrap();
            if let Some(old) = cache.as_ref() {
                overlay::merge_overlay(&mut data, old);
            }
            *cache = Some(data.clone());
            data
        }
        Err(error) => {
            eprintln!("framescope-hardware-service: overlay read failed: {error}");
            res.overlay_cache
                .lock()
                .unwrap()
                .clone()
                .unwrap_or_default()
        }
    };
    serde_json::to_value(&data).unwrap_or(serde_json::Value::Null)
}

fn memory_status_response(state: &ServiceState) -> serde_json::Value {
    let Ok(payload) = read_snapshot(state) else {
        return serde_json::Value::Null;
    };
    let used_gb = dynamic_value(&payload, "memoryUsedGb");
    let total_gb = dynamic_value(&payload, "memoryTotalGb");
    match (used_gb, total_gb) {
        (Some(used), Some(total)) if total > 0.0 => serde_json::json!({
            "total": total * 1024.0,
            "used": used * 1024.0,
            "available": (total - used) * 1024.0,
            "usage_percent": (used / total * 100.0).clamp(0.0, 100.0)
        }),
        _ => serde_json::Value::Null,
    }
}

#[cfg(windows)]
fn disk_status_response() -> serde_json::Value {
    use std::ffi::OsStr;
    use std::iter::once;
    use std::os::windows::ffi::OsStrExt;

    let drive = std::env::var("SystemDrive").unwrap_or_else(|_| "C:".into());
    let root = format!("{drive}\\");
    let wide: Vec<u16> = OsStr::new(&root).encode_wide().chain(once(0)).collect();
    let mut free_to_caller: u64 = 0;
    let mut total_bytes: u64 = 0;
    let mut free_bytes: u64 = 0;
    // SAFETY: root is a NUL-terminated wide string; out-params are plain u64s.
    let ok = unsafe {
        windows_sys::Win32::Storage::FileSystem::GetDiskFreeSpaceExW(
            wide.as_ptr(),
            &mut free_to_caller,
            &mut total_bytes,
            &mut free_bytes,
        )
    };
    if ok == 0 || total_bytes == 0 {
        return serde_json::Value::Null;
    }
    let gib = 1024.0 * 1024.0 * 1024.0;
    let total = total_bytes as f64 / gib;
    let available = free_bytes as f64 / gib;
    let used = total - available;
    serde_json::json!({
        "name": drive,
        "total_gb": (total * 100.0).round() / 100.0,
        "used_gb": (used * 100.0).round() / 100.0,
        "available_gb": (available * 100.0).round() / 100.0,
        "usage_percent": (used / total * 100.0).clamp(0.0, 100.0)
    })
}

#[cfg(not(windows))]
fn disk_status_response() -> serde_json::Value {
    serde_json::Value::Null
}

/// Full WMI-backed static hardware info; served from the provider's cached
/// `static` payload (computed on demand by the provider — expensive WMI work).
fn static_hardware_response(state: &ServiceState) -> serde_json::Value {
    if state
        .lhm_helper_path
        .as_deref()
        .unwrap_or("")
        .trim()
        .is_empty()
    {
        return serde_json::json!({
            "availability": "unavailable",
            "hardware": null,
            "errors": ["LibreHardwareMonitor provider is not configured"]
        });
    }
    let res = resident();
    let result = {
        let mut provider = res.provider.lock().unwrap();
        provider.set_helper(state.lhm_helper_path.clone());
        provider.send_raw("{\"cmd\":\"static\"}")
    };
    match result {
        Ok(value) => {
            let hardware = value
                .get("staticFull")
                .cloned()
                .unwrap_or(serde_json::Value::Null);
            let errors = value
                .get("errors")
                .cloned()
                .unwrap_or_else(|| serde_json::json!([]));
            let availability = if hardware.is_null() { "unavailable" } else { "available" };
            serde_json::json!({
                "availability": availability,
                "hardware": hardware,
                "errors": errors,
                "capturedAt": value
                    .get("capturedAt")
                    .cloned()
                    .unwrap_or_else(|| serde_json::json!(chrono_like_timestamp()))
            })
        }
        Err(error) => serde_json::json!({
            "availability": "unavailable",
            "hardware": null,
            "errors": [error]
        }),
    }
}

fn dynamic_value(snapshot: &serde_json::Value, key: &str) -> Option<f64> {
    snapshot
        .get("dynamic")
        .and_then(|dynamic| dynamic.get(key))
        .and_then(|metric| metric.get("value"))
        .and_then(serde_json::Value::as_f64)
}

fn capture_overlay_metrics(data_root: &str) -> serde_json::Value {
    let captured_at = chrono_like_timestamp();
    let Some(csv) = find_latest_presentmon(data_root) else {
        return unavailable_overlay_metrics(
            captured_at,
            "No completed PresentMon CSV is available.",
        );
    };
    let frame_times = match read_frame_times(&csv) {
        Ok(values) if !values.is_empty() => values,
        Ok(_) => {
            return unavailable_overlay_metrics(
                captured_at,
                "PresentMon CSV contains no valid frame intervals.",
            )
        }
        Err(error) => {
            return unavailable_overlay_metrics(
                captured_at,
                &format!("PresentMon metrics read failed: {error}"),
            )
        }
    };
    let average = frame_times.iter().sum::<f64>() / frame_times.len() as f64;
    let low_1 = slow_average(&frame_times, 0.01);
    let low_01 = slow_average(&frame_times, 0.001);
    serde_json::json!({
        "availability": "available",
        "capturedAt": captured_at,
        "source": "framescope-presentmon",
        "runDir": csv.parent().and_then(|p| p.to_str()).unwrap_or(""),
        "frameCount": frame_times.len(),
        "fps": round(1000.0 / average, 2),
        "frameTimeMs": round(average, 3),
        "fps_1low": round(1000.0 / low_1, 2),
        "fps_01low": round(1000.0 / low_01, 2),
        "errors": []
    })
}

fn unavailable_overlay_metrics(captured_at: String, error: &str) -> serde_json::Value {
    serde_json::json!({
        "availability": "unavailable",
        "capturedAt": captured_at,
        "source": "framescope-presentmon",
        "runDir": "",
        "frameCount": 0,
        "fps": null,
        "frameTimeMs": null,
        "fps_1low": null,
        "fps_01low": null,
        "errors": [error]
    })
}

fn find_latest_presentmon(data_root: &str) -> Option<std::path::PathBuf> {
    if data_root.trim().is_empty() {
        return None;
    }
    let root = std::path::Path::new(data_root);
    if !root.is_dir() {
        return None;
    }
    let mut latest: Option<(std::path::PathBuf, std::time::SystemTime)> = None;
    let mut pending = vec![root.to_path_buf()];
    while let Some(dir) = pending.pop() {
        let Ok(entries) = std::fs::read_dir(&dir) else {
            continue;
        };
        for entry in entries.flatten() {
            let path = entry.path();
            if path.is_dir() {
                pending.push(path);
                continue;
            }
            if path
                .file_name()
                .and_then(|v| v.to_str())
                .map(|v| v.eq_ignore_ascii_case("presentmon.csv"))
                != Some(true)
            {
                continue;
            }
            let modified = entry
                .metadata()
                .and_then(|m| m.modified())
                .unwrap_or(std::time::UNIX_EPOCH);
            if latest
                .as_ref()
                .map(|(_, time)| modified > *time)
                .unwrap_or(true)
            {
                latest = Some((path, modified));
            }
        }
    }
    latest.map(|(path, _)| path)
}

fn read_frame_times(path: &std::path::Path) -> Result<Vec<f64>, String> {
    let text = std::fs::read_to_string(path).map_err(|e| e.to_string())?;
    let mut lines = text.lines();
    let header = lines.next().unwrap_or("");
    let index = split_csv(header)
        .iter()
        .position(|v| v.trim().eq_ignore_ascii_case("MsBetweenPresents"))
        .ok_or_else(|| "MsBetweenPresents column is missing".to_string())?;
    let mut values = Vec::new();
    for line in lines {
        let fields = split_csv(line);
        let Some(raw) = fields.get(index) else {
            continue;
        };
        let Ok(value) = raw.trim().parse::<f64>() else {
            continue;
        };
        if value.is_finite() && value > 0.0 && value < 10000.0 {
            values.push(value);
        }
    }
    Ok(values)
}

fn split_csv(line: &str) -> Vec<String> {
    let mut fields = Vec::new();
    let mut current = String::new();
    let mut quoted = false;
    let chars: Vec<char> = line.chars().collect();
    let mut index = 0;
    while index < chars.len() {
        let c = chars[index];
        if c == '"' {
            if quoted && index + 1 < chars.len() && chars[index + 1] == '"' {
                current.push('"');
                index += 1;
            } else {
                quoted = !quoted;
            }
        } else if c == ',' && !quoted {
            fields.push(current.clone());
            current.clear();
        } else {
            current.push(c);
        }
        index += 1;
    }
    fields.push(current);
    fields
}

fn slow_average(values: &[f64], fraction: f64) -> f64 {
    let count = ((values.len() as f64 * fraction).ceil() as usize).max(1);
    let mut sorted = values.to_vec();
    sorted.sort_by(|a, b| b.partial_cmp(a).unwrap_or(std::cmp::Ordering::Equal));
    sorted.iter().take(count).sum::<f64>() / count as f64
}

fn round(value: f64, digits: i32) -> f64 {
    let scale = 10_f64.powi(digits);
    (value * scale).round() / scale
}

fn unavailable_payload(method: &str) -> serde_json::Value {
    let mut value = serde_json::json!({"availability":"unavailable","static":null,"dynamic":null,"sensors":[],"errors":["LibreHardwareMonitor provider is not configured"]});
    if method == "get_sensors" {
        value = serde_json::json!({"availability":"unavailable","sensors":[],"errors":["LibreHardwareMonitor provider is not configured"]});
    }
    value
}

fn unavailable_with_error(method: &str, error: String) -> serde_json::Value {
    let mut value = unavailable_payload(method);
    if let Some(errors) = value.get_mut("errors").and_then(|v| v.as_array_mut()) {
        errors[0] = serde_json::json!(error);
    }
    value
}

fn error_response(id: String, error: &str) -> serde_json::Value {
    serde_json::json!({"id": id, "ok": false, "result": null, "error": error})
}

fn chrono_like_timestamp() -> String {
    // Keep the service dependency-free while still exposing a monotonic-enough capture marker.
    match std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH) {
        Ok(duration) => duration.as_millis().to_string(),
        Err(_) => "0".to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn supported_methods_fail_closed_without_fake_values() {
        let mut state = ServiceState::default();
        let response = dispatch_with_state(
            Request {
                id: "1".into(),
                method: "get_snapshot".into(),
                params: serde_json::Value::Null,
            },
            &mut state,
        );
        assert_eq!(response["ok"], true);
        assert_eq!(response["result"]["availability"], "unavailable");
        assert!(response["result"]["sequence"].as_u64().unwrap() >= 1);
        assert!(response["result"]["dynamic"].is_null());
    }

    #[test]
    fn unknown_methods_are_rejected() {
        let response = dispatch(Request {
            id: "2".into(),
            method: "load_dll".into(),
            params: serde_json::Value::Null,
        });
        assert_eq!(response["ok"], false);
        assert_eq!(response["error"], "unsupported_method");
    }

    #[test]
    fn handshake_exposes_protocol_and_capabilities() {
        let response = dispatch(Request {
            id: "3".into(),
            method: "handshake".into(),
            params: serde_json::Value::Null,
        });
        assert_eq!(
            response["result"]["protocol"],
            "framescope-hardware-service/1"
        );
        for capability in ["get_snapshot", "get_overlay_data", "get_static_hardware"] {
            assert!(
                response["result"]["capabilities"]
                    .as_array()
                    .unwrap()
                    .iter()
                    .any(|v| v == capability),
                "missing capability {capability}"
            );
        }
    }

    #[test]
    fn sampling_interval_is_validated_and_persisted_in_state() {
        let mut state = ServiceState::default();
        let bad = dispatch_with_state(
            Request {
                id: "4".into(),
                method: "set_sampling_interval".into(),
                params: serde_json::json!({"samplingIntervalMs": 10}),
            },
            &mut state,
        );
        assert_eq!(bad["ok"], false);
        let good = dispatch_with_state(
            Request {
                id: "5".into(),
                method: "set_sampling_interval".into(),
                params: serde_json::json!({"samplingIntervalMs": 500}),
            },
            &mut state,
        );
        assert_eq!(good["ok"], true);
        assert_eq!(state.sampling_interval_ms, 500);
    }

    #[test]
    fn overlay_metrics_reads_latest_presentmon_csv_and_derives_lows() {
        let root =
            std::env::temp_dir().join(format!("framescope-overlay-metrics-{}", std::process::id()));
        let run = root.join("run");
        std::fs::create_dir_all(&run).unwrap();
        std::fs::write(
            run.join("presentmon.csv"),
            "Application,MsBetweenPresents\nGame.exe,10\nGame.exe,20\nGame.exe,30\n",
        )
        .unwrap();
        let response = dispatch(Request {
            id: "5".into(),
            method: "get_overlay_metrics".into(),
            params: serde_json::json!({"dataRoot": root.to_string_lossy()}),
        });
        assert_eq!(response["ok"], true);
        assert_eq!(response["result"]["availability"], "available");
        assert_eq!(response["result"]["frameCount"], 3);
        assert_eq!(response["result"]["fps"], 50.0);
        assert_eq!(response["result"]["frameTimeMs"], 20.0);
        assert_eq!(response["result"]["fps_1low"], 33.33);
        assert_eq!(response["result"]["fps_01low"], 33.33);
        let _ = std::fs::remove_dir_all(root);
    }

    #[test]
    fn overlay_metrics_fails_closed_without_csv() {
        let response = dispatch(Request {
            id: "6".into(),
            method: "get_overlay_metrics".into(),
            params: serde_json::json!({"dataRoot": "C:\\framescope-no-such-root"}),
        });
        assert_eq!(response["ok"], true);
        assert_eq!(response["result"]["availability"], "unavailable");
        assert!(response["result"]["fps"].is_null());
        assert!(response["result"]["errors"].as_array().unwrap().len() >= 1);
    }

    #[test]
    fn overlay_data_fails_closed_without_helper() {
        let mut state = ServiceState::default();
        let response = dispatch_with_state(
            Request {
                id: "7".into(),
                method: "get_overlay_data".into(),
                params: serde_json::Value::Null,
            },
            &mut state,
        );
        assert_eq!(response["ok"], true);
        let data = &response["result"];
        assert!(data["cpu_usage"].is_null());
        assert!(data["gpu_temp"].is_null());
        assert!(data["fps"].is_null());
        assert_eq!(data["gpu_sensors"].as_array().unwrap().len(), 0);
        assert_eq!(data["active_gpu_index"], 0);
    }

    #[test]
    fn static_hardware_fails_closed_without_helper() {
        let mut state = ServiceState::default();
        let response = dispatch_with_state(
            Request {
                id: "8".into(),
                method: "get_static_hardware".into(),
                params: serde_json::Value::Null,
            },
            &mut state,
        );
        assert_eq!(response["result"]["availability"], "unavailable");
        assert!(response["result"]["hardware"].is_null());
    }

    #[test]
    fn set_active_gpu_accepts_index_and_clear_cache_resets_it() {
        let response = dispatch(Request {
            id: "9".into(),
            method: "set_active_gpu".into(),
            params: serde_json::json!({"index": 1}),
        });
        assert_eq!(response["result"]["activeGpuIndex"], 1);
        assert_eq!(
            dispatch(Request {
                id: "10".into(),
                method: "set_active_gpu".into(),
                params: serde_json::json!({"index": "x"}),
            })["error"],
            "invalid_gpu_index"
        );
        let cleared = dispatch(Request {
            id: "11".into(),
            method: "clear_cache".into(),
            params: serde_json::Value::Null,
        });
        assert_eq!(cleared["result"]["status"], "cleared");
    }

    #[test]
    fn memory_status_fails_closed_without_helper() {
        let mut state = ServiceState::default();
        let response = dispatch_with_state(
            Request {
                id: "12".into(),
                method: "get_memory_status".into(),
                params: serde_json::Value::Null,
            },
            &mut state,
        );
        assert!(response["result"].is_null());
    }
}
