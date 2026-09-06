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
pub const RESTART_COOLDOWN: Duration = Duration::from_secs(30);
pub const MAX_CONSECUTIVE_FAILURES: u32 = 5;

pub fn stderr_log_path() -> std::path::PathBuf {
    std::env::temp_dir().join("framescope-provider-stderr.log")
}

pub struct ResidentProvider {
    helper_path: Option<String>,
    proc: Option<ProviderProc>,
    consecutive_failures: u32,
    last_spawn: Option<Instant>,
    suspended: bool,
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
        }
    }

    /// Points the manager at a helper executable; respawns when the path changes.
    pub fn set_helper(&mut self, path: Option<String>) {
        if self.helper_path == path {
            return;
        }
        self.kill();
        self.helper_path = path;
        self.consecutive_failures = 0;
        self.suspended = false;
    }

    pub fn helper_path(&self) -> Option<&str> {
        self.helper_path.as_deref()
    }

    /// One pull-mode snapshot read (`{"cmd":"read"}` → one payload line).
    pub fn read(&mut self) -> Result<Value, String> {
        self.exchange("{\"cmd\":\"read\"}")
    }

    /// Sends a raw command line (e.g. `{"cmd":"static"}`) and awaits one payload.
    pub fn send_raw(&mut self, command: &str) -> Result<Value, String> {
        self.exchange(command)
    }

    fn exchange(&mut self, command: &str) -> Result<Value, String> {
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
                    || error.contains("cooldown");
                if !policy_error {
                    self.kill();
                    self.last_spawn = Some(Instant::now());
                    self.consecutive_failures += 1;
                    if self.consecutive_failures >= MAX_CONSECUTIVE_FAILURES {
                        self.suspended = true;
                    }
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
            if let Some(last_spawn) = self.last_spawn {
                if last_spawn.elapsed() < RESTART_COOLDOWN {
                    return Err("provider is restarting (cooldown)".to_string());
                }
            }
            self.proc = Some(ProviderProc::spawn(&path)?);
            self.last_spawn = Some(Instant::now());
        }
        let proc = self.proc.as_mut().expect("provider spawned");
        proc.stdin
            .write_all(format!("{command}\n").as_bytes())
            .and_then(|_| proc.stdin.flush())
            .map_err(|error| format!("provider write failed: {error}"))?;
        let line = match proc.lines.recv_timeout(READ_TIMEOUT) {
            Ok(line) => line,
            Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {
                let status = proc._child.try_wait().ok().flatten();
                return Err(match status {
                    Some(code) => format!("provider exited while reading (status: {code})"),
                    None => "provider read timed out (8s); provider still running".to_string(),
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
        serde_json::from_str::<Value>(clean)
            .map_err(|error| format!("provider returned invalid JSON: {error}"))
    }

    /// Kills the resident provider; the next exchange respawns it.
    pub fn kill(&mut self) {
        if let Some(mut proc) = self.proc.take() {
            let _ = proc.stdin.write_all(b"{\"cmd\":\"exit\"}\n");
            let _ = proc.stdin.flush();
            let _ = proc._child.kill();
            let _ = proc._child.wait();
        }
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
    fn read_fails_closed_without_helper() {
        let mut provider = ResidentProvider::new();
        let error = provider.read().unwrap_err();
        assert!(error.contains("not configured"));
        assert!(!provider.suspended);
    }

    #[test]
    fn read_fails_closed_for_blank_helper() {
        let mut provider = ResidentProvider::new();
        provider.set_helper(Some("  ".to_string()));
        let error = provider.read().unwrap_err();
        assert!(error.contains("not configured"));
    }

    #[test]
    fn missing_helper_reports_spawn_failure() {
        let mut provider = ResidentProvider::new();
        provider.set_helper(Some("Z:\\no-such-provider.exe".to_string()));
        let error = provider.read().unwrap_err();
        assert!(error.contains("spawn failed") || error.contains("provider"));
        // The failure must count toward the consecutive-failure budget.
        assert_eq!(provider.consecutive_failures, 1);
    }
}
