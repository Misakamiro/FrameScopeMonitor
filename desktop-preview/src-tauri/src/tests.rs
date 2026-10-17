use crate::{decode_fixture, ensure_allowed, overlay_panel::OverlayHardwareData};
use serde_json::json;

#[test]
fn dragged_positions_survive_stale_settings_save_and_restart() {
    let directory = std::env::temp_dir().join(format!("framescope-position-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&directory).unwrap();
    let path = directory.join("preview-native-settings.json");
    for style in ["default", "dynamic_island", "vertical_panel"] {
        let mut dragged = crate::overlay_panel::OverlaySettings::default();
        dragged.position_x = Some(-1500); dragged.position_y = Some(270);
        dragged.vertical_position_x = Some(620); dragged.vertical_position_y = Some(-130);
        crate::write_native_settings(&path, "Shift+F10", serde_json::to_value(&dragged).unwrap()).unwrap();
        let mut stale = dragged.clone();
        stale.position_x = Some(40); stale.position_y = Some(80);
        stale.vertical_position_x = Some(40); stale.vertical_position_y = Some(80);
        stale.style = style.to_string(); stale.opacity = 99;
        let saved = crate::settings_with_native_positions(stale, &dragged);
        crate::write_native_settings(&path, "Shift+F10", serde_json::to_value(saved).unwrap()).unwrap();
        let file: serde_json::Value = serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
        let restored: crate::overlay_panel::OverlaySettings = serde_json::from_value(file["overlay-settings"].clone()).unwrap();
        assert_eq!((restored.position_x, restored.position_y), (Some(-1500), Some(270)), "{style}");
        assert_eq!((restored.vertical_position_x, restored.vertical_position_y), (Some(620), Some(-130)), "{style}");
        assert_eq!(restored.opacity, 99);
        assert_eq!(restored.style, style);
        assert_eq!(file["overlay-hotkey"], "Shift+F10");
    }
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn hotkey_same_key_failure_does_not_unregister_existing_key() {
    let events = std::cell::RefCell::new(Vec::new());
    let result = crate::apply_hotkey_change("Ctrl+F24", "Ctrl+F24", true,
        |key| { events.borrow_mut().push(format!("register:{key}")); Ok(()) },
        |key| { events.borrow_mut().push(format!("unregister:{key}")); Ok(()) },
        |_| Err("write failed".to_string()));
    assert!(result.is_ok(), "unchanged shortcut must not write or re-register");
    assert!(events.borrow().is_empty());
}

#[test]
fn hotkey_startup_conflict_keeps_host_initialization_available_and_allows_repair() {
    let directory = std::env::temp_dir().join(format!("framescope-hotkey-startup-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&directory).unwrap();
    let path = directory.join("preview-native-settings.json");
    crate::write_native_settings(&path, "Ctrl+F24", json!({})).unwrap();
    let original = std::fs::read(&path).unwrap();
    let mut state = crate::initialize_native_hotkey(&path, |_| Err("occupied by another app".into()))
        .expect("registration conflict must not abort host initialization");
    assert_eq!(state.shortcut, "Ctrl+F24");
    assert!(state.registration_error.as_deref().unwrap().contains("occupied by another app"));
    assert_eq!(std::fs::read(&path).unwrap(), original);
    let events = std::cell::RefCell::new(Vec::new());
    crate::change_native_hotkey(&mut state, "Alt+F24",
        |key| { events.borrow_mut().push(format!("register:{key}")); Ok(()) },
        |key| { events.borrow_mut().push(format!("unregister:{key}")); Ok(()) },
        |candidate| crate::write_native_settings(&path, candidate, json!({}))).unwrap();
    assert_eq!(*events.borrow(), vec!["register:Alt+F24".to_string()], "unregistered saved key must not be unregistered again");
    assert_eq!(crate::restore_native_hotkey(&path, |_| Ok(())).unwrap(), "Alt+F24");
    assert!(state.registration_error.is_none());
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn hotkey_rollback_registration_failure_is_explicit() {
    let mut state = crate::NativeHotkeyState { shortcut: "Ctrl+F24".into(), registration_error: None };
    let error = crate::change_native_hotkey(&mut state, "Alt+F24",
        |key| if key == "Ctrl+F24" { Err("old key restore conflict".into()) } else { Ok(()) },
        |_| Ok(()), |_| Err("disk write denied".into())).unwrap_err();
    assert!(error.contains("disk write denied"));
    assert!(error.contains("old key restore conflict"));
    assert!(error.contains("shortcut rollback failed"));
    assert_eq!(state.shortcut, "Ctrl+F24");
    assert_eq!(state.registration_error.as_deref(), Some(error.as_str()), "failed restore cannot claim old registration active");
}

#[test]
fn hotkey_success_persists_candidate_not_previous_managed_value() {
    let managed = std::cell::RefCell::new("Ctrl+F24".to_string());
    let persisted = std::cell::RefCell::new(String::new());
    let saved = crate::apply_hotkey_change(&managed.borrow(), "Alt+F24", true,
        |_| Ok(()), |_| Ok(()),
        |candidate| { *persisted.borrow_mut() = candidate.to_string(); Ok(()) }).unwrap();
    *managed.borrow_mut() = saved;
    assert_eq!(*persisted.borrow(), "Alt+F24");
}

#[test]
fn hotkey_production_writer_persists_and_restores_candidate_and_preserves_file_on_failure() {
    let directory = std::env::temp_dir().join(format!("framescope-hotkey-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&directory).unwrap();
    let path = directory.join("preview-native-settings.json");
    crate::write_native_settings(&path, "Ctrl+F24", json!({"style":"fixture"})).unwrap();
    let original = std::fs::read(&path).unwrap();
    let active = std::cell::RefCell::new(std::collections::HashSet::from(["Ctrl+F24".to_string()]));
    let managed = std::sync::Mutex::new("Ctrl+F24".to_string());
    {
        let mut current = managed.lock().unwrap();
        let result = crate::apply_hotkey_change(&current, "Alt+F24", true,
            |key| { active.borrow_mut().insert(key.into()); Ok(()) },
            |key| { active.borrow_mut().remove(key); Ok(()) },
            |candidate| crate::write_native_settings(&path, candidate, json!({"style":"updated"})));
        *current = result.unwrap();
    }
    let mut restored = String::new();
    assert_eq!(crate::restore_native_hotkey(&path, |key| { restored = key.into(); Ok(()) }).unwrap(), "Alt+F24");
    assert_eq!(restored, "Alt+F24");
    assert_eq!(serde_json::from_slice::<serde_json::Value>(&std::fs::read(&path).unwrap()).unwrap()["overlay-settings"]["style"], "updated");
    assert_ne!(original, std::fs::read(&path).unwrap());
    let committed = std::fs::read(&path).unwrap();
    // An open destination without FILE_SHARE_DELETE makes the real atomic rename fail on Windows.
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        let _occupied = std::fs::OpenOptions::new().read(true).share_mode(1).open(&path).unwrap();
        let current = managed.lock().unwrap();
        assert!(crate::apply_hotkey_change(&current, "Ctrl+Alt+F24", true,
            |key| { active.borrow_mut().insert(key.into()); Ok(()) },
            |key| { active.borrow_mut().remove(key); Ok(()) },
            |candidate| crate::write_native_settings(&path, candidate, json!({}))).is_err());
        assert_eq!(*current, "Alt+F24");
        assert_eq!(*active.borrow(), std::collections::HashSet::from(["Alt+F24".to_string()]));
        assert_eq!(std::fs::read(&path).unwrap(), committed);
    }
    assert!(crate::restore_native_hotkey(&path, |_| Err("system conflict".into())).unwrap_err().contains("system conflict"));
    assert_eq!(std::fs::read(&path).unwrap(), committed);
    assert_eq!(std::fs::read_dir(&directory).unwrap().count(), 1, "failed writes remove only their temporary file");
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn hotkey_and_other_native_settings_writes_share_transaction_lock() {
    let directory = std::env::temp_dir().join(format!("framescope-hotkey-concurrent-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&directory).unwrap();
    let path = directory.join("preview-native-settings.json");
    let managed = std::sync::Arc::new(std::sync::Mutex::new("Ctrl+F24".to_string()));
    let (started_tx, started_rx) = std::sync::mpsc::channel();
    let (continue_tx, continue_rx) = std::sync::mpsc::channel();
    let first_state = managed.clone(); let first_path = path.clone();
    let first = std::thread::spawn(move || {
        let mut current = first_state.lock().unwrap();
        *current = crate::apply_hotkey_change(&current, "Alt+F24", true, |_| Ok(()), |_| Ok(()),
            |candidate| { started_tx.send(()).unwrap(); continue_rx.recv().unwrap();
                crate::write_native_settings(&first_path, candidate, json!({"style":"first"})) }).unwrap();
    });
    started_rx.recv_timeout(std::time::Duration::from_secs(5)).unwrap();
    let second_state = managed.clone(); let second_path = path.clone();
    let second = std::thread::spawn(move || {
        let current = second_state.lock().unwrap();
        crate::write_native_settings(&second_path, &current, json!({"style":"second"})).unwrap();
    });
    continue_tx.send(()).unwrap(); first.join().unwrap(); second.join().unwrap();
    assert_eq!(crate::restore_native_hotkey(&path, |_| Ok(())).unwrap(), "Alt+F24");
    let data: serde_json::Value = serde_json::from_slice(&std::fs::read(&path).unwrap()).unwrap();
    assert_eq!(data["overlay-settings"]["style"], "second");
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn hotkey_change_rolls_back_on_registration_conflict_unregistration_or_save_failure() {
    fn run(failure: &str) -> (Result<String, String>, Vec<String>) {
        let active = std::sync::Arc::new(std::sync::Mutex::new(vec!["Shift+F10".to_string()]));
        let events = std::sync::Arc::new(std::sync::Mutex::new(Vec::<String>::new()));
        let active_register = active.clone(); let active_unregister = active.clone();
        let events_register = events.clone(); let events_unregister = events.clone(); let events_save = events.clone();
        let result = crate::apply_hotkey_change(
            "Shift+F10", "Ctrl+F10", true,
            move |key| { events_register.lock().unwrap().push(format!("register:{key}")); if failure == "register" { Err("conflict".into()) } else { active_register.lock().unwrap().push(key.into()); Ok(()) } },
            move |key| { events_unregister.lock().unwrap().push(format!("unregister:{key}")); if failure == "unregister" && key == "Shift+F10" { Err("busy".into()) } else { active_unregister.lock().unwrap().retain(|value| value != key); Ok(()) } },
            move |_| { events_save.lock().unwrap().push("save".into()); if failure == "save" { Err("write failed".into()) } else { Ok(()) } },
        );
        let snapshot = events.lock().unwrap().clone();
        (result, snapshot)
    }
    assert!(run("register").0.is_err());
    let (unregister_result, unregister_events) = run("unregister");
    assert!(unregister_result.is_err());
    assert!(!unregister_events.iter().any(|event| event == "register:Shift+F10"), "old registration remains on unregistration failure");
    let (save_result, save_events) = run("save");
    assert!(save_result.is_err());
    assert!(save_events.iter().any(|event| event == "unregister:Ctrl+F10"));
    assert_eq!(run("success").0.unwrap(), "Ctrl+F10");
}

#[cfg(windows)]
#[test]
#[ignore = "uses exclusive Windows global shortcut registrations for an isolated F23/F24 fixture"]
fn hotkey_windows_registration_conflict_and_restart_restore() {
    #[link(name = "user32")]
    extern "system" {
        fn RegisterHotKey(window: *mut std::ffi::c_void, id: i32, modifiers: u32, key: u32) -> i32;
        fn UnregisterHotKey(window: *mut std::ffi::c_void, id: i32) -> i32;
    }
    let directory = std::env::temp_dir().join(format!("framescope-win-hotkey-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&directory).unwrap();
    let path = directory.join("preview-native-settings.json");
    let modifiers = 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x4000; // Alt, Control, Shift, Win, no repeat.
    let old = "Ctrl+Alt+Shift+Super+F24"; let new = "Ctrl+Alt+Shift+Super+F23";
    let id = |key: &str| if key == old { 0x4317 } else { 0x4318 };
    let vk = |key: &str| if key == old { 0x87 } else { 0x86 };
    let register = |key: &str| if unsafe { RegisterHotKey(std::ptr::null_mut(), id(key), modifiers, vk(key)) } == 0
        { Err(std::io::Error::last_os_error().to_string()) } else { Ok(()) };
    let unregister = |key: &str| if unsafe { UnregisterHotKey(std::ptr::null_mut(), id(key)) } == 0
        { Err(std::io::Error::last_os_error().to_string()) } else { Ok(()) };
    struct Cleanup;
    impl Drop for Cleanup { fn drop(&mut self) { unsafe { UnregisterHotKey(std::ptr::null_mut(), 0x4317); UnregisterHotKey(std::ptr::null_mut(), 0x4318); } } }
    let _cleanup = Cleanup;
    crate::write_native_settings(&path, old, json!({"fixture":true})).unwrap();
    assert_eq!(crate::restore_native_hotkey(&path, register).unwrap(), old);
    let original = std::fs::read(&path).unwrap();
    {
        use std::os::windows::fs::OpenOptionsExt;
        let _occupied_file = std::fs::OpenOptions::new().read(true).share_mode(1).open(&path).unwrap();
        assert!(crate::apply_hotkey_change(old, new, true, register, unregister,
            |candidate| crate::write_native_settings(&path, candidate, json!({"fixture":true}))).is_err());
        let old_still_registered = std::thread::spawn(move || {
            let accepted = unsafe { RegisterHotKey(std::ptr::null_mut(), 0x4517, modifiers, 0x87) } != 0;
            if accepted { unsafe { UnregisterHotKey(std::ptr::null_mut(), 0x4517); } }
            !accepted
        }).join().unwrap();
        assert!(old_still_registered, "real old registration survives production write failure");
        assert_eq!(std::fs::read(&path).unwrap(), original);
    }
    let (ready_tx, ready_rx) = std::sync::mpsc::channel(); let (release_tx, release_rx) = std::sync::mpsc::channel();
    let occupied = std::thread::spawn(move || {
        let accepted = unsafe { RegisterHotKey(std::ptr::null_mut(), 0x4418, modifiers, 0x86) } != 0;
        ready_tx.send(accepted).unwrap();
        release_rx.recv_timeout(std::time::Duration::from_secs(10)).unwrap();
        if accepted { unsafe { UnregisterHotKey(std::ptr::null_mut(), 0x4418); } }
    });
    let accepted = ready_rx.recv_timeout(std::time::Duration::from_secs(5)).unwrap();
    let conflict = crate::apply_hotkey_change(old, new, true, register, unregister,
        |candidate| crate::write_native_settings(&path, candidate, json!({"fixture":true})));
    release_tx.send(()).unwrap(); occupied.join().unwrap();
    assert!(accepted, "fixture combination was already occupied; no existing registration is changed");
    assert!(conflict.is_err());
    assert_eq!(std::fs::read(&path).unwrap(), original);
    assert!(crate::apply_hotkey_change(old, old, true, register, unregister, |_| Err("must not write".into())).is_ok());
    assert_eq!(crate::apply_hotkey_change(old, new, true, register, unregister,
        |candidate| crate::write_native_settings(&path, candidate, json!({"fixture":true}))).unwrap(), new);
    unregister(new).unwrap();
    assert_eq!(crate::restore_native_hotkey(&path, register).unwrap(), new);
    unregister(new).unwrap();
    std::fs::remove_dir_all(directory).unwrap();
}

#[test]
fn empty_sensor_errors_return_unavailable_without_panicking() {
    for response in [json!({"errors": []}), json!({}), json!({"errors": null}), json!({"errors": [42]})] {
        assert_eq!(crate::sensor_response_error(&response, "unavailable"), "unavailable");
    }
    assert_eq!(crate::sensor_response_error(&json!({"errors": ["provider failed"]}), "unavailable"), "provider failed");
}

#[test]
fn rejects_unknown_and_system_commands() {
    for command in ["", "run_tool", "read_lhm_sensors", "start_fps_monitor", "../settings.json"] {
        assert!(ensure_allowed(command).is_err(), "{command}");
    }
    assert!(ensure_allowed("inject_inprocess_osd").is_err());
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
fn native_overlay_accepts_gpu_identity_metadata_without_losing_metrics() {
    let snapshot = json!({
        "target_pid": 42, "timestamp_ms": 1000,
        "fps": 144.4, "fps_1low": 99.75, "fps_01low": 81.25,
        "cpu_usage": 27.4, "cpu_temp": 52.5, "gpu_usage": 55.4,
        "gpu_sensors": [
            {"name":"GPU 0", "identifier":"/gpu-nvidia/0", "hardware_type":"GpuNvidia", "usage":55.4},
            {"name":"GPU 1", "identifier":"/gpu-intel/0", "hardware_type":"GpuIntel", "usage":12.6}
        ], "active_gpu_index": 1
    });
    let mapped = serde_json::to_value(decode_fixture(
        crate::hardware_service::sanitize_for_gdi(snapshot.clone())).unwrap()).unwrap();
    assert_eq!(mapped["fps"], 144);
    assert_eq!(mapped["fps_1low"], 99.75);
    assert_eq!(mapped["fps_01low"], 81.25);
    assert_eq!(mapped["cpu_usage"], 27);
    assert_eq!(mapped["cpu_temp"], 52.5);
    assert_eq!(mapped["gpu_sensors"][1]["usage"], 13);
    assert_eq!(mapped["active_gpu_index"], 1);
    assert_eq!(snapshot["gpu_sensors"][0]["identifier"], "/gpu-nvidia/0");
    assert_eq!(snapshot["gpu_sensors"][1]["usage"], 12.6);
}

#[test]
fn native_overlay_accepts_fractional_signed_network_time_offset() {
    for (offset, expected) in [(-123.6, -124), (123.6, 124), (0.0, 0)] {
        let mapped = serde_json::to_value(decode_fixture(
            crate::hardware_service::sanitize_for_gdi(json!({
                "net_time_offset_ms": offset, "cpu_temp": 52.5
            }))).unwrap()).unwrap();
        assert_eq!(mapped["net_time_offset_ms"], expected);
        assert_eq!(mapped["cpu_temp"], 52.5);
    }
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
