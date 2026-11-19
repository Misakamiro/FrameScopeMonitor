//! Declarative layouts, full-monitor coordinates and a click-through WebView.
use serde::{Deserialize,Serialize};
use serde_json::{json,Value};
use std::{path::Path,sync::{Mutex,atomic::{AtomicBool,Ordering}},time::Instant};
use tauri::{Manager,WebviewUrl,WebviewWindowBuilder};

static STORE:Mutex<()>=Mutex::new(());
pub static ACTIVE:AtomicBool=AtomicBool::new(false);
#[derive(Clone,Serialize,Deserialize,Debug)]
#[serde(deny_unknown_fields)]
pub struct Style {pub font:String,pub size:f64,pub weight:u16,pub color:String,pub align:String,pub background:String,
    pub border_color:String,pub border_width:f64,pub radius:f64,pub italic:bool,pub decoration:String,pub opacity:f64}
#[derive(Clone,Serialize,Deserialize,Debug)]
#[serde(deny_unknown_fields)]
pub struct Component {pub id:String,pub kind:String,pub name:String,pub text:String,pub metric:String,
    pub binding:Option<crate::overlay_items::SensorBinding>,pub x:f64,pub y:f64,pub width:f64,pub height:f64,
    pub minimum:f64,pub maximum:f64,pub style:Style,#[serde(default,skip_serializing_if="Option::is_none")]pub presentation:Option<Presentation>}
#[derive(Clone,Serialize,Deserialize,Debug)]
#[serde(deny_unknown_fields)]
pub struct Presentation {pub show_name:bool,pub show_value:bool,pub show_unit:bool,pub decimals:u8,pub duration:u16,
    pub line_width:f64,pub fill:bool,pub auto_range:bool,pub direction:String}
#[derive(Clone,Serialize,Deserialize,Debug)]
#[serde(deny_unknown_fields)]
pub struct Layout {pub monitor:String,pub design_width:f64,pub design_height:f64,pub components:Vec<Component>}
#[derive(Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SavedLayout {pub id:String,pub name:String,pub revision:u64,pub layout:Layout}
#[derive(Clone,Serialize,Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Store {pub schema:u8,pub revision:u64,pub draft:Layout,pub applied:Option<Layout>,pub templates:Vec<Component>,
    #[serde(default)]pub layouts:Vec<SavedLayout>,#[serde(default)]pub editing_id:Option<String>,
    #[serde(default)]pub applied_id:Option<String>,#[serde(default)]pub applied_revision:Option<u64>}
#[derive(Clone,Serialize,Deserialize,Debug)]
pub struct Display {pub id:String,pub name:String,pub x:i32,pub y:i32,pub width:u32,pub height:u32,pub scale:f64}

pub fn displays(app:&tauri::AppHandle)->Result<Vec<Display>,String>{
    app.available_monitors().map_err(|e|e.to_string()).map(|list|list.into_iter().map(|monitor|{
        let name=monitor.name().cloned().unwrap_or_default();let position=monitor.position();let size=monitor.size();
        Display{id:name.clone(),name,x:position.x,y:position.y,width:size.width,height:size.height,scale:monitor.scale_factor()}
    }).collect())
}
fn color(value:&str)->bool{matches!(value.len(),7|9)&&value.starts_with('#')&&value.as_bytes()[1..].iter().all(u8::is_ascii_hexdigit)}
fn range(value:f64,min:f64,max:f64)->bool{value.is_finite()&&(min..=max).contains(&value)}
fn component(value:&Component)->Result<(),String>{
    if value.id.is_empty()||value.id.len()>80||value.name.chars().count()>80||value.text.chars().count()>500
        ||!matches!(value.kind.as_str(),"value"|"text"|"progress"|"trend")||!range(value.x,0.0,1.0)||!range(value.y,0.0,1.0)
        ||!range(value.width,16.0,16384.0)||!range(value.height,16.0,16384.0)
        ||!range(value.minimum,-1e9,1e9)||!range(value.maximum,-1e9,1e9)||value.maximum<=value.minimum{return Err("组件尺寸、内容或范围无效".into());}
    let style=&value.style;
    if style.font.chars().count()>100||!range(style.size,6.0,240.0)||!(100..=900).contains(&style.weight)
        ||![&style.color,&style.background,&style.border_color].iter().all(|v|color(v))
        ||!matches!(style.align.as_str(),"left"|"center"|"right")||!matches!(style.decoration.as_str(),"none"|"underline"|"line-through")
        ||!range(style.border_width,0.0,20.0)||!range(style.radius,0.0,500.0)||!range(style.opacity,0.0,1.0){return Err("组件样式无效".into());}
    let special=matches!(value.metric.as_str(),"fps"|"fps_1low"|"fps_01low"|"time"|"delta_password"|"game_ping"|"cpu_core_clocks"|"cpu_core_voltages");
    if value.kind!="text"&&!special&&crate::overlay_sensors::category_unit(&value.metric).is_none(){return Err("组件传感器类别无效".into());}
    if let Some(binding)=&value.binding {
        if binding.category!=value.metric||binding.device_id.is_empty()||binding.device_id.len()>512||binding.sensor_id.is_empty()||binding.sensor_id.len()>512{return Err("组件传感器绑定无效".into());}
    }
    if let Some(p)=&value.presentation{if p.decimals>4||!(5..=300).contains(&p.duration)||!range(p.line_width,0.5,12.0)
        ||!matches!(p.direction.as_str(),"right"|"left"|"up"|"down"){return Err("组件图表设置无效".into());}}
    Ok(())
}
pub fn validate(layout:&Layout)->Result<(),String>{
    if layout.monitor.len()>256||!range(layout.design_width,320.0,16384.0)||!range(layout.design_height,200.0,16384.0)||layout.components.len()>128{return Err("画布或组件数量无效".into());}
    let mut ids=std::collections::HashSet::new();
    for item in &layout.components{component(item)?;if !ids.insert(&item.id){return Err("组件ID重复".into());}}
    Ok(())
}
fn empty()->Store{Store{schema:2,revision:0,draft:Layout{monitor:String::new(),design_width:1920.0,design_height:1080.0,components:vec![]},applied:None,templates:vec![],layouts:vec![],editing_id:None,applied_id:None,applied_revision:None}}
fn persist(path:&Path,store:&Store)->Result<(),String>{framescope_update::atomic_json(path,&serde_json::to_value(store).map_err(|e|e.to_string())?)}
fn load_at(path:&Path)->Result<Store,String>{
    let legacy=path.with_file_name("custom-overlay-v1.json");let migrating=!path.exists()&&legacy.exists();
    let source=if migrating{&legacy}else{path};
    if !source.exists(){return Ok(empty());}
    if std::fs::metadata(source).map_err(|e|e.to_string())?.len()>8*1024*1024{return Err("自定义布局文件过大".into());}
    let mut value:Store=serde_json::from_slice(&std::fs::read(source).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
    if value.schema!=if migrating{1}else{2}{return Err("自定义布局版本不支持".into());}
    validate(&value.draft)?;if let Some(layout)=&value.applied{validate(layout)?;}
    for item in &value.templates{component(item)?;}
    if migrating{
        value.schema=2;value.editing_id=Some("migrated-draft".into());
        let same=value.applied.as_ref().is_some_and(|applied|serde_json::to_value(applied).ok()==serde_json::to_value(&value.draft).ok());
        value.layouts.push(SavedLayout{id:"migrated-draft".into(),name:if same{"原自定义布局"}else{"原未应用草稿"}.into(),revision:1,layout:value.draft.clone()});
        if let Some(applied)=&value.applied{
            let id=if same{"migrated-draft"}else{"migrated-applied"};
            if !same{value.layouts.push(SavedLayout{id:id.into(),name:"原已应用布局".into(),revision:1,layout:applied.clone()});}
            value.applied_id=Some(id.into());value.applied_revision=Some(1);
        }
        persist(path,&value)?;
    }
    let mut ids=std::collections::HashSet::new();
    for saved in &value.layouts{validate(&saved.layout)?;layout_name(&saved.name)?;if !ids.insert(&saved.id){return Err("布局ID重复".into());}}
    Ok(value)
}
fn path(app:&tauri::AppHandle)->Result<std::path::PathBuf,String>{Ok(app.path().app_data_dir().map_err(|e|e.to_string())?.join("custom-overlay-v2.json"))}
fn layout_name(name:&str)->Result<String,String>{let name=name.trim();if name.is_empty()||name.chars().count()>80{return Err("请填写1至80字的布局名称".into());}Ok(name.into())}
fn modify(store:&mut Store,name:&str,args:&Value,using_custom:bool)->Result<(),String>{
    if args["revision"].as_u64()!=Some(store.revision){return Err("布局库已在其他操作中更新，请重新加载".into());}
    let id=args["id"].as_str();let index=id.and_then(|id|store.layouts.iter().position(|l|l.id==id));
    if id.is_some()&&index.is_none(){return Err("布局不存在".into());}
    if let Some(index)=index{if args["layout_revision"].as_u64()!=Some(store.layouts[index].revision){return Err("该布局已更新，请重新加载".into());}}
    match name{
        "custom_overlay_save"=>{
            let draft:Layout=serde_json::from_value(args["draft"].clone()).map_err(|e|e.to_string())?;validate(&draft)?;
            let title=layout_name(args["name"].as_str().unwrap_or(""))?;
            let templates:Vec<Component>=serde_json::from_value(args["templates"].clone()).map_err(|e|e.to_string())?;
            if templates.len()>128{return Err("组件模板最多128个".into());}let mut ids=std::collections::HashSet::new();
            for item in &templates{component(item)?;if !ids.insert(&item.id){return Err("模板ID重复".into());}}
            if let Some(index)=index{let saved=&mut store.layouts[index];saved.name=title;saved.layout=draft.clone();saved.revision+=1;store.editing_id=Some(saved.id.clone());}
            else{if store.layouts.len()>=64{return Err("布局库最多64套，请先删除不需要的布局".into());}
                let id=format!("layout-{}",store.revision+1);store.layouts.push(SavedLayout{id:id.clone(),name:title,revision:1,layout:draft.clone()});store.editing_id=Some(id);}
            store.draft=draft;store.templates=templates;
        }
        "custom_overlay_apply"=>{let saved=&store.layouts[index.ok_or("请选择已保存的布局")?];store.applied=Some(saved.layout.clone());store.applied_id=Some(saved.id.clone());store.applied_revision=Some(saved.revision);}
        "custom_overlay_rename"=>{let saved=&mut store.layouts[index.ok_or("请选择布局")?];saved.name=layout_name(args["name"].as_str().unwrap_or(""))?;saved.revision+=1;}
        "custom_overlay_copy"=>{if store.layouts.len()>=64{return Err("布局库最多64套".into());}let mut saved=store.layouts[index.ok_or("请选择布局")?].clone();saved.id=format!("layout-{}",store.revision+1);saved.name=layout_name(args["name"].as_str().unwrap_or(""))?;saved.revision=1;store.layouts.push(saved);}
        "custom_overlay_delete"=>{let index=index.ok_or("请选择布局")?;let id=store.layouts[index].id.clone();
            if store.applied_id.as_deref()==Some(&id){if using_custom{return Err("请先切换到其他布局或内置样式，再删除当前布局".into());}store.applied=None;store.applied_id=None;store.applied_revision=None;}
            if store.editing_id.as_deref()==Some(&id){store.editing_id=None;store.draft=empty().draft;}
            store.layouts.remove(index);
        }
        _=>return Err("自定义布局操作无效".into()),
    }
    store.revision+=1;Ok(())
}
pub fn command(app:&tauri::AppHandle,name:&str,args:&Value)->Result<Value,String>{
    if name=="custom_overlay_displays"{return serde_json::to_value(displays(app)?).map_err(|e|e.to_string());}
    let _lock=STORE.lock().map_err(|_|"布局存储锁失败")?;let file=path(app)?;let mut store=load_at(&file)?;
    if name!="custom_overlay_get"{
        modify(&mut store,name,args,crate::overlay_panel::get_or_init_settings().style=="custom_canvas")?;
        persist(&file,&store)?;
    }
    serde_json::to_value(store).map_err(|e|e.to_string())
}
pub fn runtime(app:&tauri::AppHandle)->Result<Value,String>{
    let applied={let _lock=STORE.lock().map_err(|_|"布局存储锁失败")?;load_at(&path(app)?)?.applied};
    let display=displays(app)?.into_iter().find(|d|applied.as_ref().is_some_and(|layout|layout.monitor==d.id));
    if let Some(window)=app.get_webview_window("custom-overlay"){
        if let Some(display)=&display{
            let position=tauri::PhysicalPosition::new(display.x,display.y);let size=tauri::PhysicalSize::new(display.width,display.height);
            if window.outer_position().ok()!=Some(position){window.set_position(position).map_err(|e|e.to_string())?;}
            if window.inner_size().ok()!=Some(size){window.set_size(size).map_err(|e|e.to_string())?;}
            window.set_ignore_cursor_events(true).map_err(|e|e.to_string())?;
            if ACTIVE.load(Ordering::SeqCst)&&!window.is_visible().unwrap_or(false){window.show().map_err(|e|e.to_string())?;}
        }else{let _=window.hide();}
    }
    Ok(json!({"layout":applied,"display":display,"data":crate::overlay_runtime::latest_raw()}))
}
pub fn create(app:&tauri::AppHandle,generation:u64,deadline:Instant)->Result<tauri::WebviewWindow,String>{
    crate::backend::ensure_not_closing()?;
    if let Some(old)=app.get_webview_window("custom-overlay"){
        old.destroy().map_err(|e|e.to_string())?;
        while app.get_webview_window("custom-overlay").is_some(){if Instant::now()>=deadline{return Err("旧自定义悬浮窗尚未关闭".into());}std::thread::sleep(std::time::Duration::from_millis(10));}
    }
    let mut builder=WebviewWindowBuilder::new(app,"custom-overlay",WebviewUrl::App("custom-overlay".into()))
        .title("FrameScope 自定义悬浮窗").inner_size(1.0,1.0).decorations(false).transparent(true).always_on_top(true)
        .skip_taskbar(true).resizable(false).shadow(false).focused(false).focusable(false).visible(false)
        .initialization_script(format!("Object.defineProperty(window,'__FRAMESCOPE_OVERLAY_GENERATION__',{{value:{generation}}});"));
    if let Some(main)=app.config().app.windows.iter().find(|w|w.label=="main"){
        if let Some(args)=&main.additional_browser_args{builder=builder.additional_browser_args(args);}
        if let Some(directory)=&main.data_directory{builder=builder.data_directory(directory.clone());}
    }
    let window=builder.build().map_err(|e|e.to_string())?;
    window.set_ignore_cursor_events(true).map_err(|e|e.to_string())?;
    window.on_window_event(move|event|{if matches!(event,tauri::WindowEvent::Destroyed)&&crate::overlay_runtime::STATE.closed(generation){ACTIVE.store(false,Ordering::SeqCst);}});
    runtime(app)?;Ok(window)
}
pub fn stop(app:&tauri::AppHandle)->Result<(),String>{ACTIVE.store(false,Ordering::SeqCst);if let Some(window)=app.get_webview_window("custom-overlay"){let _=window.hide();window.destroy().map_err(|e|e.to_string())?;}Ok(())}

#[cfg(test)]mod tests{
    use super::*;
    #[test]fn named_save_does_not_apply_and_revision_conflicts_preserve_snapshot(){
        let mut state=empty();let draft=serde_json::to_value(&state.draft).unwrap();
        modify(&mut state,"custom_overlay_save",&json!({"revision":0,"name":"游戏一","draft":draft,"templates":[]}),false).unwrap();
        assert!(state.applied.is_none());let id=state.layouts[0].id.clone();
        modify(&mut state,"custom_overlay_apply",&json!({"revision":1,"id":id,"layout_revision":1}),false).unwrap();
        let mut changed=draft.clone();changed["design_width"]=json!(2560);
        modify(&mut state,"custom_overlay_save",&json!({"revision":2,"id":id,"layout_revision":1,"name":"游戏一","draft":changed,"templates":[]}),true).unwrap();
        assert_eq!(state.applied.as_ref().unwrap().design_width,1920.0);assert_eq!(state.layouts[0].layout.design_width,2560.0);
        assert!(modify(&mut state,"custom_overlay_apply",&json!({"revision":3,"id":id,"layout_revision":1}),true).is_err());
        assert!(modify(&mut state,"custom_overlay_delete",&json!({"revision":3,"id":id,"layout_revision":2}),true).is_err());
        modify(&mut state,"custom_overlay_copy",&json!({"revision":3,"id":id,"layout_revision":2,"name":"游戏二"}),true).unwrap();assert_eq!(state.layouts.len(),2);
        modify(&mut state,"custom_overlay_delete",&json!({"revision":4,"id":id,"layout_revision":2}),false).unwrap();assert!(state.applied.is_none());
    }
    #[test]fn migration_preserves_original_and_merges_only_equal_layouts(){
        for same in [true,false]{
            let root=std::env::temp_dir().join(format!("framescope-custom-migration-{}-{same}",std::process::id()));std::fs::create_dir_all(&root).unwrap();
            let legacy=root.join("custom-overlay-v1.json");let file=root.join("custom-overlay-v2.json");let mut old=empty();old.schema=1;old.applied=Some(old.draft.clone());if !same{old.draft.design_width=2560.0;}
            let raw=serde_json::to_vec(&old).unwrap();std::fs::write(&legacy,&raw).unwrap();
            let migrated=load_at(&file).unwrap();assert_eq!(migrated.layouts.len(),if same{1}else{2});assert_eq!(migrated.applied.as_ref().unwrap().design_width,1920.0);
            assert_eq!(std::fs::read(&legacy).unwrap(),raw);assert_eq!(load_at(&file).unwrap().layouts.len(),migrated.layouts.len());std::fs::remove_dir_all(root).unwrap();
        }
    }
}
