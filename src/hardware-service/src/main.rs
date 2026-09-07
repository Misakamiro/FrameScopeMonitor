use framescope_hardware_service::Request;
use std::io::{self, BufRead, Write};

fn main() {
    let args: Vec<String> = std::env::args().collect();
    if let Some(pipe_name) = args
        .windows(2)
        .find(|pair| pair[0] == "--pipe-name")
        .map(|pair| pair[1].clone())
    {
        #[cfg(windows)]
        {
            let helper = args
                .windows(2)
                .find(|pair| pair[0] == "--lhm-helper")
                .map(|pair| pair[1].clone());
            if let Err(error) = run_named_pipe(&pipe_name, helper) {
                eprintln!("framescope-hardware-service named pipe error: {error}");
                std::process::exit(2);
            }
            return;
        }
        #[cfg(not(windows))]
        {
            eprintln!("named pipe mode is only available on Windows: {pipe_name}");
            std::process::exit(3);
        }
    }

    run_stdio(
        args.windows(2)
            .find(|pair| pair[0] == "--lhm-helper")
            .map(|pair| pair[1].clone()),
    );
}

fn run_stdio(helper: Option<String>) {
    let stdin = io::stdin();
    let mut stdout = io::BufWriter::new(io::stdout());
    let mut state = framescope_hardware_service::ServiceState::default();
    state.lhm_helper_path = helper;
    for line in stdin.lock().lines() {
        let Ok(line) = line else { break };
        if line.trim().is_empty() {
            continue;
        }
        let request = serde_json::from_str::<Request>(&line);
        let should_stop = request
            .as_ref()
            .map(|request| request.method == "stop")
            .unwrap_or(false);
        let response = match request {
            Ok(request) => framescope_hardware_service::dispatch_with_state(request, &mut state),
            Err(error) => {
                serde_json::json!({"id":"","ok":false,"result":null,"error":error.to_string()})
            }
        };
        writeln!(stdout, "{}", response).ok();
        stdout.flush().ok();
        if should_stop {
            break;
        }
    }
}

#[cfg(windows)]
fn run_named_pipe(pipe_name: &str, helper: Option<String>) -> io::Result<()> {
    use std::ffi::OsStr;
    use std::iter::once;
    use std::os::windows::ffi::OsStrExt;
    use std::ptr::null_mut;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::Arc;

    type Handle = *mut std::ffi::c_void;
    type Dword = u32;
    type Bool = i32;
    const INVALID_HANDLE_VALUE: Handle = -1isize as Handle;
    const PIPE_ACCESS_DUPLEX: Dword = 0x00000003;
    const PIPE_TYPE_MESSAGE: Dword = 0x00000004;
    const PIPE_READMODE_MESSAGE: Dword = 0x00000002;
    const PIPE_WAIT: Dword = 0x00000000;
    const PIPE_UNLIMITED_INSTANCES: Dword = 255;
    const ERROR_PIPE_CONNECTED: i32 = 535;

    extern "system" {
        fn CreateNamedPipeW(
            name: *const u16,
            open_mode: Dword,
            pipe_mode: Dword,
            max_instances: Dword,
            out_size: Dword,
            in_size: Dword,
            timeout: Dword,
            security: *mut std::ffi::c_void,
        ) -> Handle;
        fn ConnectNamedPipe(pipe: Handle, overlapped: *mut std::ffi::c_void) -> Bool;
        fn DisconnectNamedPipe(pipe: Handle) -> Bool;
        fn CloseHandle(handle: Handle) -> Bool;
        fn FlushFileBuffers(file: Handle) -> Bool;
        fn ReadFile(
            file: Handle,
            buffer: *mut u8,
            bytes: Dword,
            read: *mut Dword,
            overlapped: *mut std::ffi::c_void,
        ) -> Bool;
        fn WriteFile(
            file: Handle,
            buffer: *const u8,
            bytes: Dword,
            written: *mut Dword,
            overlapped: *mut std::ffi::c_void,
        ) -> Bool;
    }

    struct PipeHandle(Handle);
    unsafe impl Send for PipeHandle {}

    let full_name = if pipe_name.starts_with("\\\\.\\pipe\\") {
        pipe_name.to_string()
    } else {
        format!("\\\\.\\pipe\\{}", pipe_name)
    };
    let wide: Vec<u16> = OsStr::new(&full_name)
        .encode_wide()
        .chain(once(0))
        .collect();

    // Multiple clients (Tauri host + capture worker) share one resident
    // provider through the process-wide RESIDENT state; each connection gets
    // its own session thread. The process only exits on a global stop request.
    let global_stop = Arc::new(AtomicBool::new(false));
    let mut session_handles: Vec<std::thread::JoinHandle<()>> = Vec::new();

    loop {
        let handle = unsafe {
            CreateNamedPipeW(
                wide.as_ptr(),
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
                PIPE_UNLIMITED_INSTANCES,
                65536,
                65536,
                0,
                null_mut(),
            )
        };
        if handle == INVALID_HANDLE_VALUE {
            let error = io::Error::last_os_error();
            // A stop request may have raced an accept failure; honor it.
            if global_stop.load(Ordering::SeqCst) {
                break;
            }
            return Err(error);
        }
        if unsafe { ConnectNamedPipe(handle, null_mut()) } == 0 {
            // A client may connect between CreateNamedPipeW and ConnectNamedPipe.
            // ERROR_PIPE_CONNECTED (535) is a successful connection in that race.
            let error = io::Error::last_os_error();
            if error.raw_os_error() != Some(ERROR_PIPE_CONNECTED) {
                unsafe {
                    DisconnectNamedPipe(handle);
                    CloseHandle(handle);
                }
                if global_stop.load(Ordering::SeqCst) {
                    break;
                }
                return Err(error);
            }
        }
        if global_stop.load(Ordering::SeqCst) {
            unsafe {
                DisconnectNamedPipe(handle);
                CloseHandle(handle);
            }
            break;
        }

        let stop_flag = Arc::clone(&global_stop);
        let helper = helper.clone();
        // Pass the handle as usize: raw pointers make the session closure
        // non-Send even inside the Send wrapper, for capture-analysis reasons.
        let pipe_addr = handle as usize;
        let session = std::thread::Builder::new()
            .name("framescope-pipe-session".into())
            .spawn(move || {
                let handle = pipe_addr as Handle;
                let result = serve_session(handle, &stop_flag, helper);
                if let Err(error) = result {
                    eprintln!("framescope-hardware-service session error: {error}");
                }
                unsafe {
                    DisconnectNamedPipe(handle);
                    CloseHandle(handle);
                }
            });
        match session {
            Ok(join) => session_handles.push(join),
            Err(error) => {
                unsafe {
                    DisconnectNamedPipe(handle);
                    CloseHandle(handle);
                }
                return Err(io::Error::new(io::ErrorKind::Other, error.to_string()));
            }
        }

        if global_stop.load(Ordering::SeqCst) {
            break;
        }
    }

    for join in session_handles {
        let _ = join.join();
    }
    Ok(())
}

#[cfg(windows)]
fn serve_session(
    handle: *mut std::ffi::c_void,
    stop_flag: &std::sync::atomic::AtomicBool,
    helper: Option<String>,
) -> io::Result<()> {
    use std::sync::atomic::Ordering;
    type Handle = *mut std::ffi::c_void;
    type Dword = u32;
    type Bool = i32;
    extern "system" {
        fn FlushFileBuffers(file: Handle) -> Bool;
        fn ReadFile(
            file: Handle,
            buffer: *mut u8,
            bytes: Dword,
            read: *mut Dword,
            overlapped: *mut std::ffi::c_void,
        ) -> Bool;
        fn WriteFile(
            file: Handle,
            buffer: *const u8,
            bytes: Dword,
            written: *mut Dword,
            overlapped: *mut std::ffi::c_void,
        ) -> Bool;
    }

    let mut state = framescope_hardware_service::ServiceState::default();
    state.lhm_helper_path = helper;
    let mut buffer = vec![0u8; 65536];
    loop {
        let mut read = 0;
        if unsafe {
            ReadFile(
                handle,
                buffer.as_mut_ptr(),
                buffer.len() as Dword,
                &mut read,
                std::ptr::null_mut(),
            )
        } == 0
        {
            break;
        }
        if read == 0 {
            continue;
        }
        let input = String::from_utf8_lossy(&buffer[..read as usize]);
        for line in input
            .lines()
            .map(|line| line.trim_matches('\0').trim_matches('\u{feff}').trim())
            .filter(|line| !line.is_empty())
        {
            let response = match serde_json::from_str::<Request>(line) {
                Ok(request) => {
                    let should_stop = request.method == "stop";
                    let value =
                        framescope_hardware_service::dispatch_with_state(request, &mut state);
                    if should_stop {
                        stop_flag.store(true, Ordering::SeqCst);
                    }
                    value
                }
                Err(error) => {
                    serde_json::json!({"id":"","ok":false,"result":null,"error":error.to_string()})
                }
            };
            let bytes = format!("{}\n", response);
            let mut written = 0;
            if unsafe {
                WriteFile(
                    handle,
                    bytes.as_ptr(),
                    bytes.len() as Dword,
                    &mut written,
                    std::ptr::null_mut(),
                )
            } == 0
            {
                return Err(io::Error::last_os_error());
            }
            // Ensure a terminal stop response is visible to the client before closing the pipe.
            if unsafe { FlushFileBuffers(handle) } == 0 {
                return Err(io::Error::last_os_error());
            }
            if stop_flag.load(Ordering::SeqCst) {
                return Ok(());
            }
        }
    }
    Ok(())
}
