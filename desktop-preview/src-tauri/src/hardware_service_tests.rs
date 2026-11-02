use super::*;

#[test]
fn exit_gate_blocks_cold_service_start_and_cancel_restores_availability() {
    prevent_new_connections();
    let result = spawn_service(std::path::Path::new("missing-service.exe"), std::path::Path::new("missing-provider.exe"));
    assert!(result.unwrap_err().contains("shutdown"));
    cancel_shutdown();
    assert!(*SPAWN_GATE.lock().unwrap());
    assert!(!HALTING.load(Ordering::SeqCst));
    assert!(!poller_paused());
}

#[test]
fn ordinary_request_waits_for_another_reader_instead_of_reporting_maintenance() {
    let root = std::env::temp_dir().join(format!("framescope-read-queue-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let held = MaintenanceLease::acquire(&root, 0).unwrap();
    let pipe = FakePipe::new(true);
    let client = open_pipe_at(&pipe.name, &std::env::current_exe().unwrap()).unwrap();
    let (entered, entering) = std::sync::mpsc::channel();
    let request = std::thread::spawn(move || {
        entered.send(()).unwrap();
        call_with_installation_client(&root, &Mutex::new(None), &AtomicBool::new(false),
            "handshake", &Value::Null, || Ok(client))
    });
    entering.recv().unwrap();
    std::thread::sleep(Duration::from_millis(100));
    drop(held);
    assert!(request.join().unwrap().is_ok(), "a normal concurrent read must be queued");
}

#[test]
fn maintenance_excludes_other_threads_and_recovers_after_failure() {
    let root = std::env::temp_dir().join(format!("framescope-maintenance-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let guard = MaintenanceLease::acquire(&root, 0).unwrap();
    let other = root.clone();
    assert!(std::thread::spawn(move || MaintenanceLease::acquire(&other, 0).is_err()).join().unwrap());
    let independent = root.join("other-installation");
    std::fs::create_dir_all(&independent).unwrap();
    assert!(std::thread::spawn(move || MaintenanceLease::acquire(&independent, 0).is_ok()).join().unwrap());
    drop(guard);
    let other = root.clone();
    assert!(std::thread::spawn(move || MaintenanceLease::acquire(&other, 0).is_ok()).join().unwrap());
    assert_eq!(maintenance_name(&root).unwrap(), maintenance_name(&root.join(".")).unwrap());
}

#[test]
fn maintenance_waits_until_prior_request_releases_lease() {
    let root = std::env::temp_dir().join(format!("framescope-maintenance-drain-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let held = MaintenanceLease::acquire(&root, 0).unwrap();
    let (ready_tx, ready_rx) = std::sync::mpsc::channel();
    let waiter = std::thread::spawn(move || {
        ready_tx.send(()).unwrap();
        let started = Instant::now();
        let _held = MaintenanceLease::acquire(&root, 3000).unwrap();
        started.elapsed()
    });
    ready_rx.recv().unwrap();
    std::thread::sleep(Duration::from_millis(100));
    drop(held);
    assert!(waiter.join().unwrap() >= Duration::from_millis(90));
}

#[test]
fn maintenance_with_no_service_does_not_spawn_and_releases_gate() {
    let root = std::env::temp_dir().join(format!("framescope-maintenance-absent-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let slot = Mutex::new(None);
    let lease = begin_maintenance_at(&root, &root.join("absent.exe"),
        &format!("FrameScope-No-Service-Fixture-{}", std::process::id()), &slot).unwrap();
    assert!(slot.lock().unwrap().is_none());
    drop(lease);
    let other = root.clone();
    assert!(std::thread::spawn(move || MaintenanceLease::acquire(&other, 0).is_ok()).join().unwrap());
}

#[test]
fn maintenance_rejects_foreign_installation_without_sending_stop() {
    let root = std::env::temp_dir().join(format!("framescope-maintenance-foreign-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let expected = root.join("expected-service.exe");
    std::fs::write(&expected, b"not executed").unwrap();
    let pipe = FakePipe::new(true);
    let slot = Mutex::new(None);
    assert!(begin_maintenance_at(&root, &expected, &pipe.name, &slot).err().unwrap().contains("identity mismatch"));
    assert!(pipe.observed.try_recv().is_err());
    assert!(std::thread::spawn(move || MaintenanceLease::acquire(&root, 0).is_ok()).join().unwrap());
}

#[test]
#[ignore = "requires explicitly compiled isolated C# fixture"]
fn maintenance_cross_process_csharp_uses_identical_installation_lock() {
    let exe = std::path::PathBuf::from(std::env::var("FRAMESCOPE_MAINTENANCE_TEST_CLIENT").unwrap());
    let root = exe.parent().unwrap();
    let lease = MaintenanceLease::acquire(root, 0).unwrap();
    assert_eq!(std::process::Command::new(&exe).arg("probe").arg(root).status().unwrap().code(), Some(7));
    drop(lease);
    assert!(std::process::Command::new(&exe).arg("probe").arg(root).status().unwrap().success());
}

#[test]
#[ignore = "requires explicitly compiled isolated C# fixture"]
fn maintenance_unicode_and_path_aliases_share_csharp_lock() {
    let exe = std::path::PathBuf::from(std::env::var("FRAMESCOPE_MAINTENANCE_TEST_CLIENT").unwrap());
    for suffix in ["installation-ß", "installation-ﬃ", "installation-ı", "Installation-MiXeD"] {
        let root = exe.parent().unwrap().join(suffix);
        std::fs::create_dir_all(&root).unwrap();
        let aliases = [root.clone(), root.join("."), std::path::PathBuf::from(format!("{}\\", root.display())),
            std::path::PathBuf::from(root.to_string_lossy().replace("Installation-MiXeD", "INSTALLATION-MIXED"))];
        let held = MaintenanceLease::acquire(&root, 0).unwrap();
        for alias in &aliases {
            assert_eq!(maintenance_name(&root).unwrap(), maintenance_name(alias).unwrap());
            assert_eq!(std::process::Command::new(&exe).arg("probe").arg(alias).status().unwrap().code(), Some(7),
                "C# must respect Rust maintenance for {}", alias.display());
        }
        drop(held);
        for alias in aliases {
            assert!(std::process::Command::new(&exe).arg("probe").arg(alias).status().unwrap().success());
        }
    }
}

#[test]
#[ignore = "requires explicitly compiled isolated C# pipe fixture"]
fn maintenance_stops_verified_service_and_rejects_incomplete_cleanup() {
    let exe = std::path::PathBuf::from(std::env::var("FRAMESCOPE_MAINTENANCE_TEST_CLIENT").unwrap());
    for complete in [true, false] {
        let name = format!("FrameScope-Maintenance-Fixture-{}-{}", std::process::id(), complete);
        let ready = exe.parent().unwrap().join(format!("ready-{complete}"));
        let _ = std::fs::remove_file(&ready);
        let mut child = std::process::Command::new(&exe).arg("serve").arg(&name).arg(&ready).arg(complete.to_string()).spawn().unwrap();
        let started = Instant::now();
        while !ready.exists() && started.elapsed() < Duration::from_secs(5) { std::thread::sleep(Duration::from_millis(10)); }
        assert!(ready.exists());
        let slot = Mutex::new(None);
        let result = begin_maintenance_at(exe.parent().unwrap(), &exe, &name, &slot);
        assert_eq!(result.is_ok(), complete);
        if !complete { assert!(result.err().unwrap().contains("confirm maintenance cleanup")); }
        else { assert!(child.try_wait().unwrap().is_some(), "successful maintenance must wait for process exit"); }
        child.wait().unwrap();
    }
}

#[test]
fn low_precision_survives_native_overlay_transport() {
    let data = sanitize_for_gdi(json!({
        "fps": 144.4, "fps_1low": 59.8, "fps_01low": 29.3,
        "target_pid": 1234, "timestamp_ms": 123456789,
    }));
    assert_eq!(data["fps_1low"], 59.8);
    assert_eq!(data["fps_01low"], 29.3);
    assert_eq!(data["fps"], 144);
    let panel = crate::decode_fixture(data).expect("native overlay must accept fractional lows");
    let encoded = serde_json::to_value(panel).unwrap();
    assert_eq!(encoded["fps_1low"], 59.8);
    assert_eq!(encoded["fps_01low"], 29.3);
}
use std::sync::mpsc;
use windows_sys::Win32::Foundation::CloseHandle;
use windows_sys::Win32::Storage::FileSystem::{ReadFile, WriteFile, PIPE_ACCESS_DUPLEX};
use windows_sys::Win32::System::Pipes::{
    ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, PIPE_READMODE_MESSAGE,
    PIPE_TYPE_MESSAGE,
};

static NEXT_PIPE: AtomicU64 = AtomicU64::new(0);

struct FakePipe {
    name: String,
    observed: mpsc::Receiver<Value>,
    release: mpsc::Sender<()>,
    thread: Option<std::thread::JoinHandle<()>>,
}

impl FakePipe {
    fn new(reply: bool) -> Self {
        Self::with_cleanup_reply(reply, true)
    }

    fn with_cleanup_reply(reply: bool, shutdown_complete: bool) -> Self {
        let name = format!(
            "FrameScope-PipeAudit-{}-{}",
            std::process::id(),
            NEXT_PIPE.fetch_add(1, Ordering::Relaxed)
        );
        let wide: Vec<u16> = format!("\\\\.\\pipe\\{name}").encode_utf16().chain(once(0)).collect();
        let handle = unsafe {
            CreateNamedPipeW(
                wide.as_ptr(), PIPE_ACCESS_DUPLEX, PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE,
                1, 65536, 65536, 0, null_mut(),
            )
        };
        assert_ne!(handle, -1isize as Handle);
        let address = handle as usize;
        let (observed_tx, observed) = mpsc::channel();
        let (release, released) = mpsc::channel();
        let thread = std::thread::spawn(move || unsafe {
            let handle = address as Handle;
            let connected = ConnectNamedPipe(handle, null_mut()) != 0
                || std::io::Error::last_os_error().raw_os_error() == Some(535);
            if connected {
                let mut buffer = [0u8; 4096];
                let mut count = 0;
                if ReadFile(handle, buffer.as_mut_ptr(), buffer.len() as u32, &mut count, null_mut()) != 0 {
                    let request: Value = serde_json::from_slice(&buffer[..count as usize]).unwrap();
                    observed_tx.send(request.clone()).ok();
                    if reply {
                        let bytes = json!({"id":request["id"],"ok":true,"result":{
                            "fake":true,"status":"stopped","shutdownComplete":shutdown_complete
                        }}).to_string();
                        WriteFile(handle, bytes.as_ptr(), bytes.len() as u32, &mut count, null_mut());
                    }
                    released.recv_timeout(Duration::from_secs(25)).ok();
                }
            }
            DisconnectNamedPipe(handle);
            CloseHandle(handle);
        });
        Self { name, observed, release, thread: Some(thread) }
    }
}

impl Drop for FakePipe {
    fn drop(&mut self) {
        self.release.send(()).ok();
        self.thread.take().unwrap().join().unwrap();
    }
}

#[test]
fn pipe_audit_rejects_wrong_server_before_sending_commands() {
    let server = FakePipe::new(true);
    let expected = std::path::PathBuf::from(std::env::var_os("SystemRoot").unwrap())
        .join("System32").join("cmd.exe");
    assert!(expected.is_file());
    let result = open_pipe_at(&server.name, &expected).map(|mut client| {
        client.request("clear_cache", &Value::Null)
    });
    let rejected = result.is_err();
    let command_received = server.observed.recv_timeout(Duration::from_millis(200)).is_ok();
    drop(server);
    assert!(rejected && !command_received,
        "untrusted server accepted={}; command_received={command_received}", !rejected);
}

#[test]
fn pipe_audit_accepts_expected_server_and_matches_reply() {
    let server = FakePipe::new(true);
    let mut client = open_pipe_at(&server.name, &std::env::current_exe().unwrap()).unwrap();
    assert_eq!(client.request("handshake", &Value::Null).unwrap()["fake"], true);
    drop(client);
    drop(server);
}

#[test]
fn pipe_audit_concurrent_cold_requests_have_independent_deadlines() {
    let first = FakePipe::new(false);
    let second = FakePipe::new(false);
    let mut client_a = open_pipe_at(&first.name, &std::env::current_exe().unwrap()).unwrap();
    let mut client_b = open_pipe_at(&second.name, &std::env::current_exe().unwrap()).unwrap();
    let (done, results) = mpsc::channel();
    let done_a = done.clone();
    let started = Instant::now();
    let a = std::thread::spawn(move || {
        done_a.send(("first", client_a.request("handshake", &Value::Null), started.elapsed())).unwrap();
    });
    first.observed.recv_timeout(Duration::from_secs(2)).unwrap();
    std::thread::sleep(Duration::from_millis(100));
    let b = std::thread::spawn(move || {
        done.send(("second", client_b.request("handshake", &Value::Null), started.elapsed())).unwrap();
    });
    second.observed.recv_timeout(Duration::from_secs(2)).unwrap();
    let budget = Duration::from_millis(READ_TIMEOUT_MS + 2500);
    let mut finished = Vec::new();
    while finished.len() < 2 {
        let Some(remaining) = budget.checked_sub(started.elapsed()) else { break };
        match results.recv_timeout(remaining) {
            Ok(result) => finished.push(result),
            Err(_) => break,
        }
    }
    eprintln!("concurrent deadline completions before cleanup: {finished:?}");
    drop(first);
    drop(second);
    a.join().unwrap();
    b.join().unwrap();
    assert_eq!(finished.len(), 2, "one request lost watchdog ownership");
    assert!(finished.iter().all(|(_, result, _)| result.is_err()));
}

#[test]
fn pipe_audit_halting_drains_cold_call_and_never_reconnects() {
    use std::sync::Arc;
    let server = FakePipe::new(true);
    let slot = Arc::new(Mutex::new(None));
    let halting = Arc::new(AtomicBool::new(false));
    let (entered, entering) = mpsc::channel();
    let (release, released) = mpsc::channel();
    let call_slot = slot.clone();
    let call_halting = halting.clone();
    let name = server.name.clone();
    let caller = std::thread::spawn(move || {
        call_with_client(&call_slot, &call_halting, "handshake", &Value::Null, || {
            entered.send(()).unwrap();
            released.recv().unwrap();
            open_pipe_at(&name, &std::env::current_exe().unwrap())
        })
    });
    entering.recv_timeout(Duration::from_secs(2)).unwrap();
    let stop_slot = slot.clone();
    let stop_halting = halting.clone();
    let stopper = std::thread::spawn(move || stop_connected_client(&stop_slot, &stop_halting));
    while !halting.load(Ordering::SeqCst) { std::thread::yield_now(); }
    let late_connects = AtomicU64::new(0);
    let late = call_with_client(&slot, &halting, "handshake", &Value::Null, || {
        late_connects.fetch_add(1, Ordering::SeqCst);
        Err("unexpected reconnect".into())
    });
    release.send(()).unwrap();
    let call_result = caller.join().unwrap();
    let stopped = stopper.join().unwrap();
    let observed = server.observed.recv_timeout(Duration::from_secs(2)).unwrap();
    drop(server);
    assert_eq!(late_connects.load(Ordering::SeqCst), 0, "halting allowed a new connect/spawn");
    assert!(late.is_err() && call_result.is_err());
    assert_eq!(stopped.unwrap(), true, "cold connection escaped shutdown");
    assert_eq!(observed["method"], "stop");
    assert!(slot.lock().unwrap().is_none());
    assert!(!stop_connected_client(&slot, &halting).unwrap(), "shutdown must be idempotent");
}

#[test]
fn pipe_audit_legacy_stop_ack_is_not_cleanup_success() {
    let server = FakePipe::with_cleanup_reply(true, false);
    let client = open_pipe_at(&server.name, &std::env::current_exe().unwrap()).unwrap();
    let slot = Mutex::new(Some(client));
    let halting = AtomicBool::new(false);
    let result = stop_connected_client(&slot, &halting);
    assert!(result.unwrap_err().contains("did not confirm cleanup"));
    assert!(halting.load(Ordering::SeqCst));
    assert!(slot.lock().unwrap().is_none());
    assert!(!stop_connected_client(&slot, &halting).unwrap());
}
