//! Native vector artwork and layout. No web runtime, remote assets or install actions.
use super::*;
const BG:COLORREF=0x211810;
const PANEL:COLORREF=0x30261a;
const GREEN:COLORREF=0xc2df98;
const FG:COLORREF=0xf5f4e8;
const MUTED:COLORREF=0xc2b4a0;

pub unsafe fn high_contrast()->bool {
    #[repr(C)]struct Contrast{size:u32,flags:u32,scheme:*mut u16}
    let mut value=Contrast{size:std::mem::size_of::<Contrast>() as u32,flags:0,scheme:ptr::null_mut()};
    SystemParametersInfoW(SPI_GETHIGHCONTRAST,value.size,&mut value as *mut _ as _,0)!=0&&value.flags&1!=0
}
pub unsafe fn colors()->(COLORREF,COLORREF,COLORREF,COLORREF){
    if high_contrast(){(GetSysColor(COLOR_WINDOW),GetSysColor(COLOR_WINDOWTEXT),GetSysColor(COLOR_HIGHLIGHT),GetSysColor(COLOR_HIGHLIGHTTEXT))}
    else{(BG,FG,GREEN,BG)}
}
unsafe fn rectangle(dc:HDC,x:i32,y:i32,w:i32,h:i32,color:COLORREF){
    let brush=CreateSolidBrush(color);FillRect(dc,&RECT{left:x,top:y,right:x+w,bottom:y+h},brush);DeleteObject(brush as _);
}
unsafe fn card(dc:HDC,x:i32,y:i32,w:i32,h:i32,color:COLORREF){
    let brush=CreateSolidBrush(color);let old=SelectObject(dc,brush as _);let pen=SelectObject(dc,GetStockObject(NULL_PEN));
    RoundRect(dc,x,y,x+w,y+h,24,24);SelectObject(dc,pen);SelectObject(dc,old);DeleteObject(brush as _);
}
unsafe fn label(dc:HDC,value:&str,x:i32,y:i32,w:i32,h:i32,size:i32,bold:bool,color:COLORREF){
    let font=CreateFontW(-size,0,0,0,if bold{700}else{400},0,0,0,DEFAULT_CHARSET as u32,0,0,CLEARTYPE_QUALITY as u32,0,wide("Microsoft YaHei UI").as_ptr());
    let old=SelectObject(dc,font as _);SetBkMode(dc,TRANSPARENT as i32);SetTextColor(dc,color);
    let mut rect=RECT{left:x,top:y,right:x+w,bottom:y+h};let value=wide(value);
    DrawTextW(dc,value.as_ptr(),(value.len()-1) as i32,&mut rect,DT_LEFT|DT_WORDBREAK|DT_NOPREFIX);
    SelectObject(dc,old);DeleteObject(font as _);
}
unsafe fn line(dc:HDC,points:&[POINT],color:COLORREF,width:i32){
    let pen=CreatePen(PS_SOLID,width,color);let old=SelectObject(dc,pen as _);Polyline(dc,points.as_ptr(),points.len() as i32);SelectObject(dc,old);DeleteObject(pen as _);
}
pub unsafe fn paint(hwnd:HWND){
    let mut ps:PAINTSTRUCT=std::mem::zeroed();let dc=BeginPaint(hwnd,&mut ps);let saved=SaveDC(dc);
    let mut client:RECT=std::mem::zeroed();GetClientRect(hwnd,&mut client);
    SetMapMode(dc,MM_ANISOTROPIC);SetWindowExtEx(dc,1000,620,ptr::null_mut());SetViewportExtEx(dc,client.right,client.bottom,ptr::null_mut());
    let (bg,fg,accent,_)=colors();rectangle(dc,0,0,1000,620,bg);
    let (page,busy,done,uninstall,tick)={let s=STATE.lock().unwrap();(s.page,s.busy,s.done,s.uninstall,s.tick)};
    let muted=if high_contrast(){fg}else{MUTED};let panel=if high_contrast(){bg}else{PANEL};
    if page==0 {
        label(dc,"FrameScope",44,35,400,50,30,true,fg);
        line(dc,&[POINT{x:44,y:112},POINT{x:60,y:112},POINT{x:66,y:96},POINT{x:75,y:127},POINT{x:83,y:112},POINT{x:102,y:112}],accent,3);
        label(dc,"为每一次游戏，留下清晰记录",120,99,390,30,16,false,muted);
        label(dc,"把流畅，",44,175,480,65,44,true,fg);
        label(dc,"看得更清楚。",44,242,485,65,44,true,accent);
        label(dc,"实时帧率、硬件状态和性能报告",46,333,480,34,20,false,fg);
        label(dc,"从现在开始，了解你的每一帧。",46,378,470,32,16,false,muted);
        card(dc,570,115,385,280,panel);label(dc,"游戏性能",594,137,220,35,21,true,fg);
        label(dc,"界面示意",846,145,90,24,13,false,muted);label(dc,"FRAME TIME",596,194,300,30,13,false,muted);
        for y in [238,270,302]{rectangle(dc,596,y,334,1,if high_contrast(){fg}else{0x4b4430});}
        line(dc,&[POINT{x:596,y:315},POINT{x:621,y:309},POINT{x:645,y:314},POINT{x:675,y:293},POINT{x:700,y:288},POINT{x:729,y:296},POINT{x:751,y:276},POINT{x:775,y:279},POINT{x:801,y:267},POINT{x:824,y:274},POINT{x:853,y:256},POINT{x:877,y:259},POINT{x:902,y:246},POINT{x:930,y:249}],accent,2);
        label(dc,"性能报告  ↗",810,350,130,30,16,false,accent);
        card(dc,532,366,238,80,if high_contrast(){bg}else{0x343e1c});label(dc,"悬浮监测",556,380,190,30,21,true,fg);label(dc,"专注游戏，数据随行",556,414,200,25,14,false,muted);
        rectangle(dc,44,474,912,1,if high_contrast(){fg}else{0x4b4430});
        label(dc,"安装位置",46,491,400,22,14,false,muted);
    }else{
        rectangle(dc,0,0,230,620,panel);label(dc,"FrameScope",28,38,200,44,26,true,fg);
        let steps=if uninstall{["卸载设置","安全卸载","完成"]}else{["安装设置","组件检查与安装","完成"]};
        for (i,step) in steps.iter().enumerate(){let selected=page as usize==i+1;let y=150+i as i32*78;
            if selected{card(dc,16,y-10,198,56,accent);}
            label(dc,&format!("{}   {}",i+1,step),28,y,190,40,17,selected,if selected{colors().3}else{muted});
        }
        label(dc,&format!("版本 {}\n原生安装向导",installer::get_app_version()),28,525,185,60,13,false,muted);
        let title=if page==1{if uninstall{"安全卸载 FrameScope"}else{"让一切准备就绪"}}else if busy{"正在处理，请稍候"}else if done{"已完成"}else{"还差一步"};
        label(dc,title,276,45,660,52,31,true,fg);
        label(dc,if page==1{"确认设置后继续，向导会说明每一步的结果。"}else{"组件状态与文件安装结果会在这里显示。"},278,108,660,32,16,false,muted);
        if page==1&&!uninstall{label(dc,"安装位置",278,166,650,28,17,true,fg);}
        if page>=2{
            card(dc,272,170,680,280,panel);
            if busy {let x=292+(tick%100) as i32*5;rectangle(dc,292,421,638,3,if high_contrast(){fg}else{0x4b4430});rectangle(dc,x,421,60,3,accent);}
        }
        rectangle(dc,272,508,680,1,if high_contrast(){fg}else{0x4b4430});
    }
    RestoreDC(dc,saved);EndPaint(hwnd,&ps);
}
pub unsafe fn layout(hwnd:HWND){
    let mut rect:RECT=std::mem::zeroed();GetClientRect(hwnd,&mut rect);let sx=rect.right as f64/1000.0;let sy=rect.bottom as f64/620.0;
    let (page,busy,uninstall,retry)={let s=STATE.lock().unwrap();(s.page,s.busy,s.uninstall,s.pawn_retry.is_some())};
    let place=|id,x,y,w,h,visible|{let child=GetDlgItem(hwnd,id);if !child.is_null(){MoveWindow(child,(x as f64*sx) as i32,(y as f64*sy) as i32,(w as f64*sx) as i32,(h as f64*sy) as i32,1);ShowWindow(child,if visible{SW_SHOW}else{SW_HIDE});}};
    place(PATH_EDIT,278,202,550,38,page==1&&!uninstall);place(CHOOSE,840,202,112,38,page==1&&!uninstall);
    if page==0{place(FINAL,46,521,425,40,true);place(CUSTOM,494,510,175,48,true);place(ACTION,727,510,229,54,true);place(DESKTOP,278,578,220,28,true);place(AUTOSTART,46,578,220,28,true);}
    else{place(FINAL,278,258,668,65,page==1);place(CUSTOM,278,535,140,44,!uninstall&&(page==1||(page==2&&!busy)));place(ACTION,756,535,196,46,true);place(DESKTOP,278,337,290,30,page==1&&!uninstall);place(AUTOSTART,603,337,300,30,page==1&&!uninstall);}
    place(DELETE,278,337,660,30,page==1&&uninstall);
    place(STATUS,if page==1{278}else{292},if page==1{390}else{194},if page==1{668}else{638},if page==1{65}else{205},page!=0);
    place(LOCAL,278,465,360,32,!uninstall&&((page==1)||(page==2&&!busy)));place(RETRY,442,535,280,44,retry&&!busy);
    let font=CreateFontW(-(16.0*sy) as i32,0,0,0,400,0,0,0,DEFAULT_CHARSET as u32,0,0,CLEARTYPE_QUALITY as u32,0,wide("Microsoft YaHei UI").as_ptr());
    for id in [PATH_EDIT,CHOOSE,ACTION,STATUS,DESKTOP,AUTOSTART,DELETE,LOCAL,FINAL,RETRY,CUSTOM]{let child=GetDlgItem(hwnd,id);if !child.is_null(){SendMessageW(child,WM_SETFONT,font as usize,1);}}
    {let mut s=STATE.lock().unwrap();if s.font!=0{DeleteObject(s.font as _);}s.font=font as usize;}
    text(hwnd,CUSTOM,if page==0{"自定义设置"}else if page==1{"返回欢迎页"}else{"修改安装设置"});
    InvalidateRect(hwnd,ptr::null(),1);
}
pub unsafe fn draw_button(item:&DRAWITEMSTRUCT){
    let (bg,fg,accent,accent_text)=colors();let primary=item.CtlID==ACTION as u32;
    let disabled=item.itemState&ODS_DISABLED!=0;let pressed=item.itemState&ODS_SELECTED!=0;
    let color=if disabled{if high_contrast(){bg}else{PANEL}}else if primary{accent}else if pressed{if high_contrast(){accent}else{0x56452b}}else{if high_contrast(){bg}else{PANEL}};
    let brush=CreateSolidBrush(color);FillRect(item.hDC,&item.rcItem,brush);DeleteObject(brush as _);
    let old=SelectObject(item.hDC,SendMessageW(item.hwndItem,WM_GETFONT,0,0) as _);
    SetBkMode(item.hDC,TRANSPARENT as i32);SetTextColor(item.hDC,if disabled{GetSysColor(COLOR_GRAYTEXT)}else if primary||(pressed&&high_contrast()){accent_text}else{fg});
    let mut value=vec![0;GetWindowTextLengthW(item.hwndItem) as usize+1];let count=GetWindowTextW(item.hwndItem,value.as_mut_ptr(),value.len() as i32);let mut rect=item.rcItem;
    DrawTextW(item.hDC,value.as_ptr(),count,&mut rect,DT_CENTER|DT_VCENTER|DT_SINGLELINE|DT_NOPREFIX);
    if high_contrast(){let border=CreateSolidBrush(if disabled{GetSysColor(COLOR_GRAYTEXT)}else{fg});FrameRect(item.hDC,&rect,border);DeleteObject(border as _);}
    if item.itemState&ODS_FOCUS!=0{InflateRect(&mut rect,-4,-4);DrawFocusRect(item.hDC,&rect);}
    SelectObject(item.hDC,old);
}
