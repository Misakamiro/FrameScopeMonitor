//! One-shot UIAccess startup before windows or application ownership exist.

pub(crate) const RELAY_MARKER: &str = "--framescope-uiaccess-relay";

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) struct TokenState {
    pub elevated: bool,
    pub uiaccess: bool,
    pub user: Vec<u8>,
    pub session: u32,
}

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum Action {
    Continue,
    Relaunched,
}

pub(crate) trait Runtime {
    fn current(&mut self) -> Result<TokenState, String>;
    fn prepare(&mut self) -> Result<TokenState, String>;
    fn relaunch(&mut self) -> Result<(), String>;
}

pub(crate) fn bootstrap(runtime: &mut dyn Runtime, args: &[String]) -> Result<Action, String> {
    if args.iter().any(|arg| arg == "--request-exit") {
        return Ok(Action::Continue);
    }
    let current = runtime.current()?;
    if !current.elevated {
        return Err("Normal UAC administrator approval is required".into());
    }
    if current.uiaccess {
        return Ok(Action::Continue);
    }
    if args.iter().any(|arg| arg == RELAY_MARKER) {
        return Err(
            "UIAccess relaunch did not obtain the required token; no further relaunch".into(),
        );
    }
    let prepared = runtime.prepare()?;
    if prepared.user != current.user || prepared.session != current.session {
        return Err("Prepared token changed the current user/session identity".into());
    }
    if !prepared.uiaccess || !prepared.elevated {
        return Err("Prepared token lacks required UIAccess/administrator flags".into());
    }
    runtime.relaunch()?;
    Ok(Action::Relaunched)
}

#[cfg(windows)]
pub(crate) mod windows {
    use super::*;
    use std::{
        ffi::c_void,
        mem::{size_of, zeroed},
        os::windows::ffi::OsStrExt,
        ptr::{null, null_mut},
    };
    use windows_sys::Win32::{
        Foundation::{
            CloseHandle, GetLastError, SetLastError, ERROR_NOT_ALL_ASSIGNED, ERROR_NO_TOKEN,
            HANDLE, INVALID_HANDLE_VALUE,
        },
        Security::*,
        System::{
            Diagnostics::ToolHelp::*, Environment::GetCommandLineW,
            SystemInformation::GetSystemDirectoryW, Threading::*,
        },
    };

    struct Handle(HANDLE);
    impl Handle {
        fn new(raw: HANDLE, operation: &str) -> Result<Self, String> {
            if raw.is_null() || raw == INVALID_HANDLE_VALUE {
                Err(error(operation))
            } else {
                Ok(Self(raw))
            }
        }
    }
    impl Drop for Handle {
        fn drop(&mut self) {
            unsafe {
                CloseHandle(self.0);
            }
        }
    }
    fn error(operation: &str) -> String {
        format!("{operation}: {}", std::io::Error::last_os_error())
    }
    fn wide(value: &std::ffi::OsStr) -> Vec<u16> {
        value.encode_wide().chain(Some(0)).collect()
    }
    fn checked(value: i32, operation: &str) -> Result<(), String> {
        if value == 0 {
            Err(error(operation))
        } else {
            Ok(())
        }
    }
    fn token(process: HANDLE, access: u32) -> Result<Handle, String> {
        let mut raw = null_mut();
        checked(
            unsafe { OpenProcessToken(process, access, &mut raw) },
            "OpenProcessToken",
        )?;
        Handle::new(raw, "OpenProcessToken")
    }
    fn number(token: HANDLE, class: TOKEN_INFORMATION_CLASS) -> Result<u32, String> {
        let mut value = 0u32;
        let mut length = 0;
        checked(
            unsafe { GetTokenInformation(token, class, &mut value as *mut _ as _, 4, &mut length) },
            "GetTokenInformation",
        )?;
        if length != 4 {
            return Err("Unexpected token field size".into());
        }
        Ok(value)
    }
    fn user_buffer(token: HANDLE) -> Result<Vec<usize>, String> {
        let mut length = 0;
        unsafe {
            GetTokenInformation(token, TokenUser, null_mut(), 0, &mut length);
        }
        if length < size_of::<TOKEN_USER>() as u32 || length > 4096 {
            return Err(error("TokenUser size"));
        }
        let mut buffer =
            vec![0usize; (length as usize + size_of::<usize>() - 1) / size_of::<usize>()];
        checked(
            unsafe {
                GetTokenInformation(
                    token,
                    TokenUser,
                    buffer.as_mut_ptr() as _,
                    length,
                    &mut length,
                )
            },
            "TokenUser",
        )?;
        Ok(buffer)
    }
    fn state(token: HANDLE) -> Result<TokenState, String> {
        let buffer = user_buffer(token)?;
        let sid = unsafe { (*(buffer.as_ptr() as *const TOKEN_USER)).User.Sid };
        if unsafe { IsValidSid(sid) } == 0 {
            return Err("Invalid token user SID".into());
        }
        let length = unsafe { GetLengthSid(sid) } as usize;
        let begin = buffer.as_ptr() as usize;
        let end = begin + buffer.len() * size_of::<usize>();
        if (sid as usize) < begin
            || (sid as usize)
                .checked_add(length)
                .is_none_or(|last| last > end)
        {
            return Err("Token user SID escapes the query buffer".into());
        }
        Ok(TokenState {
            elevated: number(token, TokenElevation)? != 0,
            uiaccess: number(token, TokenUIAccess)? != 0,
            user: unsafe { std::slice::from_raw_parts(sid as *const u8, length) }.to_vec(),
            session: number(token, TokenSessionId)?,
        })
    }
    pub(crate) fn current_state() -> Result<TokenState, String> {
        state(token(unsafe { GetCurrentProcess() }, TOKEN_QUERY)?.0)
    }

    struct DebugPrivilege {
        token: Handle,
        previous: TOKEN_PRIVILEGES,
    }
    impl DebugPrivilege {
        fn enable() -> Result<Self, String> {
            let token = token(
                unsafe { GetCurrentProcess() },
                TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES,
            )?;
            let mut requested: TOKEN_PRIVILEGES = unsafe { zeroed() };
            requested.PrivilegeCount = 1;
            let name = wide(std::ffi::OsStr::new("SeDebugPrivilege"));
            checked(
                unsafe {
                    LookupPrivilegeValueW(null(), name.as_ptr(), &mut requested.Privileges[0].Luid)
                },
                "Lookup SeDebugPrivilege",
            )?;
            requested.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
            let mut previous = unsafe { zeroed() };
            let mut length = 0;
            unsafe {
                SetLastError(0);
            }
            checked(
                unsafe {
                    AdjustTokenPrivileges(
                        token.0,
                        0,
                        &requested,
                        size_of::<TOKEN_PRIVILEGES>() as u32,
                        &mut previous,
                        &mut length,
                    )
                },
                "Enable existing SeDebugPrivilege",
            )?;
            let guard = Self { token, previous };
            if unsafe { GetLastError() } == ERROR_NOT_ALL_ASSIGNED {
                return Err("Administrator token lacks SeDebugPrivilege".into());
            }
            Ok(guard)
        }
    }
    impl Drop for DebugPrivilege {
        fn drop(&mut self) {
            if self.previous.PrivilegeCount != 0
                && unsafe {
                    AdjustTokenPrivileges(
                        self.token.0,
                        0,
                        &self.previous,
                        0,
                        null_mut(),
                        null_mut(),
                    )
                } == 0
            {
                eprintln!("Could not restore the startup privilege; terminating startup");
                std::process::abort();
            }
        }
    }
    struct Impersonation;
    impl Impersonation {
        fn begin(token: HANDLE) -> Result<Self, String> {
            let mut existing = null_mut();
            if unsafe { OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, 1, &mut existing) } != 0 {
                drop(Handle::new(existing, "Existing thread token")?);
                return Err(
                    "Startup thread is already impersonating; preserving its context".into(),
                );
            }
            if unsafe { GetLastError() } != ERROR_NO_TOKEN {
                return Err(error("OpenThreadToken"));
            }
            checked(
                unsafe { SetThreadToken(null(), token) },
                "Set temporary startup context",
            )?;
            Ok(Self)
        }
    }
    impl Drop for Impersonation {
        fn drop(&mut self) {
            if unsafe { RevertToSelf() } == 0 {
                eprintln!("Could not restore the startup thread identity; terminating startup");
                std::process::abort();
            }
        }
    }
    fn startup_context(session: u32) -> Result<Handle, String> {
        let _privilege = DebugPrivilege::enable()?;
        let mut directory = vec![0u16; 32768];
        let length =
            unsafe { GetSystemDirectoryW(directory.as_mut_ptr(), directory.len() as u32) } as usize;
        if length == 0 || length >= directory.len() {
            return Err(error("GetSystemDirectoryW"));
        }
        let expected = format!(
            "{}\\winlogon.exe",
            String::from_utf16_lossy(&directory[..length])
        );
        let snapshot = Handle::new(
            unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0) },
            "Process snapshot",
        )?;
        let mut entry: PROCESSENTRY32W = unsafe { zeroed() };
        entry.dwSize = size_of::<PROCESSENTRY32W>() as u32;
        let mut present = unsafe { Process32FirstW(snapshot.0, &mut entry) } != 0;
        while present {
            let name_end = entry
                .szExeFile
                .iter()
                .position(|v| *v == 0)
                .unwrap_or(entry.szExeFile.len());
            if String::from_utf16_lossy(&entry.szExeFile[..name_end])
                .eq_ignore_ascii_case("winlogon.exe")
            {
                if let Ok(process) = Handle::new(
                    unsafe {
                        OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, entry.th32ProcessID)
                    },
                    "Open session process",
                ) {
                    let mut image = vec![0u16; 32768];
                    let mut image_len = image.len() as u32;
                    if unsafe {
                        QueryFullProcessImageNameW(process.0, 0, image.as_mut_ptr(), &mut image_len)
                    } != 0
                        && String::from_utf16_lossy(&image[..image_len as usize])
                            .eq_ignore_ascii_case(&expected)
                    {
                        if let Ok(donor) = token(process.0, TOKEN_QUERY | TOKEN_DUPLICATE) {
                            let user = user_buffer(donor.0)?;
                            let sid = unsafe { (*(user.as_ptr() as *const TOKEN_USER)).User.Sid };
                            if number(donor.0, TokenSessionId)? == session
                                && unsafe { IsWellKnownSid(sid, WinLocalSystemSid) } != 0
                            {
                                let mut privilege: PRIVILEGE_SET = unsafe { zeroed() };
                                // PRIVILEGE_SET_ALL_NECESSARY from the Windows SDK.
                                privilege.PrivilegeCount = 1;
                                privilege.Control = 1;
                                let name = wide(std::ffi::OsStr::new("SeTcbPrivilege"));
                                let mut enabled = 0;
                                checked(
                                    unsafe {
                                        LookupPrivilegeValueW(
                                            null(),
                                            name.as_ptr(),
                                            &mut privilege.Privilege[0].Luid,
                                        )
                                    },
                                    "Lookup SeTcbPrivilege",
                                )?;
                                checked(
                                    unsafe {
                                        PrivilegeCheck(donor.0, &mut privilege, &mut enabled)
                                    },
                                    "Check startup privilege",
                                )?;
                                if enabled != 0 {
                                    let mut raw = null_mut();
                                    checked(
                                        unsafe {
                                            DuplicateTokenEx(
                                                donor.0,
                                                TOKEN_IMPERSONATE | TOKEN_QUERY,
                                                null(),
                                                SecurityImpersonation,
                                                TokenImpersonation,
                                                &mut raw,
                                            )
                                        },
                                        "Duplicate startup context",
                                    )?;
                                    return Handle::new(raw, "Duplicate startup context");
                                }
                            }
                        }
                    }
                }
            }
            present = unsafe { Process32NextW(snapshot.0, &mut entry) } != 0;
        }
        Err("No accessible same-session system startup context with SeTcbPrivilege".into())
    }

    #[derive(Default)]
    pub(crate) struct NativeRuntime {
        prepared: Option<Handle>,
    }
    impl Runtime for NativeRuntime {
        fn current(&mut self) -> Result<TokenState, String> {
            current_state()
        }
        fn prepare(&mut self) -> Result<TokenState, String> {
            let original = token(
                unsafe { GetCurrentProcess() },
                TOKEN_QUERY | TOKEN_DUPLICATE,
            )?;
            // The temporary debug privilege is restored before copying the user token.
            let context = startup_context(number(original.0, TokenSessionId)?)?;
            let prepared = {
                let _context = Impersonation::begin(context.0)?;
                let mut raw = null_mut();
                checked(
                    unsafe {
                        DuplicateTokenEx(
                            original.0,
                            TOKEN_QUERY
                                | TOKEN_DUPLICATE
                                | TOKEN_ASSIGN_PRIMARY
                                | TOKEN_ADJUST_DEFAULT,
                            null(),
                            SecurityAnonymous,
                            TokenPrimary,
                            &mut raw,
                        )
                    },
                    "Copy current user token",
                )?;
                let token = Handle::new(raw, "Copy current user token")?;
                let enabled = 1u32;
                checked(
                    unsafe {
                        SetTokenInformation(
                            token.0,
                            TokenUIAccess,
                            &enabled as *const _ as *const c_void,
                            4,
                        )
                    },
                    "Set copied token UIAccess",
                )?;
                token
            };
            let result = state(prepared.0)?;
            self.prepared = Some(prepared);
            Ok(result)
        }
        fn relaunch(&mut self) -> Result<(), String> {
            let prepared = self.prepared.take().ok_or("No prepared UIAccess token")?;
            let expected = state(prepared.0)?;
            let exe = wide(
                std::env::current_exe()
                    .map_err(|e| e.to_string())?
                    .as_os_str(),
            );
            let cwd = wide(
                std::env::current_dir()
                    .map_err(|e| e.to_string())?
                    .as_os_str(),
            );
            let raw = unsafe { GetCommandLineW() };
            if raw.is_null() {
                return Err("Missing process command line".into());
            }
            let mut command = Vec::new();
            for index in 0..32768 {
                let value = unsafe { *raw.add(index) };
                if value == 0 {
                    break;
                }
                command.push(value);
            }
            if command.len() >= 32768 {
                return Err("Process command line exceeds Windows limit".into());
            }
            command.extend(format!(" {RELAY_MARKER}").encode_utf16());
            command.push(0);
            if command.len() > 32768 {
                return Err("UIAccess relaunch command line exceeds Windows limit".into());
            }
            let mut desktop = wide(std::ffi::OsStr::new("winsta0\\default"));
            let mut startup: STARTUPINFOW = unsafe { zeroed() };
            startup.cb = size_of::<STARTUPINFOW>() as u32;
            startup.lpDesktop = desktop.as_mut_ptr();
            let mut child: PROCESS_INFORMATION = unsafe { zeroed() };
            checked(
                unsafe {
                    CreateProcessAsUserW(
                        prepared.0,
                        exe.as_ptr(),
                        command.as_mut_ptr(),
                        null(),
                        null(),
                        0,
                        CREATE_SUSPENDED,
                        null(),
                        cwd.as_ptr(),
                        &startup,
                        &mut child,
                    )
                },
                "Create same-user UIAccess child",
            )?;
            let process = Handle::new(child.hProcess, "Child process")?;
            let thread = Handle::new(child.hThread, "Child thread")?;
            let result = (|| {
                let actual = state(token(process.0, TOKEN_QUERY)?.0)?;
                if actual != expected {
                    return Err("Created child token differs from verified startup identity".into());
                }
                if unsafe { ResumeThread(thread.0) } == u32::MAX {
                    return Err(error("Resume verified UIAccess child"));
                }
                Ok(())
            })();
            if result.is_err() {
                // Only the suspended child just created by this call is terminated.
                unsafe {
                    TerminateProcess(process.0, 1);
                    WaitForSingleObject(process.0, 3000);
                }
            }
            result
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    struct Fake {
        current: TokenState,
        prepared: Result<TokenState, String>,
        launch: Result<(), String>,
        calls: Vec<&'static str>,
    }
    fn state() -> TokenState {
        TokenState {
            elevated: true,
            uiaccess: false,
            user: vec![1, 2, 3],
            session: 1,
        }
    }
    fn fixture() -> Fake {
        let mut prepared = state();
        prepared.uiaccess = true;
        Fake {
            current: state(),
            prepared: Ok(prepared),
            launch: Ok(()),
            calls: vec![],
        }
    }
    impl Runtime for Fake {
        fn current(&mut self) -> Result<TokenState, String> {
            self.calls.push("current");
            Ok(self.current.clone())
        }
        fn prepare(&mut self) -> Result<TokenState, String> {
            self.calls.push("prepare");
            self.prepared.clone()
        }
        fn relaunch(&mut self) -> Result<(), String> {
            self.calls.push("relaunch");
            self.launch.clone()
        }
    }
    #[test]
    fn elevated_user_relaunches_once_with_prepared_uiaccess() {
        let mut runtime = fixture();
        assert_eq!(bootstrap(&mut runtime, &[]).unwrap(), Action::Relaunched);
        assert_eq!(runtime.calls, ["current", "prepare", "relaunch"]);
    }
    #[test]
    fn existing_uiaccess_does_not_touch_system_tokens_or_relaunch() {
        let mut runtime = fixture();
        runtime.current.uiaccess = true;
        assert_eq!(
            bootstrap(&mut runtime, &[RELAY_MARKER.into()]).unwrap(),
            Action::Continue
        );
        assert_eq!(runtime.calls, ["current"]);
    }
    #[test]
    fn exit_control_never_acquires_tokens() {
        let mut runtime = fixture();
        assert_eq!(
            bootstrap(&mut runtime, &["--request-exit".into()]).unwrap(),
            Action::Continue
        );
        assert!(runtime.calls.is_empty());
    }
    #[test]
    fn normal_uac_is_required_before_preparation() {
        let mut runtime = fixture();
        runtime.current.elevated = false;
        assert!(bootstrap(&mut runtime, &[])
            .unwrap_err()
            .contains("administrator"));
        assert_eq!(runtime.calls, ["current"]);
    }
    #[test]
    fn unsuccessful_child_cannot_relaunch_recursively() {
        let mut runtime = fixture();
        assert!(bootstrap(&mut runtime, &[RELAY_MARKER.into()])
            .unwrap_err()
            .contains("relaunch"));
        assert_eq!(runtime.calls, ["current"]);
    }
    #[test]
    fn a_prepared_token_must_preserve_user_and_session() {
        for change_user in [true, false] {
            let mut runtime = fixture();
            let token = runtime.prepared.as_mut().unwrap();
            if change_user {
                token.user = vec![9];
            } else {
                token.session = 2;
            }
            assert!(bootstrap(&mut runtime, &[])
                .unwrap_err()
                .contains("identity"));
            assert_eq!(runtime.calls, ["current", "prepare"]);
        }
    }
    #[test]
    fn a_prepared_token_must_have_both_required_flags() {
        for change_uiaccess in [true, false] {
            let mut runtime = fixture();
            let token = runtime.prepared.as_mut().unwrap();
            if change_uiaccess {
                token.uiaccess = false;
            } else {
                token.elevated = false;
            }
            assert!(bootstrap(&mut runtime, &[]).unwrap_err().contains("flags"));
            assert_eq!(runtime.calls, ["current", "prepare"]);
        }
    }
    #[test]
    fn token_preparation_error_is_not_a_normal_gui_start() {
        let mut runtime = fixture();
        runtime.prepared = Err("controlled preparation failure".into());
        assert_eq!(
            bootstrap(&mut runtime, &[]).unwrap_err(),
            "controlled preparation failure"
        );
        assert_eq!(runtime.calls, ["current", "prepare"]);
    }
    #[test]
    fn failed_child_creation_is_not_reported_as_relaunched() {
        let mut runtime = fixture();
        runtime.launch = Err("controlled creation failure".into());
        assert_eq!(
            bootstrap(&mut runtime, &[]).unwrap_err(),
            "controlled creation failure"
        );
    }
}
