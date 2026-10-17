use super::*;
#[test]
fn installation_never_interrupts_capture_report_or_waiting_auto_watcher(){
    for phase in ["capturing","stopping","generating","waiting","unknown"]{assert!(!installation_allowed(phase));}
    for phase in ["idle","stopped","completed"]{assert!(installation_allowed(phase));}
}
#[test]
fn startup_handshake_rejects_stale_nonce_wrong_build_and_other_process_image(){
    let build=BuildInfo::new("2.0.0-beta.1",&"a".repeat(40));
    let ticket=StartupTicket{nonce:"fresh attempt".into(),build:build.clone()};
    let expected=Path::new("C:/FrameScope/FrameScope.exe");
    let mut ack=StartupAck{nonce:ticket.nonce.clone(),build,executable:expected.into(),pid:42};
    assert!(startup_ack_matches(&ticket,&ack,expected,Some(expected)));
    assert!(!startup_ack_matches(&ticket,&ack,expected,None));
    assert!(!startup_ack_matches(&ticket,&ack,expected,Some(Path::new("C:/old/FrameScope.exe"))));
    ack.nonce="old attempt".into();assert!(!startup_ack_matches(&ticket,&ack,expected,Some(expected)));
    ack.nonce=ticket.nonce.clone();ack.build.git_sha="b".repeat(40);assert!(!startup_ack_matches(&ticket,&ack,expected,Some(expected)));
    ack.build.git_sha=ticket.build.git_sha.clone();ack.build.version="2.0.0".into();assert!(!startup_ack_matches(&ticket,&ack,expected,Some(expected)));
}

#[test]
fn channel_selection_obeys_semver_and_never_silently_downgrades() {
    assert!(!eligible("2.0.0-beta.1", "2.0.0", false, false, None));
    assert!(eligible("2.0.0-beta.10", "2.0.0-beta.2", true, false, None));
    assert!(eligible("2.0.0", "2.0.0-beta.10", true, false, None));
    assert!(!eligible("2.0.0", "2.1.0-beta.1", false, false, None));
    assert!(eligible("2.0.0", "2.1.0-beta.1", false, true, None));
    assert!(!eligible("1.2.0", "2.0.0-beta.1", true, true, None));
    assert!(!eligible("2.0.1", "2.0.0", false, false, Some("2.0.1")));
    assert!(!eligible("2.0.0-beta.01", "2.0.0", true, false, None));
}

#[test]
fn manifest_rejects_paths_channel_mismatch_and_inconsistent_identity() {
    let valid = Manifest { schema: 1, version: "2.0.0-beta.1".into(), channel: "beta".into(),
        git_sha: "a".repeat(40), platform: "windows-x86_64".into(),
        installer: Package { name: "FrameScope_2.0.0-beta.1_x64-setup.exe".into(), size: 5, sha256: "b".repeat(64) } };
    assert!(valid.validate("v2.0.0-beta.1").is_ok());
    let mut bad = valid.clone(); bad.installer.name = "../bad.exe".into();
    assert!(bad.validate("v2.0.0-beta.1").is_err());
    bad = valid.clone(); bad.channel = "stable".into();
    assert!(bad.validate("v2.0.0-beta.1").is_err());
    assert!(valid.validate("v2.0.0").is_err());
}

#[test]
fn untrusted_manifest_signature_is_rejected_before_deserialization() {
    assert!(verify_manifest(b"{}", "bogus", "bogus", "v2.0.0").is_err());
}

const TEXT: &str = include_str!("../tests/fixtures/release-manifest.json");
const SIG: &str = include_str!("../tests/fixtures/release-manifest.json.minisig");
fn test_key() -> &'static str { include_str!("../tests/fixtures/test.pub").lines().nth(1).unwrap() }
fn candidate() -> Candidate { Candidate {id:42,tag:"v2.0.0-beta.1".into(),notes:"fixture".into(),manifest:verify_manifest(TEXT.as_bytes(),SIG,test_key(),"v2.0.0-beta.1").unwrap()} }

#[test]
fn real_minisign_signature_rejects_tampering_and_foreign_key() {
    assert!(verify_manifest(TEXT.as_bytes(), SIG, test_key(), "v2.0.0-beta.1").is_ok());
    assert!(verify_manifest(TEXT.replace("beta.1", "beta.2").as_bytes(),SIG,test_key(),"v2.0.0-beta.2").is_err());
    let production = include_str!("../release.pub").lines().nth(1).unwrap();
    assert!(verify_manifest(TEXT.as_bytes(),SIG,production,"v2.0.0-beta.1").is_err());
}

struct Server { base:String, stop:std::sync::Arc<AtomicBool>, thread:Option<std::thread::JoinHandle<()>> }
impl Server {
    fn start(package: &'static [u8]) -> Self {
        use std::net::TcpListener;
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        listener.set_nonblocking(true).unwrap();
        let base = format!("http://{}",listener.local_addr().unwrap());
        let stop = std::sync::Arc::new(AtomicBool::new(false));
        let done = stop.clone();
        let thread = std::thread::spawn(move || {
            while !done.load(Ordering::SeqCst) {
                let Ok((mut socket,_)) = listener.accept() else {std::thread::sleep(Duration::from_millis(5));continue;};
                socket.set_read_timeout(Some(Duration::from_secs(2))).unwrap();
                let mut request = [0u8;4096];
                let n = socket.read(&mut request).unwrap_or(0);
                let request = String::from_utf8_lossy(&request[..n]);
                let path = request.split_whitespace().nth(1).unwrap_or("");
                let body = if path.starts_with("/proxy/") { b"<html>proxy error</html>".to_vec() }
                    else if path.starts_with("/api/releases?") {
                        let valid=serde_json::json!({"id":42,"tag_name":"v2.0.0-beta.1","draft":false,"prerelease":true,"body":"fixture","assets":[
                            {"name":MANIFEST,"size":TEXT.len()},{"name":SIGNATURE,"size":SIG.len()},
                            {"name":"FrameScope_2.0.0-beta.1_x64-setup.exe","size":17}]});
                        let mut draft=valid.clone();draft["id"]=serde_json::json!(43);draft["draft"]=serde_json::json!(true);
                        let mut legacy=valid.clone();legacy["tag_name"]=serde_json::json!("v1.2.0");
                        let mut missing=valid.clone();missing["tag_name"]=serde_json::json!("v2.0.0-beta.2");missing["assets"]=serde_json::json!([]);
                        let mut mismatch=valid.clone();mismatch["prerelease"]=serde_json::json!(false);
                        serde_json::to_vec(&serde_json::json!([draft,legacy,missing,mismatch,valid])).unwrap()
                    } else if path.ends_with(SIGNATURE) {SIG.as_bytes().to_vec()}
                    else if path.ends_with(MANIFEST) {TEXT.as_bytes().to_vec()}
                    else {package.to_vec()};
                let _ = write!(socket,"HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n",body.len());
                let _ = socket.write_all(&body);
            }
        });
        Self {base,stop,thread:Some(thread)}
    }
    fn source(&self,proxy:bool)->Source {Source::new(format!("{}/api",self.base),format!("{}/download",self.base),proxy.then(||format!("{}/proxy/",self.base)),test_key().into()).unwrap()}
}
#[test]
fn disconnected_origin_returns_error_without_a_download(){
    let listener=std::net::TcpListener::bind("127.0.0.1:0").unwrap();let url=format!("http://{}",listener.local_addr().unwrap());drop(listener);
    let source=Source::new(url.clone(),url,None,test_key().into()).unwrap();
    assert!(source.check("2.0.0-beta.0",true,false,None,&AtomicBool::new(false)).is_err());
}
impl Drop for Server {fn drop(&mut self){self.stop.store(true,Ordering::SeqCst);self.thread.take().unwrap().join().unwrap();}}

#[test]
fn loopback_proxy_failure_falls_back_and_validated_cache_rejects_mutation() {
    let server = Server::start(b"fixture installer");
    let source = server.source(true);
    let cancel = AtomicBool::new(false);
    let list = source.check("2.0.0-beta.0",true,false,None,&cancel).unwrap();
    assert_eq!(list.len(),1);
    assert!(source.check("2.0.0-beta.0",false,false,None,&cancel).unwrap().is_empty());
    let cache = tempfile::tempdir().unwrap();
    let receipt = source.download(&list[0],cache.path(),&cancel,|_,_|{}).unwrap();
    let path = source.verified_cache(cache.path(),&receipt).unwrap();
    fs::write(&path,b"tampered").unwrap();
    assert!(source.verified_cache(cache.path(),&receipt).is_err());
}
#[test]
fn cancelled_and_wrong_hash_downloads_never_produce_installable_cache() {
    let server = Server::start(b"wrong installer!!");
    let source = server.source(false);
    let cache = tempfile::tempdir().unwrap();
    let cancel = AtomicBool::new(false);
    assert!(source.download(&candidate(),cache.path(),&cancel,|_,_|{}).is_err());
    assert!(!cache.path().join("receipt.json").exists());
    assert!(!cache.path().join(&candidate().manifest.installer.name).exists());
    cancel.store(true,Ordering::SeqCst);
    assert!(source.download(&candidate(),cache.path(),&cancel,|_,_|{}).is_err());
    assert!(!cache.path().join(&candidate().manifest.installer.name).with_extension("part").exists());
}

#[test]
fn cancellation_interrupts_a_stalled_download_socket() {
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}",listener.local_addr().unwrap());
    let server = std::thread::spawn(move || {
        let (mut socket,_) = listener.accept().unwrap();
        let mut request = [0u8;1024];let _ = socket.read(&mut request);
        write!(socket,"HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\nx").unwrap();
        std::thread::sleep(Duration::from_secs(1));
    });
    let cancel = std::sync::Arc::new(AtomicBool::new(false));
    let flag = cancel.clone();
    let trigger = std::thread::spawn(move || {std::thread::sleep(Duration::from_millis(150));flag.store(true,Ordering::SeqCst);});
    let cache = tempfile::tempdir().unwrap();
    let source = Source::new(url.clone(),url.clone(),None,test_key().into()).unwrap();
    let start = std::time::Instant::now();
    assert!(source.transfer(&url,&cache.path().join("partial"),100000,&cancel,&|_,_|{}).unwrap_err().contains("取消"));
    assert!(start.elapsed()<Duration::from_millis(800));
    trigger.join().unwrap();server.join().unwrap();
}
