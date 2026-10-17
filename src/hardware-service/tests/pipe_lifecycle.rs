#![cfg(windows)]

use std::io::{Read, Write};
use std::os::windows::fs::OpenOptionsExt;
use std::process::{Child, Command, Stdio};
use std::time::{Duration, Instant};

struct OwnedService(Child);

impl Drop for OwnedService {
    fn drop(&mut self) {
        if self.0.try_wait().unwrap().is_none() {
            self.0.kill().ok();
            self.0.wait().ok();
        }
    }
}

#[test]
fn pipe_audit_stop_exits_while_accept_is_idle() {
    let name = format!("FrameScope-PipeAudit-Stop-{}", std::process::id());
    let mut child = OwnedService(Command::new(env!("CARGO_BIN_EXE_framescope-hardware-service"))
        .args(["--pipe-name", &name])
        .stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::inherit())
        .spawn().unwrap());
    let deadline = Instant::now() + Duration::from_secs(5);
    let mut pipe = loop {
        match std::fs::OpenOptions::new().read(true).write(true)
            .custom_flags(0x0010_0000 | 0x0001_0000)
            .open(format!("\\\\.\\pipe\\{name}")) {
            Ok(pipe) => break pipe,
            Err(error) if Instant::now() < deadline => {
                assert!(child.0.try_wait().unwrap().is_none(), "service exited: {error}");
                std::thread::sleep(Duration::from_millis(20));
            }
            Err(error) => panic!("unique pipe connect failed: {error}"),
        }
    };
    pipe.write_all(b"{\"id\":\"hello\",\"method\":\"handshake\"}\n").unwrap();
    let mut buffer = [0; 4096];
    let count = pipe.read(&mut buffer).unwrap();
    assert_eq!(serde_json::from_slice::<serde_json::Value>(&buffer[..count]).unwrap()["ok"], true);
    // The service has already created the next instance and is idle in accept.
    std::thread::sleep(Duration::from_millis(150));
    pipe.write_all(b"{\"id\":\"stop\",\"method\":\"stop\"}\n").unwrap();
    let count = pipe.read(&mut buffer).unwrap();
    assert_eq!(serde_json::from_slice::<serde_json::Value>(&buffer[..count]).unwrap()["result"]["status"], "stopped");
    drop(pipe);
    let deadline = Instant::now() + Duration::from_secs(2);
    while Instant::now() < deadline {
        if let Some(status) = child.0.try_wait().unwrap() {
            assert!(status.success(), "service exit: {status}");
            return;
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    panic!("stop acknowledged but process remained blocked in accept; owned PID={}", child.0.id());
}

#[test]
fn pipe_audit_client_disconnect_before_accept_does_not_exit_service() {
    let name = format!("FrameScope-PipeAudit-Disconnect-{}", std::process::id());
    let mut child = OwnedService(Command::new(env!("CARGO_BIN_EXE_framescope-hardware-service"))
        .args(["--pipe-name", &name])
        .stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::inherit())
        .spawn().unwrap());
    let deadline = Instant::now() + Duration::from_secs(5);
    let pipe = loop {
        match std::fs::OpenOptions::new().read(true).write(true)
            .open(format!("\\\\.\\pipe\\{name}")) {
            Ok(pipe) => break pipe,
            Err(_) if Instant::now() < deadline => std::thread::yield_now(),
            Err(error) => panic!("unique pipe connect failed: {error}"),
        }
    };
    drop(pipe);
    std::thread::sleep(Duration::from_millis(100));
    assert!(child.0.try_wait().unwrap().is_none(), "a client disconnected before accept; listener exited");
    let mut pipe = std::fs::OpenOptions::new().read(true).write(true)
        .open(format!("\\\\.\\pipe\\{name}")).unwrap();
    pipe.write_all(b"{\"id\":\"again\",\"method\":\"handshake\"}\n").unwrap();
    let mut buffer = [0; 4096];
    let count = pipe.read(&mut buffer).unwrap();
    assert_eq!(serde_json::from_slice::<serde_json::Value>(&buffer[..count]).unwrap()["ok"], true);
}

#[test]
fn pipe_audit_accepted_stop_survives_client_disconnect_before_reply() {
    let name = format!("FrameScope-PipeAudit-StopDisconnect-{}", std::process::id());
    let mut child = OwnedService(Command::new(env!("CARGO_BIN_EXE_framescope-hardware-service"))
        .args(["--pipe-name", &name])
        .stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::inherit())
        .spawn().unwrap());
    let deadline = Instant::now() + Duration::from_secs(5);
    let mut pipe = loop {
        match std::fs::OpenOptions::new().read(true).write(true)
            .open(format!("\\\\.\\pipe\\{name}")) {
            Ok(pipe) => break pipe,
            Err(_) if Instant::now() < deadline => std::thread::sleep(Duration::from_millis(10)),
            Err(error) => panic!("unique pipe connect failed: {error}"),
        }
    };
    pipe.write_all(b"{\"id\":\"stop\",\"method\":\"stop\"}\n").unwrap();
    // FlushFileBuffers on the client waits until the server reads the request.
    pipe.sync_all().unwrap();
    drop(pipe);
    let deadline = Instant::now() + Duration::from_secs(2);
    while Instant::now() < deadline {
        if let Some(status) = child.0.try_wait().unwrap() {
            assert!(status.success(), "service exit: {status}");
            return;
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    panic!("accepted stop was lost when the client closed before reading its reply");
}

#[test]
fn pipe_audit_stop_ack_waits_for_resident_cleanup() {
    let root = std::env::temp_dir().join(format!("FrameScope-PipeAudit-Helper-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let helper = root.join("fake-provider.exe");
    let marker = root.join("shutdown.txt");
    let fixture = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("tests/fixtures/pipe_fake_helper.rs");
    assert!(Command::new("rustc").arg(fixture).arg("-o").arg(&helper).status().unwrap().success());
    let name = format!("FrameScope-PipeAudit-Cleanup-{}", std::process::id());
    let mut child = OwnedService(Command::new(env!("CARGO_BIN_EXE_framescope-hardware-service"))
        .args(["--pipe-name", &name, "--lhm-helper"]).arg(helper)
        .env("FRAMESCOPE_PIPE_AUDIT_HELPER_LOG", &marker)
        .stdin(Stdio::null()).stdout(Stdio::null()).stderr(Stdio::inherit())
        .spawn().unwrap());
    let deadline = Instant::now() + Duration::from_secs(5);
    let mut pipe = loop {
        match std::fs::OpenOptions::new().read(true).write(true)
            .open(format!("\\\\.\\pipe\\{name}")) {
            Ok(pipe) => break pipe,
            Err(_) if Instant::now() < deadline => std::thread::sleep(Duration::from_millis(10)),
            Err(error) => panic!("unique pipe connect failed: {error}"),
        }
    };
    let mut buffer = [0; 4096];
    pipe.write_all(b"{\"id\":\"snapshot\",\"method\":\"get_snapshot\"}\n").unwrap();
    let count = pipe.read(&mut buffer).unwrap();
    assert_eq!(serde_json::from_slice::<serde_json::Value>(&buffer[..count]).unwrap()["result"]["fixture"], "pipe-audit");
    pipe.write_all(b"{\"id\":\"stop\",\"method\":\"stop\"}\n").unwrap();
    let count = pipe.read(&mut buffer).unwrap();
    let reply: serde_json::Value = serde_json::from_slice(&buffer[..count]).unwrap();
    let cleanup_at_ack = std::fs::read_to_string(&marker).unwrap_or_default();
    eprintln!("cleanup marker at stop ack: {cleanup_at_ack:?}; evidence={}", marker.display());
    assert_eq!(cleanup_at_ack, "exit-request\nhelper-exiting\n", "stop ack preceded provider cleanup");
    assert_eq!(reply["result"]["shutdownComplete"], true);
    drop(pipe);
    let deadline = Instant::now() + Duration::from_secs(2);
    while Instant::now() < deadline {
        if let Some(status) = child.0.try_wait().unwrap() {
            assert!(status.success());
            return;
        }
        std::thread::sleep(Duration::from_millis(20));
    }
    panic!("service did not exit after cleanup");
}
