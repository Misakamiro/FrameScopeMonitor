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
struct State {preferences:Preferences,phase:String,error:Option<String>,releases:Vec<Candidate>,downloaded:u64,total:u64,ready:Option<u64>}
pub struct Service {root:PathBuf,state:Mutex<State>,busy:AtomicBool,cancel:AtomicBool}
pub fn build_info()->BuildInfo {BuildInfo::new(env!("FRAMESCOPE_VERSION"),env!("FRAMESCOPE_GIT_SHA"))}
impl Service {
    pub fn new(root:PathBuf)->Result<Self,String> {
        std::fs::create_dir_all(&root).map_err(|e|e.to_string())?;
        let path=root.join("preferences.json");
        let preferences=if path.exists(){serde_json::from_slice(&std::fs::read(path).map_err(|e|e.to_string())?).map_err(|e|format!("更新设置读取失败: {e}"))?}else{Preferences::default()};
        Ok(Self {root,state:Mutex::new(State{preferences,phase:"idle".into(),error:None,releases:vec![],downloaded:0,total:0,ready:None}),busy:AtomicBool::new(false),cancel:AtomicBool::new(false)})
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
                self.state.lock().unwrap().releases=releases;
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
                let (exe,arguments)=if command=="update_install" {
                    #[derive(Deserialize)] #[serde(deny_unknown_fields)] struct Selection {release_id:u64}
                    let selection:Selection=serde_json::from_value(args).map_err(|e|e.to_string())?;
                    if self.state.lock().unwrap().ready!=Some(selection.release_id){return Err("请先完成下载".into());}
                    let receipt_path=self.root.join("cache/receipt.json");
                    let receipt:Receipt=serde_json::from_slice(&std::fs::read(&receipt_path).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
                    if receipt.candidate.id!=selection.release_id{return Err("缓存发行身份不一致".into());}
                    let exe=self.source()?.verified_cache(&self.root.join("cache"),&receipt)?;
                    (exe,vec!["--update".to_string(),"/S".into(),format!("/D={}",target.display()),format!("--receipt={}",receipt_path.display())])
                }else{
                    if !args.as_object().is_some_and(|a|a.is_empty()){return Err("恢复操作不接受文件路径".into());}
                    (target.join("FrameScope Installer.exe"),vec!["--restore-previous".into(),"/S".into(),format!("/D={}",target.display())])
                };
                crate::backend::reserve_update()?;
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

fn launch_installer(exe:&Path,args:&[String])->Result<(),String> {
    // The FrameScope host already runs elevated. CreateProcess failure (including
    // ERROR_ELEVATION_REQUIRED) leaves it running and reports a normal UAC option.
    #[cfg(windows)]
    {use std::os::windows::process::CommandExt;
        let mut child=std::process::Command::new(exe).args(args).creation_flags(0x08000000).spawn().map_err(|e|format!("安装器未启动，当前版本保持运行: {e}"))?;
        let status=child.wait().map_err(|e|e.to_string())?;
        if !status.success(){return Err(format!("安装或恢复未完成，查看安装目录更新记录 ({status})"));}Ok(())
    }
    #[cfg(not(windows))] {let _=(exe,args);Err("Windows updater only".into())}
}

pub async fn command(app:tauri::AppHandle,command:String,args:Value)->Result<Value,String> {
    let service=app.state::<Arc<Service>>().inner().clone();
    match command.as_str(){
        "build_info"=>return Ok(json!(build_info())),
        "update_status"=>return Ok(service.snapshot()),
        "update_ready"=>{let exe=std::env::current_exe().map_err(|e|e.to_string())?;update::acknowledge_startup(exe.parent().ok_or("Missing installation root")?,build_info())?;return Ok(json!(true));}
        "update_cancel"=>{service.cancel.store(true,Ordering::SeqCst);return Ok(service.snapshot());}
        "update_preferences"=>{
            if service.busy.load(Ordering::SeqCst){return Err("更新操作进行中，请稍后修改设置".into());}
            let preferences:Preferences=serde_json::from_value(args).map_err(|e|e.to_string())?;
            update::atomic_json(&service.root.join("preferences.json"),&preferences)?;
            service.state.lock().unwrap().preferences=preferences;service.emit(&app);return Ok(service.snapshot());
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
}
