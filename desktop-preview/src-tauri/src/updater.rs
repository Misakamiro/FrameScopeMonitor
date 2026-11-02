use framescope_update::{self as update, BuildInfo, Candidate, Receipt, Source};
use serde::{Deserialize,Serialize};
use serde_json::{json,Value};
use std::{path::{Path,PathBuf}, sync::{Arc,Mutex,atomic::{AtomicBool,Ordering}}};
use tauri::{Emitter,Manager};

#[derive(Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Preferences { pub startup_check:bool, pub include_beta:bool, pub accelerated:bool }
impl Default for Preferences {fn default()->Self {Self {startup_check:true,include_beta:env!("FRAMESCOPE_VERSION").contains('-'),accelerated:true}}}
#[derive(Serialize)]
struct State {preferences:Preferences,phase:String,error:Option<String>,releases:Vec<Candidate>,downloaded:u64,total:u64,ready:Option<u64>,last_checked:Option<String>,download_route:Option<String>,bytes_per_second:u64}
pub struct Service {root:PathBuf,state:Mutex<State>,busy:AtomicBool,cancel:AtomicBool}
pub fn build_info()->BuildInfo {BuildInfo::new(env!("FRAMESCOPE_VERSION"),env!("FRAMESCOPE_GIT_SHA")).with_edition(env!("FRAMESCOPE_EDITION"))}

fn write_download_diagnostic(root:&Path,event:&update::DownloadTelemetry) {
    use std::io::Write;
    let path=root.join("download-diagnostics.jsonl");
    if std::fs::metadata(&path).is_ok_and(|m|m.len()>=128*1024) {
        let previous=root.join("download-diagnostics.previous.jsonl");
        if previous.exists()&&std::fs::remove_file(&previous).is_err(){return;}
        if std::fs::rename(&path,&previous).is_err(){return;}
    }
    let value=json!({"time":chrono::Utc::now().to_rfc3339(),"version":env!("FRAMESCOPE_VERSION"),"event":event});
    if let (Ok(mut file),Ok(mut bytes))=(std::fs::OpenOptions::new().create(true).append(true).open(path),serde_json::to_vec(&value)) {
        bytes.push(b'\n');let _=file.write_all(&bytes);
    }
}
impl Service {
    pub fn new(root:PathBuf)->Result<Self,String> {
        let path=root.join("preferences.json");
        let loaded=(||{std::fs::create_dir_all(&root).map_err(|e|e.to_string())?;
            if path.exists(){serde_json::from_slice(&std::fs::read(path).map_err(|e|e.to_string())?).map_err(|e|format!("更新设置读取失败: {e}"))}else{Ok(Preferences::default())}})();
        let (preferences,error)=match loaded{Ok(p)=>(p,None),Err(e)=>{let mut p=Preferences::default();p.startup_check=false;(p,Some(e))}};
        Ok(Self {root,state:Mutex::new(State{preferences,phase:"idle".into(),error,releases:vec![],downloaded:0,total:0,ready:None,last_checked:None,download_route:None,bytes_per_second:0}),busy:AtomicBool::new(false),cancel:AtomicBool::new(false)})
    }
    fn source(&self)->Result<Source,String>{Source::github(update::public_key(),self.state.lock().unwrap().preferences.accelerated)?.with_edition(env!("FRAMESCOPE_EDITION"))}
    fn snapshot(&self)->Value {
        let state=self.state.lock().unwrap();
        let target=std::env::current_exe().ok().and_then(|p|p.parent().map(Path::to_path_buf));
        let previous=target.as_ref().and_then(|r|std::fs::read(r.join("FrameScopePreviousVersion.json")).ok()).and_then(|v|serde_json::from_slice::<Value>(&v).ok());
        json!({"build":build_info(),"state":*state,"previous":previous})
    }
    fn emit(&self,app:&tauri::AppHandle){let _=app.emit("framescope-update",self.snapshot());}
    fn run(&self,app:&tauri::AppHandle,command:&str,args:Value)->Result<Value,String> {
        let result=(||{
            match command {
            "update_check"=>{
                #[derive(Deserialize)] #[serde(deny_unknown_fields)] struct Options {#[serde(default)]return_stable:bool}
                let options:Options=serde_json::from_value(args).map_err(|e|e.to_string())?;
                let prefs=self.state.lock().unwrap().preferences.clone();
                let skipped=std::env::current_exe().ok().and_then(|p|std::fs::read(p.parent()?.join("FrameScopeRolledBack.json")).ok())
                    .and_then(|v|serde_json::from_slice::<Value>(&v).ok()).and_then(|v|v["version"].as_str().map(str::to_string));
                self.state.lock().unwrap().phase="checking".into();self.emit(app);
                let releases=self.source()?.check(env!("FRAMESCOPE_VERSION"),prefs.include_beta,options.return_stable,skipped.as_deref(),&self.cancel)?;
                {let mut s=self.state.lock().unwrap();s.releases=releases;s.last_checked=Some(chrono::Utc::now().to_rfc3339());}
                Ok(())
            }
            "update_download"=>{
                #[derive(Deserialize)] #[serde(deny_unknown_fields)] struct Selection {release_id:u64}
                let selection:Selection=serde_json::from_value(args).map_err(|e|e.to_string())?;
                let candidate=self.state.lock().unwrap().releases.iter().find(|r|r.id==selection.release_id).cloned().ok_or("请先检查并选择有效发行")?;
                {let mut s=self.state.lock().unwrap();s.phase="downloading".into();s.ready=None;s.downloaded=0;s.total=candidate.manifest.installer.size;s.download_route=None;s.bytes_per_second=0;}
                self.emit(app);
                let mut last=std::time::Instant::now();
                let last=Mutex::new(&mut last);
                self.source()?.download_observed(&candidate,&self.root.join("cache"),&self.cancel,|done,total|{
                    {let mut s=self.state.lock().unwrap();s.downloaded=done;s.total=total;}
                    let mut last=last.lock().unwrap();if last.elapsed().as_millis()>=150||done==total{**last=std::time::Instant::now();self.emit(app);}
                },|event|{
                    write_download_diagnostic(&self.root,&event);
                    if matches!(event.event,"route"|"progress"|"switch") {
                        {let mut s=self.state.lock().unwrap();s.download_route=Some(event.route.into());s.bytes_per_second=event.bytes_per_second;}
                        self.emit(app);
                    }
                })?;
                self.state.lock().unwrap().ready=Some(candidate.id);
                Ok(())
            }
            "update_install"|"update_restore"=>{
                let target=std::env::current_exe().map_err(|e|e.to_string())?.parent().ok_or("缺少安装目录")?.to_path_buf();
                run_verified_update(|| {
                let (exe,arguments,package)=if command=="update_install" {
                    #[derive(Deserialize)] #[serde(deny_unknown_fields)] struct Selection {release_id:u64}
                    let selection:Selection=serde_json::from_value(args).map_err(|e|e.to_string())?;
                    if self.state.lock().unwrap().ready!=Some(selection.release_id){return Err("请先完成下载".into());}
                    let receipt_path=self.root.join("cache/receipt.json");
                    let receipt:Receipt=serde_json::from_slice(&std::fs::read(&receipt_path).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
                    if receipt.candidate.id!=selection.release_id{return Err("缓存发行身份不一致".into());}
                    let expected=self.state.lock().unwrap().releases.iter().find(|r|r.id==selection.release_id).cloned().ok_or("所选发行已不在当前检查结果中")?;
                    if expected.tag!=receipt.candidate.tag||serde_json::to_value(&expected.manifest).unwrap()!=serde_json::to_value(&receipt.candidate.manifest).unwrap(){return Err("缓存发行与所选版本不一致".into());}
                    let exe=self.source()?.verified_cache(&self.root.join("cache"),&receipt)?;
                    (exe,vec!["--update".to_string(),"/S".into(),format!("/D={}",target.display()),format!("--receipt={}",receipt_path.display())],expected.manifest.installer)
                }else{
                    if !args.as_object().is_some_and(|a|a.is_empty()){return Err("恢复操作不接受文件路径".into());}
                    let helper:update::Package=serde_json::from_slice(&std::fs::read(target.join("FrameScopeRecoveryHost.json")).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
                    let original=target.join("FrameScope Installer.exe");let _source_lock=lock_verified(&original,&helper)?;
                    let directory=self.root.join(format!("restore-{:?}",std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map_err(|e|e.to_string())?.as_nanos()));
                    std::fs::create_dir(&directory).map_err(|e|e.to_string())?;
                    let exe=directory.join("FrameScopeRecovery.exe");std::fs::copy(&original,&exe).map_err(|e|e.to_string())?;
                    (exe,vec!["--restore-previous".into(),"/S".into(),format!("/D={}",target.display())],helper)
                };
                let verified_file=lock_verified(&exe,&package)?;
                Ok((exe,arguments,verified_file))
                },|| {
                    self.state.lock().unwrap().phase="preparing".into();self.emit(app);
                    crate::backend::reserve_update()
                },|(exe,arguments,_verified_file)| {
                    self.state.lock().unwrap().phase="installing".into();self.emit(app);
                    // The installer closes the UI only after launch/UAC succeeds.
                    launch_installer(&exe,&arguments)
                })
            }
            _=>Err("Unsupported update operation".into()),
        }})();
        {let mut state=self.state.lock().unwrap();state.phase="idle".into();state.error=result.as_ref().err().cloned();}
        self.busy.store(false,Ordering::SeqCst);self.emit(app);
        result.map(|_|self.snapshot())
    }
}
fn run_verified_update<T,R>(verify:impl FnOnce()->Result<T,String>,prepare:impl FnOnce()->Result<R,String>,launch:impl FnOnce(T)->Result<(),String>)->Result<(),String>{
    let verified=verify()?;
    let _reservation=prepare()?;
    launch(verified)
}
fn lock_verified(path:&Path,package:&update::Package)->Result<std::fs::File,String>{
    let mut options=std::fs::OpenOptions::new();options.read(true);
    #[cfg(windows)] {use std::os::windows::fs::OpenOptionsExt;options.share_mode(1);}
    let file=options.open(path).map_err(|e|e.to_string())?;
    update::verify_file(path,package)?;Ok(file)
}

fn launch_installer(exe:&Path,args:&[String])->Result<(),String> {
    #[cfg(windows)]
    {use std::os::windows::process::CommandExt;
        match std::process::Command::new(exe).args(args).creation_flags(0x08000000).spawn() {
            Ok(mut child)=>{let status=child.wait().map_err(|e|e.to_string())?;if !status.success(){return Err(format!("安装或恢复未完成，查看安装目录更新记录 ({status})"));}Ok(())}
            Err(e) if e.raw_os_error()==Some(740)=>launch_elevated(exe,args),
            Err(e)=>Err(format!("安装器未启动，当前版本保持运行: {e}")),
        }
    }
    #[cfg(not(windows))] {let _=(exe,args);Err("Windows updater only".into())}
}
#[cfg(windows)]
fn launch_elevated(exe:&Path,args:&[String])->Result<(),String>{
    use windows::{core::PCWSTR,Win32::UI::Shell::{ShellExecuteExW,SHELLEXECUTEINFOW,SEE_MASK_NOCLOSEPROCESS}};
    use std::os::windows::ffi::OsStrExt;
    let file:Vec<u16>=exe.as_os_str().encode_wide().chain(Some(0)).collect();
    let args=args.iter().map(|a|quote_argument(a)).collect::<Vec<_>>().join(" ");
    let args:Vec<u16>=args.encode_utf16().chain(Some(0)).collect();
    let verb:Vec<u16>="runas".encode_utf16().chain(Some(0)).collect();
    let mut info=SHELLEXECUTEINFOW {cbSize:std::mem::size_of::<SHELLEXECUTEINFOW>() as u32,fMask:SEE_MASK_NOCLOSEPROCESS,
        lpFile:PCWSTR(file.as_ptr()),lpParameters:PCWSTR(args.as_ptr()),lpVerb:PCWSTR(verb.as_ptr()),nShow:0,..Default::default()};
    unsafe {ShellExecuteExW(&mut info)}.map_err(|e|format!("UAC 已取消或安装器未能启动，当前版本保持运行: {e}"))?;
    let handle=info.hProcess.0;
    if handle.is_null(){return Err("安装器未返回进程句柄，当前版本保持运行".into());}
    let mut code=0;
    unsafe {
        windows_sys::Win32::System::Threading::WaitForSingleObject(handle,0xffff_ffff);
        let ok=windows_sys::Win32::System::Threading::GetExitCodeProcess(handle,&mut code);
        windows_sys::Win32::Foundation::CloseHandle(handle);
        if ok==0||code!=0 {return Err(format!("安装未完成 ({code})"));}
    }Ok(())
}
fn quote_argument(arg:&str)->String{
    let mut out=String::from("\"");let mut slashes=0;
    for c in arg.chars(){if c=='\\'{slashes+=1;continue;}if c=='"'{out.push_str(&"\\".repeat(slashes*2+1));}else{out.push_str(&"\\".repeat(slashes));}slashes=0;out.push(c);}
    out.push_str(&"\\".repeat(slashes*2));out.push('"');out
}

pub async fn command(app:tauri::AppHandle,command:String,args:Value)->Result<Value,String> {
    let service=app.state::<Arc<Service>>().inner().clone();
    match command.as_str(){
        "build_info"=>return Ok(json!(build_info())),
        "update_status"=>return Ok(service.snapshot()),
        "update_ready"=>{
            let main=app.get_webview_window("main").ok_or("Main window missing")?;
            if args["ui_ready"]!=true||!main.is_visible().unwrap_or(false)||main.is_minimized().unwrap_or(true){return Err("主界面尚未显示，暂不确认更新启动成功".into());}
            let exe=std::env::current_exe().map_err(|e|e.to_string())?;
            update::acknowledge_startup(exe.parent().ok_or("Missing installation root")?,build_info())?;return Ok(service.snapshot());
        }
        "update_cancel"=>{service.cancel.store(true,Ordering::SeqCst);
            {let mut state=service.state.lock().unwrap();if state.phase=="downloading"{state.phase="cancelling".into();}}service.emit(&app);return Ok(service.snapshot());}
        "update_preferences"=>{
            if service.busy.load(Ordering::SeqCst){return Err("更新操作进行中，请稍后修改设置".into());}
            let preferences:Preferences=serde_json::from_value(args).map_err(|e|e.to_string())?;
            update::atomic_json(&service.root.join("preferences.json"),&preferences)?;
            {let mut state=service.state.lock().unwrap();state.releases.retain(|r|preferences.include_beta||r.manifest.channel=="stable");
                if !state.releases.iter().any(|r|Some(r.id)==state.ready){state.ready=None;}
                state.preferences=preferences;state.error=None;state.last_checked=None;}service.emit(&app);return Ok(service.snapshot());
        }
        _=>()
    }
    if service.busy.swap(true,Ordering::SeqCst){return Err("已有更新操作正在进行".into());}
    service.cancel.store(false,Ordering::SeqCst);
    tauri::async_runtime::spawn_blocking(move||service.run(&app,&command,args)).await.map_err(|e|e.to_string())?
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn invalid_package_is_rejected_before_monitor_preparation(){
        let result=run_verified_update(||Err::<(),_>("invalid package signature".into()),||->Result<(),String>{panic!("must not stop monitoring for an invalid package")},|_|panic!("invalid package must not launch"));
        assert!(result.unwrap_err().contains("signature"));
    }
    #[test] fn failed_monitor_drain_never_launches_the_installer(){
        let result=run_verified_update(||Ok(()),||Err::<(),_>("report timeout".into()),|_|panic!("must not install before reports drain"));
        assert!(result.unwrap_err().contains("report timeout"));
    }
    #[test] fn installation_launch_failure_drops_the_reservation(){
        use std::sync::atomic::AtomicBool;
        struct Permit<'a>(&'a AtomicBool);
        impl Drop for Permit<'_>{fn drop(&mut self){self.0.store(false,Ordering::SeqCst);}}
        let active=AtomicBool::new(false);
        let result=run_verified_update(||Ok("verified"),||{active.store(true,Ordering::SeqCst);Ok(Permit(&active))},|value|{assert_eq!(value,"verified");assert!(active.load(Ordering::SeqCst));Err("UAC cancelled".into())});
        assert!(result.unwrap_err().contains("UAC"));assert!(!active.load(Ordering::SeqCst));
    }
    #[test] fn download_diagnostics_are_bounded_and_do_not_store_urls(){
        let root=std::env::temp_dir().join(format!("framescope-download-diagnostic-{}",std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        std::fs::create_dir(&root).unwrap();
        let event=update::DownloadTelemetry{event:"sample",route:"accelerator",bytes_per_second:1000000,ttfb_ms:Some(100),sample_bytes:Some(1048576),range_supported:Some(true),reason:None,active_requests:Some(4)};
        for _ in 0..1100{write_download_diagnostic(&root,&event);}
        let files=std::fs::read_dir(&root).unwrap().map(|e|e.unwrap().path()).collect::<Vec<_>>();assert_eq!(files.len(),2);
        for file in files{let text=std::fs::read_to_string(&file).unwrap();assert!(text.len()<129*1024);assert!(!text.contains("http://")&&!text.contains("https://"));for line in text.lines(){let value:Value=serde_json::from_str(line).unwrap();assert_eq!(value["event"]["route"],"accelerator");}std::fs::remove_file(file).unwrap();}
        std::fs::remove_dir(root).unwrap();
    }
    #[test] fn download_snapshot_adds_only_runtime_progress_fields(){
        let root=std::env::temp_dir().join(format!("framescope-update-state-{}",std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
        let service=Service::new(root.clone()).unwrap();let snapshot=service.snapshot();assert!(snapshot["state"]["download_route"].is_null());assert_eq!(snapshot["state"]["bytes_per_second"],0);assert_eq!(serde_json::to_value(Preferences::default()).unwrap().as_object().unwrap().len(),3);std::fs::remove_dir(root).unwrap();
    }
    #[test] fn default_preferences_match_channel_and_persist_explicit_choice(){
        assert!(Preferences::default().startup_check);
        assert!(Preferences::default().accelerated);
        assert_eq!(Preferences::default().include_beta,env!("FRAMESCOPE_VERSION").contains('-'));
        let choice:Preferences=serde_json::from_str(r#"{"startup_check":false,"include_beta":false,"accelerated":false}"#).unwrap();
        assert!(!choice.include_beta);
        assert!(serde_json::from_str::<Preferences>(r#"{"startup_check":true,"include_beta":true,"accelerated":true,"url":"evil"}"#).is_err());
    }
    #[test] fn elevation_arguments_keep_spaces_unicode_and_trailing_separator(){
        assert_eq!(quote_argument("/D=C:\\中文 目录"),"\"/D=C:\\中文 目录\"");
        assert_eq!(quote_argument("C:\\Path\\"),"\"C:\\Path\\\\\"");
        assert_eq!(quote_argument("a\"b"),"\"a\\\"b\"");
    }
}
