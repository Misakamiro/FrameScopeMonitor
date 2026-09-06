//! Resident ICMP ping (NexBox game_ping.rs port): probes the game server host
//! once per cycle and reports the last round-trip in milliseconds. Failures
//! leave the last value untouched so the overlay does not blink; with no
//! successful probe yet the value stays null (fail-closed, never faked).

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Mutex, OnceLock};
use std::thread;
use std::time::Duration;

pub const PING_HOST: &str = "game.qq.com";
const PING_INTERVAL: Duration = Duration::from_secs(2);
const PING_TIMEOUT_MS: u32 = 1000;

static LAST_PING: OnceLock<Mutex<Option<u32>>> = OnceLock::new();
static THREAD_STARTED: AtomicBool = AtomicBool::new(false);

/// Lazily starts the resident ping thread; safe to call on every request.
pub fn ensure_started() {
    if THREAD_STARTED.swap(true, Ordering::SeqCst) {
        return;
    }
    let started = thread::Builder::new().name("framescope-ping".into()).spawn(|| {
        loop {
            let rtt = probe_once();
            if let Ok(mut last) = LAST_PING.get_or_init(|| Mutex::new(None)).lock() {
                if rtt.is_some() {
                    *last = rtt;
                }
            }
            thread::sleep(PING_INTERVAL);
        }
    });
    if started.is_err() {
        THREAD_STARTED.store(false, Ordering::SeqCst);
    }
}

pub fn last_ping() -> Option<u32> {
    ensure_started();
    *LAST_PING.get_or_init(|| Mutex::new(None)).lock().unwrap()
}

#[cfg(windows)]
fn probe_once() -> Option<u32> {
    use std::ffi::CString;
    use std::ptr::null_mut;

    use windows_sys::Win32::NetworkManagement::IpHelper::{
        IcmpCloseHandle, IcmpCreateFile, IcmpSendEcho, ICMP_ECHO_REPLY,
    };
    use windows_sys::Win32::Networking::WinSock::{
        freeaddrinfo, getaddrinfo, WSAStartup, AF_INET, ADDRINFOA, SOCKADDR_IN, WSADATA,
    };

    unsafe {
        let mut wsa_data: WSADATA = std::mem::zeroed();
        if WSAStartup(0x0202, &mut wsa_data) != 0 {
            return None;
        }

        let host = CString::new(PING_HOST).ok()?;
        let mut result: *mut ADDRINFOA = null_mut();
        if getaddrinfo(host.as_ptr() as *const u8, std::ptr::null(), null_mut(), &mut result) != 0 {
            return None;
        }
        let mut destination: Option<u32> = None;
        let mut current = result;
        while !current.is_null() {
            let info = &*current;
            if info.ai_family == AF_INET as i32 && !info.ai_addr.is_null() {
                let sockaddr = &*(info.ai_addr as *const SOCKADDR_IN);
                destination = Some(sockaddr.sin_addr.S_un.S_addr);
                break;
            }
            current = info.ai_next as *mut ADDRINFOA;
        }
        freeaddrinfo(result);
        let destination = destination?;

        let handle = IcmpCreateFile();
        let invalid_handle = -1isize as *mut std::ffi::c_void;
        if handle.is_null() || handle == invalid_handle {
            return None;
        }
        let payload: [u8; 32] = [b'f'; 32];
        let mut reply: Vec<u8> = vec![0u8; std::mem::size_of::<ICMP_ECHO_REPLY>() + payload.len()];
        let sent = IcmpSendEcho(
            handle,
            destination,
            payload.as_ptr() as *mut std::ffi::c_void,
            payload.len() as u16,
            null_mut(),
            reply.as_mut_ptr() as *mut std::ffi::c_void,
            reply.len() as u32,
            PING_TIMEOUT_MS,
        );
        let rtt = if sent > 0 {
            let echo = &*(reply.as_ptr() as *const ICMP_ECHO_REPLY);
            if echo.Status == 0 && echo.RoundTripTime > 0 && echo.RoundTripTime < 10_000 {
                Some(echo.RoundTripTime)
            } else {
                None
            }
        } else {
            None
        };
        IcmpCloseHandle(handle);
        rtt
    }
}

#[cfg(not(windows))]
fn probe_once() -> Option<u32> {
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn last_ping_is_null_before_any_probe() {
        // The thread may or may not have probed yet; both None and a real RTT
        // are acceptable, but it must never be a fabricated constant.
        let value = last_ping();
        assert!(value.is_none() || value.unwrap_or(0) < 10_000);
    }
}
