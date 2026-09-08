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
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

pub const PIPE_NAME: &str = "FrameScopeHardwareService";

// R-A7: while the PawnIO installer/uninstaller tears the driver down, the
// 1 Hz poller must not query (or re-spawn) the sampling service. The pause
// flag is owned here so both the poller and the PawnIO flows share one gate.
static POLLER_PAUSED: std::sync::atomic::AtomicBool =
    std::sync::atomic::AtomicBool::new(false);

pub fn set_poller_paused(paused: bool) {
    POLLER_PAUSED.store(paused, std::sync::atomic::Ordering::SeqCst);
}

pub fn poller_paused() -> bool {
    POLLER_PAUSED.load(std::sync::atomic::Ordering::SeqCst)
}
const CONNECT_RETRY_MS: u64 = 300;
const CONNECT_RETRIES: usize = 20;
const SPAWN_COOLDOWN: Duration = Duration::from_secs(5);

// H7 tail: a hung service used to block the calling thread - and, under the
// CLIENT lock, every other hardware caller - forever on ReadFile. Track the
// in-flight read; a watchdog cancels it via CancelIoEx after a generous
// budget (a cold LibreHardwareMonitor scan can legitimately take ~10s), so
// the read errors out, the lock is released and the connection recycled.
static INFLIGHT_HANDLE: AtomicUsize = AtomicUsize::new(0);
static INFLIGHT_SINCE_MS: AtomicU64 = AtomicU64::new(0);
const READ_TIMEOUT_MS: u64 = 12_000;

fn now_unix_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

fn ensure_read_watchdog() {
    static START: std::sync::Once = std::sync::Once::new();
    START.call_once(|| {
        std::thread::Builder::new()
            .name("framescope-pipe-watchdog".into())
            .spawn(|| loop {
                std::thread::sleep(Duration::from_millis(200));
                let handle = INFLIGHT_HANDLE.load(Ordering::SeqCst);
                if handle == 0 {
                    continue;
                }
                let since = INFLIGHT_SINCE_MS.load(Ordering::SeqCst);
                if since == 0 || now_unix_ms().saturating_sub(since) <= READ_TIMEOUT_MS {
                    continue;
                }
                // Cancels every thread's I/O on this handle; the blocked
                // ReadFile returns ERROR_OPERATION_ABORTED and the caller
                // treats it as a transport failure (recycle + reconnect).
                unsafe {
                    windows_sys::Win32::System::IO::CancelIoEx(
                        handle as *mut core::ffi::c_void,
                        null_mut(),
                    );
                }
            })
            .expect("spawn pipe watchdog");
    });
}

type Handle = *mut core::ffi::c_void;

/// All-null overlay payload (fields matching the service's OverlayData
/// contract) for the poller's "service unreachable" broadcast: without it
/// the overlays freeze on the last snapshot instead of falling back to "--".
pub fn empty_overlay_value() -> Value {
    json!({
        "fps": null, "fps_1low": null, "fps_01low": null,
        "cpu_usage": null, "cpu_temp": null, "cpu_clock": null, "cpu_power": null,
        "cpu_voltage": null, "cpu_fan_speed": null,
        "gpu_temp": null, "gpu_usage": null, "gpu_fan_speed": null, "gpu_power": null,
        "gpu_clock": null, "gpu_voltage": null, "gpu_memory_clock": null,
        "memory_usage": null, "ssd_temp": null, "delta_password": null, "game_ping": null,
        "gpu_vram_used": null, "gpu_vram_total": null, "net_time_offset_ms": null,
        "gpu_sensors": [], "active_gpu_index": 0
    })
}

// The pipe HANDLE is just an integer token owned by this process; the client
// lives inside a static Mutex so access is already serialized across threads.
struct PipeHandle(Handle);
unsafe impl Send for PipeHandle {}

struct PipeClient {
    handle: PipeHandle,
    next_id: AtomicU64,
    // Reused read buffer: a fresh 1 MB zero-initialized allocation per
    // request was pure overhead at the 1 Hz poll rate.
    buffer: Vec<u8>,
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
    if let Ok(child) = command.spawn() {
        #[cfg(windows)]
        kill_child_when_host_dies(&child);
    }
    Ok(())
}

// M3: a host killed from Task Manager (or a crash) must not leave an orphan
// hardware service + LHM provider chain behind. The child joins a
// KILL_ON_JOB_CLOSE job owned by this process, so the last job handle closing
// (host exit) terminates it; the service in turn takes its provider down via
// stdin EOF. Handles from earlier spawns intentionally stay open until host
// exit - each job only owns its own (already dead or live) child.
#[cfg(windows)]
fn kill_child_when_host_dies(child: &std::process::Child) {
    use windows_sys::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
        SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    };
    use std::os::windows::io::AsRawHandle;

    unsafe {
        let job = CreateJobObjectW(null_mut(), null_mut());
        if job.is_null() || job == -1isize as *mut core::ffi::c_void {
            return;
        }
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = core::mem::zeroed();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if SetInformationJobObject(
            job,
            JobObjectExtendedLimitInformation,
            &info as *const _ as *const core::ffi::c_void,
            core::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
        ) == 0
        {
            return;
        }
        // Best effort: a failed assignment just keeps the old orphan behavior.
        AssignProcessToJobObject(job, child.as_raw_handle());
    }
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
            buffer: vec![0u8; 1_048_576],
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
        const ERROR_MORE_DATA: i32 = 234;
        let mut received: Vec<u8> = Vec::with_capacity(65_536);
        let mut read: u32 = 0;
        ensure_read_watchdog();
        INFLIGHT_HANDLE.store(handle as usize, Ordering::SeqCst);
        INFLIGHT_SINCE_MS.store(now_unix_ms(), Ordering::SeqCst);
        let read_result = loop {
            let ok = unsafe {
                ReadFile(handle, self.buffer.as_mut_ptr(), self.buffer.len() as u32, &mut read, null_mut())
            };
            if ok == 0 {
                let error = std::io::Error::last_os_error();
                if error.raw_os_error() == Some(ERROR_MORE_DATA) && read > 0 {
                    received.extend_from_slice(&self.buffer[..read as usize]);
                    continue;
                }
                INFLIGHT_HANDLE.store(0, Ordering::SeqCst);
                break Err(error);
            }
            received.extend_from_slice(&self.buffer[..read as usize]);
            INFLIGHT_HANDLE.store(0, Ordering::SeqCst);
            break Ok(());
        };
        if read_result.is_err() || received.is_empty() {
            let error = read_result
                .err()
                .map(|e| e.to_string())
                .unwrap_or_else(|| "empty reply".into());
            return Err(format!("pipe read failed: {error}"));
        }
        let text = String::from_utf8_lossy(&received);
        let clean = text.trim_matches('\0').trim_start_matches('\u{feff}').trim();
        let response: Value =
            serde_json::from_str(clean).map_err(|error| format!("service returned invalid JSON: {error}"))?;
        // A stale or interleaved response means the pipe framing is broken -
        // surface it as a transport error so the caller recycles the
        // connection instead of pairing requests with wrong answers.
        if response["id"].as_str() != Some(id.to_string().as_str()) {
            return Err(format!(
                "pipe protocol: response id {} does not match request {}",
                response["id"],
                id
            ));
        }
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
pub fn call(method: &str, params: Value) -> Result<Value, String> {
    // Fast path under the lock: reuse the published connection, holding the
    // lock only for the request itself.
    {
        let mut guard = CLIENT.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
        if let Some(client) = guard.as_mut() {
            match client.request(method, &params) {
                Ok(value) => return Ok(value),
                Err(error) => {
                    if !error.contains("pipe ") {
                        return Err(error); // protocol-level answer, surface as-is
                    }
                    *guard = None; // transport broke; fall through to reconnect
                }
            }
        }
    }

    // Slow path OUTSIDE the lock: spawn (cooled) + bounded connect retries, so
    // a slow connect can never stall other caller threads.
    let (service, helper) = backend_paths()?;
    spawn_service(&service, &helper)?;
    let mut last_error = String::new();
    let mut opened: Option<PipeClient> = None;
    for _ in 0..CONNECT_RETRIES {
        match open_pipe() {
            Ok(client) => {
                opened = Some(client);
                break;
            }
            Err(error) => {
                last_error = error;
                std::thread::sleep(Duration::from_millis(CONNECT_RETRY_MS));
            }
        }
    }
    let mut client = opened.ok_or_else(|| format!("hardware service unavailable: {last_error}"))?;
    let value = client.request(method, &params)?;
    *CLIENT.lock().unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(client);
    Ok(value)
}
