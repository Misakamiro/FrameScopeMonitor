//! Small platform helpers shared by the monitoring modules (the NexBox port
//! expects a `crate::optimization::is_admin`).

/// True when the current process token has the Administrators group enabled.
#[cfg(windows)]
pub fn is_admin() -> bool {
    use windows_sys::Win32::UI::Shell::IsUserAnAdmin;
    unsafe { IsUserAnAdmin() != 0 }
}

#[cfg(not(windows))]
pub fn is_admin() -> bool {
    false
}

#[cfg(test)]
mod tests {
    #[test]
    fn is_admin_never_panics() {
        let _ = super::is_admin();
    }
}
