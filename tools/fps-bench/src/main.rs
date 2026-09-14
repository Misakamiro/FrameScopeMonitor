//! fps-bench：FrameScope FPS 采集链的基准信号源。
//! D3D11 flip-model swapchain + Present(SYNC_INTERVAL=0) 不限帧渲染，忙等限帧。
//!
//! 用法：fps-bench <mode> <target_fps> [seconds]
//!   mode: windowed | borderless | exclusive
//!   target_fps: 0 = 不限帧（空场景轻松 1000+）
//!   seconds: 运行时长，默认 30
//!
//! 窗口标题每 500ms 显示真实帧率，供与 OSD / get_overlay_data 读数对照。
//!
//! 说明：windows-sys 0.59 无 Direct3D 模块，这里手写最小 COM ABI 声明
//! （vtable 槽位是 COM 稳定接口：IUnknown 0-2 + IDXGIObject 3-6 + 7=Present、13=SetFullscreenState）。

use std::time::{Duration, Instant};

#[cfg(windows)]
mod win {
    use std::time::{Duration, Instant};
    use windows_sys::Win32::Foundation::*;
    use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
    use windows_sys::Win32::System::Performance::{
        QueryPerformanceCounter, QueryPerformanceFrequency,
    };
    use windows_sys::Win32::UI::WindowsAndMessaging::*;

    // ---- 最小 DXGI/D3D11 ABI ----
    type Hr = i32;
    #[repr(C)]
    struct SwapChainVtbl {
        _qi: usize,
        _addref: usize,
        _release: usize,
        _set_private_data: usize,
        _set_private_data_interface: usize,
        _get_private_data: usize,
        _get_parent: usize,
        present: unsafe extern "system" fn(*mut SwapChain, sync_interval: u32, flags: u32) -> Hr,
        _get_buffer: usize,
        _get_fullscreen_state: usize,
        _get_desc: usize,
        _resize_buffers: usize,
        _resize_target: usize,
        set_fullscreen_state:
            unsafe extern "system" fn(*mut SwapChain, fullscreen: i32, target: *mut core::ffi::c_void) -> Hr,
    }
    #[repr(C)]
    struct SwapChain {
        lp_vtbl: *const SwapChainVtbl,
    }

    #[repr(C)]
    #[derive(Clone, Copy, Default)]
    struct DXGI_Rational {
        numerator: u32,
        denominator: u32,
    }
    #[repr(C)]
    #[derive(Clone, Copy)]
    struct DXGI_MODE_DESC {
        width: u32,
        height: u32,
        refresh_rate: DXGI_Rational,
        format: u32, // DXGI_FORMAT
        scanline_ordering: u32,
        scaling: u32,
    }
    #[repr(C)]
    #[derive(Clone, Copy)]
    struct DXGI_SAMPLE_DESC {
        count: u32,
        quality: u32,
    }
    #[repr(C)]
    struct DXGI_SWAP_CHAIN_DESC {
        mode_desc: DXGI_MODE_DESC,
        sample_desc: DXGI_SAMPLE_DESC,
        usage: u32,
        buffer_count: u32,
        output_window: HWND,
        windowed: i32,
        swap_effect: u32,
        flags: u32,
    }

    const DXGI_FORMAT_R8G8B8A8_UNORM: u32 = 28;
    const DXGI_USAGE_RENDER_TARGET_OUTPUT: u32 = 0x0000_0020;
    const DXGI_SWAP_EFFECT_FLIP_DISCARD: u32 = 4;
    const D3D11_SDK_VERSION: u32 = 7;
    const D3D_DRIVER_TYPE_HARDWARE: u32 = 1;
    const D3D_FEATURE_LEVEL_11_0: u32 = 0xb000;
    const D3D_FEATURE_LEVEL_11_1: u32 = 0xb100;

    #[link(name = "d3d11")]
    extern "system" {
        fn D3D11CreateDeviceAndSwapChain(
            adapter: *mut core::ffi::c_void,
            driver_type: u32,
            software: *const core::ffi::c_void,
            flags: u32,
            feature_levels: *const u32,
            feature_levels_count: u32,
            sdk_version: u32,
            swap_chain_desc: *mut DXGI_SWAP_CHAIN_DESC,
            swap_chain: *mut *mut SwapChain,
            device: *mut *mut core::ffi::c_void,
            feature_level: *mut u32,
            immediate_context: *mut *mut core::ffi::c_void,
        ) -> Hr;
    }

    unsafe extern "system" fn wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
        match msg {
            WM_DESTROY => {
                PostQuitMessage(0);
                0
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }

    fn qpc() -> i64 {
        let mut v = 0i64;
        unsafe { QueryPerformanceCounter(&mut v) };
        v
    }

    pub fn run(mode: &str, target_fps: u32, seconds: u64) -> i32 {
        unsafe {
            let hinstance = GetModuleHandleW(std::ptr::null());
            let class_name = windows_sys::core::w!("fps_bench_cls");
            let wc = WNDCLASSW {
                style: CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc: Some(wndproc),
                lpszClassName: class_name,
                hInstance: hinstance,
                ..std::mem::zeroed()
            };
            RegisterClassW(&wc);

            let sw = GetSystemMetrics(SM_CXSCREEN);
            let sh = GetSystemMetrics(SM_CYSCREEN);
            let (mut x, mut y, mut w, mut h, style) = match mode {
                "exclusive" => (0, 0, sw, sh, WS_POPUP),
                "borderless" => (0, 0, sw, sh, WS_POPUP | WS_VISIBLE),
                _ => (80, 80, 1280, 720, WS_OVERLAPPEDWINDOW | WS_VISIBLE),
            };
            let _ = style;
            // windowed 直接给外框尺寸（含标题栏/边框余量，客户区 ≥1280x720）
            if mode == "windowed" {
                w = 1280 + 16;
                h = 720 + 39;
            }

            let hwnd = CreateWindowExW(
                0,
                class_name,
                windows_sys::core::w!("fps-bench"),
                style,
                x,
                y,
                w,
                h,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                hinstance,
                std::ptr::null(),
            );
            if hwnd.is_null() {
                eprintln!("CreateWindowExW failed: {}", GetLastError());
                return 1;
            }
            ShowWindow(hwnd, SW_SHOW);
            // 前台化：后台窗口会被 DWM 节流/合并 present（ETW 事件随之消失），
            // 必须真正拿到前台才能代表游戏场景
            SetForegroundWindow(hwnd);

            let mut desc = DXGI_SWAP_CHAIN_DESC {
                mode_desc: DXGI_MODE_DESC {
                    width: if mode == "windowed" { 1280 } else { sw as u32 },
                    height: if mode == "windowed" { 720 } else { sh as u32 },
                    refresh_rate: DXGI_Rational { numerator: 0, denominator: 0 },
                    format: DXGI_FORMAT_R8G8B8A8_UNORM,
                    scanline_ordering: 0,
                    scaling: 0,
                },
                sample_desc: DXGI_SAMPLE_DESC { count: 1, quality: 0 },
                usage: DXGI_USAGE_RENDER_TARGET_OUTPUT,
                buffer_count: 2,
                output_window: hwnd,
                windowed: (mode != "exclusive") as i32,
                swap_effect: DXGI_SWAP_EFFECT_FLIP_DISCARD,
                flags: 0,
            };

            let feature_levels = [D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0];
            let mut swapchain: *mut SwapChain = std::ptr::null_mut();
            let mut device: *mut core::ffi::c_void = std::ptr::null_mut();
            let mut context: *mut core::ffi::c_void = std::ptr::null_mut();
            let mut fl_out = 0u32;
            let hr = D3D11CreateDeviceAndSwapChain(
                std::ptr::null_mut(),
                D3D_DRIVER_TYPE_HARDWARE,
                std::ptr::null(),
                0,
                feature_levels.as_ptr(),
                feature_levels.len() as u32,
                D3D11_SDK_VERSION,
                &mut desc,
                &mut swapchain,
                &mut device,
                &mut fl_out,
                &mut context,
            );
            if hr < 0 || swapchain.is_null() {
                eprintln!("D3D11CreateDeviceAndSwapChain failed: hr={hr:#x}");
                return 1;
            }

            if mode == "exclusive" {
                let hr = ((*(*swapchain).lp_vtbl).set_fullscreen_state)(swapchain, 1, std::ptr::null_mut());
                if hr < 0 {
                    eprintln!("SetFullscreenState failed: hr={hr:#x} (独占全屏可能被系统占用)");
                    return 1;
                }
                println!("exclusive fullscreen entered");
            }

            let mut freq = 0i64;
            QueryPerformanceFrequency(&mut freq);
            let frame_budget_ticks = if target_fps > 0 { freq / target_fps as i64 } else { 0 };
            let started = Instant::now();
            let mut frames: u64 = 0;
            let mut stat_window_start = qpc();
            let mut stat_window_frames: u64 = 0;
            let mut last_title = Instant::now();
            let present = (*(*swapchain).lp_vtbl).present as unsafe extern "system" fn(*mut SwapChain, u32, u32) -> Hr;

            let mut msg: MSG = std::mem::zeroed();
            'run: loop {
                while PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, 1) != 0 {
                    if msg.message == WM_QUIT {
                        break 'run;
                    }
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
                if started.elapsed() >= Duration::from_secs(seconds) {
                    break;
                }

                let frame_start = qpc();
                (present)(swapchain, 0, 0);
                frames += 1;
                stat_window_frames += 1;

                if frame_budget_ticks > 0 {
                    while qpc() - frame_start < frame_budget_ticks {
                        std::hint::spin_loop();
                    }
                }

                if last_title.elapsed() >= Duration::from_millis(500) {
                    last_title = Instant::now();
                    let now = qpc();
                    let dt = now - stat_window_start;
                    stat_window_start = now;
                    let actual = if dt > 0 {
                        stat_window_frames as f64 * freq as f64 / dt as f64
                    } else {
                        0.0
                    };
                    stat_window_frames = 0;
                    let title = format!(
                        "fps-bench  actual={:.0}  target={}  mode={}",
                        actual, target_fps, mode
                    );
                    let mut wide: Vec<u16> = title.encode_utf16().collect();
                    wide.push(0);
                    SetWindowTextW(hwnd, wide.as_ptr());
                }
            }

            let total = started.elapsed().as_secs_f64();
            println!(
                "done: {} frames in {:.1}s = {:.0} fps (mode={} target={})",
                frames, total, frames as f64 / total, mode, target_fps
            );
            if mode == "exclusive" {
                ((*(*swapchain).lp_vtbl).set_fullscreen_state)(swapchain, 0, std::ptr::null_mut());
            }
            0
        }
    }
}

#[cfg(windows)]
fn main() {
    let args: Vec<String> = std::env::args().collect();
    let mode = args.get(1).map(String::as_str).unwrap_or("borderless");
    let target_fps: u32 = args.get(2).and_then(|v| v.parse().ok()).unwrap_or(0);
    let seconds: u64 = args.get(3).and_then(|v| v.parse().ok()).unwrap_or(30);
    if !matches!(mode, "windowed" | "borderless" | "exclusive") {
        eprintln!("mode must be windowed | borderless | exclusive");
        std::process::exit(2);
    }
    std::process::exit(win::run(mode, target_fps, seconds));
}

#[cfg(not(windows))]
fn main() {
    eprintln!("windows only");
    std::process::exit(3);
}
