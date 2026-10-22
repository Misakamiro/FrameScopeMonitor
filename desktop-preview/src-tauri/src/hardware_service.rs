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
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};

pub const PIPE_NAME: &str = "FrameScopeHardwareService";

// Shared with the recorder. A request holds this installation-scoped mutex
// until its pipe exchange ends; maintenance acquires it before stopping the
// service and keeps it for the entire driver operation.
pub(crate) struct MaintenanceLease(Handle);

fn maintenance_name(root: &std::path::Path) -> Result<String, String> {
    use sha2::{Digest, Sha256};
    let root = std::fs::canonicalize(root).map_err(|e| e.to_string())?;
    let root = root.to_string_lossy().trim_start_matches(r"\\?\").trim_end_matches(['\\', '/']).replace('/', "\\");
    let root = invariant_uppercase(&root)?;
    Ok(format!("Local\\FrameScopeHardwareMaintenance-{:X}", Sha256::digest(root.as_bytes())))
}

fn invariant_uppercase(value: &str) -> Result<String, String> {
    use windows_sys::Win32::Globalization::{LCMapStringEx, LCMAP_UPPERCASE};
    let input: Vec<u16> = value.encode_utf16().collect();
    let locale = [0u16]; // LOCALE_NAME_INVARIANT, identical to the recorder.
    unsafe {
        let size = LCMapStringEx(locale.as_ptr(), LCMAP_UPPERCASE, input.as_ptr(), input.len() as i32,
            null_mut(), 0, null_mut(), null_mut(), 0);
        if size == 0 { return Err(std::io::Error::last_os_error().to_string()); }
        let mut mapped = vec![0u16; size as usize];
        if LCMapStringEx(locale.as_ptr(), LCMAP_UPPERCASE, input.as_ptr(), input.len() as i32,
            mapped.as_mut_ptr(), size, null_mut(), null_mut(), 0) != size {
            return Err(std::io::Error::last_os_error().to_string());
        }
        String::from_utf16(&mapped).map_err(|error| error.to_string())
    }
}

impl MaintenanceLease {
    fn acquire(root: &std::path::Path, timeout_ms: u32) -> Result<Self, String> {
        use windows_sys::Win32::System::Threading::{CreateMutexW, WaitForSingleObject};
        let name: Vec<u16> = maintenance_name(root)?.encode_utf16().chain(once(0)).collect();
        unsafe {
            let handle = CreateMutexW(null_mut(), 0, name.as_ptr());
            if handle.is_null() { return Err(std::io::Error::last_os_error().to_string()); }
            let result = WaitForSingleObject(handle, timeout_ms);
            if result == 0 || result == 0x80 { return Ok(Self(handle)); }
            windows_sys::Win32::Foundation::CloseHandle(handle);
            Err("hardware maintenance is active or requests have not drained".into())
        }
    }
}

impl Drop for MaintenanceLease {
    fn drop(&mut self) {
        unsafe {
            windows_sys::Win32::System::Threading::ReleaseMutex(self.0);
            windows_sys::Win32::Foundation::CloseHandle(self.0);
        }
    }
}

pub(crate) fn begin_maintenance() -> Result<MaintenanceLease, String> {
    let (service, _) = backend_paths()?;
    let root = service.parent().ok_or("hardware installation root missing")?;
    begin_maintenance_at(root, &service, PIPE_NAME, &CLIENT)
}

fn begin_maintenance_at(root: &std::path::Path, service: &std::path::Path,
    pipe_name: &str, client_slot: &Mutex<Option<PipeClient>>) -> Result<MaintenanceLease, String> {
    let lease = MaintenanceLease::acquire(root, 30_000)?;
    // No connection is spawned in maintenance. Remove the cached connection
    // while holding the same client lock used by normal requests.
    let mut slot = client_slot.lock().unwrap_or_else(|p| p.into_inner());
    let client = match slot.take() {
        Some(client) => Some(client),
        None => match open_pipe_at(pipe_name, service) {
            Ok(client) => Some(client),
            Err(error) if error.starts_with("pipe connect failed (absent)") => None,
            Err(error) => return Err(error),
        },
    };
    if let Some(mut client) = client {
        use windows_sys::Win32::System::Pipes::GetNamedPipeServerProcessId;
        use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE};
        let mut pid = 0;
        if unsafe { GetNamedPipeServerProcessId(client.handle.0, &mut pid) } == 0 {
            return Err("cannot identify maintenance service".into());
        }
        let process = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, 0, pid) };
        if process.is_null() { return Err("cannot wait for maintenance service".into()); }
        let stopped = client.request("stop", &Value::Null).and_then(|reply| {
            if reply["status"] != "stopped" || reply["shutdownComplete"] != true {
                return Err("service did not confirm maintenance cleanup".into());
            }
            drop(client);
            if unsafe { WaitForSingleObject(process, 20_000) } != 0 {
                return Err("service did not exit before maintenance".into());
            }
            Ok(())
        });
        unsafe { windows_sys::Win32::Foundation::CloseHandle(process); }
        stopped?;
    }
    Ok(lease)
}

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

// Cold callers may own different connections concurrently. Registration and
// cancellation share a lock so an expired handle cannot be closed/reused
// between the watchdog's lookup and CancelIoEx.
static INFLIGHT: Mutex<Vec<(usize, Instant)>> = Mutex::new(Vec::new());
const READ_TIMEOUT_MS: u64 = 12_000;

struct IoDeadline(usize);

impl IoDeadline {
    fn start(handle: Handle) -> Self {
        ensure_read_watchdog();
        INFLIGHT.lock().unwrap_or_else(|p| p.into_inner())
            .push((handle as usize, Instant::now()));
        Self(handle as usize)
    }
}

impl Drop for IoDeadline {
    fn drop(&mut self) {
        INFLIGHT.lock().unwrap_or_else(|p| p.into_inner())
            .retain(|(handle, _)| *handle != self.0);
    }
}

fn ensure_read_watchdog() {
    static START: std::sync::Once = std::sync::Once::new();
    START.call_once(|| {
        std::thread::Builder::new()
            .name("framescope-pipe-watchdog".into())
            .spawn(|| loop {
                std::thread::sleep(Duration::from_millis(200));
                let inflight = INFLIGHT.lock().unwrap_or_else(|p| p.into_inner());
                for &(handle, since) in inflight.iter() {
                    if since.elapsed() >= Duration::from_millis(READ_TIMEOUT_MS) {
                        unsafe {
                            windows_sys::Win32::System::IO::CancelIoEx(
                                handle as Handle, null_mut(),
                            );
                        }
                    }
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
        "cpu_core_clocks": [], "cpu_core_voltages": [],
        "gpu_sensors": [], "active_gpu_index": 0,
        "present_mode": null
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
static HALTING: AtomicBool = AtomicBool::new(false);
static LAST_SPAWN: Mutex<Option<Instant>> = Mutex::new(None);
static SPAWN_GATE: Mutex<bool> = Mutex::new(true);

pub fn prevent_new_connections() {
    *SPAWN_GATE.lock().unwrap_or_else(|p| p.into_inner()) = false;
}

pub fn cancel_shutdown() {
    HALTING.store(false, Ordering::SeqCst);
    set_poller_paused(false);
    *SPAWN_GATE.lock().unwrap_or_else(|p| p.into_inner()) = true;
}

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
    // Serialize the final spawn decision with exit initiation. Existing pipe
    // clients remain available for the recorder/report drain.
    let allowed = SPAWN_GATE.lock().unwrap_or_else(|p| p.into_inner());
    if !crate::windows_shutdown::may_start_process(!*allowed, crate::windows_shutdown::system_shutting_down()) {
        return Err("hardware process start refused during shutdown".into());
    }
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
    let child = command.spawn().map_err(|error| format!("hardware service spawn failed: {error}"))?;
    #[cfg(windows)]
    kill_child_when_host_dies(&child);
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
    let (service, _) = backend_paths()?;
    open_pipe_at(PIPE_NAME, &service)
}

fn open_pipe_at(pipe_name: &str, expected_service: &std::path::Path) -> Result<PipeClient, String> {
    use windows_sys::Win32::Foundation::{GENERIC_READ, GENERIC_WRITE};
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, OPEN_EXISTING, SECURITY_IDENTIFICATION, SECURITY_SQOS_PRESENT,
    };
    use windows_sys::Win32::System::Pipes::{PIPE_READMODE_MESSAGE, SetNamedPipeHandleState};

    let full = format!("\\\\.\\pipe\\{pipe_name}");
    let wide: Vec<u16> = OsStr::new(&full).encode_wide().chain(once(0)).collect();
    unsafe {
        let handle = CreateFileW(
            wide.as_ptr(),
            GENERIC_READ | GENERIC_WRITE,
            0,
            null_mut(),
            OPEN_EXISTING,
            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION,
            null_mut(),
        );
        if handle.is_null() || handle == -1isize as Handle {
            let error = std::io::Error::last_os_error();
            let absent = [Some(2), Some(3)].contains(&error.raw_os_error());
            return Err(format!("pipe connect failed{}: {error}", if absent { " (absent)" } else { "" }));
        }
        if let Err(error) = verify_server(handle, expected_service) {
            windows_sys::Win32::Foundation::CloseHandle(handle);
            return Err(error);
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

fn verify_server(handle: Handle, expected_service: &std::path::Path) -> Result<(), String> {
    use std::os::windows::ffi::OsStringExt;
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::Globalization::{CompareStringOrdinal, CSTR_EQUAL};
    use windows_sys::Win32::System::Pipes::GetNamedPipeServerProcessId;
    use windows_sys::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION,
    };

    let error = || format!("pipe server identity failed: {}", std::io::Error::last_os_error());
    unsafe {
        let mut pid = 0;
        if GetNamedPipeServerProcessId(handle, &mut pid) == 0 || pid == 0 {
            return Err(error());
        }
        let process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
        if process.is_null() {
            return Err(error());
        }
        let mut image = vec![0u16; 32768];
        let mut length = image.len() as u32;
        let ok = QueryFullProcessImageNameW(process, 0, image.as_mut_ptr(), &mut length);
        let query_error = std::io::Error::last_os_error();
        CloseHandle(process);
        if ok == 0 {
            return Err(format!("pipe server identity failed: {query_error}"));
        }
        let actual = std::path::PathBuf::from(std::ffi::OsString::from_wide(&image[..length as usize]));
        let canonical = |path: &std::path::Path| -> Result<Vec<u16>, String> {
            std::fs::canonicalize(path)
                .map(|path| path.as_os_str().encode_wide().collect())
                .map_err(|error| format!("pipe server identity path failed: {error}"))
        };
        let actual = canonical(&actual)?;
        let expected = canonical(expected_service)?;
        if CompareStringOrdinal(actual.as_ptr(), actual.len() as i32,
            expected.as_ptr(), expected.len() as i32, 1) != CSTR_EQUAL {
            return Err(format!("pipe server identity mismatch: PID {pid} is not {}", expected_service.display()));
        }
    }
    Ok(())
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
        let _deadline = IoDeadline::start(handle);
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
                break Err(error);
            }
            received.extend_from_slice(&self.buffer[..read as usize]);
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
        "fps", "cpu_usage", "cpu_clock", "gpu_usage", "game_ping",
        "cpu_fan_speed", "gpu_fan_speed", "gpu_power", "gpu_clock", "gpu_vram_used",
        "gpu_vram_total", "gpu_memory_clock",
        "net_time_offset_ms",
    ];
    if let Some(object) = overlay.as_object_mut() {
        // Routing metadata belongs to the in-process snapshot, not the strict
        // original GDI renderer contract.
        object.remove("target_pid");
        object.remove("timestamp_ms");
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
                    // GPU identity is service routing metadata; the original
                    // GDI renderer consumes only its sensor values and name.
                    gpu_object.remove("identifier");
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
    if poller_paused() { return Err("hardware maintenance is active".into()); }
    let (service, _) = backend_paths()?;
    call_with_installation_client(service.parent().ok_or("hardware installation root missing")?,
        &CLIENT, &HALTING, method, &params, connect_service)
}

fn call_with_installation_client(root: &std::path::Path, slot: &Mutex<Option<PipeClient>>,
    halting: &AtomicBool, method: &str, params: &Value,
    connect: impl FnOnce() -> Result<PipeClient, String>) -> Result<Value, String> {
    if halting.load(Ordering::SeqCst) { return Err("hardware client is shutting down".into()); }
    // Ordinary reads queue behind each other. A zero wait incorrectly classified
    // concurrent UI/recorder reads as driver maintenance.
    let _lease = MaintenanceLease::acquire(root, READ_TIMEOUT_MS as u32)?;
    call_with_client(slot, halting, method, params, connect)
}

fn call_with_client(
    slot: &Mutex<Option<PipeClient>>,
    halting: &AtomicBool,
    method: &str,
    params: &Value,
    connect: impl FnOnce() -> Result<PipeClient, String>,
) -> Result<Value, String> {
    if halting.load(Ordering::SeqCst) {
        return Err("hardware client is shutting down".into());
    }
    // Keep cold connection publication under the same lock as stop. A call
    // already connecting must publish its handle before shutdown can drain it.
    let mut guard = slot.lock().unwrap_or_else(|poisoned| poisoned.into_inner());
    if halting.load(Ordering::SeqCst) {
        return Err("hardware client is shutting down".into());
    }
    if let Some(client) = guard.as_mut() {
        match client.request(method, params) {
            Ok(value) => return Ok(value),
            Err(error) => {
                if !error.contains("pipe ") {
                    return Err(error);
                }
                *guard = None;
            }
        }
    }
    if halting.load(Ordering::SeqCst) {
        return Err("hardware client is shutting down".into());
    }
    *guard = Some(connect()?);
    if halting.load(Ordering::SeqCst) {
        return Err("hardware client is shutting down".into());
    }
    guard.as_mut().unwrap().request(method, params)
}

fn connect_service() -> Result<PipeClient, String> {
    if !*SPAWN_GATE.lock().unwrap_or_else(|p| p.into_inner()) {
        return Err("hardware client is draining for shutdown".into());
    }
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
    opened.ok_or_else(|| format!("hardware service unavailable: {last_error}"))
}

/// Call synchronously before app.exit closes the kill-on-close Job.
/// false means no cached connection, not proof that an external service stopped.
pub fn stop_connected_service() -> Result<bool, String> {
    set_poller_paused(true);
    stop_connected_client(&CLIENT, &HALTING)
}

fn stop_connected_client(
    slot: &Mutex<Option<PipeClient>>,
    halting: &AtomicBool,
) -> Result<bool, String> {
    halting.store(true, Ordering::SeqCst);
    let mut guard = slot.lock().unwrap_or_else(|p| p.into_inner());
    let Some(mut client) = guard.take() else { return Ok(false) };
    let reply = client.request("stop", &Value::Null)?;
    if reply["status"] != "stopped" || reply["shutdownComplete"] != true {
        return Err("pipe stop did not confirm cleanup; service may be outdated".into());
    }
    Ok(true)
}

#[cfg(test)]
#[path = "hardware_service_tests.rs"]
mod tests;
