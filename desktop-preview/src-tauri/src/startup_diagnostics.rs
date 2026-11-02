use std::{fs::{self,OpenOptions},io::Write,path::Path,sync::Mutex,time::{SystemTime,UNIX_EPOCH}};
static WRITE:Mutex<()>=Mutex::new(());
pub const LIMIT:u64=256*1024;
pub fn allowed_phase(phase:&str)->bool {
    matches!(phase,"native-start"|"navigation-start"|"navigation-finished"|"frontend-module"|"react-mounted"|"ui-painted"|"update-confirmed"|"frontend-failed"|"frontend-warning"|"renderer-failed"|"renderer-unresponsive"|"subframe-failed"|"utility-failed"|"other-process-failed"|"browser-failed"|"gpu-failed"|"main-visible"|"main-hidden"|"main-minimized")
}
pub fn record(path:&Path,version:&str,phase:&str)->Result<(),String> {
    if !allowed_phase(phase)||!version.bytes().all(|c|c.is_ascii_alphanumeric()||b".-".contains(&c)){return Err("Invalid startup diagnostic phase".into());}
    let _lock=WRITE.lock().unwrap_or_else(|p|p.into_inner());
    let time=SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_millis();
    let line=format!("{{\"pid\":{},\"timeMs\":{time},\"version\":\"{version}\",\"phase\":\"{phase}\"}}\n",std::process::id());
    if fs::metadata(path).map(|m|m.len()).unwrap_or(0)+line.len() as u64>LIMIT{return Ok(());}
    if let Some(parent)=path.parent(){fs::create_dir_all(parent).map_err(|e|e.to_string())?;}
    OpenOptions::new().create(true).append(true).open(path).and_then(|mut f|f.write_all(line.as_bytes())).map_err(|e|e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_free_text_and_keeps_the_file_bounded_without_deleting_history() {
        let dir=std::env::temp_dir().join(format!("framescope-startup-{}-{}",std::process::id(),SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()));
        fs::create_dir(&dir).unwrap();let path=dir.join("startup.jsonl");
        assert!(record(&path,"2.0.1-beta.12","token=private").is_err());assert!(!path.exists());
        record(&path,"2.0.1-beta.12","ui-painted").unwrap();assert!(fs::read_to_string(&path).unwrap().contains("ui-painted"));
        fs::write(&path,vec![b'x';LIMIT as usize]).unwrap();record(&path,"2.0.1-beta.12","main-hidden").unwrap();assert_eq!(fs::metadata(&path).unwrap().len(),LIMIT);
        fs::remove_file(&path).unwrap();fs::remove_dir(&dir).unwrap();
    }
}
