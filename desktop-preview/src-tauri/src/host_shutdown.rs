use serde_json::Value;
use std::time::Duration;
use std::path::Path;

pub fn same_installation_request(args: &[String], current_exe: &Path, root: &Path) -> bool {
    let Some(source) = args.first().map(Path::new) else { return false };
    let same = |a: &Path, b: &Path| match (a.canonicalize(), b.canonicalize()) {
        (Ok(a), Ok(b)) => a.to_string_lossy().eq_ignore_ascii_case(&b.to_string_lossy()),
        _ => false,
    };
    let requested_root = args.windows(2).find(|a|a[0] == "--request-exit-root")
        .map(|a|Path::new(&a[1])).or_else(||source.parent());
    args.iter().any(|a|a == "--request-exit") && same(source, current_exe)
        && requested_root.map(|p|same(p, root)).unwrap_or(false)
}

pub fn budget(pending_reports: usize) -> Duration {
    Duration::from_secs(30 + 120 * pending_reports as u64 + 15)
}

pub fn completed(exited: bool, state: &Value, token: Option<&str>) -> Result<bool, String> {
    if state["WatcherInstanceId"].as_str() != token || token.is_none() {
        return Err("监控实例不匹配，已取消退出".into());
    }
    if state["Phase"] != "stopped" || !exited { return Ok(false); }
    if !state["ActiveMonitors"].as_array().map(|monitors|monitors.is_empty()).unwrap_or(false) {
        return Err("监控终态仍有活动任务或缺少任务清单，已取消退出".into());
    }
    let code = state["StopErrorCode"].as_str().unwrap_or("");
    let error = state["StopError"].as_str().filter(|s|!s.is_empty());
    if code == "report-finalization-failed" {
        log::warn!("监控已停止，报告生成失败证据已保留：{}", error.unwrap_or(code));
        return Ok(true);
    }
    if !code.is_empty() || error.is_some() {
        let error = error.unwrap_or(code);
        return Err(format!("监控收尾失败，已取消退出：{error}"));
    }
    Ok(true)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    #[test]
    fn slow_report_needs_both_terminal_ack_and_actual_exit() {
        let mut state = json!({"WatcherInstanceId":"owned", "Phase":"generating", "ActiveMonitors":[]});
        assert!(!completed(false, &state, Some("owned")).unwrap());
        assert!(!completed(true, &state, Some("owned")).unwrap());
        state["Phase"] = json!("stopped");
        assert!(!completed(false, &state, Some("owned")).unwrap());
        assert!(completed(true, &state, Some("owned")).unwrap());
    }
    #[test]
    fn foreign_instance_or_report_error_cannot_complete_exit() {
        let mut state = json!({"WatcherInstanceId":"foreign", "Phase":"stopped", "ActiveMonitors":[]});
        assert!(completed(true, &state, Some("owned")).is_err());
        state["WatcherInstanceId"] = json!("owned");
        state["StopError"] = json!("report timeout");
        assert!(completed(true, &state, Some("owned")).is_err());
    }
    #[test]
    fn stopped_failed_report_allows_exit_but_keeps_failure_evidence() {
        let state = json!({"WatcherInstanceId":"owned", "Phase":"stopped", "ActiveMonitors":[],
            "StopErrorCode":"report-finalization-failed", "StopError":"Report finalization failed for fixture"});
        let before = state.clone();
        assert!(completed(true, &state, Some("owned")).unwrap());
        assert!(!completed(false, &state, Some("owned")).unwrap());
        assert_eq!(state, before, "failure state must remain intact");
        let mut worker_missing = state.clone();
        worker_missing["StopErrorCode"] = json!("worker-run-missing");
        assert!(completed(true, &worker_missing, Some("owned")).is_err());
    }
    #[test]
    fn terminal_state_requires_an_explicit_empty_active_monitor_array() {
        let mut state = json!({"WatcherInstanceId":"owned", "Phase":"stopped"});
        assert!(completed(true, &state, Some("owned")).is_err());
        state["ActiveMonitors"] = json!([{"RunDir":"fake-active"}]);
        assert!(completed(true, &state, Some("owned")).is_err());
        state["ActiveMonitors"] = json!([]);
        assert!(completed(true, &state, Some("owned")).unwrap());
    }
    #[test]
    fn production_watcher_publishes_distinct_failure_codes() {
        let watcher = include_str!("../../../src/app/FrameScopeNativeMonitor.Watcher.cs");
        for code in ["report-finalization-failed", "worker-run-missing"] {
            assert!(watcher.contains(&format!("RecordWatcherStopError(\"{code}\"")));
        }
        assert!(watcher.contains("StopErrorCode = WatcherStopErrorCode"));
        let mut state = json!({"WatcherInstanceId":"owned", "Phase":"stopped", "ActiveMonitors":[],
            "StopErrorCode":"unknown"});
        assert!(completed(true, &state, Some("owned")).is_err());
        state["StopErrorCode"] = json!("worker-run-missing");
        assert!(completed(true, &state, Some("owned")).is_err());
    }
    #[test]
    fn each_pending_report_has_its_full_budget() {
        assert_eq!(budget(0), Duration::from_secs(45));
        assert_eq!(budget(1), Duration::from_secs(165));
        assert_eq!(budget(3), Duration::from_secs(405));
    }
    #[test]
    fn request_exit_clears_gui_and_checks_source_installation() {
        let source = include_str!("lib.rs");
        assert!(source.contains("context.config_mut().app.windows.clear()"));
        assert!(source.contains("host_shutdown::same_installation_request"));
        assert!(source.contains("std::process::exit(3)"));
    }
    #[test]
    fn another_installation_and_wrong_root_cannot_request_exit() {
        let exe = std::env::current_exe().unwrap();
        let root = exe.parent().unwrap();
        let mut args = vec![exe.to_string_lossy().into_owned(), "--request-exit".into()];
        assert!(same_installation_request(&args, &exe, root));
        args[0] = root.join("old-install.exe").to_string_lossy().into_owned();
        assert!(!same_installation_request(&args, &exe, root));
        args[0] = exe.to_string_lossy().into_owned();
        args.extend(["--request-exit-root".into(), exe.to_string_lossy().into_owned()]);
        assert!(!same_installation_request(&args, &exe, root));
    }
}
