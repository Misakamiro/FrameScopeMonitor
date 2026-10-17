//! Bounded report execution. Pipes are polled by their sole reader; no drain thread can outlive a request.
use std::{io::Read, process::{Command, ExitStatus, Stdio}, time::{Duration, Instant}};

#[cfg(windows)]
pub fn run(mut command: Command, timeout: Duration) -> Result<(ExitStatus, Vec<u8>, Vec<u8>), String> {
    use std::os::windows::{io::AsRawHandle, process::CommandExt};
    use windows_sys::Win32::{Foundation::*, System::{Diagnostics::ToolHelp::*, JobObjects::*, Pipes::PeekNamedPipe, Threading::*}};
    struct Handle(HANDLE);
    impl Drop for Handle { fn drop(&mut self) { unsafe { CloseHandle(self.0); } } }
    fn error(operation: &str) -> String { format!("{operation}: {}", std::io::Error::last_os_error()) }
    fn drain<R: Read + AsRawHandle>(reader: &mut R, output: &mut Vec<u8>) -> Result<bool, String> {
        const LIMIT: usize = 1024 * 1024;
        let mut available = 0;
        if unsafe { PeekNamedPipe(reader.as_raw_handle(), std::ptr::null_mut(), 0, std::ptr::null_mut(), &mut available, std::ptr::null_mut()) } == 0 {
            return if unsafe { GetLastError() } == ERROR_BROKEN_PIPE { Ok(true) } else { Err(error("report pipe inspection failed")) };
        }
        // One bounded read per poll keeps both streams and the deadline responsive even with continuous output.
        if available > 0 {
            let mut buffer = [0u8; 16384];
            let requested = (available as usize).min(buffer.len());
            let count = reader.read(&mut buffer[..requested]).map_err(|e| format!("report pipe read failed: {e}"))?;
            if output.len() + count > LIMIT { output.drain(..output.len() + count - LIMIT); }
            output.extend_from_slice(&buffer[..count]);
        }
        Ok(false)
    }
    let started = Instant::now();
    let deadline = started.checked_add(timeout).ok_or("invalid report timeout")?;
    if timeout.is_zero() { return Err("报告生成超时（总期限为零）".into()); }
    // Cleanup is part of the supplied total budget, rather than an additional wait after timeout.
    let cleanup_budget = Duration::from_millis(1000).min(timeout / 4);
    let work_deadline = deadline - cleanup_budget;
    struct Job { handle: Handle, deadline: Instant }
    impl Drop for Job {
        fn drop(&mut self) {
            // Early error paths have the same ownership protection as the normal cleanup path.
            unsafe { TerminateJobObject(self.handle.0, 1); }
            while Instant::now() < self.deadline {
                let mut accounting: JOBOBJECT_BASIC_ACCOUNTING_INFORMATION = unsafe { std::mem::zeroed() };
                if unsafe { QueryInformationJobObject(self.handle.0, JobObjectBasicAccountingInformation, &mut accounting as *mut _ as _, std::mem::size_of_val(&accounting) as u32, std::ptr::null_mut()) } == 0 || accounting.ActiveProcesses == 0 { break; }
                std::thread::sleep(Duration::from_millis(2));
            }
        }
    }
    let job_owner = Job { handle: Handle(unsafe { CreateJobObjectW(std::ptr::null(), std::ptr::null()) }), deadline };
    let job = &job_owner.handle;
    if job.0.is_null() { return Err(error("report job creation failed")); }
    let mut limits: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { std::mem::zeroed() };
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if unsafe { SetInformationJobObject(job.0, JobObjectExtendedLimitInformation, &limits as *const _ as _, std::mem::size_of_val(&limits) as u32) } == 0 { return Err(error("report job configuration failed")); }
    command.stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::piped()).creation_flags(CREATE_NO_WINDOW | CREATE_SUSPENDED);
    let mut child = command.spawn().map_err(|e| format!("report process start failed: {e}"))?;
    // The primary thread remains suspended until membership is established, so no descendant can escape the job.
    if unsafe { AssignProcessToJobObject(job.0, child.as_raw_handle()) } == 0 {
        let failure = error("report job assignment failed");
        let _ = child.kill();
        while Instant::now() < deadline {
            if child.try_wait().ok().flatten().is_some() { break; }
            std::thread::sleep(Duration::from_millis(2));
        }
        return Err(failure);
    }
    let resume = (|| -> Result<(), String> {
        let snapshot = Handle(unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0) });
        if snapshot.0 == INVALID_HANDLE_VALUE { return Err(error("report thread enumeration failed")); }
        let mut entry: THREADENTRY32 = unsafe { std::mem::zeroed() };
        entry.dwSize = std::mem::size_of_val(&entry) as u32;
        let mut more = unsafe { Thread32First(snapshot.0, &mut entry) } != 0;
        while more {
            if entry.th32OwnerProcessID == child.id() {
                let thread = Handle(unsafe { OpenThread(THREAD_SUSPEND_RESUME, 0, entry.th32ThreadID) });
                if thread.0.is_null() { return Err(error("report thread open failed")); }
                if unsafe { ResumeThread(thread.0) } == u32::MAX { return Err(error("report thread resume failed")); }
                return Ok(());
            }
            more = unsafe { Thread32Next(snapshot.0, &mut entry) } != 0;
        }
        Err("report primary thread not found".into())
    })();
    resume?;
    let mut stdout = child.stdout.take().ok_or("report stdout unavailable")?;
    let mut stderr = child.stderr.take().ok_or("report stderr unavailable")?;
    let mut out = Vec::new();
    let mut err = Vec::new();
    let mut status = None;
    let mut failure = None;
    loop {
        if Instant::now() >= work_deadline { failure = Some("报告生成超时（总期限包含子进程清理）".to_string()); break; }
        if let Err(e) = drain(&mut stdout, &mut out).and_then(|_| drain(&mut stderr, &mut err)) { failure = Some(e); break; }
        match child.try_wait() { Ok(Some(exit)) => { status = Some(exit); break; }, Ok(None) => {}, Err(e) => { failure = Some(format!("report process wait failed: {e}")); break; } }
        std::thread::sleep(Duration::from_millis(5));
    }
    // Also terminate descendants after normal/nonzero parent exit: inherited pipes must not prolong the operation.
    if unsafe { TerminateJobObject(job.0, 1) } == 0 { return Err(error("report process tree cleanup failed")); }
    let mut out_done = false;
    let mut err_done = false;
    loop {
        if Instant::now() >= deadline { return Err("报告生成进程清理超过总期限".into()); }
        if !out_done { out_done = drain(&mut stdout, &mut out)?; }
        if !err_done { err_done = drain(&mut stderr, &mut err)?; }
        let mut accounting: JOBOBJECT_BASIC_ACCOUNTING_INFORMATION = unsafe { std::mem::zeroed() };
        if unsafe { QueryInformationJobObject(job.0, JobObjectBasicAccountingInformation, &mut accounting as *mut _ as _, std::mem::size_of_val(&accounting) as u32, std::ptr::null_mut()) } == 0 { return Err(error("report process tree verification failed")); }
        if accounting.ActiveProcesses == 0 && out_done && err_done { break; }
        std::thread::sleep(Duration::from_millis(2));
    }
    if let Some(failure) = failure { Err(failure) } else { Ok((status.ok_or("report exit status unavailable")?, out, err)) }
}

#[cfg(not(windows))]
pub fn run(_command: Command, _timeout: Duration) -> Result<(ExitStatus, Vec<u8>, Vec<u8>), String> {
    Err("bounded report execution requires Windows".into())
}
