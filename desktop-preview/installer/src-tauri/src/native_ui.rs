use std::{path::PathBuf,sync::{Mutex,mpsc::{self,Receiver}},ptr};
use windows_sys::Win32::{Foundation::*,UI::{WindowsAndMessaging::*,Controls::{Dialogs::*,BST_CHECKED},Input::KeyboardAndMouse::EnableWindow,Shell::*},Graphics::Gdi::*,System::{LibraryLoader::*,Com::*,SystemInformation::*}};
use crate::{installer,prerequisites};
use windows_sys::Win32::UI::{Controls::{DRAWITEMSTRUCT,ODS_DISABLED,ODS_SELECTED,ODS_FOCUS},Input::KeyboardAndMouse::SetFocus};
#[path="native_ui_view.rs"] mod view;
enum Event{Progress(String),Done(Result<String,String>,Option<PathBuf>)}
struct State {receiver:Option<Receiver<Event>>,local:Option<PathBuf>,busy:bool,done:bool,uninstall:bool,pawn_retry:Option<PathBuf>,page:u8,font:usize,tick:u32}
static STATE:Mutex<State>=Mutex::new(State{receiver:None,local:None,busy:false,done:false,uninstall:false,pawn_retry:None,page:0,font:0,tick:0});
const RETRY:i32=110;
const CUSTOM:i32=111;
const PATH_EDIT:i32=101;const CHOOSE:i32=102;const ACTION:i32=103;const STATUS:i32=104;const DESKTOP:i32=105;const AUTOSTART:i32=106;const DELETE:i32=107;const LOCAL:i32=108;const FINAL:i32=109;
fn wide(text:&str)->Vec<u16>{text.encode_utf16().chain(Some(0)).collect()}
unsafe fn text(hwnd:HWND,id:i32,value:&str){SetWindowTextW(GetDlgItem(hwnd,id),wide(value).as_ptr());}
unsafe fn read(hwnd:HWND,id:i32)->String{let control=GetDlgItem(hwnd,id);let mut value=vec![0;GetWindowTextLengthW(control) as usize+1];let n=GetWindowTextW(control,value.as_mut_ptr(),value.len() as i32);String::from_utf16_lossy(&value[..n as usize])}
pub fn destination(value:&str)->Result<PathBuf,String>{
    let path=PathBuf::from(value.trim());
    if !path.is_absolute(){return Err("请选择绝对路径".into());}
    let target=if path.file_name().is_some_and(|n|n.to_string_lossy().eq_ignore_ascii_case("FrameScope"))||path.join("FrameScopeHostCapabilities.json").is_file(){path}else{path.join("FrameScope")};
    installer::validate_install_target(&target)?;Ok(target)
}
pub fn preflight()->Result<(),String>{unsafe{
    let mut system:SYSTEM_INFO=std::mem::zeroed();GetNativeSystemInfo(&mut system);
    if system.Anonymous.Anonymous.wProcessorArchitecture!=PROCESSOR_ARCHITECTURE_AMD64{return Err("需要 Windows 10/11 x64 系统".into());}
    #[repr(C)]struct Version{size:u32,major:u32,minor:u32,build:u32,platform:u32,service:[u16;128]}
    #[link(name="ntdll")]extern "system"{fn RtlGetVersion(value:*mut Version)->i32;}
    let mut version:Version=std::mem::zeroed();version.size=std::mem::size_of_val(&version) as u32;
    if RtlGetVersion(&mut version)<0||version.major<10{return Err("需要 Windows 10 或 Windows 11".into());}
    if IsUserAnAdmin()==0{return Err("请以管理员权限重新打开安装器".into());}Ok(())
}}
unsafe fn control(hwnd:HWND,class:&str,label:&str,style:u32,id:i32,x:i32,y:i32,w:i32,h:i32)->HWND {
    let focusable=if class=="BUTTON"||class=="EDIT"{WS_TABSTOP}else{0};
    let style=if class=="BUTTON"&&style!=BS_AUTOCHECKBOX as u32{BS_OWNERDRAW as u32}else{style};
    let child=CreateWindowExW(0,wide(class).as_ptr(),wide(label).as_ptr(),WS_CHILD|WS_VISIBLE|style|focusable,x,y,w,h,hwnd,id as usize as _,ptr::null_mut(),ptr::null());
    if class=="BUTTON"&&style==BS_AUTOCHECKBOX as u32{windows_sys::Win32::UI::Controls::SetWindowTheme(child,wide("").as_ptr(),wide("").as_ptr());}
    SendMessageW(child,WM_SETFONT,GetStockObject(DEFAULT_GUI_FONT) as usize,1);child
}
unsafe fn choose_file(hwnd:HWND)->Option<PathBuf>{
    let mut buffer=[0u16;32768];let filter=wide("官方组件安装包 (*.exe)\0*.exe\0\0");
    let mut dialog:OPENFILENAMEW=std::mem::zeroed();dialog.lStructSize=std::mem::size_of_val(&dialog) as u32;dialog.hwndOwner=hwnd;
    dialog.lpstrFile=buffer.as_mut_ptr();dialog.nMaxFile=buffer.len() as u32;dialog.lpstrFilter=filter.as_ptr();dialog.Flags=OFN_FILEMUSTEXIST|OFN_PATHMUSTEXIST|OFN_NOCHANGEDIR;
    (GetOpenFileNameW(&mut dialog)!=0).then(||PathBuf::from(String::from_utf16_lossy(&buffer[..buffer.iter().position(|c|*c==0).unwrap_or(0)])))
}
unsafe fn choose_folder(hwnd:HWND)->Option<PathBuf>{
    let title=wide("选择安装位置；普通目录下将使用 FrameScope 子目录");let mut info:BROWSEINFOW=std::mem::zeroed();info.hwndOwner=hwnd;info.lpszTitle=title.as_ptr();info.ulFlags=BIF_RETURNONLYFSDIRS|BIF_NEWDIALOGSTYLE;
    let item=SHBrowseForFolderW(&info);if item.is_null(){return None;}
    let mut buffer=[0u16;260];let ok=SHGetPathFromIDListW(item,buffer.as_mut_ptr());CoTaskMemFree(item as _);
    (ok!=0).then(||PathBuf::from(String::from_utf16_lossy(&buffer[..buffer.iter().position(|c|*c==0).unwrap_or(0)])))
}
unsafe extern "system" fn window_proc(hwnd:HWND,message:u32,wparam:WPARAM,lparam:LPARAM)->LRESULT{
    match message{
        WM_PAINT=>{view::paint(hwnd);return 0;}
        WM_ERASEBKGND=>return 1,
        WM_SIZE|WM_SETTINGCHANGE=>{view::layout(hwnd);return 0;}
        WM_DPICHANGED=>{let r=&*(lparam as *const RECT);SetWindowPos(hwnd,ptr::null_mut(),r.left,r.top,r.right-r.left,r.bottom-r.top,SWP_NOZORDER|SWP_NOACTIVATE);return 0;}
        WM_DRAWITEM=>{view::draw_button(&*(lparam as *const DRAWITEMSTRUCT));return 1;}
        WM_CTLCOLORSTATIC|WM_CTLCOLOREDIT|WM_CTLCOLORBTN=>{
            let (bg,fg,_,_)=view::colors();let dc=wparam as HDC;SetTextColor(dc,fg);SetBkColor(dc,bg);SetDCBrushColor(dc,bg);return GetStockObject(DC_BRUSH) as isize;
        }
        // IsDialogMessage uses this to route Enter to the visible primary action.
        0x400=>return ((0x534bu32<<16)|ACTION as u32) as isize,
        WM_COMMAND=>{
            let id=(wparam&0xffff) as i32;
            if id==PATH_EDIT {let label=destination(&read(hwnd,PATH_EDIT)).map(|p|format!("最终安装目录：{}",p.display())).unwrap_or_else(|e|e);text(hwnd,FINAL,&label);return 0;}
            if STATE.lock().unwrap().busy{return 0;}
            if id==2{DestroyWindow(hwnd);return 0;}
            if id==CUSTOM{{let mut s=STATE.lock().unwrap();s.page=if s.page==1{0}else{1};}view::layout(hwnd);let page=STATE.lock().unwrap().page;SetFocus(GetDlgItem(hwnd,if page==1{PATH_EDIT}else{CUSTOM}));return 0;}
            if id==RETRY {
                let target=STATE.lock().unwrap().pawn_retry.clone();
                if let Some(target)=target {
                    let (sender,receiver)=mpsc::channel();
                    {let mut state=STATE.lock().unwrap();state.receiver=Some(receiver);state.busy=true;state.page=2;}
                    view::layout(hwnd);
                    EnableWindow(GetDlgItem(hwnd,RETRY),0);EnableWindow(GetDlgItem(hwnd,ACTION),0);
                    text(hwnd,STATUS,"正在重试 PawnIO 组件安装…");
                    std::thread::spawn(move||{
                        let result=prerequisites::pawnio(&target);
                        let retry=result.as_ref().err().map(|_|target);
                        let message=match result {Ok(true)=>"FrameScope 与 PawnIO 已就绪，组件要求重启。".into(),Ok(false)=>"FrameScope 与 PawnIO 已就绪。".into(),Err(error)=>format!("FrameScope 已安装。硬件监测受限：{error}\r\n可继续重试 PawnIO 或关闭向导。")};
                        let _=sender.send(Event::Done(Ok(message),retry));
                    });
                }
                return 0;
            }
            if id==CHOOSE {if let Some(path)=choose_folder(hwnd){text(hwnd,PATH_EDIT,&path.to_string_lossy());}return 0;}
            if id==LOCAL {if let Some(path)=choose_file(hwnd){text(hwnd,STATUS,&format!("已选择组件安装包：{}\r\n开始时将验证签名、类型和版本。",path.display()));STATE.lock().unwrap().local=Some(path);}return 0;}
            if id==ACTION {
                if STATE.lock().unwrap().done{DestroyWindow(hwnd);return 0;}
                let uninstall=STATE.lock().unwrap().uninstall;
                let target=if uninstall{Ok(PathBuf::from(installer::get_state().install_path))}else{destination(&read(hwnd,PATH_EDIT))};
                let target=match target.and_then(|target|{preflight()?;Ok(target)}){Ok(path)=>path,Err(error)=>{show_preflight_error(hwnd,&error);return 0;}};
                let desktop=SendMessageW(GetDlgItem(hwnd,DESKTOP),BM_GETCHECK,0,0)==BST_CHECKED as isize;
                let autostart=SendMessageW(GetDlgItem(hwnd,AUTOSTART),BM_GETCHECK,0,0)==BST_CHECKED as isize;
                let delete_data=SendMessageW(GetDlgItem(hwnd,DELETE),BM_GETCHECK,0,0)==BST_CHECKED as isize;
                let (sender,receiver)=mpsc::channel();let local;
                {let mut state=STATE.lock().unwrap();state.receiver=Some(receiver);state.busy=true;state.page=2;local=state.local.clone();}
                view::layout(hwnd);
                for id in [ACTION,PATH_EDIT,CHOOSE,LOCAL,DESKTOP,AUTOSTART,DELETE]{EnableWindow(GetDlgItem(hwnd,id),0);}
                text(hwnd,STATUS,"正在检查安装状态…");
                std::thread::spawn(move||{
                    let progress=|value:&str|{let _=sender.send(Event::Progress(value.into()));};
                    let mut pawn_retry=None;
                    let result=(||->Result<String,String>{
                        if uninstall {progress("正在停止当前安装实例并安全卸载…");installer::uninstall_headless(delete_data)?;return Ok(if delete_data{"已卸载，并删除本产品数据。其他文件和共享组件已保留。"}else{"已卸载。配置、报告、其他文件和共享组件已保留。"}.into());}
                        installer::validate_install_destination(&target)?;
                        let reboot=prerequisites::prepare(&target,local.as_deref(),&progress)?;
                        installer::install_shared(&target.to_string_lossy(),desktop,autostart,&|index,name|progress(&format!("安装文件 {index}：{name}")))?;
                        progress("正在检查 PawnIO…");
                        let pawn=prerequisites::pawnio(&target);
                        let mut message="FrameScope 安装完成。".to_string();
                        if reboot {message.push_str("组件需要重启，请在方便时重启电脑。");}
                        match pawn{Ok(true) if !reboot=>message.push_str("PawnIO 需要重启，请在方便时重启电脑。"),Err(error)=>{pawn_retry=Some(target.clone());message.push_str(&format!("\r\nPawnIO 未就绪：{error}\r\n硬件监测功能受限，可点击“重试 PawnIO”。"));},_=>{}}
                        prerequisites::complete(&target)?;
                        Ok(message)
                    })();let _=sender.send(Event::Done(result,pawn_retry));
                });return 0;
            }
        }
        WM_TIMER=>{
            {let mut state=STATE.lock().unwrap();if state.busy{state.tick=state.tick.wrapping_add(1);InvalidateRect(hwnd,ptr::null(),0);}}
            let events={let state=STATE.lock().unwrap();state.receiver.as_ref().map(|r|r.try_iter().collect::<Vec<_>>()).unwrap_or_default()};
            for event in events{match event{Event::Progress(value)=>text(hwnd,STATUS,&value),Event::Done(result,pawn_retry)=>{
                let success=result.is_ok();text(hwnd,STATUS,&result.unwrap_or_else(|error|format!("未完成：{error}\r\n可重试；已有文件由安装事务保护。")));
                ShowWindow(GetDlgItem(hwnd,RETRY),if pawn_retry.is_some(){SW_SHOW}else{SW_HIDE});
                EnableWindow(GetDlgItem(hwnd,RETRY),1);
                {let mut state=STATE.lock().unwrap();state.busy=false;state.done=success;state.page=if success{3}else{2};state.receiver=None;state.pawn_retry=pawn_retry;}
                for id in [ACTION,PATH_EDIT,CHOOSE,LOCAL,DESKTOP,AUTOSTART,DELETE]{EnableWindow(GetDlgItem(hwnd,id),1);}
                text(hwnd,ACTION,if success{"完成"}else{"重试"});
                view::layout(hwnd);SetFocus(GetDlgItem(hwnd,ACTION));
            }}}
            return 0;
        }
        WM_CLOSE=>{if STATE.lock().unwrap().busy{text(hwnd,STATUS,"正在进行安装事务，请等待当前操作结束。请勿强制退出。");return 0;}DestroyWindow(hwnd);return 0;}
        WM_DESTROY=>{let mut state=STATE.lock().unwrap();if state.font!=0{DeleteObject(state.font as _);state.font=0;}PostQuitMessage(0);return 0;}
        _=>{}
    }DefWindowProcW(hwnd,message,wparam,lparam)
}
unsafe fn show_preflight_error(hwnd:HWND,error:&str){
    STATE.lock().unwrap().page=1;
    text(hwnd,STATUS,error);view::layout(hwnd);
    SetFocus(GetDlgItem(hwnd,ACTION));
}
pub fn run(){unsafe{
    CoInitializeEx(ptr::null(),COINIT_APARTMENTTHREADED as u32);
    let uninstall=installer::get_launch_mode()=="uninstall";{let mut s=STATE.lock().unwrap();*s=State{receiver:None,local:None,busy:false,done:false,uninstall,pawn_retry:None,page:if uninstall{1}else{0},font:0,tick:0};}
    let instance=GetModuleHandleW(ptr::null());let class=wide("FrameScopeNativeInstaller");
    let mut wc:WNDCLASSW=std::mem::zeroed();wc.lpfnWndProc=Some(window_proc);wc.hInstance=instance;wc.lpszClassName=class.as_ptr();wc.hbrBackground=(COLOR_WINDOW+1) as _;wc.hCursor=LoadCursorW(ptr::null_mut(),IDC_ARROW);RegisterClassW(&wc);
    let title=wide(if uninstall{"卸载 FrameScope"}else{"安装 FrameScope"});
    let dc=GetDC(ptr::null_mut());let scale=GetDeviceCaps(dc,LOGPIXELSX as i32) as f64/96.0;ReleaseDC(ptr::null_mut(),dc);
    let mut work:RECT=std::mem::zeroed();SystemParametersInfoW(SPI_GETWORKAREA,0,&mut work as *mut _ as _,0);
    let width=((1000.0*scale) as i32).min(work.right-work.left-40);let height=((660.0*scale) as i32).min(work.bottom-work.top-40);
    let hwnd=CreateWindowExW(WS_EX_DLGMODALFRAME,class.as_ptr(),title.as_ptr(),WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,work.left+(work.right-work.left-width)/2,work.top+(work.bottom-work.top-height)/2,width,height,ptr::null_mut(),ptr::null_mut(),instance,ptr::null());
    if hwnd.is_null(){return;}
    if uninstall {
        control(hwnd,"STATIC",&format!("安装目录：{}",installer::get_state().install_path),0,FINAL,24,65,610,45);
        control(hwnd,"BUTTON","同时删除本产品配置和报告（默认保留）",BS_AUTOCHECKBOX as u32,DELETE,24,120,610,26);
    }else{
        let pending=prerequisites::pending_target();
        let initial=pending.as_ref().ok().and_then(|path|path.as_ref()).map(|path|path.to_string_lossy().into_owned()).unwrap_or_else(installer::get_default_install_path);
        control(hwnd,"EDIT",&initial,WS_BORDER|ES_AUTOHSCROLL as u32,PATH_EDIT,24,90,520,28);
        control(hwnd,"BUTTON","浏览…",BS_PUSHBUTTON as u32,CHOOSE,554,90,80,28);
        control(hwnd,"STATIC","",0,FINAL,24,125,610,40);
        let target=destination(&initial).map(|p|p.display().to_string()).unwrap_or_default();text(hwnd,FINAL,&format!("最终安装目录：{target}"));
        control(hwnd,"BUTTON","创建桌面快捷方式",BS_AUTOCHECKBOX as u32,DESKTOP,24,175,285,25);SendMessageW(GetDlgItem(hwnd,DESKTOP),BM_SETCHECK,BST_CHECKED as usize,0);
        control(hwnd,"BUTTON","开机启动",BS_AUTOCHECKBOX as u32,AUTOSTART,330,175,280,25);
        control(hwnd,"BUTTON","选择官方离线组件安装包…",BS_PUSHBUTTON as u32,LOCAL,24,215,300,30);
    }
    control(hwnd,"STATIC",if uninstall{"只删除归属明确的 FrameScope 文件；不会删除其他游戏或共享运行组件。"}else{"将检测并补齐 WebView2、.NET Framework 和 PawnIO。需要联网下载缺失组件；不会强制重启。"},0,STATUS,24,265,610,100);
    control(hwnd,"BUTTON",if uninstall{"安全卸载"}else{"开始安装  →"},BS_DEFPUSHBUTTON as u32,ACTION,500,380,132,35);
    control(hwnd,"BUTTON","自定义设置",BS_PUSHBUTTON as u32,CUSTOM,0,0,1,1);
    let retry=control(hwnd,"BUTTON","重试 PawnIO",BS_PUSHBUTTON as u32,RETRY,24,380,160,35);ShowWindow(retry,SW_HIDE);
    view::layout(hwnd);SetTimer(hwnd,1,100,None);ShowWindow(hwnd,SW_SHOW);UpdateWindow(hwnd);SetFocus(GetDlgItem(hwnd,ACTION));
    let mut message:MSG=std::mem::zeroed();while GetMessageW(&mut message,ptr::null_mut(),0,0)>0{if IsDialogMessageW(hwnd,&message)==0{TranslateMessage(&message);DispatchMessageW(&message);}}
    CoUninitialize();
}}
#[cfg(test)]mod tests{use super::*;#[test]fn parent_and_drive_root_get_dedicated_child(){assert_eq!(destination(r"C:\").unwrap(),PathBuf::from(r"C:\FrameScope"));assert_eq!(destination(r"C:\game").unwrap(),PathBuf::from(r"C:\game\FrameScope"));assert_eq!(destination(r"C:\game\FrameScope").unwrap(),PathBuf::from(r"C:\game\FrameScope"));assert!(destination("relative").is_err());}}

#[cfg(test)]
#[test]
#[ignore = "explicit native UI preview; never presses install, uninstall or component actions"]
fn native_wizard_preview_does_not_require_webview() {
    let probe=std::thread::spawn(||unsafe {
        let deadline=std::time::Instant::now()+std::time::Duration::from_secs(10);
        let hwnd=loop {
            let hwnd=FindWindowW(wide("FrameScopeNativeInstaller").as_ptr(),wide("安装 FrameScope").as_ptr());
            let mut pid=0;
            if !hwnd.is_null()&&GetWindowThreadProcessId(hwnd,&mut pid)!=0&&pid==std::process::id()&&!GetDlgItem(hwnd,ACTION).is_null(){break hwnd;}
            assert!(std::time::Instant::now()<deadline,"native preview did not appear");
            std::thread::sleep(std::time::Duration::from_millis(30));
        };
        text(hwnd,PATH_EDIT,r"C:\game");
        let hold=std::env::var("FRAMESCOPE_WIZARD_PREVIEW_HOLD_MS").ok().and_then(|v|v.parse::<u64>().ok()).unwrap_or(0).min(60000);
        std::thread::sleep(std::time::Duration::from_millis(hold));
        let destination=read(hwnd,FINAL);
        let action=read(hwnd,ACTION);
        SendMessageW(hwnd,WM_COMMAND,CUSTOM as usize,0);
        std::thread::sleep(std::time::Duration::from_millis(hold));
        let dependency_message=read(hwnd,STATUS);
        let focusable=[PATH_EDIT,CHOOSE,LOCAL,ACTION].iter().all(|id|GetWindowLongW(GetDlgItem(hwnd,*id),GWL_STYLE) as u32&WS_TABSTOP!=0);
        // Invalid path fails before preflight/components; no install action can run.
        SendMessageW(hwnd,WM_COMMAND,CUSTOM as usize,0);
        text(hwnd,PATH_EDIT,"relative");
        SendMessageW(hwnd,WM_COMMAND,ACTION as usize,0);
        assert_ne!(IsWindowVisible(GetDlgItem(hwnd,STATUS)),0);
        assert!(read(hwnd,STATUS).contains("绝对路径"));
        text(hwnd,PATH_EDIT,r"C:\game");
        // Feed worker results through the real event handler without starting a worker.
        let (sender,receiver)=mpsc::channel();
        {let mut s=STATE.lock().unwrap();s.receiver=Some(receiver);s.page=2;s.busy=true;}
        SendMessageW(hwnd,WM_SIZE,0,0);
        sender.send(Event::Progress("预览：正在检查组件".into())).unwrap();
        SendMessageW(hwnd,WM_TIMER,1,0);
        assert_eq!(read(hwnd,STATUS),"预览：正在检查组件");
        assert_eq!(IsWindowVisible(GetDlgItem(hwnd,CUSTOM)),0);
        sender.send(Event::Done(Err("预览：网络不可用".into()),None)).unwrap();
        SendMessageW(hwnd,WM_TIMER,1,0);
        assert_ne!(IsWindowVisible(GetDlgItem(hwnd,CUSTOM)),0);
        assert_ne!(IsWindowVisible(GetDlgItem(hwnd,LOCAL)),0);
        assert_eq!(read(hwnd,ACTION),"重试");
        SendMessageW(hwnd,WM_COMMAND,CUSTOM as usize,0);
        assert_ne!(IsWindowVisible(GetDlgItem(hwnd,PATH_EDIT)),0);
        let (sender,receiver)=mpsc::channel();
        {let mut s=STATE.lock().unwrap();s.receiver=Some(receiver);s.page=2;s.busy=true;}
        sender.send(Event::Done(Ok("预览：安装完成，PawnIO 受限，可重试".into()),Some(PathBuf::from(r"C:\game\FrameScope")))).unwrap();
        SendMessageW(hwnd,WM_TIMER,1,0);
        assert_eq!(read(hwnd,ACTION),"完成");
        assert_ne!(IsWindowVisible(GetDlgItem(hwnd,RETRY)),0);
        assert_eq!(IsWindowVisible(GetDlgItem(hwnd,PATH_EDIT)),0);
        PostMessageW(hwnd,WM_CLOSE,0,0);
        (destination,action,dependency_message,focusable)
    });
    run();
    let (destination,action,message,focusable)=probe.join().unwrap();
    assert_eq!(destination,r"最终安装目录：C:\game\FrameScope");
    assert_eq!(action,"开始安装  →");assert!(focusable);
    assert!(message.contains("WebView2")&&message.contains("PawnIO"));
}
