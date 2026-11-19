//! Current-user, interactive logon task for the administrator/UIAccess host.
//! No passwords, service account or machine-wide logon trigger are used.
use std::path::Path;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use winreg::{RegKey, enums::*};

const RUN: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct State { pub exists: bool, pub enabled: bool }

fn normalized_executable_path(exe: &Path) -> String {
    let path = exe.to_string_lossy().replace('/', "\\").to_lowercase();
    if let Some(unc) = path.strip_prefix(r"\\?\unc\") { format!(r"\\{unc}") }
        else { path.strip_prefix(r"\\?\").unwrap_or(&path).to_string() }
}

fn task_id(exe: &Path) -> String {
    let hash = format!("{:x}", Sha256::digest(normalized_executable_path(exe).as_bytes()));
    format!("FrameScope-Logon-{}", &hash[..16])
}

fn current_sid() -> windows::core::Result<String> {
    use windows::Win32::{Foundation::{CloseHandle,HANDLE,HLOCAL,LocalFree},Security::{*,Authorization::ConvertSidToStringSidW},System::Threading::{GetCurrentProcess,OpenProcessToken}};
    unsafe {
        let mut token=HANDLE::default();
        OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&mut token)?;
        let result=(|| {
            let mut needed=0;
            let _=GetTokenInformation(token,TokenUser,None,0,&mut needed);
            if needed==0 || needed>65536 {return Err(windows::core::Error::from_win32());}
            let mut storage=vec![0usize;(needed as usize+std::mem::size_of::<usize>()-1)/std::mem::size_of::<usize>()];
            GetTokenInformation(token,TokenUser,Some(storage.as_mut_ptr() as _),needed,&mut needed)?;
            let user=&*(storage.as_ptr() as *const TOKEN_USER);
            let mut sid=windows::core::PWSTR::null();
            ConvertSidToStringSidW(user.User.Sid,&mut sid)?;
            let text=sid.to_string();
            LocalFree(Some(HLOCAL(sid.0 as _)));
            text.map_err(|_|windows::core::Error::from_hresult(windows::core::HRESULT(0x80070057u32 as i32)))
        })();
        let _=CloseHandle(token);
        result
    }
}

fn task(exe: &Path, operation: &str) -> Result<State, String> {
    task_configured(exe, operation, false)
}
fn task_configured(exe: &Path, operation: &str, amd: bool) -> Result<State, String> {
    use windows::{core::{BSTR,Interface},Win32::{Foundation::{VARIANT_FALSE,VARIANT_TRUE,RPC_E_CHANGED_MODE},System::{Com::*,TaskScheduler::*,Variant::VARIANT}}};
    struct Apartment(bool);
    impl Drop for Apartment {fn drop(&mut self){if self.0 {unsafe{CoUninitialize();}}}}
    let result=(|| -> windows::core::Result<State> {unsafe {
        let initialized=CoInitializeEx(None,COINIT_APARTMENTTHREADED);
        if initialized.is_err() && initialized!=RPC_E_CHANGED_MODE {initialized.ok()?;}
        let _apartment=Apartment(initialized.is_ok());
        let service:ITaskService=CoCreateInstance(&TaskScheduler,None,CLSCTX_INPROC_SERVER)?;
        let empty=VARIANT::default();
        service.Connect(&empty,&empty,&empty,&empty)?;
        let folder=service.GetFolder(&BSTR::from("\\"))?;
        let sid=current_sid()?;
        let id=if amd{task_id(exe).replacen("FrameScope-Logon-","FrameScope-Amd-Logon-",1)}else{task_id(exe)};
        let name=BSTR::from(format!("{id}-{sid}"));
        let arguments=if amd{"--startup"}else{"--autostart"};
        let read=|| -> windows::core::Result<Option<IRegisteredTask>> {
            match folder.GetTask(&name) {
                Ok(task)=>{
                    let actions=task.Definition()?.Actions()?;
                    let mut count=0;actions.Count(&mut count)?;
                    if count!=1 {return Err(windows::core::Error::new(windows::core::HRESULT(0x8007000du32 as i32),"FrameScope logon task identity mismatch"));}
                    let action:IExecAction=actions.get_Item(1)?.cast()?;
                    let mut path=BSTR::new();let mut args=BSTR::new();
                    action.Path(&mut path)?;action.Arguments(&mut args)?;
                    if normalized_executable_path(Path::new(&path.to_string())) != normalized_executable_path(exe)
                        || args.to_string()!=arguments {
                        return Err(windows::core::Error::new(windows::core::HRESULT(0x8007000du32 as i32),"FrameScope logon task identity mismatch"));
                    }
                    Ok(Some(task))
                },
                Err(error) if matches!(error.code().0 as u32,0x80070002|0x80070003)=>Ok(None),
                Err(error)=>Err(error)
            }
        };
        let existing=read()?;
        match operation {
            "enable"=>{
                if let Some(task)=existing {
                    if !task.Enabled()?.as_bool(){task.SetEnabled(VARIANT_TRUE)?;}
                }else{
                let definition=service.NewTask(0)?;
                let principal=definition.Principal()?;
                principal.SetUserId(&BSTR::from(&sid))?;
                principal.SetLogonType(TASK_LOGON_INTERACTIVE_TOKEN)?;
                principal.SetRunLevel(TASK_RUNLEVEL_HIGHEST)?;
                let trigger:ILogonTrigger=definition.Triggers()?.Create(TASK_TRIGGER_LOGON)?.cast()?;
                trigger.SetUserId(&BSTR::from(&sid))?;
                let action:IExecAction=definition.Actions()?.Create(TASK_ACTION_EXEC)?.cast()?;
                action.SetPath(&BSTR::from(exe.to_string_lossy().as_ref()))?;
                action.SetArguments(&BSTR::from(arguments))?;
                if let Some(parent)=exe.parent(){action.SetWorkingDirectory(&BSTR::from(parent.to_string_lossy().as_ref()))?;}
                let settings=definition.Settings()?;
                settings.SetDisallowStartIfOnBatteries(VARIANT_FALSE)?;
                settings.SetStopIfGoingOnBatteries(VARIANT_FALSE)?;
                settings.SetExecutionTimeLimit(&BSTR::from(if amd{"PT2M"}else{"PT0S"}))?;
                settings.SetMultipleInstances(TASK_INSTANCES_IGNORE_NEW)?;
                folder.RegisterTaskDefinition(&name,&definition,TASK_CREATE_OR_UPDATE.0,&VARIANT::from(sid.as_str()),&empty,TASK_LOGON_INTERACTIVE_TOKEN,&empty)?;
                }
            },
            "disable"=>{if existing.is_some(){folder.DeleteTask(&name,0)?;}},
            "restore-disabled"=>{if let Some(task)=existing {task.SetEnabled(VARIANT_FALSE)?;}},
            "query"=>{},
            _=>return Err(windows::core::Error::from_hresult(windows::core::HRESULT(0x80070057u32 as i32)))
        }
        match read()? {Some(task)=>Ok(State{exists:true,enabled:task.Enabled()?.as_bool()}),None=>Ok(State::default())}
    }})();
    result.map_err(|error|format!("Windows 自启动任务操作失败：{error}"))
}

pub fn state(exe: &Path) -> Result<State, String> { task(exe, "query") }
pub fn amd_state(exe:&Path)->Result<State,String>{task_configured(exe,"query",true)}
pub fn amd_set(exe:&Path,enabled:bool)->Result<State,String>{task_configured(exe,if enabled{"enable"}else{"disable"},true)}
pub fn amd_restore(exe:&Path,state:&State)->Result<(),String>{
    task_configured(exe,if state.exists{"enable"}else{"disable"},true)?;
    if state.exists&&!state.enabled{task_configured(exe,"restore-disabled",true)?;}Ok(())
}
pub fn legacy_enabled(exe: &Path) -> Result<bool, String> {
    match RegKey::predef(HKEY_CURRENT_USER).open_subkey(RUN) {
        Ok(key) => {
            let value: String = match key.get_value("FrameScope") {
                Ok(value) => value,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(false),
                Err(e) => return Err(e.to_string()),
            };
            let expected = format!("\"{}\"", exe.display());
            Ok(value.eq_ignore_ascii_case(&expected) || value.eq_ignore_ascii_case(&format!("{expected} --autostart")))
        }
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(e) => Err(e.to_string()),
    }
}
pub fn restore(exe: &Path, state: &State) -> Result<(), String> {
    task(exe, if state.exists { "enable" } else { "disable" })?;
    if state.exists && !state.enabled { task(exe, "restore-disabled")?; }
    Ok(())
}
pub fn set(exe: &Path, enabled: bool) -> Result<State, String> {
    let before = state(exe)?;
    let changed = task(exe, if enabled { "enable" } else { "disable" })?;
    let legacy = (|| -> Result<(), String> {
        match RegKey::predef(HKEY_CURRENT_USER).open_subkey_with_flags(RUN, KEY_QUERY_VALUE | KEY_SET_VALUE) {
            Ok(key) => {
                let value: String = key.get_value("FrameScope").unwrap_or_default();
                let expected = format!("\"{}\"", exe.display());
                if value.eq_ignore_ascii_case(&expected) || value.eq_ignore_ascii_case(&format!("{expected} --autostart")) {
                    key.delete_value("FrameScope").map_err(|e| e.to_string())?;
                }
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => (),
            Err(e) => return Err(e.to_string()),
        }
        Ok(())
    })();
    if let Err(error) = legacy {
        let rollback = restore(exe, &before);
        return Err(format!("旧启动项迁移失败：{error}; rollback={rollback:?}"));
    }
    if changed.enabled != enabled { return Err("Windows 自启动状态回读不一致".into()); }
    Ok(changed)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn task_names_are_stable_per_installation_and_do_not_collide() {
        assert_eq!(task_id(Path::new("C:/FrameScope/FrameScope.exe")), task_id(Path::new(r"c:\framescope\FRAMESCOPE.exe")));
        assert_ne!(task_id(Path::new("C:/A/FrameScope.exe")), task_id(Path::new("C:/B/FrameScope.exe")));
        assert_eq!(task_id(Path::new("C:/A/FrameScope.exe")), task_id(Path::new(r"\\?\C:\A\FrameScope.exe")));
    }
    #[test]
    #[ignore = "reads the Windows Task Scheduler; explicitly run on Windows"]
    fn task_scheduler_query_returns_real_state_without_registration() {
        let state = state(&std::env::current_exe().unwrap()).unwrap();
        assert!(!state.exists && !state.enabled);
    }
    #[test]
    #[ignore = "restores the current state of an explicitly selected installed logon task"]
    fn task_scheduler_restore_preserves_existing_task() {
        let exe = std::path::PathBuf::from(std::env::var_os("FRAMESCOPE_AUTOSTART_TEST_EXE")
            .expect("an installed executable with an existing logon task is required"));
        let before = state(&exe).unwrap();
        assert!(before.exists, "the selected installation must have an existing logon task");
        restore(&exe, &before).unwrap();
        let after = state(&exe).unwrap();
        assert_eq!((before.exists, before.enabled), (after.exists, after.enabled));
    }
    #[test]
    #[ignore = "reads an explicitly selected installed logon task without registration"]
    fn task_scheduler_accepts_equivalent_installation_paths_without_registration() {
        let exe = std::path::PathBuf::from(std::env::var_os("FRAMESCOPE_AUTOSTART_TEST_EXE")
            .expect("an installed executable with an existing logon task is required"));
        let ordinary = state(&exe).unwrap();
        assert!(ordinary.exists, "the selected installation must have an existing logon task");
        let canonical = state(&exe.canonicalize().unwrap()).unwrap();
        assert_eq!((ordinary.exists, ordinary.enabled), (canonical.exists, canonical.enabled));
        let forward = state(Path::new(&exe.to_string_lossy().replace('\\', "/"))).unwrap();
        assert_eq!((ordinary.exists, ordinary.enabled), (forward.exists, forward.enabled));
    }
}
