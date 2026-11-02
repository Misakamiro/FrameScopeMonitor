//! Bounded local stages, never raw settings, paths, credentials or service payloads.
use std::{collections::HashMap,fs::{self,OpenOptions},io::Write,path::Path,sync::{Mutex,OnceLock},time::{Duration,Instant,SystemTime,UNIX_EPOCH}};
pub const LIMIT:u64=256*1024;
pub fn read_events(path:&Path)->Vec<serde_json::Value>{
    use std::io::Read;
    [path.with_extension("jsonl.1"),path.to_path_buf()].into_iter().flat_map(|file|{
        let mut text=String::new();
        if let Ok(file)=fs::File::open(file){let _=file.take(LIMIT).read_to_string(&mut text);}
        text.lines().filter_map(|line|serde_json::from_str(line).ok()).collect::<Vec<_>>()
    }).collect()
}
fn error_fields(error:&str)->(&'static str,Option<String>,Option<String>){
    let lower=error.to_ascii_lowercase();
    let category=if lower.contains("取消")||lower.contains("superseded"){"cancelled"}else if lower.contains("timeout")||lower.contains("timed out")||lower.contains("超时"){"timeout"}else if lower.contains("fixture field")||lower.contains("invalid type")||lower.contains("unknown field"){"decode-contract"}else if lower.contains("access denied")||lower.contains("权限")||lower.contains("os error 5"){"access-denied"}else{"native-error"};
    let code=lower.find("0x").and_then(|at|lower.get(at+2..at+10)).filter(|s|s.len()==8&&s.bytes().all(|c|c.is_ascii_hexdigit())).map(|s|format!("0x{s}"));
    let field=error.strip_prefix("Unknown fixture field: ").map(str::trim).filter(|s|!s.is_empty()&&s.len()<=64&&s.bytes().all(|c|c.is_ascii_alphanumeric()||c==b'_')).map(str::to_string);
    (category,code,field)
}
pub fn record(path:&Path,version:&str,generation:u64,style:&str,stage:&str,error:Option<&str>)->Result<(),String>{
    if version.len()>40||!version.bytes().all(|c|c.is_ascii_alphanumeric()||b".-".contains(&c))||stage.len()>40||!stage.bytes().all(|c|c.is_ascii_lowercase()||c==b'-'){return Err("invalid diagnostic stage".into());}
    let style=match style {"default"|"dynamic_island"|"vertical_panel"=>style,_=>"unknown"};
    let (category,code,field)=error.map(error_fields).unwrap_or(("ok",None,None));
    static LAST:OnceLock<Mutex<HashMap<String,Instant>>>=OnceLock::new();
    let mut last=LAST.get_or_init(||Mutex::new(HashMap::new())).lock().unwrap_or_else(|p|p.into_inner());
    let key=format!("{}:{generation}:{style}:{stage}:{category}:{code:?}:{field:?}",path.display());
    if last.get(&key).is_some_and(|at|at.elapsed()<Duration::from_secs(30)){return Ok(());}
    if last.len()>=256{last.retain(|_,at|at.elapsed()<Duration::from_secs(30));if last.len()>=256{last.clear();}}
    let time_ms=SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_millis();
    let line=serde_json::to_vec(&serde_json::json!({"pid":std::process::id(),"timeMs":time_ms,"version":version,"generation":generation,"style":style,"stage":stage,"category":category,"code":code,"field":field})).map_err(|e|e.to_string())?;
    if let Some(parent)=path.parent(){fs::create_dir_all(parent).map_err(|e|e.to_string())?;}
    if fs::metadata(path).map(|m|m.len()).unwrap_or(0)+line.len() as u64+1>LIMIT{
        let previous=path.with_extension("jsonl.1");
        match fs::remove_file(&previous){Ok(())=>(),Err(e) if e.kind()==std::io::ErrorKind::NotFound=>(),Err(e)=>return Err(e.to_string())}
        if path.exists(){fs::rename(path,&previous).map_err(|e|e.to_string())?;}
    }
    let mut file=OpenOptions::new().create(true).append(true).open(path).map_err(|e|e.to_string())?;
    file.write_all(&line).and_then(|_|file.write_all(b"\n")).map_err(|e|e.to_string())?;last.insert(key,Instant::now());Ok(())
}
#[cfg(test)]
mod tests{
    use super::*;
    #[test] fn redacts_free_text_throttles_duplicates_and_rotates(){
        let root=std::env::temp_dir().join(format!("framescope-osd-log-{}-{}",std::process::id(),SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()));let path=root.join("overlay.jsonl");
        record(&path,"2.1.1-beta.2",1,"vertical_panel","create",Some("C:\\Users\\private token=secret 0x8007139f")).unwrap();
        let text=fs::read_to_string(&path).unwrap();assert!(!text.contains("private")&&!text.contains("secret")&&text.contains("0x8007139f"));
        record(&path,"2.1.1-beta.2",1,"vertical_panel","create",Some("C:\\Users\\private token=secret 0x8007139f")).unwrap();assert_eq!(text,fs::read_to_string(&path).unwrap());
        fs::write(&path,vec![b'x';LIMIT as usize]).unwrap();record(&path,"2.1.1-beta.2",2,"vertical_panel","stopped",None).unwrap();assert!(fs::metadata(&path).unwrap().len()<=LIMIT);assert!(path.with_extension("jsonl.1").exists());
        fs::remove_dir_all(root).unwrap();
    }
}
