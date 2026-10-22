//! Current-user, interactive logon task for the administrator/UIAccess host.
//! No passwords, service account or machine-wide logon trigger are used.
use std::{path::Path, process::{Command, Stdio}, time::{Duration, Instant}};
use std::os::windows::process::CommandExt;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use winreg::{RegKey, enums::*};

const RUN: &str = r"Software\Microsoft\Windows\CurrentVersion\Run";
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct State { pub exists: bool, pub enabled: bool }

fn task_id(exe: &Path) -> String {
    let path = exe.to_string_lossy().replace('/', "\\").to_lowercase();
    let path = if let Some(unc) = path.strip_prefix(r"\\?\unc\") { format!(r"\\{unc}") }
        else { path.strip_prefix(r"\\?\").unwrap_or(&path).to_string() };
    let hash = format!("{:x}", Sha256::digest(path.as_bytes()));
    format!("FrameScope-Logon-{}", &hash[..16])
}

fn task(exe: &Path, operation: &str) -> Result<State, String> {
    // Values cross the process boundary as environment data, never script code.
    let script = r#"
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
$sid=[System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$name=$env:FRAMESCOPE_TASK_ID+'-'+$sid
$existing=Get-ScheduledTask -TaskPath '\' | Where-Object TaskName -eq $name
if($existing -and ($existing.Actions.Count -ne 1 -or $existing.Actions[0].Execute -ne $env:FRAMESCOPE_TASK_EXE -or $existing.Actions[0].Arguments -ne '--autostart')){throw 'FrameScope logon task identity mismatch'}
switch($env:FRAMESCOPE_TASK_OPERATION){
 'enable' {
  $action=New-ScheduledTaskAction -Execute $env:FRAMESCOPE_TASK_EXE -Argument '--autostart'
  $trigger=New-ScheduledTaskTrigger -AtLogOn -User $sid
  $principal=New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Highest
  $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([timespan]::Zero) -MultipleInstances IgnoreNew
  Register-ScheduledTask -TaskPath '\' -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
 }
 'disable' { if($existing){Unregister-ScheduledTask -InputObject $existing -Confirm:$false} }
 'restore-disabled' { if($existing){Disable-ScheduledTask -InputObject $existing | Out-Null} }
}
$t=Get-ScheduledTask -TaskPath '\' | Where-Object TaskName -eq $name
if($t -and ($t.Actions.Count -ne 1 -or $t.Actions[0].Execute -ne $env:FRAMESCOPE_TASK_EXE -or $t.Actions[0].Arguments -ne '--autostart')){throw 'FrameScope logon task identity mismatch'}
@{exists=($null -ne $t);enabled=($null -ne $t -and $t.State -ne 'Disabled')} | ConvertTo-Json -Compress
"#;
    let root = std::env::temp_dir().join(format!("framescope-logon-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos()));
    std::fs::create_dir(&root).map_err(|e| e.to_string())?;
    let result = (|| {
        // Files keep PowerShell output bounded without pipe-buffer deadlocks.
        let output = root.join("out"); let error = root.join("err");
        let shell = std::env::var_os("SystemRoot").ok_or("SystemRoot missing")?;
        let mut child = Command::new(Path::new(&shell).join(r"System32\WindowsPowerShell\v1.0\powershell.exe"))
            .args(["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script])
            .env("FRAMESCOPE_TASK_ID", task_id(exe)).env("FRAMESCOPE_TASK_EXE", exe)
            .env("FRAMESCOPE_TASK_OPERATION", operation).stdin(Stdio::null())
            .stdout(std::fs::File::create(&output).map_err(|e| e.to_string())?)
            .stderr(std::fs::File::create(&error).map_err(|e| e.to_string())?)
            .creation_flags(0x0800_0000).spawn().map_err(|e| e.to_string())?;
        let deadline = Instant::now() + Duration::from_secs(30);
        let status = loop {
            if let Some(status) = child.try_wait().map_err(|e| e.to_string())? { break status; }
            if Instant::now() >= deadline { let _ = child.kill(); let _ = child.wait(); return Err("Windows 自启动任务操作超时".into()); }
            std::thread::sleep(Duration::from_millis(30));
        };
        if !status.success() { return Err(format!("Windows 自启动任务操作失败：{}", std::fs::read_to_string(error).unwrap_or_default().trim())); }
        serde_json::from_slice(&std::fs::read(output).map_err(|e| e.to_string())?).map_err(|e| e.to_string())
    })();
    let _ = std::fs::remove_dir_all(&root);
    result
}

pub fn state(exe: &Path) -> Result<State, String> { task(exe, "query") }
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
}
