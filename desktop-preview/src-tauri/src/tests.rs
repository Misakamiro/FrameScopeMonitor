use crate::{decode_fixture, ensure_allowed, overlay_panel::OverlayHardwareData};
use serde_json::json;

#[test]
fn rejects_unknown_and_system_commands() {
    for command in ["", "run_tool", "read_lhm_sensors", "start_fps_monitor", "../settings.json"] {
        assert!(ensure_allowed(command).is_err(), "{command}");
    }
    assert!(ensure_allowed("start_overlay_panel").is_ok());
    assert!(ensure_allowed("open_sensor_monitor").is_ok());
}

#[test]
fn fixture_round_trips_original_renderer_schema() {
    let fixture = json!({
        "fps": 144, "fps_1low": 99, "cpu_usage": 27, "cpu_temp": 52.5,
        "memory_usage": 42.5, "gpu_vram_used": 4096, "gpu_vram_total": 12288,
        "gpu_sensors": [{"name": "MOCK GPU", "hardware_type": "GpuNvidia",
            "temperature": 61.5, "usage": 55}],
        "active_gpu_index": 0
    });
    let mapped = serde_json::to_value(decode_fixture(fixture).unwrap()).unwrap();
    assert_eq!(mapped["fps"], 144);
    assert_eq!(mapped["cpu_temp"], 52.5);
    assert_eq!(mapped["gpu_sensors"][0]["name"], "MOCK GPU");
    assert_eq!(mapped["gpu_vram_used"], 4096);
    assert!(mapped["game_ping"].is_null());
}

#[test]
fn missing_fixture_never_samples_or_carries_stale_values() {
    let first = decode_fixture(json!({"cpu_usage": 77})).unwrap();
    assert_eq!(serde_json::to_value(first).unwrap()["cpu_usage"], 77);
    let empty = decode_fixture(json!({})).unwrap();
    assert_eq!(serde_json::to_value(empty).unwrap(),
        serde_json::to_value(OverlayHardwareData::default()).unwrap());
}

#[test]
fn invalid_or_non_normalized_fixture_is_rejected() {
    for fixture in [json!({"cpu_usage": -1}), json!({"cpu_usage": "27"}),
        json!({"sensors": []}), json!({"gpu_sensors": [], "active_gpu_index": 4}), json!(null)] {
        assert!(decode_fixture(fixture).is_err());
    }
}

#[test]
fn host_source_does_not_link_production_services() {
    let renderer = include_str!("overlay_panel.rs");
    for forbidden in ["crate::sensor", "crate::hardware", "crate::game_fps",
        "crate::game_ping", "reqwest::", "SetWinEventHook", "read_lhm_sensors"] {
        assert!(!renderer.contains(forbidden), "{forbidden}");
    }
    let config: serde_json::Value = serde_json::from_str(include_str!("../tauri.conf.json")).unwrap();
    assert_eq!(config["identifier"], "com.framescope.ui-preview");
    assert_eq!(config["app"]["windows"][0]["width"], 1230);
}
