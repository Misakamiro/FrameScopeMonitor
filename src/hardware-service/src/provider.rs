//! Resident provider process manager.
//!
//! Keeps one `FrameScopeHardwareProvider.exe --serve` child alive and issues
//! pull-mode commands over its stdin/stdout JSON protocol, mirroring the NexBox
//! sensor bridge policies: a bounded read timeout, kill-on-timeout, crash
//! restart with a cooldown, and a cap on consecutive restarts before giving
//! up (fail-closed — callers must surface unavailability, never fake data).

use serde_json::Value;
use std::io::{BufRead, BufReader, Write};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::mpsc::{channel, Receiver};
use std::thread;
use std::time::{Duration, Instant};

pub const READ_TIMEOUT: Duration = Duration::from_secs(8);
pub const FIRST_READ_TIMEOUT: Duration = Duration::from_secs(20);
pub const RESTART_COOLDOWN: Duration = Duration::from_secs(30);
pub const MAX_CONSECUTIVE_FAILURES: u32 = 5;
pub fn response_timeout(first: bool) -> Duration { if first { FIRST_READ_TIMEOUT } else { READ_TIMEOUT } }

pub fn stderr_log_path() -> std::path::PathBuf {
    #[cfg(test)]
    if let Ok(fixture) = std::env::var("FRAMESCOPE_PROVIDER_TEST_HELPER") {
        if let Some(root) = std::path::Path::new(&fixture).parent() {
            return root.join(format!("provider-stderr-{}.log", std::process::id()));
        }
    }
    // PID suffix: concurrent service processes (host + worker spawns racing
    // during the FIRST_PIPE_INSTANCE window) must not overwrite each other's
    // only window into console-less startup failures.
    std::env::temp_dir().join(format!(
        "framescope-provider-stderr-{}.log",
        std::process::id()
    ))
}

pub struct ResidentProvider {
    helper_path: Option<String>,
    proc: Option<ProviderProc>,
    consecutive_failures: u32,
    last_spawn: Option<Instant>,
    suspended: bool,
    awaiting_first_response: bool,
    last_initialization_error: Option<String>,
    last_read_error: Option<String>,
}

struct ProviderProc {
    _child: Child,
    stdin: ChildStdin,
    lines: Receiver<String>,
}

impl ProviderProc {
    fn spawn(path: &str) -> Result<ProviderProc, String> {
        // Provider stderr goes to a temp file: it must never fill a pipe
        // backlog, and it is the only window into console-less startup
        // failures (the provider runs under CREATE_NO_WINDOW).
        let stderr_log = std::fs::File::create(stderr_log_path()).ok();
        let mut child = Command::new(path)
            .arg("--serve")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(stderr_log.map(Stdio::from).unwrap_or(Stdio::null()))
            .creation_flags(0x0800_0000) // CREATE_NO_WINDOW
            .spawn()
            .map_err(|error| format!("provider spawn failed: {error}"))?;
        let stdin = child
            .stdin
            .take()
            .ok_or_else(|| "provider stdin unavailable".to_string())?;
        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| "provider stdout unavailable".to_string())?;
        let (sender, receiver) = channel();
        thread::Builder::new()
            .name("framescope-provider-reader".into())
            .spawn(move || {
                let reader = BufReader::new(stdout);
                for line in reader.lines() {
                    match line {
                        Ok(text) => {
                            if sender.send(text).is_err() {
                                break;
                            }
                        }
                        Err(_) => break,
                    }
                }
            })
            .map_err(|error| format!("provider reader thread failed: {error}"))?;
        Ok(ProviderProc {
            _child: child,
            stdin,
            lines: receiver,
        })
    }
}

impl ResidentProvider {
    pub fn new() -> ResidentProvider {
        ResidentProvider {
            helper_path: None,
            proc: None,
            consecutive_failures: 0,
            last_spawn: None,
            suspended: false,
            awaiting_first_response: false,
            last_initialization_error: None,
            last_read_error: None,
        }
    }

    /// Points the manager at a helper executable; respawns when the path changes.
    pub fn set_helper(&mut self, path: Option<String>) -> Result<(), String> {
        if self.helper_path == path {
            return Ok(());
        }
        self.kill()?;
        self.helper_path = path;
        self.consecutive_failures = 0;
        self.suspended = false;
        self.awaiting_first_response = false;
        self.last_initialization_error = None;
        self.last_read_error = None;
        Ok(())
    }

    pub fn helper_path(&self) -> Option<&str> {
        self.helper_path.as_deref()
    }

    #[cfg(test)]
    pub(crate) fn proc_is_stopped(&self) -> bool { self.proc.is_none() }

    /// One pull-mode snapshot read (`{"cmd":"read"}` → one payload line).
    pub fn read(&mut self) -> Result<Value, String> {
        self.exchange("{\"cmd\":\"read\"}")
    }

    /// Sends a raw command line (e.g. `{"cmd":"static"}`) and awaits one payload.
    pub fn send_raw(&mut self, command: &str) -> Result<Value, String> {
        self.exchange(command)
    }

    fn exchange(&mut self, command: &str) -> Result<Value, String> {
        let initializing = self.proc.is_none() || self.awaiting_first_response;
        let result = self.perform(command);
        match result {
            Ok(value) => {
                self.consecutive_failures = 0;
                Ok(value)
            }
            Err(error) => {
                // Policy refusals (not configured / suspended / cooling down)
                // are not provider crashes and must not consume the restart budget.
                let policy_error = error.contains("not configured")
                    || error.contains("gave up after")
                    || error.contains("shutdown")
                    || error.contains("cooldown");
                if !policy_error {
                    let cleanup = self.kill();
                    self.last_spawn = Some(Instant::now());
                    self.consecutive_failures += 1;
                    if self.consecutive_failures >= MAX_CONSECUTIVE_FAILURES {
                        self.suspended = true;
                    }
                    let phase = if initializing { "initialization" } else { "read" };
                    let failure = format!("provider {phase} failed: {error}");
                    if initializing { self.last_initialization_error = Some(failure.clone()); }
                    else { self.last_read_error = Some(failure.clone()); }
                    if let Err(cleanup_error) = cleanup {
                        self.suspended = true;
                        return Err(format!("{failure}; cleanup failed: {cleanup_error}"));
                    }
                    return Err(failure);
                }
                Err(error)
            }
        }
    }

    fn perform(&mut self, command: &str) -> Result<Value, String> {
        let Some(path) = self
            .helper_path
            .as_deref()
            .filter(|p| !p.trim().is_empty())
            .map(|p| p.to_string())
        else {
            return Err("LibreHardwareMonitor provider is not configured".to_string());
        };
        if self.suspended {
            return Err(format!(
                "provider gave up after {MAX_CONSECUTIVE_FAILURES} consecutive failures; restart required"
            ));
        }
        if self.proc.is_none() {
            if !crate::windows_shutdown::may_start_process(crate::SHUTTING_DOWN.load(std::sync::atomic::Ordering::SeqCst), crate::windows_shutdown::system_shutting_down()) {
                return Err("provider start refused during Windows shutdown".into());
            }
            if let Some(last_spawn) = self.last_spawn {
                if last_spawn.elapsed() < RESTART_COOLDOWN {
                    return Err("provider is restarting (cooldown)".to_string());
                }
            }
            self.proc = Some(ProviderProc::spawn(&path)?);
            self.last_spawn = Some(Instant::now());
            self.awaiting_first_response = true;
        }
        let proc = self.proc.as_mut().expect("provider spawned");
        proc.stdin
            .write_all(format!("{command}\n").as_bytes())
            .and_then(|_| proc.stdin.flush())
            .map_err(|error| format!("provider write failed: {error}"))?;
        let timeout = response_timeout(self.awaiting_first_response);
        let line = match proc.lines.recv_timeout(timeout) {
            Ok(line) => line,
            Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {
                let status = proc._child.try_wait().ok().flatten();
                return Err(match status {
                    Some(code) => format!("provider exited while reading (status: {code})"),
                    None => format!("provider read timed out ({}s); provider still running", timeout.as_secs()),
                });
            }
            Err(_) => {
                let status = proc._child.try_wait().ok().flatten();
                return Err(match status {
                    Some(code) => format!("provider exited (status: {code})"),
                    None => "provider stdout closed without exit".to_string(),
                });
            }
        };
        let clean = line.trim_matches('\0').trim_start_matches('\u{feff}').trim();
        let value = serde_json::from_str::<Value>(clean)
            .map_err(|error| format!("provider returned invalid JSON: {error}"))?;
        self.awaiting_first_response = false;
        Ok(value)
    }

    /// Kills the resident provider AND clears the failure budget, so a
    /// manual restart (restart_provider) can recover from a suspended state.
    pub fn force_reset(&mut self) -> Result<(), String> {
        self.kill()?;
        self.suspended = false;
        self.consecutive_failures = 0;
        self.last_initialization_error = None;
        self.last_read_error = None;
        Ok(())
    }

    pub fn diagnostics(&self) -> Value {
        serde_json::json!({"awaitingFirstResponse": self.awaiting_first_response,
            "lastInitializationError": self.last_initialization_error, "lastReadError": self.last_read_error,
            "consecutiveFailures": self.consecutive_failures, "suspended": self.suspended})
    }

    /// Kills the resident provider; the next exchange respawns it.
    pub fn kill(&mut self) -> Result<(), String> {
        if let Some(mut proc) = self.proc.take() {
            let graceful = (|| -> Option<()> {
                proc.stdin.write_all(b"{\"cmd\":\"exit\"}
").ok()?;
                proc.stdin.flush().ok()?;
                // A hung provider (an LHM driver stall is exactly why kill
                // exists) must not hold the provider mutex forever: bound the
                // graceful wait and fall through to kill on timeout.
                for _ in 0..30 {
                    match proc._child.try_wait() {
                        Ok(Some(_)) => return Some(()),
                        Ok(None) => std::thread::sleep(std::time::Duration::from_millis(100)),
                        Err(_) => return None,
                    }
                }
                None
            })();
            if graceful.is_none() {
                if let Err(error) = proc._child.kill() {
                    if proc._child.try_wait().ok().flatten().is_none() {
                        self.proc = Some(proc);
                        return Err(format!("provider termination failed: {error}"));
                    }
                }
                let deadline = Instant::now() + Duration::from_secs(3);
                loop {
                    match proc._child.try_wait() {
                        Ok(Some(_)) => break,
                        Ok(None) if Instant::now() < deadline => thread::sleep(Duration::from_millis(25)),
                        Ok(None) => {
                            self.proc = Some(proc);
                            return Err("provider exit confirmation timed out".into());
                        }
                        Err(error) => {
                            self.proc = Some(proc);
                            return Err(format!("provider exit wait failed: {error}"));
                        }
                    }
                }
            }
        }
        Ok(())
    }
}

trait CreationFlags {
    fn creation_flags(&mut self, flags: u32) -> &mut Command;
}

#[cfg(windows)]
impl CreationFlags for Command {
    fn creation_flags(&mut self, flags: u32) -> &mut Command {
        std::os::windows::process::CommandExt::creation_flags(self, flags)
    }
}

#[cfg(not(windows))]
impl CreationFlags for Command {
    fn creation_flags(&mut self, _flags: u32) -> &mut Command {
        self
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn startup_and_steady_state_timeouts_are_distinct_and_cooldown_is_stable() {
        assert_eq!(response_timeout(true), Duration::from_secs(20));
        assert_eq!(response_timeout(false), Duration::from_secs(8));
        assert_eq!(RESTART_COOLDOWN, Duration::from_secs(30));
        assert_eq!(MAX_CONSECUTIVE_FAILURES, 5);
    }

    #[test]
    fn read_fails_closed_without_helper() {
        let mut provider = ResidentProvider::new();
        let error = provider.read().unwrap_err();
        assert!(error.contains("not configured"));
        assert!(!provider.suspended);
    }

    #[test]
    fn read_fails_closed_for_blank_helper() {
        let mut provider = ResidentProvider::new();
        provider.set_helper(Some("  ".to_string())).unwrap();
        let error = provider.read().unwrap_err();
        assert!(error.contains("not configured"));
    }

    #[test]
    fn missing_helper_reports_spawn_failure() {
        let mut provider = ResidentProvider::new();
        provider.set_helper(Some("Z:\\no-such-provider.exe".to_string())).unwrap();
        let error = provider.read().unwrap_err();
        assert!(error.contains("spawn failed") || error.contains("provider"));
        assert!(error.contains("provider initialization failed"), "startup failure must be distinguishable from an established provider read failure");
        // The failure must count toward the consecutive-failure budget.
        assert_eq!(provider.consecutive_failures, 1);
    }

    #[test]
    fn policy_refusals_do_not_consume_failure_budget_and_reset_restores_state() {
        let mut provider = ResidentProvider::new();
        provider.set_helper(Some("Z:\\no-such-provider.exe".into())).unwrap();
        for failure in 1..=MAX_CONSECUTIVE_FAILURES {
            provider.last_spawn = Some(Instant::now() - RESTART_COOLDOWN);
            assert!(provider.read().unwrap_err().contains("initialization failed"));
            assert_eq!(provider.consecutive_failures, failure);
            let refusal = provider.read().unwrap_err();
            assert!(refusal.contains(if failure == MAX_CONSECUTIVE_FAILURES { "gave up after" } else { "cooldown" }));
            assert_eq!(provider.consecutive_failures, failure);
        }
        assert!(provider.last_initialization_error.is_some());
        assert!(provider.last_read_error.is_none());
        provider.force_reset().unwrap();
        assert!(!provider.suspended);
        assert_eq!(provider.consecutive_failures, 0);
        assert!(provider.last_initialization_error.is_none());
    }

    #[test]
    #[cfg(windows)]
    #[ignore = "requires explicitly compiled isolated fake provider"]
    fn controlled_initialization_read_failure_and_shutdown() {
        use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE};
        use windows_sys::Win32::Foundation::CloseHandle;
        let fixture = std::path::PathBuf::from(std::env::var("FRAMESCOPE_PROVIDER_TEST_HELPER").expect("isolated fake provider path"));
        let root = fixture.parent().unwrap();
        for mode in ["slow-init", "invalid-init", "invalid-read", "hang-exit"] {
            let path = root.join(format!("{mode}.exe"));
            std::fs::copy(&fixture, &path).unwrap();
            let mut provider = ResidentProvider::new();
            provider.set_helper(Some(path.to_string_lossy().into_owned())).unwrap();
            let started = Instant::now();
            let first = provider.read();
            if mode == "invalid-init" {
                assert!(first.unwrap_err().contains("initialization failed"));
                assert!(provider.last_initialization_error.is_some());
                assert!(provider.last_read_error.is_none());
                assert!(provider.proc.is_none());
                continue;
            }
            let pid = provider.proc.as_ref().unwrap()._child.id();
            let handle = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, 0, pid) };
            assert!(!handle.is_null());
            let subsequent = provider.read();
            let stopped = provider.kill();
            let exited = unsafe { WaitForSingleObject(handle, 1000) };
            unsafe { CloseHandle(handle); }
            assert!(first.is_ok());
            assert!(stopped.is_ok());
            assert_eq!(exited, 0, "owned provider must actually exit before cleanup succeeds");
            assert!(provider.proc.is_none());
            if mode == "slow-init" { assert!(started.elapsed() >= READ_TIMEOUT); }
            if mode == "invalid-read" {
                assert!(subsequent.unwrap_err().contains("provider read failed"));
                assert!(provider.last_read_error.is_some());
                assert!(provider.last_initialization_error.is_none());
            } else { assert!(subsequent.is_ok()); }
        }
    }
}
