//! Feature-only native acceptance. Uses the shipping bootstrap, queue, windows and service.
use crate::{overlay_panel,overlay_runtime::{self,Action}};
use serde_json::{json,Value};
use std::{sync::Mutex,time::{Duration,Instant}};
use tauri::Manager;

static FRAME:Mutex<Option<Value>>=Mutex::new(None);
static INPUT:Mutex<Vec<Value>>=Mutex::new(Vec::new());
static INPUT_GENERATIONS:Mutex<Vec<(usize,u64)>>=Mutex::new(Vec::new());
pub fn register_input(window:&tauri::WebviewWindow,generation:u64){
    if let Ok(hwnd)=window.hwnd(){
        let mut entries=INPUT_GENERATIONS.lock().unwrap_or_else(|p|p.into_inner());
        if entries.len()>=64{entries.remove(0);}
        entries.push((hwnd.0 as usize,generation));
    }
    record_input(window,"created");
}
pub fn record_input(window:&tauri::WebviewWindow,stage:&str){
    use windows_sys::Win32::UI::WindowsAndMessaging::*;
    let Ok(hwnd)=window.hwnd()else{return;};let raw=hwnd.0 as _;
    let generation=INPUT_GENERATIONS.lock().unwrap_or_else(|p|p.into_inner()).iter().rev().find(|(h,_)|*h==hwnd.0 as usize).map(|(_,g)|*g);
    let flags=unsafe{GetWindowLongW(raw,GWL_EXSTYLE)} as u32;
    let foreground=unsafe{GetForegroundWindow()};
    let mut gui:GUITHREADINFO=unsafe{std::mem::zeroed()};gui.cbSize=std::mem::size_of::<GUITHREADINFO>() as u32;
    let capture=if unsafe{GetGUIThreadInfo(0,&mut gui)}!=0{gui.hwndCapture}else{std::ptr::null_mut()};
    let event=json!({"stage":stage,"generation":generation,"layered":flags&WS_EX_LAYERED!=0,"transparent":flags&WS_EX_TRANSPARENT!=0,"noActivate":flags&WS_EX_NOACTIVATE!=0,
        "visible":unsafe{IsWindowVisible(raw)}!=0,"overlayForeground":foreground==raw||unsafe{IsChild(raw,foreground)}!=0,
        "overlayCapture":!capture.is_null()&&(capture==raw||unsafe{IsChild(raw,capture)}!=0)});
    let mut entries=INPUT.lock().unwrap_or_else(|p|p.into_inner());
    if entries.len()<256{entries.push(event);}
}
pub fn receive_frame(window:&tauri::WebviewWindow,args:Value)->Result<Value,String>{
    let id=args["generation"].as_u64().ok_or("missing frame generation")?;
    if window.label()!="vertical-overlay"||!overlay_runtime::STATE.live(id){return Err("stale acceptance frame".into());}
    let text=args["text"].as_str().filter(|s|s.len()<4096).ok_or("invalid frame text")?;
    *FRAME.lock().unwrap_or_else(|p|p.into_inner())=Some(json!({"generation":id,"text":text,"height":args["height"],"width":args["width"],"events":args["events"],"inputEvents":args["inputEvents"],"availableValuesMatch":args["availableValuesMatch"]}));
    Ok(Value::Null)
}
fn pause(){std::thread::sleep(Duration::from_millis(100));}
fn wait_for(check:impl Fn()->bool,seconds:u64)->Result<(),String>{
    let deadline=Instant::now()+Duration::from_secs(seconds);
    while Instant::now()<deadline{if check(){return Ok(());}pause();}
    Err("acceptance observation timed out".into())
}
fn operation(app:&tauri::AppHandle,action:Action)->Result<Value,String>{overlay_runtime::request_blocking(app.clone(),action,crate::overlay_lifecycle::START_BUDGET)}
fn native_window(hwnd:usize)->Result<Value,String>{
    use windows_sys::Win32::{Foundation::RECT,UI::WindowsAndMessaging::*};
    let mut rect=RECT{left:0,top:0,right:0,bottom:0};
    unsafe{
        if IsWindowVisible(hwnd as _) ==0||GetWindowRect(hwnd as _,&mut rect)==0{return Err("native OSD window is not visible".into());}
    }
    if rect.right<=rect.left||rect.bottom<=rect.top{return Err("native OSD has no layout".into());}
    Ok(json!({"hwnd":hwnd,"rect":[rect.left,rect.top,rect.right,rect.bottom]}))
}
fn sample_vertical(app:&tauri::AppHandle)->Result<Value,String>{
    let window=app.get_webview_window("vertical-overlay").ok_or("vertical native window missing")?;
    let native=native_window(window.hwnd().map_err(|e|e.to_string())?.0 as usize)?;
    *FRAME.lock().unwrap_or_else(|p|p.into_inner())=None;
    window.eval(r#"(async()=>{
      const api=window.__TAURI_INTERNALS__;
      if(!window.__osdAcceptance){
        const probe=window.__osdAcceptance={events:0,last:null,inputEvents:0};
        for(const type of ['pointerdown','pointerup','contextmenu'])window.addEventListener(type,()=>{probe.inputEvents++;},true);
        await api.invoke('plugin:event|listen',{event:'vertical-overlay-data',target:{kind:'Any'},handler:api.transformCallback(e=>{probe.events++;probe.last=e.payload;})});
      }
      const started=performance.now();
      const observe=()=>{
        const root=document.getElementById('root'),text=root.innerText,probe=window.__osdAcceptance,data=probe.last;
        const match=!!data&&data.cpu_usage!=null&&data.memory_usage!=null&&text.includes(`${data.cpu_usage}%`)&&text.includes(`${data.memory_usage.toFixed(1)}%`);
        if((probe.events>0&&match)||performance.now()-started>2000){
          void api.invoke('preview_native',{command:'uiaccess_acceptance_frame',args:{generation:window.__FRAMESCOPE_OVERLAY_GENERATION__,text,height:root.scrollHeight,width:root.scrollWidth,events:probe.events,inputEvents:probe.inputEvents,availableValuesMatch:match}});
        }else requestAnimationFrame(observe);
      };requestAnimationFrame(observe);
    })()"#).map_err(|e|e.to_string())?;
    wait_for(||FRAME.lock().unwrap_or_else(|p|p.into_inner()).is_some(),3)?;
    let frame=FRAME.lock().unwrap_or_else(|p|p.into_inner()).clone().unwrap();
    let text=frame["text"].as_str().unwrap_or("");
    if !text.contains("CPU占用")||!text.contains("内存占用")||frame["availableValuesMatch"]!=true{return Err("vertical DOM did not render current hardware event values".into());}
    Ok(json!({"native":native,"frame":frame,"raw":selected_raw()}))
}
fn selected_raw()->Value{
    let raw=overlay_runtime::latest_raw();
    json!({"cpu_usage":raw["cpu_usage"],"cpu_clock":raw["cpu_clock"],"gpu_usage":raw["gpu_usage"],"memory_usage":raw["memory_usage"]})
}
fn run(app:&tauri::AppHandle,checks:&mut Vec<Value>,manual_input:bool)->Result<(),String>{
    let token=crate::runtime_uiaccess::windows::current_state()?;
    if !token.elevated||!token.uiaccess{return Err("acceptance requires the shipping elevated UIAccess token".into());}
    checks.push(json!({"name":"token","elevated":token.elevated,"uiaccess":token.uiaccess}));
    wait_for(||{let raw=selected_raw();!raw["cpu_usage"].is_null()&&!raw["memory_usage"].is_null()},12)?;
    let mut settings=overlay_panel::get_or_init_settings();
    for item in &mut settings.display_items{item.enabled=matches!(item.id.as_str(),"time"|"cpu_usage"|"cpu_clock"|"gpu_usage"|"memory_usage");}
    settings.custom_items.clear();
    // Twelve real transitions, including immediate re-open and explicit stop/re-open.
    for index in 0..12{
        let style=["default","dynamic_island","vertical_panel"][index%3];
        settings.style=style.into();settings.item_width=if style=="vertical_panel"{220}else{130};
        if index%2==1&&crate::active(app){operation(app,Action::Settings(settings.clone()))?;}
        else{let result=operation(app,Action::Start(Some(settings.clone())))?;if result["success"]!=true{return Err("native start did not succeed".into());}}
        wait_for(||crate::active(app),3)?;
        let mut observations=Vec::new();
        for _ in 0..3{
            std::thread::sleep(Duration::from_millis(600));
            if style=="vertical_panel"{observations.push(sample_vertical(app)?);}else{
                let render=overlay_panel::render_diagnostics();
                native_window(render["hwnd"].as_u64().ok_or("GDI render not observed")? as usize)?;
                if render["updated"]!=true||render["style"]!=style{return Err("GDI first paint was not committed".into());}
                let cache=serde_json::to_value(overlay_panel::collect_hardware_data()).map_err(|e|e.to_string())?;
                if cache["cpu_usage"].is_null()||cache["memory_usage"].is_null(){return Err("GDI hardware cache lost available values".into());}
                observations.push(json!({"render":render,"raw":selected_raw(),"cpu_usage":cache["cpu_usage"],"memory_usage":cache["memory_usage"]}));
            }
        }
        if style=="vertical_panel"&&observations.last().unwrap()["frame"]["events"].as_u64().unwrap_or(0)<=observations.first().unwrap()["frame"]["events"].as_u64().unwrap_or(0){return Err("vertical hardware event stream did not continue".into());}
        if manual_input&&style=="vertical_panel"{
            let owned=app.get_webview_window("vertical-overlay").ok_or("vertical window missing")?;
            crate::overlay_set_hit_test_transparent(&owned,false)?;
            crate::overlay_set_hit_test_transparent(&owned,true)?;
            let (send,receive)=std::sync::mpsc::channel();let target=owned.clone();
            owned.run_on_main_thread(move||{
                let result=(||{
                    target.hide().map_err(|e|e.to_string())?;
                    target.show().map_err(|e|e.to_string())?;
                    record_input(&target,"reshown");
                    use windows_sys::Win32::UI::WindowsAndMessaging::*;
                    let flags=unsafe{GetWindowLongW(target.hwnd().map_err(|e|e.to_string())?.0 as _,GWL_EXSTYLE)} as u32;
                    let mask=WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_NOACTIVATE;
                    if flags&mask!=mask{return Err("Tao show erased input transparency flags".to_string());}
                    Ok(())
                })();let _=send.send(result);
            }).map_err(|e|e.to_string())?;
            receive.recv_timeout(Duration::from_secs(3)).map_err(|e|e.to_string())??;
            checks.push(json!({"name":"input-move-return-and-reshow","passed":true}));
        }
        checks.push(json!({"name":"native-transition","index":index,"style":style,"observations":observations}));
        if index%3==2{
            let before=overlay_panel::get_or_init_settings();
            operation(app,Action::Start(Some(before.clone())))?;
            if overlay_panel::get_or_init_settings().vertical_position_x!=before.vertical_position_x{return Err("duplicate start moved vertical position".into());}
            checks.push(json!({"name":"duplicate-start","index":index,"passed":true}));
        }
        if index%3==2{
            operation(app,Action::Stop)?;
            wait_for(||!crate::active(app)&&app.get_webview_window("vertical-overlay").is_none(),3)?;
        }
    }
    for index in 0..3{
        let owner=app.clone();let mut requested=settings.clone();requested.style="vertical_panel".into();requested.item_width=220;
        let start=std::thread::spawn(move||operation(&owner,Action::Start(Some(requested))));
        wait_for(||overlay_runtime::STATE.starting(),3)?;
        operation(app,Action::Stop)?;
        let start_result=start.join().map_err(|_|"rapid start worker panicked")?;
        std::thread::sleep(Duration::from_millis(500));
        if crate::active(app)||app.get_webview_window("vertical-overlay").is_some(){return Err("a cancelled vertical window appeared late".into());}
        checks.push(json!({"name":"rapid-start-stop","index":index,"startCancelled":start_result.is_err(),"closed":true}));
    }
    // Leave an actual live vertical panel visible long enough for user observation.
    settings.style="vertical_panel".into();settings.item_width=220;
    operation(app,Action::Start(Some(settings)))?;
    std::thread::sleep(Duration::from_secs(8));
    checks.push(json!({"name":"final-live-vertical","observation":sample_vertical(app)?}));
    if manual_input{
        // User operates Explorer and other applications manually. No input is
        // generated or forwarded by the acceptance host.
        for _ in 0..180{
            std::thread::sleep(Duration::from_secs(1));
            if let Some(window)=app.get_webview_window("vertical-overlay"){record_input(&window,"manual-observation");}
        }
        if app.get_webview_window("vertical-overlay").is_some(){checks.push(json!({"name":"manual-end-frame","observation":sample_vertical(app)?}));}
    }
    operation(app,Action::Stop)?;
    crate::hardware_service::set_poller_paused(true);
    let lease=crate::hardware_service::begin_maintenance()?;
    drop(lease);
    checks.push(json!({"name":"owned-hardware-cleanup","serviceStopAndExitVerified":true}));
    Ok(())
}
pub fn start(app:&tauri::AppHandle){
    let manual_input=std::env::args().any(|a|a=="--osd-input-manual");
    if !manual_input&&!std::env::args().any(|a|a=="--osd-acceptance"){return;}
    // Refuse to touch normal application data or the installed hardware service.
    let (identifier,pipe)=if manual_input&&app.config().identifier=="com.framescope.statistics-v2-qa"{
        ("com.framescope.statistics-v2-qa","FrameScopeHardwareService-qa-statistics-v2")
    }else if manual_input{("com.framescope.osd-input-qa","FrameScopeHardwareService-qa-input")}else{("com.framescope.osd-beta2-qa","FrameScopeHardwareService-qa-beta2")};
    if app.config().identifier!=identifier||std::env::var("FRAMESCOPE_OSD_ACCEPTANCE_PIPE").ok().as_deref()!=Some(pipe){
        log::error!("OSD acceptance isolation is missing");app.exit(5);return;
    }
    let app=app.clone();
    std::thread::spawn(move||{
        let mut checks=Vec::new();let started=Instant::now();
        let result=run(&app,&mut checks,manual_input);
        let _=operation(&app,Action::Stop);
        if let Ok(root)=app.path().app_data_dir(){
            let _=std::fs::create_dir_all(&root);
            let report=json!({"schema":1,"pid":std::process::id(),"version":env!("FRAMESCOPE_VERSION"),"statisticsVersion":crate::frame_statistics::VERSION,"passed":result.is_ok(),"error":result.err(),"elapsedMs":started.elapsed().as_millis(),"checks":checks,"inputStates":*INPUT.lock().unwrap_or_else(|p|p.into_inner()),"desktopClickAcceptance":"pending-user-manual","installedAcceptance":"pending","cpuExternalClockAcceptance":"unverified"});
            if let Ok(bytes)=serde_json::to_vec_pretty(&report){let _=std::fs::write(root.join("osd-acceptance-result.json"),bytes);}
        }
        crate::request_shutdown(&app);
    });
}
