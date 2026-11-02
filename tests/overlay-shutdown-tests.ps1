param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
# Compile the actual production functions with fake window/IO adapters. Never
# start a product window, stop a watcher, or touch installed configuration.
$owned=Join-Path $env:TEMP ('framescope-overlay-shutdown-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $owned|Out-Null
$source=Get-Content (Join-Path $RepoRoot 'desktop-preview/src-tauri/src/lib.rs') -Raw
function Extract([string]$start,[string]$end){
    $first=$source.IndexOf($start);$last=$source.IndexOf($end,$first)
    if($first -lt 0 -or $last -lt 0){throw 'Production function boundary changed'}
    $source.Substring($first,$last-$first)
}
$functions=(Extract 'fn stop_windows(' 'fn active(')+(Extract 'fn save_overlay_position(' 'fn write_native_settings(')
$hasCache=$source.Contains('fn record_vertical_position(')
if($hasCache){$functions+=(Extract 'fn record_vertical_position(' 'fn save_overlay_position(')}
$harness=@'
use std::sync::{Mutex,atomic::{AtomicBool,Ordering}};
static SERIAL: Mutex<()> = Mutex::new(());
static CALLS: Mutex<Vec<&'static str>> = Mutex::new(Vec::new());
static FAIL_SAVE: AtomicBool = AtomicBool::new(false);
static SAVED: Mutex<Option<Settings>> = Mutex::new(None);
static VERTICAL_ACTIVE: AtomicBool = AtomicBool::new(true);
#[derive(Clone,Default,Debug,PartialEq)]
struct Settings { position_x:Option<i32>,position_y:Option<i32>,vertical_position_x:Option<i32>,vertical_position_y:Option<i32>,vertical_position_reference:Option<overlay_position::PositionReference> }
mod overlay_position { #[derive(Clone,Default,Debug,PartialEq)] pub struct PositionReference; }
mod tauri {
    #[derive(Clone,Copy)] pub enum Kind { Missing,Alive,Closed }
    pub struct AppHandle { pub kind:Kind }
    pub struct Window { kind:Kind }
    pub struct PhysicalPosition<T> { pub x:T,pub y:T }
    impl AppHandle { pub fn get_webview_window(&self,_:&str)->Option<Window>{match self.kind{Kind::Missing=>None,k=>Some(Window{kind:k})}} }
    impl Window {
        pub fn outer_position(&self)->Result<PhysicalPosition<i32>,String>{
            super::CALLS.lock().unwrap().push("query");
            match self.kind {Kind::Closed=>Err("runtime error: failed to receive message from webview".into()),_=>Ok(PhysicalPosition{x:999,y:888})}
        }
        pub fn hide(&self)->Result<(),String>{super::CALLS.lock().unwrap().push("hide");Ok(())}
        pub fn destroy(&self)->Result<(),String>{super::CALLS.lock().unwrap().push("destroy");Ok(())}
    }
}
mod overlay_panel {
    pub static CURRENT_SETTINGS:super::Mutex<Option<super::Settings>>=super::Mutex::new(None);
    pub fn take_position_changed()->bool{super::CALLS.lock().unwrap().push("ack");false}
    pub fn stop_overlay()->Result<(),String>{super::CALLS.lock().unwrap().push("gdi-stop");Ok(())}
}
fn save_native_settings(_: &tauri::AppHandle)->Result<(),String>{
    CALLS.lock().unwrap().push("persist");
    if FAIL_SAVE.load(Ordering::SeqCst){return Err("disk write denied".into())}
    *SAVED.lock().unwrap()=overlay_panel::CURRENT_SETTINGS.lock().unwrap().clone();Ok(())
}
fn reset(){
    CALLS.clear_poison();SAVED.clear_poison();overlay_panel::CURRENT_SETTINGS.clear_poison();
    CALLS.lock().unwrap().clear();FAIL_SAVE.store(false,Ordering::SeqCst);VERTICAL_ACTIVE.store(true,Ordering::SeqCst);
    *SAVED.lock().unwrap()=None;
    *overlay_panel::CURRENT_SETTINGS.lock().unwrap()=Some(Settings{position_x:Some(-1500),position_y:Some(270),vertical_position_x:Some(620),vertical_position_y:Some(-130),vertical_position_reference:None});
}
#[test] fn closed_webview_cannot_cancel_position_persistence(){
    let _guard=SERIAL.lock().unwrap_or_else(|p|p.into_inner());reset();
    let before=overlay_panel::CURRENT_SETTINGS.lock().unwrap().clone();
    assert!(save_overlay_position(&tauri::AppHandle{kind:tauri::Kind::Closed}).is_ok());
    assert_eq!(*SAVED.lock().unwrap(),before);
    assert!(!CALLS.lock().unwrap().contains(&"query"));
}
#[test] fn closed_webview_cannot_skip_overlay_teardown(){
    let _guard=SERIAL.lock().unwrap_or_else(|p|p.into_inner());reset();
    assert!(stop_windows(&tauri::AppHandle{kind:tauri::Kind::Closed}).is_ok());
    assert_eq!(*CALLS.lock().unwrap(),vec!["persist","ack","gdi-stop","hide","destroy"]);
    assert!(!VERTICAL_ACTIVE.load(Ordering::SeqCst));
}
#[test] fn alive_window_also_uses_the_recorded_position_without_late_queries(){
    let _guard=SERIAL.lock().unwrap_or_else(|p|p.into_inner());reset();
    let before=overlay_panel::CURRENT_SETTINGS.lock().unwrap().clone();
    save_overlay_position(&tauri::AppHandle{kind:tauri::Kind::Alive}).unwrap();
    assert_eq!(*SAVED.lock().unwrap(),before);
    assert!(!CALLS.lock().unwrap().contains(&"query"));
}
#[test] fn absent_or_repeatedly_closed_window_is_an_idempotent_stop(){
    let _guard=SERIAL.lock().unwrap_or_else(|p|p.into_inner());reset();
    for kind in [tauri::Kind::Missing,tauri::Kind::Closed,tauri::Kind::Closed]{stop_windows(&tauri::AppHandle{kind}).unwrap();}
    assert_eq!(CALLS.lock().unwrap().iter().filter(|&&s|s=="gdi-stop").count(),3);
}
#[test] fn real_save_failure_is_not_acknowledged_or_disguised_as_success(){
    let _guard=SERIAL.lock().unwrap_or_else(|p|p.into_inner());reset();FAIL_SAVE.store(true,Ordering::SeqCst);
    assert_eq!(stop_windows(&tauri::AppHandle{kind:tauri::Kind::Missing}).unwrap_err(),"disk write denied");
    assert_eq!(*CALLS.lock().unwrap(),vec!["persist"]);
    assert!(SAVED.lock().unwrap().is_none());
}
#[cfg(has_native_move_cache)]
#[test] fn latest_native_move_survives_a_stale_frontend_and_closed_webview(){
    let _guard=SERIAL.lock().unwrap_or_else(|p|p.into_inner());reset();
    record_vertical_position(tauri::PhysicalPosition{x:20,y:40},Default::default());
    record_vertical_position(tauri::PhysicalPosition{x:-1820,y:105},Default::default());
    save_overlay_position(&tauri::AppHandle{kind:tauri::Kind::Closed}).unwrap();
    assert_eq!(*SAVED.lock().unwrap(),Some(Settings{position_x:Some(-1500),position_y:Some(270),vertical_position_x:Some(-1820),vertical_position_y:Some(105),vertical_position_reference:Some(Default::default())}));
}
'@
try{
    $file=Join-Path $owned 'overlay_shutdown.rs'
    [IO.File]::WriteAllText($file,($harness+"`n"+$functions),[Text.UTF8Encoding]::new($false))
    $exe=Join-Path $owned 'overlay-shutdown-tests.exe'
    $compiler=@('--edition','2021','--test',$file,'-o',$exe,'-A','dead_code')
    if($hasCache){$compiler+=@('--cfg','has_native_move_cache')}
    & rustc @compiler
    if($LASTEXITCODE -ne 0){throw 'Regression harness compilation failed'}
    & $exe --nocapture
    if($LASTEXITCODE -ne 0){throw 'Production overlay shutdown regression failed'}
}finally{
    . (Join-Path $RepoRoot 'desktop-preview/installer/pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly
    Assert-PayloadPath -Path $owned -Root $env:TEMP
    Remove-Item -LiteralPath $owned -Recurse -Force
}
