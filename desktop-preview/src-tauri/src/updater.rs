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
struct State {preferences:Preferences,phase:String,error:Option<String>,releases:Vec<Candidate>,downloaded:u64,total:u64,ready:Option<u64>,last_checked:Option<String>}
pub struct Service {root:PathBuf,state:Mutex<State>,busy:AtomicBool,cancel:AtomicBool}
pub fn build_info()->BuildInfo {BuildInfo::new(env!("FRAMESCOPE_VERSION"),env!("FRAMESCOPE_GIT_SHA"))}
impl Service {
    pub fn new(root:PathBuf)->Result<Self,String> {
        let path=root.join("preferences.json");
        let loaded=(||{std::fs::create_dir_all(&root).map_err(|e|e.to_string())?;
            if path.exists(){serde_json::from_slice(&std::fs::read(path).map_err(|e|e.to_string())?).map_err(|e|format!("更新设置读取失败: {e}"))}else{Ok(Preferences::default())}})();
        let (preferences,error)=match loaded{Ok(p)=>(p,None),Err(e)=>{let mut p=Preferences::default();p.startup_check=false;(p,Some(e))}};
        Ok(Self {root,state:Mutex::new(State{preferences,phase:"idle".into(),error,releases:vec![],downloaded:0,total:0,ready:None,last_checked:None}),busy:AtomicBool::new(false),cancel:AtomicBool::new(false)})
    }
    fn source(&self)->Result<Source,String>{Source::github(update::public_key(),self.state.lock().unwrap().preferences.accelerated)}
    fn snapshot(&self)->Value {
        let state=self.state.lock().unwrap();
        let target=std::env::current_exe().ok().and_then(|p|p.parent().map(Path::to_path_buf));
        let previous=target.as_ref().and_then(|r|std::fs::read(r.join("FrameScopePreviousVersion.json")).ok()).and_then(|v|serde_json::from_slice::<Value>(&v).ok());
        json!({"build":build_info(),"state":*state,"previous":previous})
    }
    fn emit(&self,app:&tauri::AppHandle){let _=app.emit("framescope-update",self.snapshot());}
    fn run(&self,app:&tauri::AppHandle,command:&str,args:Value)->Result<Value,String> {
        let result=(||match command {
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
                {let mut s=self.state.lock().unwrap();s.phase="downloading".into();s.ready=None;s.downloaded=0;s.total=candidate.manifest.installer.size;}
                self.emit(app);
                let mut last=std::time::Instant::now();
                let last=Mutex::new(&mut last);
                self.source()?.download(&candidate,&self.root.join("cache"),&self.cancel,|done,total|{
                    {let mut s=self.state.lock().unwrap();s.downloaded=done;s.total=total;}
                    let mut last=last.lock().unwrap();if last.elapsed().as_millis()>=150||done==total{**last=std::time::Instant::now();self.emit(app);}
                })?;
                self.state.lock().unwrap().ready=Some(candidate.id);
                Ok(())
            }
            "update_install"|"update_restore"=>{
                crate::backend::ensure_update_idle()?;
                let target=std::env::current_exe().map_err(|e|e.to_string())?.parent().ok_or("缺少安装目录")?.to_path_buf();
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
                let _verified_file=lock_verified(&exe,&package)?;
                crate::backend::reserve_update()?;
                self.state.lock().unwrap().phase="installing".into();self.emit(app);
                // The elevated installer owns normal shutdown. Do not close this
                // process before launch/UAC succeeds; cancellation leaves it usable.
                let result=launch_installer(&exe,&arguments);
                crate::backend::release_update();
                result
            }
            _=>Err("Unsupported update operation".into()),
        })();
        {let mut state=self.state.lock().unwrap();state.phase="idle".into();state.error=result.as_ref().err().cloned();}
        self.busy.store(false,Ordering::SeqCst);self.emit(app);
        result.map(|_|self.snapshot())
    }
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
        "update_ready"=>{let exe=std::env::current_exe().map_err(|e|e.to_string())?;update::acknowledge_startup(exe.parent().ok_or("Missing installation root")?,build_info())?;return Ok(service.snapshot());}
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
