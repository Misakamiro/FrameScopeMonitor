//! Hold original process handles throughout normal shutdown and bounded fallback.
use crate::install_transaction::{assert_path, Result};
use std::{path::{Path,PathBuf},time::{Duration,Instant}};
use windows_sys::Win32::{Foundation::*,System::{Diagnostics::ToolHelp::*,Threading::*,Pipes::*},Storage::FileSystem::*};
struct Process { handle: HANDLE, pid:u32, start:u64, image:PathBuf }
impl Drop for Process {fn drop(&mut self){unsafe{CloseHandle(self.handle);}}}
pub(crate) fn identity(handle:HANDLE)->Result<(u64,PathBuf)>{unsafe{
    let mut times:[FILETIME;4]=std::mem::zeroed();
    if GetProcessTimes(handle,&mut times[0],&mut times[1],&mut times[2],&mut times[3])==0{return Err(std::io::Error::last_os_error().to_string());}
    let mut name=vec![0u16;32768];let mut len=name.len() as u32;
    if QueryFullProcessImageNameW(handle,0,name.as_mut_ptr(),&mut len)==0{return Err(std::io::Error::last_os_error().to_string());}
    Ok((((times[0].dwHighDateTime as u64)<<32)|times[0].dwLowDateTime as u64,PathBuf::from(String::from_utf16_lossy(&name[..len as usize]))))
}}
fn same(a:&Path,b:&Path)->bool{match(a.canonicalize(),b.canonicalize()){(Ok(a),Ok(b))=>a.to_string_lossy().eq_ignore_ascii_case(&b.to_string_lossy()),_=>false}}
fn capture(root:&Path)->Result<Vec<Process>>{unsafe{
    assert_path(root)?;let mut result=Vec::new();
    let snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);if snapshot==INVALID_HANDLE_VALUE{return Err(std::io::Error::last_os_error().to_string());}
    let mut entry:PROCESSENTRY32W=std::mem::zeroed();entry.dwSize=std::mem::size_of_val(&entry) as u32;
    let mut more=Process32FirstW(snapshot,&mut entry);
    while more!=0 {
        let name=String::from_utf16_lossy(&entry.szExeFile[..entry.szExeFile.iter().position(|v|*v==0).unwrap_or(entry.szExeFile.len())]);
        let product=matches!(name.to_ascii_lowercase().as_str(),"framescope.exe"|"framescope-ui-preview.exe"|"framescopemonitor.exe"|"framescopehardwareservice.exe"|"framescopehardwareprovider.exe"|"framescopeprocesssampler.exe"|"framescopesystemsampler.exe"|"framescopereportgenerator.exe"|"presentmon-2.4.1-x64.exe");
        if product&&entry.th32ProcessID!=std::process::id(){
            let handle=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_TERMINATE|SYNCHRONIZE,0,entry.th32ProcessID);
            if handle.is_null() {
                let error=GetLastError();
                if error!=ERROR_INVALID_PARAMETER {CloseHandle(snapshot);return Err(format!("无法核验进程 {} 的归属（错误 {error}），文件未修改",entry.th32ProcessID));}
            }else{
                match identity(handle){Ok((start,image)) if same(&image,&root.join(&name))||same(&image,&root.join("tools").join(&name))=>{
                    if let Err(error)=assert_path(&image){CloseHandle(handle);CloseHandle(snapshot);return Err(error);}
                    result.push(Process{handle,pid:entry.th32ProcessID,start,image});
                },Err(error)=>{
                    let exited=WaitForSingleObject(handle,0)==WAIT_OBJECT_0;
                    CloseHandle(handle);
                    if !exited {CloseHandle(snapshot);return Err(format!("无法核验运行进程：{error}"));}
                },_=>{CloseHandle(handle);}}
            }
        }
        more=Process32NextW(snapshot,&mut entry);
    }
    let error=GetLastError();CloseHandle(snapshot);
    if error!=ERROR_NO_MORE_FILES {return Err(format!("进程枚举未完成：{error}"));}
    Ok(result)
}}
pub fn has_processes(root:&Path)->Result<bool>{Ok(!capture(root)?.is_empty())}
fn request_service_stop(process:&Process){unsafe{
    let name:Vec<u16>="\\\\.\\pipe\\FrameScopeHardwareService".encode_utf16().chain(Some(0)).collect();
    if WaitNamedPipeW(name.as_ptr(),1000)==0{return;}
    let pipe=CreateFileW(name.as_ptr(),GENERIC_READ|GENERIC_WRITE,0,std::ptr::null(),OPEN_EXISTING,0,std::ptr::null_mut());
    if pipe==INVALID_HANDLE_VALUE{return;}
    let mut server=0;
    if GetNamedPipeServerProcessId(pipe,&mut server)!=0&&server==process.pid {
        let mode=PIPE_READMODE_BYTE|PIPE_NOWAIT;
        if SetNamedPipeHandleState(pipe,&mode,std::ptr::null(),std::ptr::null())!=0 {
            let request=b"{\"id\":\"native-installer-stop\",\"method\":\"stop\",\"params\":{}}\n";let mut written=0;
            WriteFile(pipe,request.as_ptr(),request.len() as u32,&mut written,std::ptr::null_mut());
        }
    }
    CloseHandle(pipe);
}}
pub fn stop(root:&Path)->Result<()> {
    if !root.exists(){return Ok(());}
    let processes=capture(root)?;if processes.is_empty(){return Ok(());}
    crate::installer::validate_owned_installation(root)?;
    let state_path=root.join("framescope-watcher-state.json");assert_path(&state_path)?;
    let state:serde_json::Value=std::fs::read(&state_path).ok().and_then(|b|serde_json::from_slice(&b).ok()).unwrap_or_default();
    let token=state["WatcherInstanceId"].as_str().unwrap_or("");
    let watcher=processes.iter().find(|p|state["WatcherPid"].as_u64()==Some(p.pid as u64)
        && state["WatcherStartTimeUnix"].as_u64()==Some((p.start/10_000_000).saturating_sub(11_644_473_600))
        && same(&p.image,&root.join("FrameScopeMonitor.exe")));
    if watcher.is_some() && state["StopProtocolVersion"]==1 && token.len()==32 && token.bytes().all(|b|b.is_ascii_hexdigit()) {
        framescope_update::atomic_json(&root.join(format!("framescope-watcher-stop-{token}.json")),&serde_json::json!({
            "Action":"stop","WatcherPid":state["WatcherPid"],"WatcherStartTimeUnix":state["WatcherStartTimeUnix"],
            "WatcherInstanceId":token,"ConfigPath":state["ConfigPath"]}))?;
    }
    if processes.iter().any(|p|same(&p.image,&root.join("FrameScope.exe"))) {
        use std::os::windows::process::CommandExt;
        let _=std::process::Command::new(root.join("FrameScope.exe")).arg("--request-exit").arg("--request-exit-root").arg(root).creation_flags(0x08000000).spawn();
    }
    let reports=state["ActiveMonitors"].as_array().map_or(0,Vec::len).min(8);
    let deadline=Instant::now()+Duration::from_secs(45+reports as u64*120);
    let mut service_requested=false;
    loop {
        if !processes.iter().any(|p|unsafe{WaitForSingleObject(p.handle,0)!=WAIT_OBJECT_0})||Instant::now()>=deadline{break;}
        if !service_requested&&!processes.iter().any(|p|same(&p.image,&root.join("FrameScopeMonitor.exe"))&&unsafe{WaitForSingleObject(p.handle,0)!=WAIT_OBJECT_0}) {
            for process in &processes {if same(&process.image,&root.join("FrameScopeHardwareService.exe")){request_service_stop(process);}}
            service_requested=true;
        }
        std::thread::sleep(Duration::from_millis(200));
    }
    for process in &processes {unsafe{
        if WaitForSingleObject(process.handle,0)==WAIT_OBJECT_0{continue;}
        let (start,image)=identity(process.handle)?;
        if start!=process.start||!same(&image,&process.image){return Err("进程身份变化，停止失败；文件未修改".into());}
        assert_path(&image)?;
        if TerminateProcess(process.handle,1)==0||WaitForSingleObject(process.handle,5000)!=WAIT_OBJECT_0{return Err("程序未能退出，请重试；文件未修改".into());}
    }}
    if has_processes(root)? {return Err("检测到新的程序实例，请重试；文件未修改".into());}
    Ok(())
}
