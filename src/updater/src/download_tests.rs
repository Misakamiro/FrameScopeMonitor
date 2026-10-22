use super::*;
use std::{net::TcpListener, sync::{Arc, Mutex}, thread, time::Instant};

const TEXT: &str = include_str!("../tests/fixtures/release-manifest.json");
const SIG: &str = include_str!("../tests/fixtures/release-manifest.json.minisig");
const PACKAGE: &[u8] = b"fixture installer";

struct Routes {
    base: String,
    requests: Arc<Mutex<Vec<String>>>,
    stop: Arc<AtomicBool>,
    thread: Option<thread::JoinHandle<()>>,
}
impl Routes {
    fn start() -> Self {
        Self::with_late_slowdown(false)
    }
    fn with_late_slowdown(late_slowdown: bool) -> Self {
        Self::with_behavior(late_slowdown, false)
    }
    fn with_behavior(late_slowdown: bool, corrupt_accelerator: bool) -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        listener.set_nonblocking(true).unwrap();
        let base = format!("http://{}", listener.local_addr().unwrap());
        let requests = Arc::new(Mutex::new(Vec::<String>::new()));
        let stop = Arc::new(AtomicBool::new(false));
        let (seen, done) = (requests.clone(), stop.clone());
        let thread = thread::spawn(move || {
            let mut workers = Vec::new();
            while !done.load(Ordering::SeqCst) {
                let Ok((mut socket, _)) = listener.accept() else { thread::sleep(Duration::from_millis(5)); continue; };
                let seen = seen.clone();
                workers.push(thread::spawn(move || {
                    socket.set_nonblocking(false).unwrap();
                    socket.set_read_timeout(Some(Duration::from_secs(2))).unwrap();
                    let mut bytes = [0u8;4096];
                    let n = socket.read(&mut bytes).unwrap_or(0);
                    let request = String::from_utf8_lossy(&bytes[..n]).to_string();
                    let path = request.split_whitespace().nth(1).unwrap_or("").to_string();
                    let sample = request.to_ascii_lowercase().contains("range:");
                    seen.lock().unwrap().push(request);
                    let body = if path.ends_with(SIGNATURE) { SIG.as_bytes() }
                        else if path.ends_with(MANIFEST) { TEXT.as_bytes() } else { PACKAGE };
                    let body = if corrupt_accelerator && path.starts_with("/proxy/") && path.ends_with("setup.exe") {
                        b"tampered payload!".as_slice()
                    } else { body };
                    if write!(socket, "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", body.len()).is_err() { return; }
                    if late_slowdown && sample && path.starts_with("/proxy/") { thread::sleep(Duration::from_millis(200)); }
                    if path.ends_with("setup.exe") && !path.starts_with("/proxy/") && (!late_slowdown || !sample) {
                        // The origin serves metadata quickly but its asset CDN
                        // trickles. The accelerator serves the same valid bytes.
                        for byte in body {
                            if socket.write_all(&[*byte]).is_err() { break; }
                            thread::sleep(Duration::from_millis(if late_slowdown { 600 } else { 20 }));
                        }
                    } else { let _ = socket.write_all(body); }
                }));
            }
            for worker in workers { worker.join().unwrap(); }
        });
        Self { base, requests, stop, thread: Some(thread) }
    }
    fn source(&self, accelerated: bool) -> Source {
        Source::new(self.base.clone(), format!("{}/download", self.base),
            accelerated.then(|| format!("{}/proxy/", self.base)),
            include_str!("../tests/fixtures/test.pub").lines().nth(1).unwrap().into()).unwrap()
    }
    fn candidate() -> Candidate {
        let key = include_str!("../tests/fixtures/test.pub").lines().nth(1).unwrap();
        Candidate { id: 42, tag: "v2.0.0-beta.1".into(), notes: String::new(),
            manifest: verify_manifest(TEXT.as_bytes(), SIG, key, "v2.0.0-beta.1").unwrap() }
    }
}
impl Drop for Routes {
    fn drop(&mut self) { self.stop.store(true, Ordering::SeqCst); self.thread.take().unwrap().join().unwrap(); }
}

#[test]
fn asset_routing_uses_payload_speed_instead_of_fast_metadata() {
    let server = Routes::start();
    let source = server.source(true);
    source.origin_first.store(true, Ordering::Relaxed);
    let cache = tempfile::tempdir().unwrap();
    let receipt = source.download(&Routes::candidate(), cache.path(), &AtomicBool::new(false), |_,_|{}).unwrap();
    assert_eq!(fs::read(source.verified_cache(cache.path(), &receipt).unwrap()).unwrap(), PACKAGE);
    let requests = server.requests.lock().unwrap();
    let assets: Vec<_> = requests.iter().filter(|request| request.lines().next().unwrap_or("").contains("setup.exe")).collect();
    assert_eq!(assets.iter().filter(|request| request.to_ascii_lowercase().contains("range: bytes=0-")).count(), 2,
        "both installer routes must be sampled; metadata success is not download speed");
    let final_request = assets.iter().rev().find(|request| !request.to_ascii_lowercase().contains("range:")).unwrap();
    assert!(final_request.starts_with("GET /proxy/"), "the faster asset route must win despite origin metadata preference");
}

#[test]
fn disabling_acceleration_never_probes_or_downloads_from_the_accelerator() {
    let server = Routes::start();
    let source = server.source(false);
    let cache = tempfile::tempdir().unwrap();
    source.download(&Routes::candidate(), cache.path(), &AtomicBool::new(false), |_,_|{}).unwrap();
    let requests = server.requests.lock().unwrap();
    assert!(requests.iter().all(|request| !request.contains("/proxy/") && !request.to_ascii_lowercase().contains("range:")));
}

#[test]
fn a_faster_tampered_asset_never_bypasses_hash_verification() {
    let server = Routes::with_behavior(false, true);
    let source = server.source(true);
    let cache = tempfile::tempdir().unwrap();
    let receipt = source.download(&Routes::candidate(), cache.path(), &AtomicBool::new(false), |_,_|{}).unwrap();
    assert_eq!(fs::read(source.verified_cache(cache.path(), &receipt).unwrap()).unwrap(), PACKAGE);
    let requests = server.requests.lock().unwrap();
    let full_assets: Vec<_> = requests.iter().filter(|request| request.lines().next().unwrap_or("").contains("setup.exe")
        && !request.to_ascii_lowercase().contains("range:")).collect();
    assert_eq!(full_assets.len(), 2);
    assert!(full_assets[0].starts_with("GET /proxy/"));
    assert!(full_assets[1].starts_with("GET /download/"));
}

#[test]
fn the_last_available_route_can_finish_on_a_low_bandwidth_connection() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    let server = thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        socket.set_read_timeout(Some(Duration::from_secs(2))).unwrap();
        let mut request = [0u8;2048]; let _ = socket.read(&mut request);
        write!(socket, "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", PACKAGE.len()).unwrap();
        for byte in PACKAGE { socket.write_all(&[*byte]).unwrap(); thread::sleep(Duration::from_millis(600)); }
    });
    let source = Source::new(url.clone(), url.clone(), None, String::new()).unwrap();
    let cache = tempfile::tempdir().unwrap();
    let path = cache.path().join("slow-but-valid");
    source.transfer_with_speed_check(&url, &path, PACKAGE.len() as u64, &AtomicBool::new(false), &|_,_|{}, false).unwrap();
    server.join().unwrap();
    assert_eq!(fs::read(path).unwrap(), PACKAGE);
}

#[test]
fn a_route_that_slows_after_sampling_falls_back_and_still_verifies_the_complete_package() {
    let server = Routes::with_late_slowdown(true);
    let source = server.source(true);
    let cache = tempfile::tempdir().unwrap();
    let progress = Mutex::new(Vec::new());
    let receipt = source.download(&Routes::candidate(), cache.path(), &AtomicBool::new(false), |done,_| {
        progress.lock().unwrap().push(done);
    }).unwrap();
    assert_eq!(fs::read(source.verified_cache(cache.path(), &receipt).unwrap()).unwrap(), PACKAGE);
    assert_eq!(progress.lock().unwrap().iter().filter(|&&done| done == 0).count(), 2, "route switch resets progress exactly once");
    let requests = server.requests.lock().unwrap();
    let full_assets: Vec<_> = requests.iter().filter(|request| request.lines().next().unwrap_or("").contains("setup.exe")
        && !request.to_ascii_lowercase().contains("range:")).collect();
    assert_eq!(full_assets.len(), 2);
    assert!(full_assets[0].starts_with("GET /download/"));
    assert!(full_assets[1].starts_with("GET /proxy/"));
}

#[test]
fn cancelling_asset_sampling_closes_both_connections() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    listener.set_nonblocking(true).unwrap();
    let (connected, waiting) = std::sync::mpsc::channel();
    let server = thread::spawn(move || {
        let deadline = Instant::now() + Duration::from_secs(3);
        let mut sockets = Vec::new();
        while sockets.len() < 2 && Instant::now() < deadline {
            let Ok((mut socket, _)) = listener.accept() else { thread::sleep(Duration::from_millis(5)); continue; };
            socket.set_nonblocking(false).unwrap();
            socket.set_read_timeout(Some(Duration::from_secs(2))).unwrap();
            let mut request = [0u8;2048]; let _ = socket.read(&mut request);
            write!(socket, "HTTP/1.1 200 OK\r\nContent-Length: 1048576\r\nConnection: close\r\n\r\nx").unwrap();
            sockets.push(socket);
        }
        connected.send(sockets.len()).unwrap();
        let mut closed = 0;
        for mut socket in sockets {
            let mut byte = [0u8;1];
            if matches!(socket.read(&mut byte), Ok(0)) { closed += 1; }
        }
        closed
    });
    let cancel = Arc::new(AtomicBool::new(false));
    let flag = cancel.clone();
    let trigger = thread::spawn(move || { let count = waiting.recv_timeout(Duration::from_secs(4)).unwrap(); flag.store(true, Ordering::SeqCst); count });
    let source = Source::new(url.clone(), url.clone(), Some(format!("{url}/proxy/")), String::new()).unwrap();
    let start = Instant::now();
    assert!(source.download_routes(&url, 1048576, &cancel).unwrap_err().contains("取消"));
    assert!(start.elapsed() < Duration::from_secs(2));
    assert_eq!(trigger.join().unwrap(), 2);
    assert_eq!(server.join().unwrap(), 2, "cancelled probes must not keep downloading in the background");
}

#[test]
fn slow_trickles_switch_before_the_overall_download_timeout() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    let server = thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        socket.set_read_timeout(Some(Duration::from_secs(2))).unwrap();
        let mut request = [0u8;2048]; let _ = socket.read(&mut request);
        write!(socket, "HTTP/1.1 200 OK\r\nContent-Length: 1048576\r\nConnection: close\r\n\r\n").unwrap();
        let until = Instant::now() + Duration::from_secs(12);
        while Instant::now() < until {
            if socket.write_all(&[0u8;1024]).is_err() { break; }
            thread::sleep(Duration::from_millis(40));
        }
    });
    let cancel = Arc::new(AtomicBool::new(false));
    let flag = cancel.clone();
    let trigger = thread::spawn(move || { thread::sleep(Duration::from_millis(9500)); flag.store(true, Ordering::SeqCst); });
    let source = Source::new(url.clone(), url.clone(), None, String::new()).unwrap();
    let cache = tempfile::tempdir().unwrap();
    let start = Instant::now();
    let error = source.transfer(&url, &cache.path().join("partial"), 1048576, &cancel, &|_,_|{}).unwrap_err();
    trigger.join().unwrap(); server.join().unwrap();
    assert!(error.contains("持续低速"), "a live trickle must trigger speed fallback, got: {error}");
    assert!(start.elapsed() < Duration::from_secs(12));
}
