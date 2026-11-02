use super::*;
use std::{net::TcpListener,sync::{Arc,Mutex,atomic::AtomicUsize},thread};

struct RangeServer { base:String, stop:Arc<AtomicBool>, requests:Arc<Mutex<Vec<(bool,u64,u64)>>>, closed:Arc<AtomicUsize>, worker:Option<thread::JoinHandle<()>>, data:Arc<Vec<u8>> }
impl RangeServer {
    fn start(ignore_range:bool, bad_range:bool, late_slow:bool)->Self {
        let listener=TcpListener::bind("127.0.0.1:0").unwrap();listener.set_nonblocking(true).unwrap();
        let base=format!("http://{}",listener.local_addr().unwrap());let stop=Arc::new(AtomicBool::new(false));let requests=Arc::new(Mutex::new(Vec::new()));let closed=Arc::new(AtomicUsize::new(0));
        let data=Arc::new((0..20*1024*1024).map(|i|(i%251) as u8).collect::<Vec<_>>());
        let (done,record,closed_jobs,payload)=(stop.clone(),requests.clone(),closed.clone(),data.clone());
        let worker=thread::spawn(move||{let mut jobs=Vec::new();while !done.load(Ordering::SeqCst){
            let Ok((mut socket,_))=listener.accept() else{thread::sleep(Duration::from_millis(2));continue;};
            let (record,closed,payload,done)=(record.clone(),closed_jobs.clone(),payload.clone(),done.clone());
            jobs.push(thread::spawn(move||{socket.set_nonblocking(false).unwrap();socket.set_read_timeout(Some(Duration::from_secs(3))).unwrap();let mut request=Vec::new();let mut byte=[0];while request.len()<16384{if socket.read(&mut byte).unwrap_or(0)==0{return;}request.push(byte[0]);if request.ends_with(b"\r\n\r\n"){break;}}
                let request=String::from_utf8_lossy(&request);let proxy=request.lines().next().unwrap_or("").contains("/proxy/");
                let range=request.lines().find_map(|l|l.to_ascii_lowercase().strip_prefix("range: bytes=").map(str::to_string));
                let parsed=range.as_deref().and_then(|r|r.split_once('-')).map(|(s,e)|(s.parse::<u64>().unwrap(),e.parse::<u64>().unwrap()));
                let (start,end)=if ignore_range{(0,payload.len() as u64-1)}else{parsed.unwrap_or((0,payload.len() as u64-1))};
                record.lock().unwrap().push((proxy,start,end));
                if proxy&&end-start<1024*1024{thread::sleep(Duration::from_millis(60));}
                let status=if parsed.is_some()&&!ignore_range{"206 Partial Content"}else{"200 OK"};let advertised=if bad_range&&start>0{start-1}else{start};
                let _=write!(socket,"HTTP/1.1 {status}\r\nContent-Length: {}\r\nContent-Range: bytes {advertised}-{end}/{}\r\nConnection: close\r\n\r\n",end-start+1,payload.len());
                if late_slow&&!proxy&&start>0{while !done.load(Ordering::SeqCst){if socket.write_all(&[0]).is_err(){closed.fetch_add(1,Ordering::SeqCst);break;}thread::sleep(Duration::from_millis(30));}}
                else{for chunk in payload[start as usize..=end as usize].chunks(65536){if socket.write_all(chunk).is_err(){break;}if end-start>=4*1024*1024-1{thread::sleep(Duration::from_millis(1));}else if proxy{thread::sleep(Duration::from_millis(2));}}}
            }));
        }for job in jobs{job.join().unwrap();}});
        Self{base,stop,requests,closed,worker:Some(worker),data}
    }
    fn source(&self)->Source{Source::new(self.base.clone(),self.base.clone(),Some(format!("{}/proxy/",self.base)),String::new()).unwrap()}
    fn package(&self)->Package{Package{name:"fixture.exe".into(),size:self.data.len() as u64,sha256:format!("{:x}",Sha256::digest(&*self.data))}}
}
impl Drop for RangeServer{fn drop(&mut self){self.stop.store(true,Ordering::SeqCst);self.worker.take().unwrap().join().unwrap();}}

#[test]
fn range_headers_must_bind_exact_offsets_and_total(){
    assert!(crate::asset_download::range_matches("bytes 4-7/12",4,7,12));
    for value in ["bytes 0-7/12","bytes 4-7/13","bytes 4-8/12","bytes */12","nonsense"]{assert!(!crate::asset_download::range_matches(value,4,7,12));}
}
#[test]
fn segmented_asset_is_assembled_verified_and_limited_to_four_requests(){
    let server=RangeServer::start(false,false,false);let source=server.source();let dir=tempfile::tempdir().unwrap();let path=dir.path().join("asset.part");
    let events=Mutex::new(Vec::new());source.download_asset(&format!("{}/asset",server.base),&path,&server.package(),&AtomicBool::new(false),&|_,_|{},&|e|events.lock().unwrap().push(e)).unwrap();
    assert_eq!(fs::read(&path).unwrap(),*server.data);let concurrent=events.lock().unwrap().iter().filter_map(|e|e.active_requests).collect::<Vec<_>>();assert_eq!(concurrent.iter().max(),Some(&4));assert!(concurrent.iter().all(|n|*n<=4));assert!(server.requests.lock().unwrap().iter().any(|(_,start,_)|*start==4*1024*1024),"requests={:?}, telemetry={:?}",server.requests.lock().unwrap(),events.lock().unwrap());assert_eq!(fs::read_dir(dir.path()).unwrap().count(),1);
}
#[test]
fn ignored_or_invalid_ranges_fall_back_without_corrupting_the_asset(){
    for (ignore,bad) in [(true,false),(false,true)]{let server=RangeServer::start(ignore,bad,false);let source=server.source();let dir=tempfile::tempdir().unwrap();let path=dir.path().join("asset.part");
        source.download_asset(&format!("{}/asset",server.base),&path,&server.package(),&AtomicBool::new(false),&|_,_|{},&|_|{}).unwrap();assert_eq!(fs::read(&path).unwrap(),*server.data);assert_eq!(fs::read_dir(dir.path()).unwrap().count(),1);
    }
}
#[test]
fn slow_route_switch_keeps_completed_chunks(){
    let server=RangeServer::start(false,false,true);let source=server.source();source.origin_first.store(true,Ordering::SeqCst);let dir=tempfile::tempdir().unwrap();let path=dir.path().join("asset.part");let events=Mutex::new(Vec::new());
    source.download_asset(&format!("{}/asset",server.base),&path,&server.package(),&AtomicBool::new(false),&|_,_|{},&|event|events.lock().unwrap().push(event)).unwrap();assert_eq!(fs::read(path).unwrap(),*server.data);
    let requests=server.requests.lock().unwrap();assert!(requests.iter().any(|(proxy,start,_)|*proxy&&*start>=4*1024*1024));assert!(!requests.iter().any(|(proxy,start,end)|*proxy&&*start==0&&*end==4*1024*1024-1));assert!(events.lock().unwrap().iter().any(|e|e.event=="switch"));
}

#[test]
fn cancelling_segments_closes_sockets_and_preserves_verified_cache(){
    let server=RangeServer::start(false,false,true);let source=server.source();source.origin_first.store(true,Ordering::SeqCst);let dir=tempfile::tempdir().unwrap();let path=dir.path().join("asset.part");let verified=dir.path().join("previous.exe");fs::write(&verified,b"verified baseline").unwrap();
    let cancel=Arc::new(AtomicBool::new(false));let flag=cancel.clone();let requests=server.requests.clone();let trigger=thread::spawn(move||{let deadline=std::time::Instant::now()+Duration::from_secs(5);while std::time::Instant::now()<deadline{if requests.lock().unwrap().iter().filter(|(proxy,start,_)|!*proxy&&*start>0).count()>=2{flag.store(true,Ordering::SeqCst);return;}thread::sleep(Duration::from_millis(5));}panic!("segment requests did not start");});
    let error=source.download_asset(&format!("{}/asset",server.base),&path,&server.package(),&cancel,&|_,_|{},&|_|{}).unwrap_err();trigger.join().unwrap();assert!(error.contains("取消"));let deadline=std::time::Instant::now()+Duration::from_secs(2);while server.closed.load(Ordering::SeqCst)==0&&std::time::Instant::now()<deadline{thread::sleep(Duration::from_millis(10));}assert!(server.closed.load(Ordering::SeqCst)>0);assert_eq!(fs::read(&verified).unwrap(),b"verified baseline");assert_eq!(fs::read_dir(dir.path()).unwrap().count(),1);
}

#[test]
fn wrong_complete_hash_never_replaces_a_verified_asset_and_switches_are_bounded(){
    let server=RangeServer::start(false,false,false);let source=server.source();let dir=tempfile::tempdir().unwrap();let path=dir.path().join("asset.part");let verified=dir.path().join("previous.exe");fs::write(&verified,b"verified baseline").unwrap();let mut package=server.package();package.sha256="0".repeat(64);let events=Mutex::new(Vec::new());
    assert!(source.download_asset(&format!("{}/asset",server.base),&path,&package,&AtomicBool::new(false),&|_,_|{},&|e|events.lock().unwrap().push(e)).is_err());assert_eq!(fs::read(&verified).unwrap(),b"verified baseline");assert_eq!(events.lock().unwrap().iter().filter(|e|e.event=="switch").count(),2);assert!(!fs::read_dir(dir.path()).unwrap().any(|e|e.unwrap().path().is_dir()));
}
