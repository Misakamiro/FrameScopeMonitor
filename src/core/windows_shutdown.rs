/// Windows can still dispatch timers and pipe failures while ending a session.
/// Starting another hardware process at that point can fail DLL initialization.
pub fn system_shutting_down() -> bool {
    #[cfg(windows)]
    unsafe { return windows_sys::Win32::UI::WindowsAndMessaging::GetSystemMetrics(0x2000) != 0; }
    #[cfg(not(windows))]
    false
}

pub fn may_start_process(closing: bool, system_shutdown: bool) -> bool {
    !closing && !system_shutdown
}

#[cfg(test)]
mod tests {
    #[test]
    fn shutdown_never_allows_a_replacement_process() {
        assert!(super::may_start_process(false, false));
        for state in [(true, false), (false, true), (true, true)] {
            assert!(!super::may_start_process(state.0, state.1));
        }
    }
}
