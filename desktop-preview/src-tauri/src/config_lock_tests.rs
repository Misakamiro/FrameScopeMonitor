use super::*;

#[test]
#[ignore = "temporary backend environment; run explicitly with one test thread"]
fn startup_window_options_and_partial_report_history() {
    let root = std::env::temp_dir().join(format!("framescope-startup-options-{}-{}", std::process::id(), now_unix()));
    std::fs::create_dir(&root).unwrap();
    std::fs::write(root.join("FrameScopeMonitor.exe"), "fixture; never executed").unwrap();
    struct Restore(Option<std::ffi::OsString>, PathBuf);
    impl Drop for Restore {
        fn drop(&mut self) {
            if let Some(value) = self.0.take() { std::env::set_var("FRAMESCOPE_BACKEND_DIR", value); }
            else { std::env::remove_var("FRAMESCOPE_BACKEND_DIR"); }
            let _ = std::fs::remove_dir_all(&self.1);
        }
    }
    let _restore = Restore(std::env::var_os("FRAMESCOPE_BACKEND_DIR"), root.clone());
    std::env::set_var("FRAMESCOPE_BACKEND_DIR", &root);
    let mut config = default_config();
    config["DataRoot"] = json!(root.join("runs"));
    write_json_atomic(&root.join("framescope-config.json"), &config).unwrap();
    let mut history = String::new();
    for index in 0..6 {
        let run = root.join("runs").join("Game").join(format!("run-{index}"));
        std::fs::create_dir_all(run.join("charts")).unwrap();
        std::fs::write(run.join("charts/framescope-interactive-report.html"), "fixture").unwrap();
        if index < 3 { history.push_str(&format!("{}\n", json!({"RunDir":run,"Game":"Game"}))); }
    }
    std::fs::write(root.join("framescope-history.jsonl"), &history).unwrap();
    let list = reports_list().unwrap();
    assert_eq!(list["count"], 6, "three indexed reports must not hide other disk runs");
    let ids: std::collections::HashSet<_> = list["reports"].as_array().unwrap().iter().map(|r|r["id"].as_str().unwrap()).collect();
    assert_eq!(ids.len(), 6, "history and disk discovery must deduplicate");
    assert_eq!(config_read().unwrap()["config"]["autoStartOverlay"], false);
    assert_eq!(config_read().unwrap()["config"]["startMinimized"], false);
    assert_eq!(startup_preferences().unwrap(), (false, false));
    config_save(&json!({"autoStartOverlay":true,"startMinimized":true})).unwrap();
    let saved = config_read().unwrap();
    assert_eq!(saved["config"]["autoStartOverlay"], true);
    assert_eq!(saved["config"]["startMinimized"], true);
    assert_eq!(startup_preferences().unwrap(), (true, true));
    config_save(&json!({"autoStartOverlay":false})).unwrap();
    assert_eq!(startup_preferences().unwrap(), (false, true));
    config_save(&json!({"startMinimized":false})).unwrap();
    assert_eq!(startup_preferences().unwrap(), (false, false));
    for index in 6..55 {
        std::fs::create_dir_all(root.join("runs").join("Game").join(format!("run-{index}"))).unwrap();
    }
    assert_eq!(reports_list().unwrap()["count"], 50, "merged discovery keeps the existing bounded limit");
}

#[test]
#[ignore = "isolated real watcher lifecycle; FRAMESCOPE_STARTUP_WATCHER_FIXTURE required; run alone"]
fn startup_preference_and_manual_stop_use_one_owned_waiting_watcher() {
    let fixture = PathBuf::from(std::env::var_os("FRAMESCOPE_STARTUP_WATCHER_FIXTURE").expect("compiled isolated watcher"));
    let root = fixture.parent().unwrap().join(format!("startup-fixture-{}-{}", std::process::id(), now_unix()));
    std::fs::create_dir(&root).unwrap();
    std::fs::copy(&fixture, root.join("FrameScopeMonitor.exe")).unwrap();
    for dependency in ["Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll"] {
        std::fs::copy(fixture.parent().unwrap().join(dependency), root.join(dependency)).unwrap();
    }
    struct Restore(Option<std::ffi::OsString>);
    impl Drop for Restore {
        fn drop(&mut self) {
            let _ = shutdown_owned_monitor();
            if let Some(value) = self.0.take() { std::env::set_var("FRAMESCOPE_BACKEND_DIR", value); }
            else { std::env::remove_var("FRAMESCOPE_BACKEND_DIR"); }
        }
    }
    let _restore = Restore(std::env::var_os("FRAMESCOPE_BACKEND_DIR"));
    std::env::set_var("FRAMESCOPE_BACKEND_DIR", &root);
    let mut config = default_config();
    config["DataRoot"] = json!(root.join("runs"));
    config["Targets"] = json!([{"Enabled":true,"Name":"Fixture only","ProcessName":format!("NeverRunningStartupFixture{}.exe",std::process::id())}]);
    config["AutoStartMonitoring"] = json!(false);
    config["EnableVerboseLogs"] = json!(true);
    write_json_atomic(&root.join("framescope-config.json"), &config).unwrap();
    auto_start_monitor().unwrap();
    assert!(OWNED_WATCHER.lock().unwrap().is_none(), "explicit false must not spawn a watcher");
    assert_eq!(config_read().unwrap()["config"]["autoStart"], false);
    // An old config with no preference follows the new default.
    config.as_object_mut().unwrap().remove("AutoStartMonitoring");
    write_json_atomic(&root.join("framescope-config.json"), &config).unwrap();
    auto_start_monitor().unwrap();
    let pid = OWNED_WATCHER.lock().unwrap().as_ref().unwrap().child.id();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    loop {
        let status = monitor_status().unwrap();
        if status["running"] == true && status["phase"] == "waiting" {
            assert_eq!(status["targetNames"], json!([])); break;
        }
        assert!(std::time::Instant::now() < deadline, "real watcher never reached waiting: {status}");
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    auto_start_monitor().unwrap();
    assert_eq!(OWNED_WATCHER.lock().unwrap().as_ref().unwrap().child.id(), pid);
    let mut targets = targets_payload("loaded").unwrap()["targets"].as_array().unwrap().clone();
    targets.push(json!({"name":"Added while waiting", "executable":format!("NeverRunningAddedFixture{}.exe", std::process::id()), "enabled":true}));
    targets_save(&targets).unwrap();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
    loop {
        if std::fs::read_to_string(root.join("framescope-watcher.log")).unwrap_or_default().contains("target-scan name=Added while waiting") { break; }
        assert!(std::time::Instant::now() < deadline, "existing watcher did not hot-load the newly saved target");
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    assert_eq!(OWNED_WATCHER.lock().unwrap().as_ref().unwrap().child.id(), pid, "target edit must not restart the waiting watcher");
    assert_eq!(monitor_stop().unwrap()["status"], "stopping");
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
    loop {
        if monitor_status().unwrap()["phase"] == "stopped" { break; }
        assert!(std::time::Instant::now() < deadline, "real watcher did not stop");
        std::thread::sleep(std::time::Duration::from_millis(100));
    }
    config_save(&json!({"autoStart":false})).unwrap();
    assert_eq!(config_read().unwrap()["config"]["autoStart"], false);
    config_save(&json!({"autoStart":true})).unwrap();
    assert_eq!(monitor_status().unwrap()["running"], false, "changing preference cannot restart the current session");
    assert!(OWNED_WATCHER.lock().unwrap().as_mut().unwrap().child.try_wait().unwrap().is_some());
    eprintln!("PASS: real startup preference, idempotent waiting watcher, manual stop, next-launch-only setting; root={}", root.display());
}

#[test]
#[ignore = "temporary backend environment; run explicitly with one test thread"]
fn target_edit_preserves_existing_advanced_fields() {
    let root = std::env::temp_dir().join(format!("framescope-target-preserve-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    std::fs::write(root.join("FrameScopeMonitor.exe"), "fixture; never executed").unwrap();
    let mut config = default_config();
    let original = json!({"Enabled":true,"Name":"Old","ProcessName":"Game.exe",
        "SampleIntervalMs":1750,"ProcessSamplingMode":"high-precision","ProcessSampleIntervalMs":100,
        "SlowSampleIntervalMs":2500,"OpenReportOnComplete":false,"PrivateTargetSetting":{"value":7}});
    config["Targets"] = json!([original]);
    let path = root.join("framescope-config.json");
    write_json_atomic(&path, &config).unwrap();
    let old_root = std::env::var_os("FRAMESCOPE_BACKEND_DIR");
    std::env::set_var("FRAMESCOPE_BACKEND_DIR", &root);
    let result = targets_save(&[json!({"enabled":false,"name":"Renamed","executable":"game.EXE"}),
        json!({"enabled":true,"name":"New","executable":"New.exe"})]);
    if let Some(value) = old_root { std::env::set_var("FRAMESCOPE_BACKEND_DIR", value); } else { std::env::remove_var("FRAMESCOPE_BACKEND_DIR"); }
    assert!(result.is_ok());
    let saved: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
    let existing = &saved["Targets"][0];
    assert_eq!(existing["Enabled"], false);
    assert_eq!(existing["Name"], "Renamed");
    assert_eq!(existing["ProcessName"], "game.EXE");
    for key in ["SampleIntervalMs", "ProcessSamplingMode", "ProcessSampleIntervalMs", "SlowSampleIntervalMs", "OpenReportOnComplete", "PrivateTargetSetting"] {
        assert_eq!(existing[key], original[key], "target edit overwrote {key}");
    }
    assert_eq!(saved["Targets"][1]["SampleIntervalMs"], 1000);
    assert_eq!(saved["Targets"][1]["OpenReportOnComplete"], true);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
#[ignore = "requires explicitly compiled C# fixture; temporary directories only"]
fn csharp_and_rust_contend_for_unicode_and_alias_configuration() {
    use std::os::windows::process::CommandExt;
    let fixture = std::env::var_os("FRAMESCOPE_CONFIG_LOCK_FIXTURE").expect("compiled fixture required");
    let root = std::env::temp_dir().join(format!("framescope-config-lock-{}", std::process::id()));
    std::fs::create_dir_all(root.join("éı").join("nested")).unwrap();
    let junction = root.join("alias");
    let status = std::process::Command::new("powershell.exe")
        .args(["-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:FS_CONFIG_ALIAS -Target $env:FS_CONFIG_PHYSICAL | Out-Null"])
        .env("FS_CONFIG_ALIAS", &junction).env("FS_CONFIG_PHYSICAL", root.join("éı"))
        .creation_flags(0x08000000).status().unwrap();
    assert!(status.success(), "temporary junction creation failed");
    for index in 0..3 {
        let canonical = root.join("éı").join("framescope-config.json");
        let rust_path = match index {
            1 => root.join("éı").join("nested").join("..").join("framescope-config.json"),
            2 => junction.join("framescope-config.json"),
            _ => canonical.clone(),
        };
        let ready = root.join(format!("ready-{index}"));
        let release = root.join(format!("release-{index}"));
        let mut child = std::process::Command::new(&fixture).args([canonical.as_os_str(), ready.as_os_str(), release.as_os_str()]).creation_flags(0x08000000).spawn().unwrap();
        let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
        while !ready.exists() && std::time::Instant::now() < deadline { std::thread::sleep(std::time::Duration::from_millis(20)); }
        if !ready.exists() { let _ = child.kill(); panic!("C# fixture did not acquire lock"); }
        let acquired = acquire_config_file_mutex_with_timeout(&rust_path, 0);
        let unexpectedly_acquired = acquired.is_ok();
        drop(acquired);
        std::fs::write(&release, "release").unwrap();
        assert!(child.wait().unwrap().success());
        assert!(!unexpectedly_acquired, "Rust bypassed C# configuration lock (case={index})");
        assert!(acquire_config_file_mutex_with_timeout(&rust_path, 0).is_ok(), "released lock remains unavailable");
    }
    std::fs::remove_dir(junction).unwrap();
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
#[ignore = "requires explicitly compiled C# fixture; temporary backend only"]
fn rust_settings_preserve_targets_saved_by_csharp_writer() {
    use std::os::windows::process::CommandExt;
    let fixture = std::env::var_os("FRAMESCOPE_CONFIG_LOCK_FIXTURE").expect("compiled fixture required");
    let root = std::env::temp_dir().join(format!("framescope-config-rmw-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    std::fs::write(root.join("FrameScopeMonitor.exe"), "fixture; never executed").unwrap();
    let path = root.join("framescope-config.json");
    write_json_atomic(&path, &default_config()).unwrap();
    let ready = root.join("ready");
    let release = root.join("release");
    let mut child = std::process::Command::new(fixture).args([path.as_os_str(), ready.as_os_str(), release.as_os_str(), std::ffi::OsStr::new("save-target")]).creation_flags(0x08000000).spawn().unwrap();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(5);
    while !ready.exists() && std::time::Instant::now() < deadline { std::thread::sleep(std::time::Duration::from_millis(20)); }
    if !ready.exists() { let _ = child.kill(); panic!("C# fixture did not acquire lock"); }
    let old_root = std::env::var_os("FRAMESCOPE_BACKEND_DIR");
    std::env::set_var("FRAMESCOPE_BACKEND_DIR", &root);
    let (sender, receiver) = std::sync::mpsc::channel();
    let writer = std::thread::spawn(move || { sender.send(config_save(&json!({"autoOpenReport":false}))).unwrap(); });
    let blocked = receiver.recv_timeout(std::time::Duration::from_millis(200)).is_err();
    std::fs::write(&release, "release").unwrap();
    let child_ok = child.wait().unwrap().success();
    let result = receiver.recv_timeout(std::time::Duration::from_secs(5));
    writer.join().unwrap();
    if let Some(value) = old_root { std::env::set_var("FRAMESCOPE_BACKEND_DIR", value); } else { std::env::remove_var("FRAMESCOPE_BACKEND_DIR"); }
    assert!(blocked, "Rust settings bypassed C# writer");
    assert!(child_ok);
    assert!(result.unwrap().is_ok());
    let config: Value = serde_json::from_str(&std::fs::read_to_string(&path).unwrap()).unwrap();
    assert_eq!(config["Targets"][0]["ProcessName"], "Concurrent.exe");
    assert_eq!(config["OpenReportOnComplete"], false);
    std::fs::remove_dir_all(root).unwrap();
}
