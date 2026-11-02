use super::*;

struct RuntimeFixture {
    root: PathBuf,
    previous: Option<std::ffi::OsString>,
}

impl RuntimeFixture {
    fn new() -> Self {
        let source = PathBuf::from(std::env::var_os("FRAMESCOPE_UPDATE_TEST_RUNTIME").expect("isolated runtime required"));
        assert!(source.starts_with("C:\\fs-build\\b203"));
        let root = std::env::temp_dir().join(format!("framescope-update-drain-{}-{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        std::fs::create_dir(&root).unwrap();
        for entry in std::fs::read_dir(source).unwrap() {
            let entry = entry.unwrap();
            if entry.file_type().unwrap().is_file() {
                std::fs::copy(entry.path(), root.join(entry.file_name())).unwrap();
            }
        }
        let config = json!({"DataRoot":root.join("runs"),"AutoStartMonitoring":false,"AutoStartOverlay":false,
            "Targets":[{"Name":"isolated-disabled-target","ProcessName":"framescope_disabled_fixture.exe","Enabled":false}]});
        std::fs::write(root.join("framescope-config.json"), serde_json::to_vec(&config).unwrap()).unwrap();
        let previous = std::env::var_os("FRAMESCOPE_BACKEND_DIR");
        std::env::set_var("FRAMESCOPE_BACKEND_DIR", &root);
        UPDATE_PENDING.store(false, std::sync::atomic::Ordering::SeqCst);
        HOST_CLOSING.store(false, std::sync::atomic::Ordering::SeqCst);
        Self { root, previous }
    }
}

impl Drop for RuntimeFixture {
    fn drop(&mut self) {
        UPDATE_PENDING.store(false, std::sync::atomic::Ordering::SeqCst);
        let stopped = shutdown_owned_monitor().is_ok();
        match self.previous.take() {
            Some(previous) => std::env::set_var("FRAMESCOPE_BACKEND_DIR", previous),
            None => std::env::remove_var("FRAMESCOPE_BACKEND_DIR"),
        }
        if stopped && self.root.parent().and_then(|p| p.canonicalize().ok()) == std::env::temp_dir().canonicalize().ok()
            && self.root.file_name().unwrap().to_string_lossy().starts_with("framescope-update-drain-") {
            let _ = std::fs::remove_dir_all(&self.root);
        }
    }
}

#[test]
#[ignore = "requires verified isolated C# runtime; never uses the installed configuration"]
fn install_reservation_normally_stops_the_owned_waiting_watcher() {
    let _fixture = RuntimeFixture::new();
    monitor_start().unwrap();
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(15);
    loop {
        if monitor_status().is_ok_and(|s| s["phase"] == "waiting") { break; }
        assert!(std::time::Instant::now() < deadline, "isolated watcher did not reach waiting");
        std::thread::sleep(std::time::Duration::from_millis(50));
    }
    let reservation = reserve_update().expect("install must normally stop the waiting watcher automatically");
    assert_eq!(monitor_status().unwrap()["phase"], "stopped");
    assert!(OWNED_WATCHER.lock().unwrap().is_none());
    assert!(ensure_not_closing().is_err());
    drop(reservation);
    assert!(ensure_not_closing().is_ok());
}

#[test]
fn failed_drain_releases_the_update_gate_without_launching_new_work() {
    let result=reserve_update_after(||{
        assert!(ensure_not_closing().is_err());
        Err("fixture report finalization timeout".into())
    },||panic!("status must not be accepted after a failed drain"),std::time::Duration::from_millis(50));
    assert!(result.err().unwrap().contains("fixture report"));
    assert!(ensure_not_closing().is_ok());
}

#[test]
fn a_foreign_or_still_running_watcher_does_not_receive_install_permission() {
    let result=reserve_update_after(||Ok(()),||Ok(json!({"phase":"capturing"})),std::time::Duration::from_millis(50));
    assert!(result.is_err());
    assert!(ensure_not_closing().is_ok());
    assert!(REPORT_OPERATION.try_lock().is_ok());
}

#[test]
fn report_lock_is_retained_until_install_or_uac_failure_releases_the_reservation() {
    let reservation=reserve_update_after(||Ok(()),||Ok(json!({"phase":"stopped"})),std::time::Duration::from_millis(50)).unwrap();
    assert!(REPORT_OPERATION.try_lock().is_err());
    assert!(monitor_start().unwrap_err().contains("正在安装"));
    assert!(report_regenerate("not-a-real-run").unwrap_err().contains("报告正在处理"));
    drop(reservation);
    assert!(REPORT_OPERATION.try_lock().is_ok());
    assert!(ensure_not_closing().is_ok());
}

#[test]
fn preexisting_report_operations_are_waited_for_and_timeout_cleans_the_gate() {
    let report=REPORT_OPERATION.lock().unwrap();
    let worker=std::thread::spawn(||reserve_update_after(||Ok(()),||panic!("busy report cannot be accepted"),std::time::Duration::from_millis(30)).is_err());
    assert!(worker.join().unwrap());drop(report);
    assert!(ensure_not_closing().is_ok());
    let (ready,started)=std::sync::mpsc::channel();let (release,wait)=std::sync::mpsc::channel();
    let worker=std::thread::spawn(move||{let _report=REPORT_OPERATION.lock().unwrap();ready.send(()).unwrap();wait.recv().unwrap();});
    started.recv().unwrap();
    let reservation=reserve_update_after(||{release.send(()).unwrap();Ok(())},||Ok(json!({"phase":"idle"})),std::time::Duration::from_secs(1)).unwrap();
    assert!(REPORT_OPERATION.try_lock().is_err());drop(reservation);worker.join().unwrap();
    assert!(ensure_not_closing().is_ok());
}
