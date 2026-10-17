use super::*;

#[test]
#[ignore = "explicit compiled disposable controlled report executable; never uses production roots"]
fn regeneration_process_failures_publish_retryable_terminal_state() {
    let helper = PathBuf::from(std::env::var("FRAMESCOPE_CONTROLLED_REPORT_EXE").expect("explicit compiled fixture required"));
    for mode in ["nonzero", "invalid-success", "timeout", "changed", "success", "spawn-failure"] {
        let root = fixture();
        let run = root.join("game/run");
        std::fs::copy(&helper,root.join("FrameScopeReportGenerator.exe")).unwrap();
        std::fs::write(run.join("presentmon.csv"),"header\nvalid-frame\n").unwrap();
        std::fs::write(run.join("fixture-mode"),mode).unwrap();
        if mode == "spawn-failure" {
            let mut invalid_exe = b"MZReportInspectionProtocol".to_vec();
            invalid_exe.extend(REPORT_INSPECTION_PROTOCOL.encode_utf16().flat_map(u16::to_le_bytes));
            std::fs::write(root.join("FrameScopeReportGenerator.exe"),invalid_exe).unwrap();
        }
        let started = std::time::Instant::now();
        let result = report_regeneration_scope(|| {
            assert!(REPORT_OPERATION.try_lock().is_err(),"operation must remain locked during processing");
            regenerate_report_with_root(&root,&run,std::time::Duration::from_secs(3))
        });
        assert!(started.elapsed() < std::time::Duration::from_secs(4),"business deadline exceeded: {mode}");
        let progress = read_json_fixture(&run.join("report-progress.json"));
        assert!(REGENERATING_RUN.lock().unwrap().is_none(),"regeneration ownership leaked: {mode}");
        assert!(REPORT_OPERATION.try_lock().is_ok(),"operation lock leaked: {mode}");
        if mode == "success" {
            assert!(result.is_ok(),"{result:?}");
            assert_eq!(progress["ReportProgressPhase"],"completed");
        } else {
            assert!(result.is_err(),"invalid result accepted: {mode}");
            if mode == "spawn-failure" { assert!(result.as_ref().unwrap_err().contains("start failed"),"spawn fixture did not reach process creation: {result:?}"); }
            assert_eq!(progress["ReportCanRetry"],true,"failure not retryable: {mode}");
            assert!(matches!(progress["ReportProgressPhase"].as_str(),Some("failed" | "timeout")),"nonterminal failure: {mode}");
            assert!(run.join("report-regeneration-failure.json").is_file());
            let status = read_json_fixture(&run.join("status.json"));
            assert_eq!(status["ReportCanRetry"],true);
            assert_eq!(status["ReportGenerationTimedOut"],mode == "timeout");
            std::fs::copy(&helper,root.join("FrameScopeReportGenerator.exe")).unwrap();
            std::fs::write(run.join("fixture-mode"),"success").unwrap();
            assert!(report_regeneration_scope(|| regenerate_report_with_root(&root,&run,std::time::Duration::from_secs(3))).is_ok(),"retry rejected after {mode}");
        }
        println!("{mode}: terminal publication and ownership release verified in {}ms",started.elapsed().as_millis());
        std::fs::remove_dir_all(root).unwrap();
    }
}

fn read_json_fixture(path: &Path) -> Value {
    serde_json::from_slice(&std::fs::read(path).expect("terminal progress evidence missing")).unwrap()
}

fn fixture() -> PathBuf {
    let path = std::env::temp_dir().join(format!("framescope-report-ops-{}-{}",std::process::id(), std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir_all(path.join("game/run")).unwrap();
    path
}

#[test]
fn manifest_presence_cannot_claim_current_inputs_are_complete() {
    let root = fixture();
    let run = root.join("game/run");
    let charts = run.join("charts");
    std::fs::create_dir_all(&charts).unwrap();
    let html = charts.join("framescope-interactive-report.html");
    let data = charts.join("framescope-interactive-data.js");
    std::fs::write(&html, "fixture report").unwrap();
    std::fs::write(&data, "fixture data").unwrap();
    std::fs::write(charts.join("framescope-interactive-manifest.json"), serde_json::to_vec(&json!({
        "report": html, "data": data, "inputFingerprintVersion": 2, "inputFingerprint": "not-current-inputs"
    })).unwrap()).unwrap();
    let report = build_report_entry(run.to_str().unwrap(), html.to_str().unwrap(), &mut Vec::new()).unwrap();
    std::fs::remove_dir_all(root).unwrap();
    assert_ne!(report["artifactStatus"], "complete", "completion requires the authoritative artifact inspector, not manifest presence");
    assert_eq!(report["canOpenReport"], false);
}

#[test]
fn unknown_report_duration_is_unavailable() {
    let root = fixture();
    let run = root.join("game/run");
    let html = run.join("charts/framescope-interactive-report.html");
    let report = build_report_entry(run.to_str().unwrap(), html.to_str().unwrap(), &mut Vec::new()).unwrap();
    std::fs::remove_dir_all(root).unwrap();
    assert!(report["durationSeconds"].is_null(), "unknown collection duration must not become zero");
}

#[test]
fn deletion_only_accepts_owned_single_runs_and_detects_busy_runs() {
    let root = fixture();
    let run = root.join("game/run");
    assert!(checked_report_run(&root, &run).is_ok());
    assert!(checked_report_run(&root, &root).is_err());
    assert!(checked_report_run(&root, &root.join("game")).is_err());
    assert!(checked_report_run(&run, &root).is_err());
    assert!(watcher_uses_run(&json!({"GeneratingRunDir":run}), &run));
    assert!(watcher_uses_run(&json!({"ActiveMonitors":[{"RunDir":run}]}), &run));
    assert!(!watcher_uses_run(&json!({"ActiveMonitors":[]}), &run));
    assert!(watcher_uses_run(&json!({"Phase":"starting","ActiveMonitors":[]}), &run));
    #[cfg(windows)] {
        let deletion_lock=lock_report_for_delete(&run).unwrap();
        let competing_run=run.clone();
        assert!(std::thread::spawn(move || lock_report_for_delete(&competing_run).is_err()).join().unwrap());
        if let Ok(generator) = std::env::var("FRAMESCOPE_REPORT_TEST_EXE") {
            let progress=run.join("report-progress.json");
            std::fs::write(&progress,"unchanged-active-progress").unwrap();
            let output=std::process::Command::new(generator).arg(&run).arg("--progress").arg(&progress).output().unwrap();
            assert!(!output.status.success());
            assert!(String::from_utf8_lossy(&output.stderr).contains("ReportOperationBusyException"));
            assert_eq!(std::fs::read_to_string(progress).unwrap(),"unchanged-active-progress");
        }
        drop(deletion_lock);
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn progress_uses_the_generating_run_and_unknown_eta_is_null() {
    let root = fixture();
    let run = root.join("game/run");
    std::fs::write(run.join("report-progress.json"),r#"{"ReportProgressPercent":42}"#).unwrap();
    let state_path=root.join("framescope-watcher-state.json");
    let state=json!({"Phase":"generating","GeneratingRunDir":run,"ActiveMonitors":[],"CompletedRuns":1});
    let status=monitor_status_from_state(&state_path,&state,true).unwrap();
    assert_eq!(status["reportPercent"],42);
    assert!(status["reportEtaSeconds"].is_null());
    let waiting=json!({"Phase":"idle","ActiveMonitors":[],"CompletedRuns":1});
    assert_eq!(monitor_status_from_state(&state_path,&waiting,true).unwrap()["phase"],"waiting");
    let different=json!({"Phase":"generating","GeneratingRunDir":root.join("other"),"ActiveMonitors":[]});
    assert!(monitor_status_from_state(&state_path,&different,true).unwrap()["reportPercent"].is_null());
    std::fs::write(run.join("report-progress.json"),format!(r#"{{"ReportProgressPercent":42,"ReportProgressUpdatedAt":"{}"}}"#,(chrono::Utc::now()+chrono::Duration::minutes(1)).to_rfc3339())).unwrap();
    assert!(read_active_report_progress(&run).is_none());
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn shell_open_command_is_native_and_preserves_percent_paths() {
    let source = std::fs::read_to_string(std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("src/backend.rs")).unwrap();
    assert!(!source.contains("Command::new(\"cmd\")"));
    assert!(source.contains("ShellExecuteW"));
    assert!(source.contains("%PATH%" ) == false);
}

#[test]
#[ignore = "explicit real Windows recycle-bin integration on a disposable fixture"]
fn recycle_disposable_run_preserves_it_in_the_windows_recycle_bin() {
    let root=fixture();
    let run=root.join("game/run");
    std::fs::write(run.join("marker.txt"),"FrameScope disposable recycle acceptance").unwrap();
    let _run_lock=lock_report_for_delete(&run).unwrap();
    recycle_run(&checked_report_run(&root,&run).unwrap()).unwrap();
    assert!(!run.exists());
    println!("RECYCLED_ORIGINAL_PATH={}",run.display());
    // Keep the parent for the explicit restore check; never purge the recycle bin.
}
