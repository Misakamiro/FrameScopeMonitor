use serde::{Deserialize, Serialize};
use std::fs;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};

const MIN_INTERVAL_MS: u64 = 250;
const MAX_INTERVAL_MS: u64 = 5000;

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
            "capabilities": ["get_static_info", "get_snapshot", "get_sensors", "set_sampling_interval", "get_overlay_metrics", "start", "stop"]
        }),
        "get_static_info" => capture_from_lhm(state, "get_static_info"),
        "get_snapshot" => {
            state.sequence = state.sequence.saturating_add(1);
            let mut value = capture_from_lhm(state, "get_snapshot");
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
            let snapshot = capture_from_lhm(state, "get_sensors");
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
        "set_sampling_interval" => {
            let interval = request
                .params
                .get("samplingIntervalMs")
                .and_then(|v| v.as_u64());
            match interval {
                Some(value) if (MIN_INTERVAL_MS..=MAX_INTERVAL_MS).contains(&value) => {
                    state.sampling_interval_ms = value;
                    serde_json::json!({ "status": "accepted", "samplingIntervalMs": value })
                }
                _ => return error_response(id, "invalid_sampling_interval"),
            }
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

fn find_latest_presentmon(data_root: &str) -> Option<PathBuf> {
    if data_root.trim().is_empty() {
        return None;
    }
    let root = Path::new(data_root);
    if !root.is_dir() {
        return None;
    }
    let mut latest: Option<(PathBuf, std::time::SystemTime)> = None;
    let mut pending = vec![root.to_path_buf()];
    while let Some(dir) = pending.pop() {
        let Ok(entries) = fs::read_dir(&dir) else {
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

fn read_frame_times(path: &Path) -> Result<Vec<f64>, String> {
    let text = fs::read_to_string(path).map_err(|e| e.to_string())?;
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

fn capture_from_lhm(state: &ServiceState, method: &str) -> serde_json::Value {
    let Some(path) = state
        .lhm_helper_path
        .as_deref()
        .filter(|p| !p.trim().is_empty())
    else {
        return unavailable_payload(method);
    };
    let mut child = match Command::new(path)
        .arg("--json")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
    {
        Ok(child) => child,
        Err(error) => {
            return unavailable_with_error(
                method,
                format!("LibreHardwareMonitor helper start failed: {error}"),
            )
        }
    };
    let request = serde_json::json!({"method": method});
    if let Some(mut stdin) = child.stdin.take() {
        if let Err(error) = writeln!(stdin, "{}", request) {
            let _ = child.kill();
            return unavailable_with_error(
                method,
                format!("LibreHardwareMonitor helper write failed: {error}"),
            );
        }
    }
    let output = match child.wait_with_output() {
        Ok(output) => output,
        Err(error) => {
            return unavailable_with_error(
                method,
                format!("LibreHardwareMonitor helper wait failed: {error}"),
            )
        }
    };
    if !output.status.success() {
        return unavailable_with_error(
            method,
            format!("LibreHardwareMonitor helper exited with {}", output.status),
        );
    }
    let text = String::from_utf8_lossy(&output.stdout);
    let clean = text
        .trim_matches('\0')
        .trim_start_matches('\u{feff}')
        .trim();
    match serde_json::from_str::<serde_json::Value>(clean) {
        Ok(value) => value,
        Err(error) => unavailable_with_error(
            method,
            format!("LibreHardwareMonitor helper returned invalid JSON: {error}"),
        ),
    }
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
        assert!(response["result"]["capabilities"]
            .as_array()
            .unwrap()
            .iter()
            .any(|v| v == "get_snapshot"));
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
}
