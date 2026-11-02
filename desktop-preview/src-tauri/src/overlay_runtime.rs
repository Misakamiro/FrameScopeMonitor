//! One physical-operation queue. Its worker never holds a state/operation mutex while waiting for UI/JS.
use crate::{overlay_lifecycle::{Lifecycle,START_BUDGET},overlay_panel::{self,OverlaySettings}};
use serde_json::{Value,json};
use std::{sync::{Mutex,OnceLock,mpsc},time::{Duration,Instant}};
use tauri::{Emitter,Manager};

pub static STATE:Lifecycle=Lifecycle::new();
static RAW:Mutex<Option<(Value,Instant)>>=Mutex::new(None);
pub enum Action {Start(Option<OverlaySettings>),Stop,Toggle,Settings(OverlaySettings),Reset(bool)}
struct Job {app:tauri::AppHandle,id:u64,action:Action,deadline:Instant,reply:mpsc::Sender<Result<Value,String>>}
pub fn trace(app:&tauri::AppHandle,id:u64,style:&str,stage:&str,error:Option<&str>){
    if let Ok(root)=app.path().app_data_dir(){let path=root.join("diagnostics").join(concat!("overlay-",env!("FRAMESCOPE_VERSION"),".jsonl"));let _=crate::overlay_diagnostics::record(&path,env!("FRAMESCOPE_VERSION"),id,style,stage,error);}
}
pub fn publish_raw(data:Value){*RAW.lock().unwrap_or_else(|p|p.into_inner())=Some((data,Instant::now()));}
pub fn latest_raw()->Value {RAW.lock().unwrap_or_else(|p|p.into_inner()).as_ref().filter(|(_,at)|at.elapsed()<Duration::from_secs(2)).map(|(data,_)|data.clone()).unwrap_or_else(crate::hardware_service::empty_overlay_value)}
fn queue()->&'static mpsc::Sender<Job>{
    static QUEUE:OnceLock<mpsc::Sender<Job>>=OnceLock::new();
    QUEUE.get_or_init(||{let (send,receive)=mpsc::channel::<Job>();std::thread::Builder::new().name("framescope-osd-lifecycle".into()).spawn(move||{
        while let Ok(job)=receive.recv(){
            let style=match &job.action{Action::Start(Some(settings))|Action::Settings(settings)=>settings.style.clone(),_=>overlay_panel::get_or_init_settings().style};
            trace(&job.app,job.id,&style,"queued",None);
            let was_current=STATE.current(job.id);
            let result=process(&job);
            if let Err(error)=&result {
                // Physical work is serialized, so only this worker can retire its stale window.
                if was_current{let _=crate::stop_windows(&job.app);}
                STATE.stop(job.id);trace(&job.app,job.id,&style,"failed",Some(error));
            }
            STATE.complete(job.id);
            let _=job.app.emit("overlay-status-changed",crate::active(&job.app));
            let _=job.reply.send(result);
        }
    }).expect("spawn OSD lifecycle worker");send})
}
pub fn request_blocking(app:tauri::AppHandle,action:Action,budget:Duration)->Result<Value,String>{
    let budget=budget.min(START_BUDGET);let deadline=Instant::now()+budget;
    let id=match &action {Action::Start(_)=>STATE.issue(true),Action::Stop=>STATE.issue(false),Action::Toggle=>STATE.toggle(crate::active(&app)),Action::Settings(_)|Action::Reset(_)=>STATE.preserve(crate::active(&app))};
    let (send,receive)=mpsc::channel();
    queue().send(Job{app:app.clone(),id,action,deadline,reply:send}).map_err(|_|"悬浮框操作队列已关闭".to_string())?;
    match receive.recv_timeout(deadline.saturating_duration_since(Instant::now())){
        Ok(result)=>result,
        Err(_)=>{
            if let Some(cleanup)=STATE.cancel(id){
                let (reply,_)=mpsc::channel();
                let _=queue().send(Job{app:app.clone(),id:cleanup,action:Action::Stop,deadline:Instant::now()+START_BUDGET,reply});
            }
            let error="悬浮框操作超时，迟到的窗口不会显示";trace(&app,id,&overlay_panel::get_or_init_settings().style,"timeout",Some(error));Err(error.into())
        }
    }
}
pub async fn request(app:tauri::AppHandle,action:Action,budget:Duration)->Result<Value,String>{
    tauri::async_runtime::spawn_blocking(move||request_blocking(app,action,budget)).await.map_err(|e|e.to_string())?
}
fn ensure(job:&Job)->Result<(),String>{if !STATE.current(job.id){Err("悬浮框操作已被后续请求取消".into())}else if Instant::now()>=job.deadline{Err("悬浮框操作超时".into())}else{Ok(())}}
fn process(job:&Job)->Result<Value,String>{
    ensure(job)?;
    if !matches!(&job.action,Action::Stop){crate::backend::ensure_not_closing()?;}
    match &job.action {
        Action::Start(requested)=>{
            let before=overlay_panel::get_or_init_settings();
            if let Some(requested)=requested{
                let settings=crate::settings_with_native_positions(requested.clone(),&overlay_panel::get_or_init_settings());
                overlay_panel::store_settings(settings);crate::save_native_settings(&job.app)?;
            }
            if crate::active(&job.app)&&serde_json::to_value(&before).unwrap()==serde_json::to_value(overlay_panel::get_or_init_settings()).unwrap(){STATE.keep_running(job.id)?;return Ok(json!({"success":true,"message":"悬浮框已处于启用状态"}));}
        }
        Action::Settings(requested)=>{
            let old=overlay_panel::get_or_init_settings();let settings=crate::settings_with_native_positions(requested.clone(),&old);
            let mut a=serde_json::to_value(&old).unwrap();let b=serde_json::to_value(&settings).unwrap();a["opacity"]=b["opacity"].clone();
            let same_vertical=old.style=="vertical_panel"&&settings.style=="vertical_panel"&&crate::active(&job.app);
            let only_opacity=old.style==settings.style&&a==b&&overlay_panel::is_overlay_active();
            if same_vertical||only_opacity{
                overlay_panel::store_settings(settings.clone());crate::save_native_settings(&job.app)?;
                if same_vertical||overlay_panel::request_overlay_repaint(){let _=job.app.emit("vertical-overlay-settings",settings);STATE.keep_running(job.id)?;return Ok(Value::Null);}
            }
            crate::stop_windows(&job.app)?;ensure(job)?;
            overlay_panel::store_settings(crate::settings_with_native_positions(settings,&overlay_panel::get_or_init_settings()));crate::save_native_settings(&job.app)?;
            let _=job.app.emit("vertical-overlay-settings",overlay_panel::get_or_init_settings());
            if !STATE.desired(){STATE.stop(job.id);return Ok(Value::Null);}
        }
        Action::Stop=>{crate::stop_windows(&job.app)?;STATE.stop(job.id);trace(&job.app,job.id,&overlay_panel::get_or_init_settings().style,"stopped",None);return Ok(json!({"success":true,"message":"悬浮框已关闭"}));}
        Action::Toggle=>{if !STATE.desired(){crate::stop_windows(&job.app)?;STATE.stop(job.id);return Ok(json!({"success":true,"message":"悬浮框已关闭"}));}}
        Action::Reset(vertical)=>{
            if !vertical{crate::stop_windows(&job.app)?;}
            let mut settings=overlay_panel::get_or_init_settings();
            if *vertical {settings.vertical_position_x=Some(40);settings.vertical_position_y=Some(80);settings.vertical_position_reference=None;}
            else{settings.position_x=Some(40);settings.position_y=Some(80);settings.position_reference=None;}
            overlay_panel::store_settings(settings);crate::save_native_settings(&job.app)?;
            if *vertical {
                if let Some(window)=job.app.get_webview_window("vertical-overlay"){window.set_position(tauri::PhysicalPosition::new(40,80)).map_err(|e|e.to_string())?;}
                if crate::active(&job.app){STATE.keep_running(job.id)?;return Ok(Value::Null);}
            }
            if !STATE.desired(){STATE.stop(job.id);return Ok(Value::Null);}
        }
    }
    ensure(job)?;
    crate::stop_windows(&job.app)?;ensure(job)?;
    let settings=overlay_panel::get_or_init_settings();let ready=STATE.prepare(job.id,job.deadline)?;
    trace(&job.app,job.id,&settings.style,"create",None);
    if settings.style=="vertical_panel"{
        let window=crate::create_vertical(&job.app,job.id,job.deadline)?;
        loop{
            if let Err(error)=ensure(job){let _=window.destroy();return Err(error);}
            match ready.recv_timeout(Duration::from_millis(25)){
                Ok(result)=>{result?;break;},Err(mpsc::RecvTimeoutError::Timeout)=>(),Err(_)=>{let _=window.destroy();return Err("竖排窗口初始化已取消".into());}
            }
        }
        ensure(job)?;
        let (send,receive)=mpsc::channel();let owned=window.clone();let id=job.id;
        window.run_on_main_thread(move||{
            let result=STATE.commit(id,||{
                owned.show().map_err(|e|e.to_string())?;
                crate::overlay_set_hit_test_transparent(&owned,true)?;
                Ok(())
            });
            if result.is_ok(){crate::VERTICAL_ACTIVE.store(true,std::sync::atomic::Ordering::SeqCst);let _=owned.emit("vertical-overlay-data",latest_raw());}
            let _=send.send(result);
        }).map_err(|e|e.to_string())?;
        receive.recv_timeout(job.deadline.saturating_duration_since(Instant::now())).map_err(|_|"竖排显示确认超时".to_string())??;
    }else{
        overlay_panel::start_overlay(settings.clone())?;
        ensure(job)?;STATE.keep_running(job.id)?;
    }
    trace(&job.app,job.id,&settings.style,"running",None);
    Ok(json!({"success":true,"message":"悬浮框已启动"}))
}

pub fn auxiliary(window:tauri::WebviewWindow,app:tauri::AppHandle,command:&str,args:&Value)->Result<Value,String>{
    let generation=args["generation"].as_u64();
    if window.label()=="vertical-overlay" {
        let id=generation.ok_or("竖排窗口缺少操作代号")?;
        if !STATE.live(id){return Err("旧竖排窗口操作已失效".into());}
    }
    match command {
        "set_overlay_drag_mode"=>{
            let enabled=args["enabled"].as_bool().ok_or("enabled must be boolean")?;
            if !enabled{crate::save_overlay_position(&app)?;}overlay_panel::set_drag_mode(enabled);
            if let Some(owned)=app.get_webview_window("vertical-overlay"){crate::overlay_set_hit_test_transparent(&owned,!enabled)?;let _=owned.emit("vertical-overlay-drag-mode",enabled);}
        }
        "set_vertical_overlay_click_through"=>{
            let enabled=args["enabled"].as_bool().ok_or("enabled must be boolean")?;
            let owned=if window.label()=="vertical-overlay"{Some(window.clone())}else{app.get_webview_window("vertical-overlay")};
            if let Some(owned)=owned{if enabled{crate::save_overlay_position(&app)?;}crate::overlay_set_hit_test_transparent(&owned,enabled)?;}
        }
        "save_vertical_overlay_position"=>{crate::save_overlay_position(&app)?;}
        "resize_vertical_overlay"=>{
            let id=generation.ok_or("竖排布局缺少操作代号")?;
            let height=args["height"].as_f64().filter(|n|n.is_finite()).ok_or("height must be a finite number")?.clamp(40.0,1600.0);
            let owned=app.get_webview_window("vertical-overlay").ok_or("竖排窗口不存在")?;
            let (send,receive)=mpsc::channel();let target=owned.clone();
            owned.run_on_main_thread(move||{
                let result=(||{
                    if !STATE.live(id){return Err("旧竖排布局消息已失效".into());}
                    target.set_size(tauri::LogicalSize::new(overlay_panel::get_or_init_settings().item_width as f64,height)).map_err(|e|e.to_string())?;
                    #[cfg(windows)] crate::vertical_overlay_position::layout_ready();
                    STATE.layout(id)
                })();let _=send.send(result);
            }).map_err(|e|e.to_string())?;
            receive.recv_timeout(START_BUDGET).map_err(|_|"竖排布局确认超时".to_string())??;
        }
        "vertical_overlay_ready"=>{
            let id=generation.ok_or("竖排就绪缺少操作代号")?;
            if window.label()!="vertical-overlay"{return Err("竖排就绪必须来自对应窗口".into());}
            let error=args["error"].as_str().map(|_|"竖排前端设置、监听或布局初始化失败".to_string());
            trace(&app,id,"vertical_panel","frontend-ready",error.as_deref());STATE.ready(id,error)?;
        }
        _=>return Err("Unknown overlay auxiliary operation".into()),
    }
    Ok(Value::Null)
}
