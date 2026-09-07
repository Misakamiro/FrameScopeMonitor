//! Named-pipe client for FrameScopeHardwareService (protocol
//! `framescope-hardware-service/1`). The host owns one persistent connection;
//! the service process is spawned on demand next to the preview executable
//! (`--lhm-helper` points at the sibling provider) and exits by itself once
//! the pipe drops. Every failure reconnects on the next call — the UI layer
//! only ever sees a plain error string, never synthetic data.

use serde_json::{json, Value};
use std::ffi::OsStr;
use std::iter::once;
use std::os::windows::ffi::OsStrExt;
use std::ptr::null_mut;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

pub const PIPE_NAME: &str = "FrameScopeHardwareService";
const CONNECT_RETRY_MS: u64 = 300;
const CONNECT_RETRIES: usize = 20;
const SPAWN_COOLDOWN: Duration = Duration::from_secs(5);

type Handle = *mut core::ffi::c_void;

// The pipe HANDLE is just an integer token owned by this process; the client
// lives inside a static Mutex so access is already serialized across threads.
struct PipeHandle(Handle);
unsafe impl Send for PipeHandle {}

struct PipeClient {
    handle: PipeHandle,
    next_id: AtomicU64,
}

static CLIENT: Mutex<Option<PipeClient>> = Mutex::new(None);
static LAST_SPAWN: Mutex<Option<Instant>> = Mutex::new(None);

/// `FRAMESCOPE_BACKEND_DIR` overrides the directory holding
/// FrameScopeHardwareService.exe / FrameScopeHardwareProvider.exe; otherwise
/// both are expected next to the preview executable.
fn backend_paths() -> Result<(std::path::PathBuf, std::path::PathBuf), String> {
    let dir = match std::env::var("FRAMESCOPE_BACKEND_DIR") {
        Ok(dir) if !dir.trim().is_empty() => std::path::PathBuf::from(dir),
        _ => std::env::current_exe()
            .ok()
            .and_then(|exe| exe.parent().map(|parent| parent.to_path_buf()))
            .ok_or("cannot resolve executable directory")?,
    };
    Ok((
        dir.join("FrameScopeHardwareService.exe"),
        dir.join("FrameScopeHardwareProvider.exe"),
    ))
}

fn spawn_service(service: &std::path::Path, helper: &std::path::Path) -> Result<(), String> {
    {
        let last = LAST_SPAWN.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        if let Some(last) = *last {
            if last.elapsed() < SPAWN_COOLDOWN {
                return Ok(());
            }
        }
    }
    if !service.exists() {
        return Err(format!(
            "hardware backend is not deployed: {} is missing",
            service.display()
        ));
    }
    *LAST_SPAWN.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(Instant::now());
    let mut command = std::process::Command::new(service);
    command
        .arg("--pipe-name")
        .arg(PIPE_NAME)
        .arg("--lhm-helper")
        .arg(helper);
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x0800_0000); // CREATE_NO_WINDOW
    }
    // A spawn racing a healthy instance just exits with a pipe-name clash;
    // either way the connect loop below settles it.
    let _ = command.spawn();
    Ok(())
}

fn open_pipe() -> Result<PipeClient, String> {
    use windows_sys::Win32::Foundation::{GENERIC_READ, GENERIC_WRITE};
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, FILE_FLAG_FIRST_PIPE_INSTANCE, OPEN_EXISTING,
    };
    use windows_sys::Win32::System::Pipes::{PIPE_READMODE_MESSAGE, SetNamedPipeHandleState};

    let full = format!("\\\\.\\pipe\\{PIPE_NAME}");
    let wide: Vec<u16> = OsStr::new(&full).encode_wide().chain(once(0)).collect();
    unsafe {
        let handle = CreateFileW(
            wide.as_ptr(),
            GENERIC_READ | GENERIC_WRITE,
            0,
            null_mut(),
            OPEN_EXISTING,
            FILE_FLAG_FIRST_PIPE_INSTANCE,
            null_mut(),
        );
        if handle.is_null() || handle == -1isize as Handle {
            return Err(format!("pipe connect failed: {}", std::io::Error::last_os_error()));
        }
        let mode = PIPE_READMODE_MESSAGE;
        if SetNamedPipeHandleState(handle, &mode, null_mut(), null_mut()) == 0 {
            let error = std::io::Error::last_os_error();
            windows_sys::Win32::Foundation::CloseHandle(handle);
            return Err(format!("pipe readmode failed: {error}"));
        }
        Ok(PipeClient {
            handle: PipeHandle(handle),
            next_id: AtomicU64::new(1),
        })
    }
}

impl PipeClient {
    fn request(&mut self, method: &str, params: &Value) -> Result<Value, String> {
        use windows_sys::Win32::Storage::FileSystem::{ReadFile, WriteFile};
        let id = self.next_id.fetch_add(1, Ordering::Relaxed);
        let mut bytes = json!({"id": id.to_string(), "method": method, "params": params})
            .to_string()
            .into_bytes();
        bytes.push(b'\n');
        let handle = self.handle.0;
        let mut written: u32 = 0;
        unsafe {
            if WriteFile(
                handle,
                bytes.as_ptr(),
                bytes.len() as u32,
                &mut written,
                null_mut(),
            ) == 0
                || written as usize != bytes.len()
            {
                return Err(format!("pipe write failed: {}", std::io::Error::last_os_error()));
            }
        }
        let mut buffer = vec![0u8; 1_048_576];
        let mut read: u32 = 0;
        unsafe {
            if ReadFile(handle, buffer.as_mut_ptr(), buffer.len() as u32, &mut read, null_mut()) == 0
                || read == 0
            {
                return Err(format!("pipe read failed: {}", std::io::Error::last_os_error()));
            }
        }
        let text = String::from_utf8_lossy(&buffer[..read as usize]);
        let clean = text.trim_matches('\0').trim_start_matches('\u{feff}').trim();
        let response: Value =
            serde_json::from_str(clean).map_err(|error| format!("service returned invalid JSON: {error}"))?;
        if response["ok"] == true {
            Ok(response["result"].clone())
        } else {
            Err(response["error"].as_str().unwrap_or("service error").to_string())
        }
    }
}

impl Drop for PipeClient {
    fn drop(&mut self) {
        unsafe { windows_sys::Win32::Foundation::CloseHandle(self.handle.0) };
    }
}

/// Rounds the numeric overlay fields the GDI panel stores as integers
/// (clocks, fans, percents, VRAM GB). The service reports f64 for everything;
/// the frontend keeps full precision while this feeds the native panel.
pub fn sanitize_for_gdi(mut overlay: Value) -> Value {
    const INT_FIELDS: &[&str] = &[
        "fps", "fps_1low", "fps_01low", "cpu_usage", "cpu_clock", "gpu_usage", "game_ping",
        "cpu_fan_speed", "gpu_fan_speed", "gpu_power", "gpu_clock", "gpu_vram_used",
        "gpu_vram_total", "gpu_memory_clock",
    ];
    if let Some(object) = overlay.as_object_mut() {
        for field in INT_FIELDS {
            if let Some(value) = object.get_mut(*field) {
                if let Some(number) = value.as_f64() {
                    *value = json!(number.round() as i64);
                }
            }
        }
        if let Some(gpus) = object.get_mut("gpu_sensors").and_then(Value::as_array_mut) {
            for gpu in gpus {
                if let Some(gpu_object) = gpu.as_object_mut() {
                    for field in ["usage", "fan_speed", "power", "clock", "memory_clock", "vram_used", "vram_total"] {
                        if let Some(value) = gpu_object.get_mut(field) {
                            if let Some(number) = value.as_f64() {
                                *value = json!(number.round() as i64);
                            }
                        }
                    }
                }
            }
        }
    }
    overlay
}

/// One request against the hardware service; connects (and spawns the service
/// process when needed) on first use and after any transport failure.
pub fn call(method: &str, params: Value) -> Result<Value, String> {    let mut guard = CLIENT.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    let mut connected_here = false;
    for attempt in 0..=1 {
        if guard.is_none() {
            let (service, helper) = backend_paths()?;
            spawn_service(&service, &helper)?;
            let mut last_error = String::new();
            let mut client = None;
            for _ in 0..CONNECT_RETRIES {
                match open_pipe() {
                    Ok(opened) => {
                        client = Some(opened);
                        break;
                    }
                    Err(error) => {
                        last_error = error;
                        std::thread::sleep(Duration::from_millis(CONNECT_RETRY_MS));
                    }
                }
            }
            match client {
                Some(opened) => {
                    *guard = Some(opened);
                    connected_here = true;
                }
                None => return Err(format!("hardware service unavailable: {last_error}")),
            }
        }
        let result = guard.as_mut().expect("connected").request(method, &params);
        match result {
            Ok(value) => return Ok(value),
            Err(error) => {
                // Transport-level breakage recycles the connection; protocol
                // errors (ok:false) are real answers and must surface as-is.
                let transport = error.contains("pipe ");
                if connected_here || attempt == 1 || !transport {
                    return Err(error);
                }
                *guard = None;
            }
        }
    }
    unreachable!("retry loop returns on both branches")
}
