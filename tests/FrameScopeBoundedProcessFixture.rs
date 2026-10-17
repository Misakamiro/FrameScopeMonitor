#[path = "../desktop-preview/src-tauri/src/bounded_process.rs"]
mod bounded_process;
#[cfg(feature = "baseline")]
mod baseline { include!(concat!(env!("CARGO_MANIFEST_DIR"), "/baseline.rs")); }
use std::{io::Write, process::{Command, Stdio}, time::{Duration, Instant}};
fn main() {
    let args: Vec<String> = std::env::args().collect();
    if args.get(1).map(String::as_str) == Some("fixture") {
        let mode = args[2].as_str();
        if mode == "holder" { std::fs::write(&args[3], std::process::id().to_string()).unwrap(); std::thread::sleep(Duration::from_secs(60)); return; }
        std::fs::write(format!("{}.parent", args[3]), std::process::id().to_string()).unwrap();
        if mode == "inherited" || mode == "hang-tree" {
            let _child = Command::new(std::env::current_exe().unwrap()).args(["fixture", "holder", &args[3]]).stdin(Stdio::null()).stdout(Stdio::inherit()).stderr(Stdio::inherit()).spawn().unwrap();
            while !std::path::Path::new(&args[3]).exists() { std::thread::sleep(Duration::from_millis(2)); }
            if mode == "hang-tree" { std::thread::sleep(Duration::from_secs(60)); }
            return;
        }
        if mode == "output" { let bytes = vec![b'x'; 1024 * 1024 + 8192]; std::io::stdout().write_all(&bytes).unwrap(); std::io::stderr().write_all(&bytes).unwrap(); return; }
        if mode == "nonzero" { eprint!("controlled failure"); std::process::exit(17); }
        if mode == "hang" { std::thread::sleep(Duration::from_secs(60)); return; }
        if mode == "flood" { let bytes = [b'x'; 16384]; loop { std::io::stdout().write_all(&bytes).unwrap(); std::io::stderr().write_all(&bytes).unwrap(); } }
        panic!("unknown fixture");
    }
    let mode = args[2].as_str();
    let mut command = Command::new(std::env::current_exe().unwrap());
    command.args(["fixture", mode]);
    let pid_path = std::env::temp_dir().join(format!("framescope-owned-tree-{}-{mode}.pid", std::process::id()));
    assert!(!pid_path.exists());
    command.arg(&pid_path);
    let timeout = Duration::from_secs(3);
    let started = Instant::now();
    #[cfg(feature = "baseline")]
    let result = if args[1] == "baseline" { baseline::run(command, timeout) } else { bounded_process::run(command, timeout) };
    #[cfg(not(feature = "baseline"))]
    let result = bounded_process::run(command, timeout);
    assert!(started.elapsed() < timeout + Duration::from_millis(150), "total deadline exceeded: {:?}", started.elapsed());
    match mode {
        "inherited" => assert!(result.as_ref().unwrap().0.success()),
        "output" => { let (_, out, err) = result.as_ref().unwrap(); assert_eq!(out.len(), 1024*1024); assert_eq!(err.len(), 1024*1024); assert!(out.iter().chain(err).all(|b| *b == b'x')); },
        "nonzero" => { let (status, _, err) = result.as_ref().unwrap(); assert_eq!(status.code(), Some(17)); assert_eq!(err, b"controlled failure"); },
        "hang" | "hang-tree" | "flood" => assert!(result.as_ref().unwrap_err().contains("超时")),
        _ => panic!("unknown test"),
    }
    for path in [&pid_path, &pid_path.with_extension("pid.parent")] {
      if path.exists() {
        let pid: u32 = std::fs::read_to_string(path).unwrap().parse().unwrap();
        unsafe {
            use windows_sys::Win32::{Foundation::{CloseHandle, WAIT_TIMEOUT}, System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE}};
            let handle = OpenProcess(PROCESS_SYNCHRONIZE, 0, pid);
            if !handle.is_null() { let state = WaitForSingleObject(handle, 0); CloseHandle(handle); assert_ne!(state, WAIT_TIMEOUT, "owned descendant remains alive: {pid}"); }
        }
        std::fs::remove_file(path).unwrap();
      }
    }
    println!("PASS {mode}: {:?}", started.elapsed());
}
