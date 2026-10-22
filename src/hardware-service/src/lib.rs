pub mod game_fps;
pub mod optimization;
pub mod overlay;
pub mod ping;
pub mod provider;
#[path = "../../core/windows_shutdown.rs"]
mod windows_shutdown;

use provider::ResidentProvider;
use serde::{Deserialize, Serialize};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
#[cfg(test)]
use std::sync::atomic::AtomicUsize;
use std::sync::{Mutex, OnceLock};
use std::collections::HashMap;
use std::time::{Duration, Instant};

const MIN_INTERVAL_MS: u64 = 250;
const MAX_INTERVAL_MS: u64 = 5000;
// Only the physical hardware read is throttled. fps_sample() is evaluated on
// every overlay request independently of this cache (the host polls at 10Hz).
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
    last_payload: Mutex<Option<(serde_json::Value, Instant)>>,
    overlay_cache: Mutex<Option<OverlayCache>>,
    active_gpu: Mutex<overlay::GpuSelection>,
    throttle: Mutex<Duration>,
}

struct OverlayCache {
    data: overlay::OverlayData,
    last_real: HashMap<String, Instant>,
}

fn aggregate_overlay_sample(cache: &mut Option<OverlayCache>, mut data: overlay::OverlayData,
    observed_at: Option<Instant>, now: Instant) -> overlay::OverlayData {
    if observed_at.map(|time| now.saturating_duration_since(time) <= Duration::from_secs(1)) != Some(true) {
        overlay::clear_hardware_values(&mut data);
    }
    let mut last_real = cache.as_ref().map(|c| c.last_real.clone()).unwrap_or_default();
    if let Some(time) = observed_at {
        // Cached provider payloads keep their original observation time across all consumers.
        overlay::record_observed_fields(&data, time, &mut last_real);
    }
    if let Some(previous) = cache.as_mut() {
        overlay::merge_overlay_with_ttl(&mut data, &previous.data, now, &last_real,
            Duration::from_secs(1));
    }
    last_real.retain(|_, time| now.saturating_duration_since(*time) <= Duration::from_secs(1));
    *cache = Some(OverlayCache { data: data.clone(), last_real });
    data
}

fn aggregate_overlay_failure(cache: &mut Option<OverlayCache>, requested: usize, now: Instant) -> overlay::OverlayData {
    let mut missing = overlay::OverlayData::default();
    if let Some(previous) = cache.as_ref() {
        // Keep identities so each GPU can expire independently; retain no cached numbers as new observations.
        missing.gpu_sensors = previous.data.gpu_sensors.clone();
        missing.active_gpu_index = requested.min(missing.gpu_sensors.len().saturating_sub(1));
        missing.cpu_core_clocks.resize(previous.data.cpu_core_clocks.len(), None);
        missing.cpu_core_voltages.resize(previous.data.cpu_core_voltages.len(), None);
    }
    aggregate_overlay_sample(cache, missing, None, now)
}

static RESIDENT: OnceLock<ResidentState> = OnceLock::new();

fn resident() -> &'static ResidentState {
    RESIDENT.get_or_init(|| ResidentState {
        provider: Mutex::new(ResidentProvider::new()),
        last_payload: Mutex::new(None),
        overlay_cache: Mutex::new(None),
        active_gpu: Mutex::new(overlay::GpuSelection::default()),
        throttle: Mutex::new(Duration::from_millis(DEFAULT_THROTTLE_MS)),
    })
}

// Wired up by the resident ICMP thread (NexBox game_ping port).
fn ping_sample() -> Option<u32> {
    ping::last_ping()
}

// The ETW FPS monitor (NexBox game_fps port) starts lazily on the first
// overlay request; without admin rights the session start fails and the
// values stay null (fail-closed).
struct FpsStartGate {
    last_attempt: AtomicU64,
}

static FPS_START: FpsStartGate = FpsStartGate {
    last_attempt: AtomicU64::new(0),
};
static SHUTTING_DOWN: AtomicBool = AtomicBool::new(false);
static FPS_LIFECYCLE: Mutex<()> = Mutex::new(());

pub fn shutdown_service() -> Result<(), String> {
    SHUTTING_DOWN.store(true, Ordering::SeqCst);
    {
        let _lifecycle = FPS_LIFECYCLE.lock().unwrap_or_else(|p| p.into_inner());
        game_fps::stop_fps_monitor();
    }
    if let Some(resident) = RESIDENT.get() {
        resident.provider.lock().unwrap_or_else(|p| p.into_inner()).kill()?;
    }
    Ok(())
}

impl FpsStartGate {
    fn ensure(&self, now: u64, active: impl Fn() -> bool, start: impl FnOnce()) {
        if active() {
            return;
        }
        let last = self.last_attempt.load(Ordering::SeqCst);
        if now.saturating_sub(last) >= 5_000
            && self.last_attempt.compare_exchange(last, now, Ordering::SeqCst, Ordering::SeqCst).is_ok()
        {
            start();
        }
    }
}

fn fps_sample() -> (Option<f64>, Option<f64>, Option<f64>) {
    let _lifecycle = FPS_LIFECYCLE.lock().unwrap_or_else(|p| p.into_inner());
    if SHUTTING_DOWN.load(Ordering::SeqCst) {
        return (None, None, None);
    }
    // Recheck the worker each time: start sets active before ETW initialization,
    // and asynchronous initialization or a later session failure can clear it.
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0);
    FPS_START.ensure(now, game_fps::fps_monitor_active, game_fps::start_fps_monitor);
    (
        game_fps::get_cached_fps().map(f64::from),
        game_fps::get_cached_1low_fps().map(f64::from),
        game_fps::get_cached_01low_fps().map(f64::from),
    )
}

pub fn dispatch(request: Request) -> serde_json::Value {
    let mut state = ServiceState::default();
    dispatch_with_state(request, &mut state)
}

pub fn dispatch_with_state(request: Request, state: &mut ServiceState) -> serde_json::Value {
    let id = request.id.clone();
    if SHUTTING_DOWN.load(Ordering::SeqCst) && request.method != "stop" {
        return error_response(id, "service_shutting_down");
    }
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
                    let res = resident();
                    let current = res.overlay_cache.lock().unwrap().as_ref()
                        .map(|cache| cache.data.gpu_sensors.clone()).unwrap_or_default();
                    res.active_gpu.lock().unwrap().request(value as usize, &current);
                    serde_json::json!({ "status": "accepted", "activeGpuIndex": value })
                }
                None => return error_response(id, "invalid_gpu_index"),
            }
        }
        "clear_cache" => {
            let res = resident();
            *res.last_payload.lock().unwrap() = None;
            *res.overlay_cache.lock().unwrap() = None;
            *res.active_gpu.lock().unwrap() = overlay::GpuSelection::default();
            serde_json::json!({ "status": "cleared" })
        }
        "restart_provider" => {
            // force_reset also clears the suspended flag, so a manual restart
            // recovers from consecutive-failure give-up.
            let mut provider = resident().provider.lock().unwrap();
            if SHUTTING_DOWN.load(Ordering::SeqCst) { return error_response(id, "service_shutting_down"); }
            if let Err(error) = provider.force_reset() { return error_response(id, &error); }
            serde_json::json!({ "status": "restarting" })
        }
        "start" => {
            state.running = true;
            serde_json::json!({ "status": "started" })
        }
        "stop" => {
            state.running = false;
            if let Err(error) = shutdown_service() { return error_response(id, &error); }
            serde_json::json!({ "status": "stopped", "shutdownComplete": true })
        }
        _ => return error_response(id, "unsupported_method"),
    };

    serde_json::json!({"id": id, "ok": true, "result": response, "error": null})
}

/// One snapshot from the resident provider, throttled: repeated calls inside
/// the sampling interval reuse the last payload instead of re-reading LHM.
fn read_snapshot(state: &ServiceState) -> Result<serde_json::Value, String> {
    read_snapshot_with_observation(state).map(|(payload, _)| payload)
}

fn read_snapshot_with_observation(state: &ServiceState) -> Result<(serde_json::Value, Instant), String> {
    let res = resident();
    // Serialize cache check/read/publication with provider requests: a payload and its
    // actual observation time are one snapshot, including get_sensors/get_snapshot reads.
    let mut provider = res.provider.lock().unwrap();
    if SHUTTING_DOWN.load(Ordering::SeqCst) { return Err("service_shutting_down".into()); }
    provider.set_helper(state.lhm_helper_path.clone())?;
    let throttle = *res.throttle.lock().unwrap();
    if let Some((payload, observed_at)) = res.last_payload.lock().unwrap().as_ref() {
        if observed_at.elapsed() < throttle {
            return Ok((payload.clone(), *observed_at));
        }
    }
    let payload = provider.read()?;
    let observed_at = Instant::now();
    *res.last_payload.lock().unwrap() = Some((payload.clone(), observed_at));
    Ok((payload, observed_at))
}

fn capture_snapshot(state: &ServiceState, method: &str) -> serde_json::Value {
    if state.lhm_helper_path.as_deref().unwrap_or("").trim().is_empty() {
        return unavailable_payload(method);
    }
    match read_snapshot(state) {
        Ok(value) => value,
        Err(error) => {
            let mut value = unavailable_with_error(method, error);
            value["providerState"] = resident().provider.lock().unwrap().diagnostics();
            value
        },
    }
}

/// Aggregated overlay snapshot. Carry-forward (field-level OR merge against
/// the previous snapshot) is only applied while each actual observation is fresh
/// (≤1s old) — without that bound a retired sensor or an exited game would
/// keep serving its last value forever. Stale/failing paths return nulls
/// (fail-closed), never synthetic numbers.
fn overlay_data_response(state: &ServiceState) -> serde_json::Value {
    let res = resident();
    let data = match read_snapshot_with_observation(state) {
        Ok((payload, observed_at)) => {
            let mut data = overlay::build_overlay_data(&payload, fps_sample(), ping_sample(), 0);
            res.active_gpu.lock().unwrap().apply(&mut data);
            aggregate_overlay_sample(&mut res.overlay_cache.lock().unwrap(), data, Some(observed_at), Instant::now())
        }
        Err(error) => {
            eprintln!("framescope-hardware-service: overlay read failed: {error}");
            let mut cache = res.overlay_cache.lock().unwrap();
            let requested = cache.as_ref().map(|c| c.data.active_gpu_index).unwrap_or(0);
            aggregate_overlay_failure(&mut cache, requested, Instant::now())
        }
    };
    let mut value = serde_json::to_value(&data).unwrap_or(serde_json::Value::Null);
    if let Some(object) = value.as_object_mut() {
        object.insert("target_pid".into(), serde_json::json!(game_fps::current_target_pid()));
        object.insert("timestamp_ms".into(), serde_json::json!(
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH)
                .unwrap_or_default().as_millis() as u64));
    }
    value
}

#[cfg(test)]
fn publish_inprocess_snapshot(path: &std::path::Path, value: &serde_json::Value) -> std::io::Result<()> {
    // The DLL accepts at most 4096 bytes. Sensor arrays belong to the main UI;
    // forwarding them here can make an otherwise valid snapshot unreadable.
    let compact = serde_json::json!({
        "target_pid": value["target_pid"], "timestamp_ms": value["timestamp_ms"],
        "fps": value["fps"], "fps_1low": value["fps_1low"], "fps_01low": value["fps_01low"],
    });
    let temp = path.with_extension("json.tmp");
    std::fs::write(&temp, serde_json::to_vec(&compact)?)?;
    std::fs::rename(temp, path)
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
        if SHUTTING_DOWN.load(Ordering::SeqCst) { Err("service_shutting_down".into()) }
        else { provider.set_helper(state.lhm_helper_path.clone()).and_then(|_| provider.send_raw("{\"cmd\":\"static\"}")) }
    };
    match result {
        Ok(value) => {
            let mut hardware = value
                .get("staticFull")
                .cloned()
                .unwrap_or(serde_json::Value::Null);
            if !hardware.is_null() {
                let overlay = overlay_data_response(state);
                merge_live_into_static(&mut hardware, &overlay);
            }
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

/// The provider's static payload carries null live values; the UI expects the
/// current GPU temperature/usage and CPU load inline, so overlay them in.
fn merge_live_into_static(hardware: &mut serde_json::Value, overlay: &serde_json::Value) {
    if let Some(obj) = hardware.as_object_mut() {
        if let Some(cpu) = obj.get_mut("cpu").and_then(|v| v.as_object_mut()) {
            cpu.insert("load_percentage".into(), overlay.get("cpu_usage").cloned().unwrap_or(serde_json::Value::Null));
        }
        if let Some(gpus) = obj.get_mut("gpu").and_then(|v| v.as_array_mut()) {
            let sensors = overlay
                .get("gpu_sensors")
                .and_then(|v| v.as_array())
                .cloned()
                .unwrap_or_default();
            let mut static_names = HashMap::new();
            for gpu in gpus.iter() {
                let name = gpu.get("name").and_then(|v| v.as_str()).unwrap_or("").to_ascii_lowercase();
                *static_names.entry(name).or_insert(0usize) += 1;
            }
            for gpu in gpus.iter_mut() {
                let name = gpu.get("name").and_then(|v| v.as_str()).unwrap_or("");
                let identifier = gpu.get("identifier").and_then(|v| v.as_str()).filter(|id| !id.is_empty());
                let matches: Vec<_> = sensors.iter().filter(|sensor| {
                    if let Some(id) = identifier {
                        sensor.get("identifier").and_then(|v| v.as_str())
                            .map(|other| id.eq_ignore_ascii_case(other)).unwrap_or(false)
                    } else {
                        !name.is_empty() && static_names.get(&name.to_ascii_lowercase()) == Some(&1)
                            && sensor.get("name").and_then(|v| v.as_str())
                                .map(|other| name.eq_ignore_ascii_case(other)).unwrap_or(false)
                    }
                }).collect();
                let sensor = if matches.len() == 1 { Some(matches[0]) } else { None };
                if let Some(gpu_obj) = gpu.as_object_mut() {
                    for key in ["temperature", "usage"] {
                        let value = sensor.and_then(|s| s.get(key)).cloned().unwrap_or(serde_json::Value::Null);
                        gpu_obj.insert(key.to_string(), value);
                    }
                }
            }
        }
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
    // Do not expose a single startup frame as a percentile. The live tracker
    // uses the same minimum sample gates (100 / 900) and reports unavailable
    // until the corresponding window has enough data.
    let low_1 = slow_average(&frame_times, 0.01, 100);
    let low_01 = slow_average(&frame_times, 0.001, 900);
    serde_json::json!({
        "availability": "available",
        "capturedAt": captured_at,
        "source": "framescope-presentmon",
        "runDir": csv.parent().and_then(|p| p.to_str()).unwrap_or(""),
        "frameCount": frame_times.len(),
        "fps": round(1000.0 / average, 2),
        "frameTimeMs": round(average, 3),
        "fps_1low": low_1.map(|v| round(1000.0 / v, 2)),
        "fps_01low": low_01.map(|v| round(1000.0 / v, 2)),
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

fn slow_average(values: &[f64], fraction: f64, minimum_samples: usize) -> Option<f64> {
    if values.len() < minimum_samples {
        return None;
    }
    let count = ((values.len() as f64 * fraction).ceil() as usize).max(1);
    let mut sorted = values.to_vec();
    sorted.sort_by(|a, b| b.partial_cmp(a).unwrap_or(std::cmp::Ordering::Equal));
    Some(sorted.iter().take(count).sum::<f64>() / count as f64)
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
        if let Some(first) = errors.first_mut() {
            *first = serde_json::json!(error);
        } else {
            errors.push(serde_json::json!(error));
        }
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
    fn missing_selected_discrete_gpu_ages_out_without_borrowing_integrated_values() {
        let now = Instant::now();
        let integrated = overlay::GpuSensorData { name: "iGPU".into(), identifier: "/igpu/0".into(),
            usage: Some(0.0), vram_total: Some(0.5), ..Default::default() };
        let discrete = overlay::GpuSensorData { name: "dGPU".into(), identifier: "/dgpu/0".into(),
            usage: Some(65.0), vram_total: Some(8.0), ..Default::default() };
        let mut selection = overlay::GpuSelection::default();
        let mut cache = None;
        let mut first = overlay::OverlayData { gpu_sensors: vec![integrated.clone(), discrete], ..Default::default() };
        selection.apply(&mut first);
        aggregate_overlay_sample(&mut cache, first, Some(now), now);
        for (millis, expected) in [(500, Some(65.0)), (1100, None)] {
            let time = now + Duration::from_millis(millis);
            let mut missing = overlay::OverlayData { gpu_sensors: vec![integrated.clone()], ..Default::default() };
            selection.apply(&mut missing);
            let result = aggregate_overlay_sample(&mut cache, missing, Some(time), time);
            assert_eq!(result.gpu_usage, expected);
            assert_eq!(result.gpu_sensors[result.active_gpu_index].identifier, "/dgpu/0");
        }
    }

    #[test]
    #[ignore = "uses an isolated fake provider and process-wide sampling cache; run alone"]
    fn fast_overlay_refresh_reuses_hardware_but_refreshes_frame_values() {
        let helper = std::env::var("FRAMESCOPE_PROVIDER_TEST_HELPER").expect("isolated fake provider");
        let state = ServiceState { lhm_helper_path: Some(helper), ..Default::default() };
        let (first, observed_at) = read_snapshot_with_observation(&state).unwrap();
        let mut cache = None;
        for tick in 0..9 {
            std::thread::sleep(Duration::from_millis(100));
            let (payload, time) = read_snapshot_with_observation(&state).unwrap();
            assert_eq!(payload["readCount"], first["readCount"], "10Hz overlay requests must not trigger 10Hz physical hardware reads");
            assert_eq!(time, observed_at, "cache reuse cannot renew hardware observation age");
            let data = overlay::build_overlay_data(&payload, (Some(100.0 + tick as f64), None, None), None, 0);
            let result = aggregate_overlay_sample(&mut cache, data, Some(time), Instant::now());
            assert_eq!(result.fps, Some(100.0 + tick as f64));
            assert_eq!(result.cpu_temp, Some(60.0));
        }
        std::thread::sleep(Duration::from_millis(150));
        let (next, next_time) = read_snapshot_with_observation(&state).unwrap();
        assert_eq!(next["readCount"].as_u64(), first["readCount"].as_u64().map(|n| n + 1));
        assert!(next_time > observed_at);
        resident().provider.lock().unwrap().kill().unwrap();
    }

    #[test]
    #[ignore = "isolated shutdown gate test mutates the service singleton; run alone"]
    fn queued_provider_request_cannot_restart_after_shutdown() {
        let path = std::env::var("FRAMESCOPE_PROVIDER_TEST_HELPER").expect("isolated fake provider path");
        let res = resident();
        let mut provider = res.provider.lock().unwrap();
        provider.kill().unwrap();
        SHUTTING_DOWN.store(false, Ordering::SeqCst);
        let barrier = std::sync::Arc::new(std::sync::Barrier::new(2));
        let queued = barrier.clone();
        let helper = path.clone();
        let reader = std::thread::spawn(move || {
            let state = ServiceState { lhm_helper_path: Some(helper), ..Default::default() };
            queued.wait();
            read_snapshot_with_observation(&state)
        });
        barrier.wait();
        // The reader cannot acquire the provider mutex until shutdown has been published.
        SHUTTING_DOWN.store(true, Ordering::SeqCst);
        drop(provider);
        let result = reader.join().unwrap();
        let state = ServiceState { lhm_helper_path: Some(path), ..Default::default() };
        let static_result = static_hardware_response(&state);
        let remained_stopped = res.provider.lock().unwrap().proc_is_stopped();
        SHUTTING_DOWN.store(false, Ordering::SeqCst);
        assert!(result.unwrap_err().contains("service_shutting_down"));
        assert!(static_result["hardware"].is_null());
        assert!(remained_stopped, "previously accepted calls cannot spawn a new provider after shutdown");
    }

    #[test]
    fn missing_live_cpu_usage_clears_cached_static_load() {
        for overlay in [serde_json::json!({"cpu_usage": null}), serde_json::json!({})] {
            let mut hardware = serde_json::json!({"cpu": {"load_percentage": 44.0}});
            merge_live_into_static(&mut hardware, &overlay);
            assert!(hardware["cpu"]["load_percentage"].is_null());
        }
    }

#[test] fn ttl_review_static_projection_respects_gpu_identifiers() {
 let mut hardware = serde_json::json!({"gpu":[{"name":"Same GPU", "identifier":"gpu-A"},{"name":"Same GPU","identifier":"gpu-B"}]});
 let overlay = serde_json::json!({"gpu_sensors":[{"name":"Same GPU", "identifier":"gpu-A","temperature":61.0,"usage":30.0},{"name":"Same GPU", "identifier":"gpu-B","temperature":82.0,"usage":70.0}]});
 merge_live_into_static(&mut hardware, &overlay);
 assert_eq!(hardware["gpu"][1]["temperature"], serde_json::json!(82.0));
}
#[test] fn ttl_review_unidentified_gpu_does_not_borrow_identified_same_name_device() {
 let payload = serde_json::json!({"sensors":[{"hardware":"Same GPU", "identifier":"gpu-A","hardwareType":"Gpu", "name":"GPU Core", "sensorType":"Temperature","value":82.0},{"hardware":"Same GPU", "identifier":"","hardwareType":"Gpu", "name":"GPU Core", "sensorType":"Load","value":30.0}]});
 let data = overlay::build_overlay_data(&payload, (None,None,None), None, 1);
 assert_eq!(data.gpu_sensors.len(),2);
 assert_eq!(data.gpu_temp,None);
}
#[test] fn ttl_review_first_overlay_from_fresh_shared_provider_cache_remains_available() {
 // A prior get_sensors/get_snapshot request observed this payload 250 ms earlier.
 let now = Instant::now();
 let payload = serde_json::json!({"dynamic":{"cpuTemperature":{"value":61.0}}});
 let data = overlay::build_overlay_data(&payload,(None,None,None),None,0);
 let observed_at = now - Duration::from_millis(250);
 let mut cache = None;
 let result = aggregate_overlay_sample(&mut cache,data.clone(),Some(observed_at), now);
 assert_eq!(result.cpu_temp,Some(61.0));
 let expired = aggregate_overlay_sample(&mut cache,data,Some(observed_at), now + Duration::from_millis(751));
 assert_eq!(expired.cpu_temp,None);
}

#[test] fn ttl_review_wmi_same_name_without_correlated_identity_is_unavailable() {
 let mut hardware = serde_json::json!({"gpu":[{"name":"Same GPU","device_id":"VideoController1","pnp_device_id":"PCI-A"},{"name":"Same GPU","device_id":"VideoController2","pnp_device_id":"PCI-B"}]});
 let overlay = serde_json::json!({"gpu_sensors":[{"name":"Same GPU","identifier":"/gpu-nvidia/0","temperature":61.0},{"name":"Same GPU","identifier":"/gpu-nvidia/1","temperature":82.0}]});
 merge_live_into_static(&mut hardware, &overlay);
 assert!(hardware["gpu"][0]["temperature"].is_null());
 assert!(hardware["gpu"][1]["temperature"].is_null());
}

    #[test]
    fn ttl_review_unique_wmi_name_maps_and_unknown_identity_does_not_guess_index() {
        let overlay = serde_json::json!({"gpu_sensors":[
            {"name":"Unique GPU", "identifier":"gpu-A", "temperature":61.0,"usage":30.0},
            {"name":"Other GPU", "identifier":"gpu-B", "temperature":82.0,"usage":70.0}
        ]});
        let mut hardware = serde_json::json!({"gpu":[
            {"name":"Unique GPU", "device_id":"VideoController1","pnp_device_id":"PCI-A"},
            {"name":"Other GPU", "identifier":"unmatched", "temperature":99.0}
        ]});
        merge_live_into_static(&mut hardware, &overlay);
        assert_eq!(hardware["gpu"][0]["temperature"], 61.0);
        assert!(hardware["gpu"][1]["temperature"].is_null());
        assert!(hardware["gpu"][1]["usage"].is_null());
    }

    #[test]
    fn ttl_provider_payload_aggregation_expires_without_changing_live_fps_or_ping() {
        let now = Instant::now();
        let mut cache = None;
        let payload = serde_json::json!({"dynamic":{"cpuTemperature":{"value":60.0}}, "sensors":[
            {"hardware":"Same model","identifier":"gpu-A","hardwareType":"Gpu","name":"GPU Core","sensorType":"Temperature","value":70.0},
            {"hardware":"Same model","identifier":"gpu-B","hardwareType":"Gpu","name":"GPU Core","sensorType":"Temperature","value":50.0}
        ]});
        let first = overlay::build_overlay_data(&payload, (Some(144.0), Some(100.0), Some(80.0)), Some(12), 0);
        let first = aggregate_overlay_sample(&mut cache, first, Some(now), now);
        assert_eq!(first.cpu_temp, Some(60.0));
        assert_eq!(first.gpu_temp, Some(70.0));
        let repeated = overlay::build_overlay_data(&payload, (Some(120.0), Some(90.0), Some(70.0)), Some(13), 1);
        let expired = aggregate_overlay_sample(&mut cache, repeated, Some(now), now + Duration::from_millis(1001));
        assert_eq!(expired.cpu_temp, None);
        assert_eq!(expired.gpu_temp, None);
        assert!(expired.gpu_sensors.iter().all(|gpu| gpu.temperature.is_none()));
        assert_eq!((expired.fps, expired.fps_1low, expired.fps_01low), (Some(120.0), Some(90.0), Some(70.0)));
        assert_eq!(expired.game_ping, Some(13));
    }

    #[test]
    fn ttl_failure_expires_each_field_from_its_real_observation() {
        let now = Instant::now();
        let mut cache = None;
        aggregate_overlay_sample(&mut cache, overlay::OverlayData {
            cpu_temp: Some(60.0), cpu_usage: Some(40.0), cpu_core_clocks: vec![Some(4200.0)],
            ..Default::default()
        }, Some(now), now);
        aggregate_overlay_sample(&mut cache, overlay::OverlayData {
            cpu_usage: Some(70.0), ..Default::default()
        }, Some(now + Duration::from_millis(900)), now + Duration::from_millis(900));
        let failure = aggregate_overlay_failure(&mut cache, 0, now + Duration::from_millis(1001));
        assert_eq!(failure.cpu_temp, None);
        assert_eq!(failure.cpu_core_clocks, vec![None]);
        assert_eq!(failure.cpu_usage, Some(70.0));
        let expired = aggregate_overlay_failure(&mut cache, 0, now + Duration::from_millis(1901));
        assert_eq!(expired.cpu_usage, None);
    }

    #[test]
    fn ttl_single_field_recovery_does_not_restore_other_expired_fields() {
        let now = Instant::now();
        let mut cache = None;
        aggregate_overlay_sample(&mut cache, overlay::OverlayData {
            cpu_temp: Some(60.0), cpu_usage: Some(40.0), ..Default::default()
        }, Some(now), now);
        let recovered = aggregate_overlay_sample(&mut cache, overlay::OverlayData {
            cpu_temp: Some(65.0), ..Default::default()
        }, Some(now + Duration::from_millis(1100)), now + Duration::from_millis(1100));
        assert_eq!(recovered.cpu_temp, Some(65.0));
        assert_eq!(recovered.cpu_usage, None);
    }

    #[test]
    fn ttl_gpu_switch_uses_selected_identity_and_never_previous_gpu_fields() {
        let now = Instant::now();
        let mut cache = None;
        let topology = || vec![
            overlay::GpuSensorData { identifier: "gpu-A".into(), name: "Identical name".into(), ..Default::default() },
            overlay::GpuSensorData { identifier: "gpu-B".into(), name: "Identical name".into(), ..Default::default() },
        ];
        let mut initial = overlay::OverlayData { gpu_sensors: topology(), ..Default::default() };
        initial.gpu_sensors[0].temperature = Some(70.0);
        initial.gpu_sensors[0].power = Some(200.0);
        initial.gpu_sensors[1].temperature = Some(50.0);
        aggregate_overlay_sample(&mut cache, initial, Some(now), now);
        let switched = aggregate_overlay_sample(&mut cache, overlay::OverlayData {
            gpu_sensors: topology(), active_gpu_index: 1, ..Default::default()
        }, Some(now + Duration::from_millis(500)), now + Duration::from_millis(500));
        assert_eq!(switched.gpu_temp, Some(50.0));
        assert_eq!(switched.gpu_power, None);
        assert_eq!(switched.gpu_sensors[0].temperature, Some(70.0));
        let failed = aggregate_overlay_failure(&mut cache, 1, now + Duration::from_millis(1001));
        assert_eq!(failed.gpu_temp, None);
        assert_eq!(failed.gpu_sensors[0].temperature, None);
        assert_eq!(failed.gpu_sensors[1].temperature, None);
    }

    #[test]
    fn ttl_missing_fields_do_not_renew_from_inheritance() {
        let now = Instant::now();
        let mut cache = None;
        let sample = overlay::OverlayData { cpu_temp: Some(60.0), cpu_usage: Some(40.0), ..Default::default() };
        aggregate_overlay_sample(&mut cache, sample, Some(now), now);
        let inherited = aggregate_overlay_sample(&mut cache, overlay::OverlayData::default(), Some(now + Duration::from_millis(900)), now + Duration::from_millis(900));
        assert_eq!(inherited.cpu_temp, Some(60.0));
        let expired = aggregate_overlay_sample(&mut cache, overlay::OverlayData::default(), Some(now + Duration::from_millis(1001)), now + Duration::from_millis(1001));
        assert_eq!(expired.cpu_temp, None);
    }

    #[test]
    fn ttl_repeated_throttled_payload_is_not_a_new_observation() {
        let now = Instant::now();
        let mut cache = None;
        let sample = overlay::OverlayData { cpu_temp: Some(60.0), ..Default::default() };
        aggregate_overlay_sample(&mut cache, sample.clone(), Some(now), now);
        let expired = aggregate_overlay_sample(&mut cache, sample, Some(now), now + Duration::from_millis(1001));
        assert_eq!(expired.cpu_temp, None);
    }

    #[test]
    fn inprocess_snapshot_replaces_existing_and_excludes_sensor_arrays() {
        let dir = std::env::temp_dir().join(format!("framescope-snapshot-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("42.json");
        for timestamp in [1000, 1250] {
            let value = serde_json::json!({"target_pid":42,"timestamp_ms":timestamp,
                "fps":144,"fps_1low":59.8,"fps_01low":29.3,"sensors":"x".repeat(20000)});
            publish_inprocess_snapshot(&path, &value).unwrap();
            let bytes = std::fs::read(&path).unwrap();
            assert!(bytes.len() < 4096);
            let decoded: serde_json::Value = serde_json::from_slice(&bytes).unwrap();
            assert_eq!(decoded["timestamp_ms"], timestamp);
            assert_eq!(decoded["fps_1low"], 59.8);
            assert!(decoded.get("sensors").is_none());
        }
        std::fs::remove_file(path).unwrap();
        std::fs::remove_dir(dir).unwrap();
    }

    #[test]
    fn pipe_audit_fps_retries_after_asynchronous_start_failure() {
        let gate = FpsStartGate {
            last_attempt: AtomicU64::new(0),
        };
        let active = AtomicBool::new(false);
        let attempts = AtomicUsize::new(0);
        let start = || {
            attempts.fetch_add(1, Ordering::SeqCst);
            active.store(true, Ordering::SeqCst);
        };
        gate.ensure(10_000, || active.load(Ordering::SeqCst), start);
        assert_eq!(attempts.load(Ordering::SeqCst), 1);
        // start_fps_monitor sets active before the ETW worker can fail.
        std::thread::scope(|scope| {
            scope.spawn(|| active.store(false, Ordering::SeqCst)).join().unwrap();
        });
        gate.ensure(14_999, || active.load(Ordering::SeqCst), start);
        assert_eq!(attempts.load(Ordering::SeqCst), 1, "retry throttle must hold");
        gate.ensure(15_000, || active.load(Ordering::SeqCst), start);
        assert_eq!(attempts.load(Ordering::SeqCst), 2, "failed worker latched FPS startup forever");
        gate.ensure(30_000, || active.load(Ordering::SeqCst), start);
        assert_eq!(attempts.load(Ordering::SeqCst), 2, "healthy worker must not restart");
    }

    #[test]
    fn pipe_audit_concurrent_fps_failures_only_attempt_once_per_retry_window() {
        let gate = FpsStartGate { last_attempt: AtomicU64::new(0) };
        let attempts = AtomicUsize::new(0);
        for now in [10_000, 15_000] {
            let barrier = std::sync::Barrier::new(16);
            std::thread::scope(|scope| {
                for _ in 0..16 {
                    let barrier = &barrier;
                    let gate = &gate;
                    let attempts = &attempts;
                    scope.spawn(move || {
                        barrier.wait();
                        gate.ensure(now, || false, || {
                            attempts.fetch_add(1, Ordering::SeqCst);
                        });
                    });
                }
            });
        }
        assert_eq!(attempts.load(Ordering::SeqCst), 2);
    }

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
        assert_eq!(response["result"]["fps_1low"], serde_json::Value::Null);
        assert_eq!(response["result"]["fps_01low"], serde_json::Value::Null);
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
