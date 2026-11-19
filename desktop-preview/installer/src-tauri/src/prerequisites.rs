use crate::install_transaction::{assert_path,Result};
use std::{path::{Path,PathBuf},io::Read,time::{Duration,Instant},os::windows::ffi::OsStrExt};
use winreg::{RegKey,enums::*};
use windows_sys::Win32::{Security::{Cryptography::*,WinTrust::*},Storage::FileSystem::*};
#[derive(Clone,Copy,Debug,PartialEq,Eq)]
pub enum Component { WebView, DotNet, PawnIO }
impl Component {
    pub fn name(self)->&'static str {match self{Self::WebView=>"WebView2",Self::DotNet=>".NET Framework 4.8",Self::PawnIO=>"PawnIO"}}
    fn args(self)->&'static [&'static str]{match self{Self::WebView=>&["/silent","/install"],Self::DotNet=>&["/q","/norestart"],Self::PawnIO=>&["-install","-silent"]}}
}
pub fn installed(component:Component)->bool {
    let machine=RegKey::predef(HKEY_LOCAL_MACHINE);
    match component {
        Component::WebView=>crate::installer::check_webview2(),
        Component::DotNet=>machine.open_subkey_with_flags(r"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",KEY_READ|KEY_WOW64_64KEY)
            .and_then(|key|key.get_value::<u32,_>("Release")).is_ok_and(|release|release>=528040),
        Component::PawnIO=>machine.open_subkey_with_flags(r"SYSTEM\CurrentControlSet\Services\PawnIO",KEY_READ)
            .and_then(|key|key.get_value::<String,_>("ImagePath")).is_ok_and(|path|{
                let path=path.trim_start_matches(r"\??\").replace(r"\SystemRoot",&std::env::var("SystemRoot").unwrap_or_else(|_|r"C:\Windows".into()));
                verify_binary(Path::new(&path),Component::PawnIO,true).is_ok()
            }),
    }
}
fn wide(path:&Path)->Vec<u16>{path.as_os_str().encode_wide().chain(Some(0)).collect()}
pub(crate) fn parse_version(value:&str)->Option<[u32;4]> {
    let parts=value.split('.').map(str::parse::<u32>).collect::<std::result::Result<Vec<_>,_>>().ok()?;
    if !(3..=4).contains(&parts.len()){return None;}
    let mut result=[0;4];result[..parts.len()].copy_from_slice(&parts);Some(result)
}
pub(crate) fn verify_webview_runtime(path:&Path)->Result<()> {
    assert_path(path)?;
    verify_binary_policy(path,Component::WebView,false,true)
}
pub fn verify_package(path:&Path,component:Component)->Result<()> {
    assert_path(path)?;
    verify_binary(path,component,false)
}
fn verify_binary(path:&Path,component:Component,driver:bool)->Result<()> {
    let offline=if driver { pinned_hash(path,"pawnIODriver") } else { pinned_package(path,component) };
    verify_binary_policy(path,component,driver,offline)
}
fn verify_binary_policy(path:&Path,component:Component,driver:bool,pinned_offline:bool)->Result<()> {
    // Pinned offline packages retain signature, chain and publisher checks without
    // fetching a revocation list. Unpinned/user-supplied binaries keep full checks.
    unsafe {
        let path_w=wide(path);let mut info:WINTRUST_FILE_INFO=std::mem::zeroed();info.cbStruct=std::mem::size_of_val(&info) as u32;info.pcwszFilePath=path_w.as_ptr();
        let mut data:WINTRUST_DATA=std::mem::zeroed();data.cbStruct=std::mem::size_of_val(&data) as u32;
        data.dwUIChoice=WTD_UI_NONE;data.dwUnionChoice=WTD_CHOICE_FILE;data.Anonymous.pFile=&mut info;data.dwStateAction=WTD_STATEACTION_VERIFY;
        if pinned_offline {
            data.fdwRevocationChecks=WTD_REVOKE_NONE;
            data.dwProvFlags=WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE;
        } else {
            data.fdwRevocationChecks=WTD_REVOKE_WHOLECHAIN;data.dwProvFlags=WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT;
        }
        let mut action=WINTRUST_ACTION_GENERIC_VERIFY_V2;
        let status=WinVerifyTrust(std::ptr::null_mut(),&mut action,&mut data as *mut _ as _);
        let mut publisher=String::new();
        if status==0 {
            let provider=WTHelperProvDataFromStateData(data.hWVTStateData);
            if !provider.is_null(){let signer=WTHelperGetProvSignerFromChain(provider,0,0,0);
                if !signer.is_null(){let certificate=WTHelperGetProvCertFromChain(signer,0);
                    if !certificate.is_null(){let mut name=[0u16;512];let count=CertGetNameStringW((*certificate).pCert,CERT_NAME_SIMPLE_DISPLAY_TYPE,0,std::ptr::null(),name.as_mut_ptr(),512);
                        if count>0{publisher=String::from_utf16_lossy(&name[..count.saturating_sub(1).min(511) as usize]);}
                    }
                }
            }
        }
        data.dwStateAction=WTD_STATEACTION_CLOSE;WinVerifyTrust(std::ptr::null_mut(),&mut action,&mut data as *mut _ as _);
        if status!=0{return Err(format!("{} 签名或证书验证失败：0x{:08X}",component.name(),status as u32));}
        let expected=if driver{"Microsoft Windows Hardware Compatibility Publisher"}else if component==Component::PawnIO{"namazso.eu"}else{"Microsoft Corporation"};
        if publisher!=expected&&!(driver&&publisher=="namazso.eu"){return Err(format!("{} 安装包发布者不匹配：{publisher}",component.name()));}
        let size=GetFileVersionInfoSizeW(path_w.as_ptr(),std::ptr::null_mut());
        if size==0||size>1024*1024{return Err("组件缺少有效版本信息".into());}
        let mut bytes=vec![0u8;size as usize];if GetFileVersionInfoW(path_w.as_ptr(),0,size,bytes.as_mut_ptr() as _)==0{return Err("无法读取组件版本".into());}
        let query=|key:&str|->String {
            let mut pointer=std::ptr::null_mut();let mut length=0;
            let key:Vec<u16>=key.encode_utf16().chain(Some(0)).collect();
            if VerQueryValueW(bytes.as_ptr() as _,key.as_ptr(),&mut pointer,&mut length)==0||pointer.is_null()||length==0{return String::new();}
            String::from_utf16_lossy(std::slice::from_raw_parts(pointer as *const u16,length.saturating_sub(1) as usize))
        };
        let mut translation=std::ptr::null_mut();let mut length=0;
        let key:Vec<u16>="\\VarFileInfo\\Translation".encode_utf16().chain(Some(0)).collect();
        if VerQueryValueW(bytes.as_ptr() as _,key.as_ptr(),&mut translation,&mut length)==0||length<4{return Err("组件版本语言信息缺失".into());}
        let lang=std::slice::from_raw_parts(translation as *const u16,2);
        let prefix=format!("\\StringFileInfo\\{:04x}{:04x}\\",lang[0],lang[1]);
        let product=query(&(prefix.clone()+"ProductName")).to_ascii_lowercase();
        let version=query(&(prefix+"ProductVersion"));
        let expected=match component{
            Component::WebView=>product.contains("webview2")||product=="microsoft edge update",
            Component::DotNet=>product.contains(".net framework")&&version.starts_with("4.8"),
            Component::PawnIO=>product=="pawnio"&&parse_version(&version).is_some_and(|v|v>=[2,2,0,0]),
        };
        if !expected{return Err(format!("安装包类型或版本不符：{product} {version}"));}
    }
    Ok(())
}

// Read only a known prerequisite from the embedded offline payload. Both the
// pinned build hash and Microsoft's signature/product identity must match.
fn pinned_package(path:&Path,component:Component)->bool {
    let key=match component {Component::WebView=>"webView2StandaloneInstaller",Component::DotNet=>"dotNet48OfflineInstaller",Component::PawnIO=>"pawnIOInstaller"};
    pinned_hash(path,key)
}
fn pinned_hash(path:&Path,key:&str)->bool {
    use sha2::{Digest,Sha256};
    let Ok(lock)=serde_json::from_str::<serde_json::Value>(include_str!("../../../../dependencies.lock.json")) else{return false;};
    let Some(expected)=lock[key]["sha256"].as_str() else{return false;};
    let Ok(data)=std::fs::read(path) else{return false;};
    format!("{:x}",Sha256::digest(data)).eq_ignore_ascii_case(expected)
}
fn embedded_package(component:Component,cache:&Path,progress:&dyn Fn(&str))->Result<PathBuf>{
    use sha2::{Digest,Sha256};
    let(name,key)=match component{
        Component::WebView=>("WebView2.exe","webView2StandaloneInstaller"),
        Component::DotNet=>("DotNet48.exe","dotNet48OfflineInstaller"),
        Component::PawnIO=>return Err("PawnIO 使用安装目录中的随包文件".into()),
    };
    let lock:serde_json::Value=serde_json::from_str(include_str!("../../../../dependencies.lock.json")).map_err(|e|e.to_string())?;
    let expected=lock[key]["sha256"].as_str().ok_or("离线依赖缺少锁定哈希")?.to_ascii_lowercase();
    progress(&format!("正在准备离线 {} 安装包…",component.name()));
    let mut archive=zip::ZipArchive::new(std::io::Cursor::new(crate::installer::payload_zip())).map_err(|e|e.to_string())?;
    let mut file=archive.by_name(&format!("dependencies/Offline/{name}")).map_err(|_|"离线运行依赖缺失，请重新获取完整安装包")?;
    if file.size()>400*1024*1024{return Err("离线运行依赖大小异常".into());}
    let mut data=Vec::new();file.read_to_end(&mut data).map_err(|e|e.to_string())?;
    if format!("{:x}",Sha256::digest(&data))!=expected{return Err("离线运行依赖哈希不匹配".into());}
    let target=cache.join(format!("offline-{name}"));assert_path(&target)?;
    std::fs::write(&target,data).map_err(|e|e.to_string())?;
    verify_package(&target,component)?;Ok(target)
}
struct Cache { path:PathBuf, _lock:std::fs::File }
fn resume_target(path:&Path)->Result<Option<PathBuf>> {
    assert_path(path)?;
    let file=match std::fs::File::open(path) {
        Ok(file)=>file,
        Err(error) if error.kind()==std::io::ErrorKind::NotFound=>return Ok(None),
        Err(error)=>return Err(error.to_string()),
    };
    let mut bytes=Vec::new();file.take(65537).read_to_end(&mut bytes).map_err(|e|e.to_string())?;
    if bytes.len()>65536{return Err("组件恢复记录过大".into());}
    let value:serde_json::Value=serde_json::from_slice(&bytes).map_err(|_|"组件恢复记录损坏")?;
    if value["version"]!=env!("FRAMESCOPE_VERSION"){return Ok(None);}
    let target=PathBuf::from(value["target"].as_str().ok_or("组件恢复位置缺失")?);
    crate::installer::validate_install_destination(&target)?;
    Ok(Some(target))
}
pub(crate) fn pending_target()->Result<Option<PathBuf>> {
    resume_target(&protected_cache_root()?.join("resume.json"))
}
pub(crate) fn complete(target:&Path)->Result<()> {
    let path=protected_cache_root()?.join("resume.json");
    if resume_target(&path)?.is_some_and(|pending|pending.to_string_lossy().eq_ignore_ascii_case(&target.to_string_lossy())) {
        std::fs::remove_file(path).map_err(|e|e.to_string())?;
    }
    Ok(())
}
pub(crate) fn protected_cache_root()->Result<PathBuf> {
    use windows_sys::Win32::{UI::Shell::{SHGetKnownFolderPath,FOLDERID_ProgramFiles},System::Com::CoTaskMemFree};
    let path=unsafe {
        let mut raw=std::ptr::null_mut();
        if SHGetKnownFolderPath(&FOLDERID_ProgramFiles,0,std::ptr::null_mut(),&mut raw)<0 {return Err("无法定位受保护的安装器缓存".into());}
        let mut len=0;while *raw.add(len)!=0{len+=1;}
        let path=PathBuf::from(String::from_utf16_lossy(std::slice::from_raw_parts(raw,len)));
        CoTaskMemFree(raw as _);path.join("FrameScope Component Cache")
    };
    assert_path(&path)?;
    Ok(path)
}
impl Cache {
    fn open()->Result<Self> {
        use std::os::windows::fs::OpenOptionsExt;
        let path=protected_cache_root()?;
        std::fs::create_dir_all(&path).map_err(|e|e.to_string())?;
        let lock=path.join("lock");assert_path(&lock)?;
        let lock=std::fs::OpenOptions::new().create(true).read(true).write(true).share_mode(0).open(lock)
            .map_err(|e|format!("另一个安装器正在检查组件，请稍后重试：{e}"))?;
        let cache=Self{path,_lock:lock};
        cache.check_pending()?;
        Ok(cache)
    }
    fn check_pending(&self)->Result<()> {
        use windows_sys::Win32::{Foundation::*,System::Threading::*};
        let path=self.path.join("active.json");assert_path(&path)?;
        let value:serde_json::Value=match std::fs::read(&path) {
            Ok(bytes)=>serde_json::from_slice(&bytes).map_err(|_|"组件恢复记录损坏，请联系支持；不会重复启动组件安装")?,
            Err(e) if e.kind()==std::io::ErrorKind::NotFound=>return Ok(()),
            Err(e)=>return Err(e.to_string())
        };
        let pid=value["pid"].as_u64().filter(|id|*id>0&&*id<=u32::MAX as u64).ok_or("组件进程记录无效")? as u32;
        unsafe {
            let process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,0,pid);
            if process.is_null(){
                if GetLastError()!=ERROR_INVALID_PARAMETER{return Err("无法核验上次组件安装是否已结束，请稍后重试".into());}
            }else{
                let identity=crate::native_stop::identity(process);
                let exited=WaitForSingleObject(process,0)==WAIT_OBJECT_0;CloseHandle(process);
                if !exited {
                    let (start,image)=identity?;
                    if value["start"].as_u64()==Some(start) && value["image"].as_str().is_some_and(|s|image.to_string_lossy().eq_ignore_ascii_case(s)) {
                        return Err("上次组件安装仍在运行，请等待结束后重试；不会重复启动".into());
                    }
                }
            }
        }
        std::fs::remove_file(path).map_err(|e|e.to_string())?;Ok(())
    }
}
pub fn prepare(target:&Path,local:Option<&Path>,progress:&dyn Fn(&str))->Result<bool>{
    prepare_from(target,local,None,progress)
}
pub fn prepare_with_staged(target:&Path,stage:&Path,progress:&dyn Fn(&str))->Result<bool>{
    prepare_from(target,None,Some(stage),progress)
}
fn prepare_from(target:&Path,local:Option<&Path>,stage:Option<&Path>,progress:&dyn Fn(&str))->Result<bool>{
    let local_component=if let Some(path)=local {
        Some([Component::DotNet,Component::WebView,Component::PawnIO].into_iter()
            .find(|component|verify_package(path,*component).is_ok()).ok_or("所选文件未通过官方签名、产品类型或版本检查，请重新选择")?)
    }else{None};
    if [Component::DotNet,Component::WebView].into_iter().all(installed){return Ok(false);}
    let cache=Cache::open()?;
    framescope_update::atomic_json(&cache.path.join("resume.json"),&serde_json::json!({"version":env!("FRAMESCOPE_VERSION"),"target":target,"rebootRequired":false}))?;
    let mut reboot=false;
    for component in [Component::DotNet,Component::WebView] {
        if installed(component){continue;}
        progress(&format!("补齐 {}",component.name()));
        let cached=cache.path.join(format!("{:?}.exe",component));
        let name=if component==Component::WebView {"WebView2.exe"}else{"DotNet48.exe"};
        let installed_package=target.join("dependencies/Offline").join(name);
        let staged_package=stage.map(|root|root.join("dependencies/Offline").join(name));
        let reusable=staged_package.as_ref().into_iter().chain(std::iter::once(&installed_package))
            .find(|path|pinned_package(path,component) && verify_package(path,component).is_ok());
        let package=if local_component==Some(component){local.unwrap().to_path_buf()}
            else if let Some(path)=reusable {path.clone()}
            else if verify_package(&cached,component).is_ok(){cached.clone()}
            else{embedded_package(component,&cache.path,progress)?};
        reboot|=run_package(&package,component,&cache,target)?;
        if !installed(component){return Err(if reboot{"组件要求重启。安装状态已保留，请重启后重新打开安装器继续".into()}else{format!("{} 安装后仍不可用，请重试",component.name())});}
        if package==cached {std::fs::remove_file(&cached).map_err(|e|e.to_string())?;}
    }
    Ok(reboot)
}
fn installer_launch_path(path:&Path)->PathBuf {
    let text=path.as_os_str().to_string_lossy();
    if let Some(unc)=text.strip_prefix(r"\\?\UNC\") { return PathBuf::from(format!(r"\\{unc}")); }
    if let Some(disk)=text.strip_prefix(r"\\?\") { return PathBuf::from(disk); }
    path.to_path_buf()
}
fn run_package(path:&Path,component:Component,cache:&Cache,target:&Path)->Result<bool>{
    use std::os::windows::fs::OpenOptionsExt;
    use windows_sys::Win32::{Foundation::*,System::Threading::*};
    let _guard=std::fs::OpenOptions::new().read(true).share_mode(FILE_SHARE_READ).open(path).map_err(|e|e.to_string())?;
    verify_package(path,component)?;
    let executable=installer_launch_path(&path.canonicalize().map_err(|e|e.to_string())?);
    // Third-party setup programs use their own executable/cwd with Win32 path
    // APIs; a verbatim prefix can be accepted by CreateProcess but rejected by setup.
    let directory=wide(executable.parent().ok_or("组件安装路径缺少父目录")?);
    let mut command=wide(Path::new(&format!("\"{}\" {}",executable.display(),component.args().join(" "))));
    unsafe {
        let mut startup:STARTUPINFOW=std::mem::zeroed();startup.cb=std::mem::size_of_val(&startup) as u32;
        let mut process:PROCESS_INFORMATION=std::mem::zeroed();
        if CreateProcessW(wide(&executable).as_ptr(),command.as_mut_ptr(),std::ptr::null(),std::ptr::null(),0,
            CREATE_NO_WINDOW|CREATE_SUSPENDED,std::ptr::null(),directory.as_ptr(),&startup,&mut process)==0{return Err(std::io::Error::last_os_error().to_string());}
        let active=cache.path.join("active.json");
        let saved=(|| {
            let (start,image)=crate::native_stop::identity(process.hProcess)?;
            framescope_update::atomic_json(&active,&serde_json::json!({"pid":process.dwProcessId,"start":start,"image":image,"target":target,"component":component.name()}))
        })();
        if let Err(error)=saved {
            TerminateProcess(process.hProcess,1);CloseHandle(process.hThread);CloseHandle(process.hProcess);return Err(error);
        }
        if ResumeThread(process.hThread)==u32::MAX {
            TerminateProcess(process.hProcess,1);CloseHandle(process.hThread);CloseHandle(process.hProcess);return Err("无法开始组件安装，可重试".into());
        }
        CloseHandle(process.hThread);
        let deadline=Instant::now()+Duration::from_secs(900);
        loop {
            match WaitForSingleObject(process.hProcess,200) {
                WAIT_OBJECT_0=>{
                    let mut code=0;
                    let got=GetExitCodeProcess(process.hProcess,&mut code);CloseHandle(process.hProcess);
                    if got==0{return Err("无法读取组件安装结果，可重试".into());}
                    std::fs::remove_file(&active).map_err(|e|e.to_string())?;
                    let reboot=matches!(code,3010|1641);
                    framescope_update::atomic_json(&cache.path.join("resume.json"),&serde_json::json!({"version":env!("FRAMESCOPE_VERSION"),"target":target,"component":component.name(),"exitCode":code,"rebootRequired":reboot}))?;
                    return match code{0|3010|1641=>Ok(reboot),_=>Err(format!("{} 安装失败：{code}",component.name()))};
                },
                WAIT_TIMEOUT=>{},
                _=>{CloseHandle(process.hProcess);return Err("组件安装状态无法确认，请稍后重试".into());}
            }
            if Instant::now()>=deadline {CloseHandle(process.hProcess);return Err(format!("{} 安装仍在进行，请等待结束后重试",component.name()));}
        }
    }
}
pub fn pawnio(target:&Path)->Result<bool>{
    if installed(Component::PawnIO){return Ok(false);}
    let cache=Cache::open()?;
    let reboot=run_package(&target.join("dependencies/PawnIO/PawnIO_setup.exe"),Component::PawnIO,&cache,target)?;
    if !installed(Component::PawnIO){return Err(if reboot{"PawnIO 需要重启后重新核验".into()}else{"PawnIO 安装后仍不可用".into()});}
    Ok(reboot)
}
pub fn record_pawnio(target:&Path)->Result<()> {
    let result=pawnio(target);
    let state=target.join("FrameScopeComponentStatus.json");assert_path(&state)?;
    framescope_update::atomic_json(&state,&serde_json::json!({"pawnioAvailable":installed(Component::PawnIO),"rebootRequired":result.as_ref().copied().unwrap_or(false),"error":result.err()}))?;
    complete(target)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]fn setup_receives_win32_paths_including_spaces_and_unicode(){
        assert_eq!(installer_launch_path(Path::new(r"\\?\C:\测试 文件\PawnIO_setup.exe")),PathBuf::from(r"C:\测试 文件\PawnIO_setup.exe"));
        assert_eq!(installer_launch_path(Path::new(r"\\?\UNC\server\share\setup.exe")),PathBuf::from(r"\\server\share\setup.exe"));
        assert_eq!(installer_launch_path(Path::new(r"C:\normal\setup.exe")),PathBuf::from(r"C:\normal\setup.exe"));
    }
    #[test]fn unpinned_file_never_uses_offline_signature_policy(){
        let path=std::env::temp_dir().join(format!("framescope-unpinned-{}.exe",std::process::id()));
        std::fs::write(&path,b"untrusted fixture").unwrap();
        assert!(!pinned_package(&path,Component::WebView));assert!(!pinned_package(&path,Component::DotNet));assert!(!pinned_package(&path,Component::PawnIO));
        std::fs::remove_file(path).unwrap();
    }
    #[test]
    #[ignore = "Verifies the exact embedded offline prerequisites without installing"]
    fn embedded_offline_packages_match_hash_and_microsoft_signature(){
        let root=std::env::temp_dir().join(format!("framescope-offline-packages-{}",std::process::id()));
        std::fs::create_dir_all(&root).unwrap();
        for component in [Component::WebView,Component::DotNet] {
            let path=embedded_package(component,&root,&|_|{}).unwrap();
            assert!(pinned_package(&path,component));std::fs::remove_file(path).unwrap();
        }
        std::fs::remove_dir(root).unwrap();
    }
    #[test]fn recovery_location_is_versioned_bounded_and_validated(){
        let root=std::env::temp_dir().join(format!("framescope-component-resume-{}",std::process::id()));
        std::fs::create_dir_all(&root).unwrap();let record=root.join("resume.json");
        let target=root.join("FrameScope");
        let save=|version:&str,target:&Path|framescope_update::atomic_json(&record,&serde_json::json!({"version":version,"target":target})).unwrap();
        save(env!("FRAMESCOPE_VERSION"),&target);assert_eq!(resume_target(&record).unwrap(),Some(target.clone()));
        save("old-version",&target);assert!(resume_target(&record).unwrap().is_none());
        for invalid in [Path::new(r"C:\"),Path::new("relative"),Path::new(r"C:\game\..\FrameScope")] {
            save(env!("FRAMESCOPE_VERSION"),invalid);assert!(resume_target(&record).is_err());
        }
        std::fs::write(&record,vec![b' ';65537]).unwrap();assert!(resume_target(&record).is_err());
        std::fs::remove_file(&record).unwrap();std::fs::remove_dir(&root).unwrap();
    }
    #[test]fn version_comparison_is_numeric_and_rejects_invalid_values(){
        assert_eq!(parse_version("2.2.0"),Some([2,2,0,0]));
        assert!(parse_version("10.0.0.0")>parse_version("2.0.0.0"));
        for bad in ["","-1.2.3","4.8","2.2.0.evil","999999999999.1.2.3","1.2.3.4.5"]{assert!(parse_version(bad).is_none(),"{bad}");}
    }
    #[test]
    #[ignore = "Read-only local component and Task Scheduler verification; never installs"]
    fn installed_components_and_official_package_verify_without_installing(){
        let package=std::env::var_os("FRAMESCOPE_TEST_PAWNIO_PACKAGE").expect("explicit local package path");
        verify_package(Path::new(&package),Component::PawnIO).unwrap();
        assert!(verify_package(Path::new(&package),Component::DotNet).is_err());
        let driver:String=RegKey::predef(HKEY_LOCAL_MACHINE).open_subkey(r"SYSTEM\CurrentControlSet\Services\PawnIO").unwrap().get_value("ImagePath").unwrap();
        let driver=driver.trim_start_matches(r"\??\").replace(r"\SystemRoot",&std::env::var("SystemRoot").unwrap());
        verify_binary(Path::new(&driver),Component::PawnIO,true).unwrap();
        assert!(installed(Component::PawnIO));
        assert!(installed(Component::WebView));
        assert!(installed(Component::DotNet));
        let task=framescope_update::autostart::state(&std::env::current_exe().unwrap()).unwrap();
        assert!(!task.exists&&!task.enabled);
    }
}
