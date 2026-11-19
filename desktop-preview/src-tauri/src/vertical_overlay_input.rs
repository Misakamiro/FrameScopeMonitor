//! The composition controller owns a separate Chromium input root. A transparent
//! Tao host alone does not make that root transparent to Explorer.
use std::sync::{Mutex, atomic::{AtomicUsize, Ordering}};
use webview2_com::Microsoft::Web::WebView2::Win32::*;
use windows::core::Interface;
use windows_sys::Win32::{Foundation::*,UI::WindowsAndMessaging::*};

struct State { host:usize, peer:usize, pid:u32, controller:usize, cookie:usize, original_input:u32, transparent:bool }
static STATE:Mutex<Option<State>>=Mutex::new(None);
static OWNER:Mutex<Option<tauri::WebviewWindow>>=Mutex::new(None);
static GENERATION:AtomicUsize=AtomicUsize::new(1);
static NEXT_COOKIE:AtomicUsize=AtomicUsize::new(1);
thread_local! { static DISCOVERING:std::cell::Cell<bool>=const {std::cell::Cell::new(false)}; }
const COOKIE:windows_sys::core::PCWSTR=windows_sys::core::w!("FrameScope.VerticalInput.Owner");
const INPUT_BITS:u32=WS_EX_TRANSPARENT|WS_EX_NOACTIVATE;

pub unsafe fn composition_peer(controller:&ICoreWebView2Controller,host:HWND)->Result<Option<(HWND,u32)>,String>{
    match controller.cast::<ICoreWebView2CompositionController>(){
        Ok(_)=>(),
        Err(error) if error.code()==windows::Win32::Foundation::E_NOINTERFACE=>return Ok(None),
        Err(error)=>return Err(error.to_string()),
    }
    let mut parent=windows::Win32::Foundation::HWND::default();
    controller.ParentWindow(&mut parent).map_err(|e|e.to_string())?;
    if parent.0!=host&&GetAncestor(parent.0 as _,GA_ROOT)!=host{return Err("竖排 WebView2 控制器归属不一致".into());}
    let mut pid=0;controller.CoreWebView2().and_then(|core|core.BrowserProcessId(&mut pid)).map_err(|e|e.to_string())?;
    if let Some(saved)=STATE.lock().unwrap_or_else(|p|p.into_inner()).as_ref(){
        let peer=saved.peer as HWND;let mut actual=0;GetWindowThreadProcessId(peer,&mut actual);
        if saved.host==host as usize&&saved.pid==pid&&actual==pid&&IsWindow(peer)!=0
            &&saved.controller==controller.as_raw() as usize&&GetPropW(peer,COOKIE) as usize==saved.cookie{return Ok(Some((peer,pid)));}
    }
    if DISCOVERING.with(|active|active.replace(true)){return Err("竖排输入窗口归属确认正在进行".into());}
    struct Discovery;
    impl Drop for Discovery{fn drop(&mut self){DISCOVERING.with(|active|active.set(false));}}
    let _discovery=Discovery;
    let generation=GENERATION.load(Ordering::SeqCst);
    let mut visible=windows::core::BOOL::default();controller.IsVisible(&mut visible).map_err(|e|e.to_string())?;
    let original=visible.as_bool();
    let result=(||{
        controller.SetIsVisible(true).map_err(|e|e.to_string())?;pump();
        let shown=browser_windows(pid,host);
        controller.SetIsVisible(false).map_err(|e|e.to_string())?;pump();
        let hidden=browser_windows(pid,host);
        controller.SetIsVisible(true).map_err(|e|e.to_string())?;pump();
        let restored=browser_windows(pid,host);
        if generation!=GENERATION.load(Ordering::SeqCst)||IsWindow(host)==0{return Err("竖排输入窗口确认期间宿主已销毁".into());}
        let candidates:Vec<usize>=shown.into_iter().filter(|h|!hidden.contains(h)&&restored.contains(h)).collect();
        if candidates.len()!=1{return Err(format!("无法唯一确认竖排 WebView2 输入窗口：{} 个随控制器变化的窗口",candidates.len()));}
        Ok(Some((candidates[0] as HWND,pid)))
    })();
    controller.SetIsVisible(original).map_err(|e|e.to_string())?;
    result
}

pub(super) unsafe fn pump(){
    let deadline=std::time::Instant::now()+std::time::Duration::from_millis(60);
    while std::time::Instant::now()<deadline{
        let mut message:MSG=std::mem::zeroed();
        while PeekMessageW(&mut message,std::ptr::null_mut(),0,0,PM_REMOVE)!=0{
            if message.message==WM_QUIT{PostQuitMessage(message.wParam as i32);return;}
            TranslateMessage(&message);DispatchMessageW(&message);
        }
        std::thread::sleep(std::time::Duration::from_millis(2));
    }
}
unsafe fn browser_windows(pid:u32,host:HWND)->Vec<usize>{
    struct Scan{pid:u32,host:HWND,rect:RECT,found:Vec<usize>}
    unsafe extern "system" fn collect(hwnd:HWND,ptr:isize)->i32{
        let scan=&mut *(ptr as *mut Scan);let mut pid=0;GetWindowThreadProcessId(hwnd,&mut pid);
        if hwnd==scan.host||pid!=scan.pid||IsWindowVisible(hwnd)==0{return 1;}
        let mut rect:RECT=std::mem::zeroed();GetWindowRect(hwnd,&mut rect);
        if (rect.left-scan.rect.left).abs()<=2&&(rect.top-scan.rect.top).abs()<=2&&
            (rect.right-scan.rect.right).abs()<=2&&(rect.bottom-scan.rect.bottom).abs()<=2{scan.found.push(hwnd as usize);}
        1
    }
    let mut rect:RECT=std::mem::zeroed();GetWindowRect(host,&mut rect);
    let mut scan=Scan{pid,host,rect,found:Vec::new()};EnumWindows(Some(collect),&mut scan as *mut _ as isize);scan.found
}

pub fn set(controller:&ICoreWebView2Controller,host:HWND,transparent:bool)->Result<(),String>{
    unsafe{
        let peer=composition_peer(controller,host)?;
        let mut state=STATE.lock().unwrap_or_else(|p|p.into_inner());
        let Some((peer,pid))=peer else{return Ok(());};
        let flags=GetWindowLongW(peer,GWL_EXSTYLE) as u32;
        if flags&WS_EX_LAYERED==0{return Err("竖排 WebView2 输入窗口缺少分层样式".into());}
        if state.as_ref().is_none_or(|s|s.host!=host as usize||s.peer!=peer as usize||s.pid!=pid||s.controller!=controller.as_raw() as usize||GetPropW(peer,COOKIE) as usize!=s.cookie){
            let cookie=NEXT_COOKIE.fetch_add(1,Ordering::SeqCst);
            if SetPropW(peer,COOKIE,cookie as HANDLE)==0{return Err(format!("无法登记竖排输入窗口身份：{}",std::io::Error::last_os_error()));}
            *state=Some(State{host:host as usize,peer:peer as usize,pid,controller:controller.as_raw() as usize,cookie,original_input:flags&INPUT_BITS,transparent});
        }
        let saved=state.as_mut().unwrap();
        let desired=if transparent{flags|INPUT_BITS}else{(flags&!INPUT_BITS)|saved.original_input};
        SetLastError(0);
        if desired!=flags&&SetWindowLongW(peer,GWL_EXSTYLE,desired as i32)==0&&GetLastError()!=0{
            return Err(format!("无法设置竖排 WebView2 鼠标穿透：{}",std::io::Error::last_os_error()));
        }
        if GetWindowLongW(peer,GWL_EXSTYLE) as u32&INPUT_BITS!=desired&INPUT_BITS{return Err("竖排 WebView2 鼠标穿透状态不一致".into());}
        saved.transparent=transparent;
        Ok(())
    }
}

pub fn desired(host:HWND)->bool{STATE.lock().unwrap_or_else(|p|p.into_inner()).as_ref().filter(|s|s.host==host as usize).map_or(true,|s|s.transparent)}
pub fn generation()->usize{GENERATION.load(Ordering::SeqCst)}
pub fn register(window:&tauri::WebviewWindow){GENERATION.fetch_add(1,Ordering::SeqCst);*OWNER.lock().unwrap_or_else(|p|p.into_inner())=Some(window.clone());}
pub fn refresh(window:&tauri::WebviewWindow){
    let generation=generation();
    let owned=window.clone();
    let _=window.with_webview(move|platform|{if generation!=self::generation(){return;} if let Ok(hwnd)=owned.hwnd(){
        if let Err(error)=set(&platform.controller(),hwnd.0 as _,desired(hwnd.0 as _)){log::warn!("竖排输入状态核对失败: {error}");}
    }});
}
pub fn restore(host:HWND){
    if desired(host){return;}
    let generation=generation();
    let window=OWNER.lock().unwrap_or_else(|p|p.into_inner()).clone();
    if let Some(window)=window.filter(|w|w.hwnd().is_ok_and(|hwnd|hwnd.0==host)){
        let owned=window.clone();
        let _=window.with_webview(move|platform|{
            if generation!=self::generation(){return;}
            // Window-style changes can synchronously send focus/capture messages.
            if let Some(state)=STATE.lock().unwrap_or_else(|p|p.into_inner()).as_mut(){state.transparent=true;}
            let result=(||->Result<(),String>{
                owned.set_ignore_cursor_events(true).map_err(|e|e.to_string())?;
                owned.set_focusable(false).map_err(|e|e.to_string())?;
                let hwnd=owned.hwnd().map_err(|e|e.to_string())?;
                set(&platform.controller(),hwnd.0 as _,true)?;
                unsafe{if SetLayeredWindowAttributes(hwnd.0 as _,0,255,LWA_ALPHA)==0{return Err(std::io::Error::last_os_error().to_string());}}
                Ok(())
            })();
            if let Err(error)=result{log::warn!("恢复竖排鼠标穿透失败: {error}");}
            else{use tauri::Emitter;let _=owned.emit("vertical-overlay-drag-mode",false);}
        });
    }
}
pub fn forget(host:HWND){
    let mut state=STATE.lock().unwrap_or_else(|p|p.into_inner());
    if state.as_ref().is_some_and(|s|s.host==host as usize){
        if let Some(saved)=state.as_ref(){unsafe{if GetPropW(saved.peer as HWND,COOKIE) as usize==saved.cookie{RemovePropW(saved.peer as HWND,COOKIE);}}}
        *state=None;
    }
    drop(state);
    GENERATION.fetch_add(1,Ordering::SeqCst);
    *OWNER.lock().unwrap_or_else(|p|p.into_inner())=None;
}
