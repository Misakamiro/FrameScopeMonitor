//! Only FrameScope's watcher descendants belong to this job; existing games never do.
use std::{process::{Child,Command},time::{Duration,Instant}};
use std::os::windows::{io::AsRawHandle,process::CommandExt};
use windows_sys::Win32::{Foundation::*,System::{JobObjects::*,Threading::*,Diagnostics::ToolHelp::*}};
pub struct Job(HANDLE);
unsafe impl Send for Job {}
impl Drop for Job {fn drop(&mut self){unsafe{CloseHandle(self.0);}}}
impl Job {
    pub fn terminate(&self)->Result<(),String>{
        if unsafe{TerminateJobObject(self.0,1)}==0{return Err(std::io::Error::last_os_error().to_string());}
        let until=Instant::now()+Duration::from_secs(10);
        loop {
            let mut state:JOBOBJECT_BASIC_ACCOUNTING_INFORMATION=unsafe{std::mem::zeroed()};
            if unsafe{QueryInformationJobObject(self.0,JobObjectBasicAccountingInformation,&mut state as *mut _ as _,std::mem::size_of_val(&state) as u32,std::ptr::null_mut())}==0{return Err(std::io::Error::last_os_error().to_string());}
            if state.ActiveProcesses==0{return Ok(());}
            if Instant::now()>=until{return Err("监测子进程尚未退出，原始数据已保留".into());}
            std::thread::sleep(Duration::from_millis(20));
        }
    }
}
pub fn spawn(command:&mut Command)->Result<(Child,Job),String>{
    let job=Job(unsafe{CreateJobObjectW(std::ptr::null(),std::ptr::null())});
    if job.0.is_null(){return Err(std::io::Error::last_os_error().to_string());}
    let mut limits:JOBOBJECT_EXTENDED_LIMIT_INFORMATION=unsafe{std::mem::zeroed()};
    limits.BasicLimitInformation.LimitFlags=JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if unsafe{SetInformationJobObject(job.0,JobObjectExtendedLimitInformation,&limits as *const _ as _,std::mem::size_of_val(&limits) as u32)}==0{return Err(std::io::Error::last_os_error().to_string());}
    command.creation_flags(CREATE_NO_WINDOW|CREATE_SUSPENDED);
    let mut child=command.spawn().map_err(|e|e.to_string())?;
    if unsafe{AssignProcessToJobObject(job.0,child.as_raw_handle())}==0{
        let error=std::io::Error::last_os_error().to_string();let _=child.kill();let _=child.wait();return Err(error);
    }
    struct Handle(HANDLE);
    impl Drop for Handle {fn drop(&mut self){unsafe{CloseHandle(self.0);}}}
    let snapshot=Handle(unsafe{CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD,0)});
    if snapshot.0==INVALID_HANDLE_VALUE{return Err(std::io::Error::last_os_error().to_string());}
    let mut entry:THREADENTRY32=unsafe{std::mem::zeroed()};entry.dwSize=std::mem::size_of_val(&entry) as u32;
    let mut more=unsafe{Thread32First(snapshot.0,&mut entry)}!=0;
    while more {
        if entry.th32OwnerProcessID==child.id(){
            let thread=Handle(unsafe{OpenThread(THREAD_SUSPEND_RESUME,0,entry.th32ThreadID)});
            if thread.0.is_null() || unsafe{ResumeThread(thread.0)}==u32::MAX{return Err(std::io::Error::last_os_error().to_string());}
            return Ok((child,job));
        }
        more=unsafe{Thread32Next(snapshot.0,&mut entry)}!=0;
    }
    Err("无法启动受管监测线程".into())
}
