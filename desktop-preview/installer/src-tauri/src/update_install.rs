//! Explicit update/recovery modes. All program replacement uses the same durable
//! install transaction as first install and uninstall.
use crate::{install_transaction::{self as transaction,Runtime,Result},installation_system::WindowsRuntime};
use framescope_update::{self as update,BuildInfo,Receipt,StartupTicket,StartupAck};
use std::{fs,path::Path,io::Read,time::{Duration,Instant}};

fn installed_build(target:&Path)->Result<BuildInfo>{
    let bytes=fs::read(target.join("FrameScopeBuildInfo.json")).map_err(|e|e.to_string())?;
    let build:BuildInfo=serde_json::from_slice(&bytes).map_err(|e|e.to_string())?;
    if build.version.split('.').next().and_then(|n|n.parse::<u64>().ok()).unwrap_or(0)<2
        || build.git_sha.len()!=40 || !build.git_sha.bytes().all(|b|b.is_ascii_hexdigit()) {return Err("更新与恢复仅支持已有 2.x 安装".into());}
    Ok(build)
}
fn verify_installed(target:&Path)->Result<()> {
    let bytes=fs::read(target.join(transaction::MANIFEST)).map_err(|e|e.to_string())?;
    let bytes=bytes.strip_prefix(&[0xef,0xbb,0xbf]).unwrap_or(&bytes);
    let manifest:serde_json::Value=serde_json::from_slice(bytes).map_err(|e|e.to_string())?;
    for file in manifest["files"].as_array().ok_or("Missing installed payload manifest")? {
        let relative=Path::new(file["path"].as_str().ok_or("Invalid installed file")?);
        crate::installer::checked_relative(relative)?;
        let path=target.join(relative);transaction::assert_path(&path)?;
        update::verify_file(&path,&update::Package{name:relative.to_string_lossy().into(),size:file["length"].as_u64().ok_or("Missing file length")?,sha256:file["sha256"].as_str().ok_or("Missing file hash")?.into()})?;
    }
    if !target.join("FrameScope Installer.exe").is_file(){return Err("Missing independent recovery installer".into());}
    Ok(())
}
pub fn perform_update(target:&Path,receipt_path:&Path)->Result<()> {
    let target=transaction::normalize(target)?;
    let old=installed_build(&target)?;
    verify_installed(&target)?;
    transaction::assert_path(receipt_path)?;
    let receipt:Receipt=serde_json::from_slice(&fs::read(receipt_path).map_err(|e|e.to_string())?).map_err(|e|e.to_string())?;
    let manifest=update::verify_manifest(receipt.manifest_text.as_bytes(),&receipt.signature,update::public_key(),&receipt.candidate.tag)?;
    if manifest.version!=env!("FRAMESCOPE_VERSION") || manifest.git_sha!=env!("FRAMESCOPE_GIT_SHA"){return Err("签名清单与安装器构建身份不一致".into());}
    let self_exe=std::env::current_exe().map_err(|e|e.to_string())?;
    update::verify_file(&self_exe,&manifest.installer)?;
    let mut zip=zip::ZipArchive::new(std::io::Cursor::new(crate::installer::PAYLOAD_ZIP)).map_err(|e|e.to_string())?;
    let mut metadata=String::new();zip.by_name("FrameScopeBuildInfo.json").map_err(|e|e.to_string())?.read_to_string(&mut metadata).map_err(|e|e.to_string())?;
    let payload_build:BuildInfo=serde_json::from_str(&metadata).map_err(|e|e.to_string())?;
    if payload_build.version!=manifest.version||payload_build.git_sha!=manifest.git_sha{return Err("Payload build identity does not match signed release".into());}
    let mut runtime=WindowsRuntime::new(&target,false,false,false)?;
    runtime.keep_previous=true;runtime.preserve_settings=true;
    runtime.startup_build=Some(BuildInfo::new(&manifest.version,&manifest.git_sha));
    let result=transaction::execute(transaction::Request{target:&target,payload:crate::installer::PAYLOAD_ZIP,self_exe:&self_exe,
        required:&crate::installer::required_payload_files()?,uninstall:false,delete_data:false,
        deletions:vec![],data_roots:vec![],progress:&|_,_|{}},&mut runtime);
    if result.is_ok(){update::atomic_json(&target.join("FrameScopeInstalledRelease.json"),&receipt)?;}
    else if installed_build(&target).is_ok_and(|b|b.version==old.version) {
        let _=update::atomic_json(&target.join("FrameScopeRolledBack.json"),&serde_json::json!({"version":manifest.version}));
    }
    finish(&target,result,Some(old))
}
pub fn restore_previous(target:&Path)->Result<()> {
    let target=transaction::normalize(target)?;
    let mut runtime=WindowsRuntime::new(&target,false,false,false)?;
    // An interrupted update has priority over a completed-version rollback.
    if let Some(helper)=transaction::interrupted_helper(&target,&runtime)? {
        runtime.stop(&target,&helper)?;
        transaction::recover(&target,&mut runtime)?;
        clear_handshake(&target)?;
        std::process::Command::new(target.join("FrameScope.exe")).current_dir(&target).spawn().map_err(|e|e.to_string())?;
        return Ok(());
    }
    transaction::recover(&target,&mut runtime)?;
    let current=installed_build(&target)?;
    let previous=transaction::previous_payload(&target,&runtime)?;
    let build:BuildInfo=serde_json::from_value(previous.build).map_err(|e|e.to_string())?;
    runtime.preserve_settings=true;runtime.startup_build=Some(build);
    update::atomic_json(&target.join("FrameScopeRolledBack.json"),&serde_json::json!({"version":current.version}))?;
    let result=transaction::execute(transaction::Request{target:&target,payload:&previous.payload,self_exe:&previous.installer,
        required:&["FrameScope.exe".into(),"FrameScopeMonitor.exe".into(),"FrameScopeUninstaller.exe".into()],uninstall:false,delete_data:false,
        deletions:previous.deletions,data_roots:vec![],progress:&|_,_|{}},&mut runtime);
    if result.is_ok(){
        transaction::discard_previous(&target,&runtime)?;
    }
    finish(&target,result,Some(current))
}
fn finish(target:&Path,result:Result<()>,old:Option<BuildInfo>)->Result<()> {
    let summary=serde_json::json!({"success":result.is_ok(),"error":result.as_ref().err(),"time":format!("{:?}",std::time::SystemTime::now())});
    let _=update::atomic_json(&target.join("FrameScopeUpdateResult.json"),&summary);
    if result.as_ref().err().is_some_and(|e|!e.contains("incomplete")&&!e.contains("journal retained")) {
        clear_handshake(target)?;
        if let Some(old)=old {if installed_build(target).is_ok_and(|b|b.version==old.version&&b.git_sha==old.git_sha){
            let _=std::process::Command::new(target.join("FrameScope.exe")).current_dir(target).spawn();
        }}
    }else if result.is_ok(){clear_handshake(target)?;}
    result
}
fn clear_handshake(target:&Path)->Result<()> {
    for name in ["FrameScopeUpdatePending.json","FrameScopeUpdateReady.json"] {
        let path=target.join(name);transaction::assert_path(&path)?;
        if path.exists(){fs::remove_file(path).map_err(|e|e.to_string())?;}
    }Ok(())
}
pub fn confirm_startup(runtime:&mut WindowsRuntime,build:BuildInfo)->Result<()> {
    let target=runtime.target.clone();
    let pending=target.join("FrameScopeUpdatePending.json");let ready=target.join("FrameScopeUpdateReady.json");
    transaction::assert_path(&pending)?;transaction::assert_path(&ready)?;
    if ready.exists(){fs::remove_file(&ready).map_err(|e|e.to_string())?;}
    let nonce=format!("{}-{:?}",std::process::id(),std::time::SystemTime::now());
    let ticket=StartupTicket{nonce:nonce.clone(),build:build.clone()};
    update::atomic_json(&pending,&ticket)?;
    let executable=target.join("FrameScope.exe").canonicalize().map_err(|e|e.to_string())?;
    let expected_hash=transaction::hash(&executable)?;
    let _child=std::process::Command::new(&executable).current_dir(&target).spawn().map_err(|e|e.to_string())?;
    runtime.started_new=true;
    let deadline=Instant::now()+Duration::from_secs(120);
    while Instant::now()<deadline {
        let ack=fs::read(&ready).ok().and_then(|v|serde_json::from_slice::<StartupAck>(&v).ok());
        if let Some(ack)=ack {
            let running=crate::self_relaunch::running_image(ack.pid).and_then(|p|p.canonicalize().map_err(|e|e.to_string())).ok();
            if update::startup_ack_matches(&ticket,&ack,&executable,running.as_deref())
                && transaction::hash(&executable)?==expected_hash {return Ok(());}
        }
        std::thread::sleep(Duration::from_millis(100));
    }
    // Never force terminate a capture/report. If normal shutdown cannot drain,
    // transaction recovery material is retained and replacement is abandoned.
    runtime.stop(&target,&target.join("FrameScopeUninstaller.exe"))?;
    runtime.started_new=false;
    Err("新版启动确认超时，恢复原版本".into())
}
