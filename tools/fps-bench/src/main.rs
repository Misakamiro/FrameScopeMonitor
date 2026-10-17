//! fps-bench：FrameScope FPS 采集链的基准信号源。
//! D3D11 flip-model swapchain + Present(SYNC_INTERVAL=0) 不限帧渲染，忙等限帧。
//!
//! 用法：fps-bench <mode> <target_fps> [seconds] [--wait-start] [--steps FPS:SECONDS,...]
//!       [--frame-csv PATH]
//!   mode: windowed | borderless | exclusive
//!   target_fps: 0 = 不主动限速（DXGI 仍可能节流）
//!   seconds: 运行时长，默认 30
//!
//! stdout 每秒显示成功 Present 提交率，不代表上屏帧率。Space 解锁后需稳定前台 2 秒。
//!
//! 说明：windows-sys 0.59 无 Direct3D 模块，这里手写最小 COM ABI 声明
//! （SDK: IUnknown 0-2、IDXGIObject 3-6、GetDevice 7、Present 8、SetFullscreenState 10）。

#[cfg(windows)]
mod control;

#[cfg(windows)]
mod frame_csv;

#[cfg(windows)]
mod win {
    use super::control::{parse_steps, step_at, tearing_flags, StartGate, Step};
    use super::frame_csv::{capacity_for, Capture};
    use core::ffi::c_void;
    use std::fs::OpenOptions;
    use std::io::BufWriter;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::time::{Duration, Instant};
    use windows_sys::core::GUID;
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
        _get_device: usize,
        present: unsafe extern "system" fn(*mut SwapChain, sync_interval: u32, flags: u32) -> Hr,
        get_buffer:
            unsafe extern "system" fn(*mut SwapChain, u32, *const GUID, *mut *mut c_void) -> Hr,
        set_fullscreen_state: unsafe extern "system" fn(
            *mut SwapChain,
            fullscreen: i32,
            target: *mut core::ffi::c_void,
        ) -> Hr,
        get_fullscreen_state:
            unsafe extern "system" fn(*mut SwapChain, *mut i32, *mut *mut core::ffi::c_void) -> Hr,
        _get_desc: usize,
        resize_buffers: unsafe extern "system" fn(*mut SwapChain, u32, u32, u32, u32, u32) -> Hr,
        _resize_target: usize,
        _get_containing_output: usize,
    }
    #[repr(C)]
    struct SwapChain {
        lp_vtbl: *const SwapChainVtbl,
    }

    // Prefixes and signatures checked against Windows SDK 10.0.26100.0 headers.
    #[repr(C)]
    struct DeviceVtbl {
        _before_rtv: [usize; 9],
        create_render_target_view: unsafe extern "system" fn(
            *mut c_void,
            *mut c_void,
            *const c_void,
            *mut *mut c_void,
        ) -> Hr,
    }

    #[repr(C)]
    struct ContextVtbl {
        _before_clear: [usize; 50],
        clear_render_target_view: unsafe extern "system" fn(*mut c_void, *mut c_void, *const f32),
    }

    #[repr(C)]
    struct FactoryVtbl {
        _before_check: [usize; 28],
        check_feature_support: unsafe extern "system" fn(*mut c_void, u32, *mut c_void, u32) -> Hr,
    }

    const IID_TEXTURE_2D: GUID = GUID::from_u128(0x6f15aaf2_d208_4e89_9ab4_489535d34f9c);
    const IID_FACTORY_5: GUID = GUID::from_u128(0x7632e1f5_ee65_4dca_87fd_84cd75f8838d);
    static FOCUS_LOST: AtomicBool = AtomicBool::new(false);

    #[link(name = "dxgi")]
    extern "system" {
        fn CreateDXGIFactory1(iid: *const GUID, factory: *mut *mut c_void) -> Hr;
    }

    unsafe fn supports_tearing() -> bool {
        let mut factory = std::ptr::null_mut();
        let hr = CreateDXGIFactory1(&IID_FACTORY_5, &mut factory);
        if hr != 0 || factory.is_null() {
            println!("tearing_check: factory_hr={hr:#x} supported=false");
            release_com(factory);
            return false;
        }
        let vtable = *(factory as *const *const FactoryVtbl);
        let mut supported = 0i32;
        let hr =
            ((*vtable).check_feature_support)(factory, 0, (&mut supported as *mut i32).cast(), 4);
        release_com(factory);
        println!(
            "tearing_check: feature_hr={hr:#x} supported={}",
            hr == 0 && supported != 0
        );
        hr == 0 && supported != 0
    }

    unsafe fn create_target(
        swapchain: *mut SwapChain,
        device: *mut c_void,
    ) -> Result<*mut c_void, String> {
        let mut texture = std::ptr::null_mut();
        let hr = ((*(*swapchain).lp_vtbl).get_buffer)(swapchain, 0, &IID_TEXTURE_2D, &mut texture);
        if hr != 0 {
            release_com(texture);
            return Err(format!("GetBuffer failed: hr={hr:#x}"));
        }
        let mut target = std::ptr::null_mut();
        let vtable = *(device as *const *const DeviceVtbl);
        let hr =
            ((*vtable).create_render_target_view)(device, texture, std::ptr::null(), &mut target);
        release_com(texture);
        if hr != 0 || target.is_null() {
            release_com(target);
            return Err(format!("CreateRenderTargetView failed: hr={hr:#x}"));
        }
        Ok(target)
    }

    fn scene_color(elapsed: f32, target_fps: u32) -> [f32; 4] {
        let pulse = 0.55 + 0.2 * (elapsed * 2.0).sin();
        if target_fps != 0 && target_fps <= 60 {
            [pulse, 0.15, 0.08, 1.0]
        } else {
            [0.05, pulse, 0.65, 1.0]
        }
    }

    unsafe fn render_present(
        context: *mut c_void,
        target: *mut c_void,
        swapchain: *mut SwapChain,
        color: &[f32; 4],
        flags: u32,
        capture: Option<&mut Capture>,
        tag: (usize, u32),
    ) -> Result<Hr, String> {
        let vtable = *(context as *const *const ContextVtbl);
        ((*vtable).clear_render_target_view)(context, target, color.as_ptr());
        let present = (*(*swapchain).lp_vtbl).present;
        match capture {
            Some(capture) => capture.present(tag, qpc, || present(swapchain, 0, flags)),
            None => Ok(present(swapchain, 0, flags)),
        }
    }

    unsafe fn set_title(hwnd: HWND, title: &str) {
        let wide: Vec<u16> = title.encode_utf16().chain(Some(0)).collect();
        SetWindowTextW(hwnd, wide.as_ptr());
    }

    unsafe fn pump_messages() -> Result<bool, String> {
        let mut space = false;
        let mut msg: MSG = std::mem::zeroed();
        while PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, PM_REMOVE) != 0 {
            if msg.message == WM_QUIT {
                return Err("window closed before the requested duration".into());
            }
            if msg.message == WM_KEYDOWN && msg.wParam == 0x1b {
                return Err("cancelled by Escape".into());
            }
            space |= msg.message == WM_KEYDOWN && msg.wParam == 0x20 && msg.lParam & (1 << 30) == 0;
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        Ok(space)
    }

    unsafe fn wait_for_start(hwnd: HWND, manual: bool) -> Result<(), String> {
        let started = Instant::now();
        let mut gate = StartGate::default();
        let mut last_report = None;
        FOCUS_LOST.store(false, Ordering::Relaxed);
        set_title(
            hwnd,
            if manual {
                "fps-bench READY - Space, then 2s foreground; Esc cancels"
            } else {
                "fps-bench READY - waiting for 2s stable foreground; Esc cancels"
            },
        );
        loop {
            let space = pump_messages()?;
            let focused = GetForegroundWindow() == hwnd && IsIconic(hwnd) == 0;
            let elapsed = started.elapsed();
            if FOCUS_LOST.swap(false, Ordering::Relaxed) {
                gate.ready(elapsed, false, false, manual);
            }
            if gate.ready(elapsed, focused, space, manual) {
                println!("activation: ready=true stable_foreground_seconds=2 manual={manual}");
                return Ok(());
            }
            if !manual && elapsed >= Duration::from_secs(20) {
                return Err("20s foreground deadline expired; no sample collected".into());
            }
            if last_report != Some(elapsed.as_secs()) {
                println!("waiting: focus={focused} manual={manual} sample_started=false");
                last_report = Some(elapsed.as_secs());
            }
            std::thread::sleep(Duration::from_millis(10));
        }
    }

    pub struct Options {
        mode: String,
        steps: Vec<Step>,
        wait_start: bool,
        frame_csv: Option<PathBuf>,
    }

    impl Options {
        pub fn parse(args: &[String]) -> Result<Self, String> {
            let mut positional = Vec::new();
            let mut wait_start = false;
            let mut steps = None;
            let mut frame_csv = None;
            let mut args = args.iter();
            while let Some(arg) = args.next() {
                match arg.as_str() {
                    "--wait-start" => wait_start = true,
                    "--steps" => {
                        steps = Some(parse_steps(
                            args.next().ok_or("--steps needs FPS:SECONDS,...")?,
                        )?)
                    }
                    "--frame-csv" => {
                        let path = args.next().ok_or("--frame-csv needs a path")?;
                        if path.is_empty() || path.starts_with("--") || frame_csv.is_some() {
                            return Err("--frame-csv needs one nonempty path".into());
                        }
                        frame_csv = Some(PathBuf::from(path));
                    }
                    value if value.starts_with("--") => {
                        return Err(format!("unknown option: {value}"))
                    }
                    _ => positional.push(arg.as_str()),
                }
            }
            if positional.len() > 3 {
                return Err("too many positional arguments".into());
            }
            let mode = positional.first().copied().unwrap_or("borderless");
            if !matches!(mode, "windowed" | "borderless" | "exclusive") {
                return Err("mode must be windowed | borderless | exclusive".into());
            }
            let fps: u32 = positional
                .get(1)
                .unwrap_or(&"0")
                .parse()
                .map_err(|_| "invalid target FPS")?;
            let seconds: u64 = positional
                .get(2)
                .unwrap_or(&"30")
                .parse()
                .map_err(|_| "invalid seconds")?;
            if seconds == 0 {
                return Err("seconds must be positive".into());
            }
            Ok(Self {
                mode: mode.into(),
                steps: steps.unwrap_or_else(|| vec![Step { fps, seconds }]),
                wait_start,
                frame_csv,
            })
        }
    }

    fn validate_state(
        foreground: bool,
        state_hr: Hr,
        fullscreen: bool,
        expected_fullscreen: bool,
    ) -> Result<(), String> {
        if state_hr != 0 || !foreground || fullscreen != expected_fullscreen {
            return Err(format!(
                "invalid sample: foreground={foreground} GetFullscreenState={state_hr:#x} \
                 fullscreen={fullscreen} expected_fullscreen={expected_fullscreen}"
            ));
        }
        Ok(())
    }

    fn count_present(hr: Hr, frames: &mut u64) -> Result<(), String> {
        if hr != 0 {
            return Err(format!(
                "Present returned {hr:#x}; sample invalid (only S_OK is counted)"
            ));
        }
        *frames += 1;
        Ok(())
    }

    unsafe fn verify_window(
        swapchain: *mut SwapChain,
        hwnd: HWND,
        exclusive: bool,
        report: bool,
    ) -> Result<(), String> {
        let foreground = GetForegroundWindow();
        let mut fullscreen = 0;
        let hr = ((*(*swapchain).lp_vtbl).get_fullscreen_state)(
            swapchain,
            &mut fullscreen,
            std::ptr::null_mut(),
        );
        let active =
            foreground == hwnd && IsIconic(hwnd) == 0 && !FOCUS_LOST.load(Ordering::Relaxed);
        let result = validate_state(active, hr, fullscreen != 0, exclusive);
        if report || result.is_err() {
            let mut foreground_pid = 0;
            GetWindowThreadProcessId(foreground, &mut foreground_pid);
            println!(
                "state: pid={} hwnd={hwnd:p} foreground_hwnd={foreground:p} \
                 foreground_pid={foreground_pid} GetFullscreenState={hr:#x} \
                 dxgi_fullscreen={fullscreen} expected_fullscreen={exclusive} focus_valid={active}",
                std::process::id()
            );
        }
        result
    }

    unsafe fn release_com(object: *mut core::ffi::c_void) {
        if !object.is_null() {
            // All three returned COM interfaces inherit IUnknown, Release is slot 2.
            let vtable = *(object as *const *const usize);
            let release: unsafe extern "system" fn(*mut core::ffi::c_void) -> u32 =
                std::mem::transmute(*vtable.add(2));
            release(object);
        }
    }

    #[cfg(test)]
    mod tests {
        use super::*;
        use std::mem::{offset_of, size_of};

        // Windows SDK 10.0.26100.0 shared/dxgi.h, IDXGISwapChainVtbl.
        #[test]
        fn present_uses_sdk_slot_8() {
            let slot = size_of::<usize>();
            assert_eq!(offset_of!(SwapChainVtbl, present), 8 * slot);
        }

        #[test]
        fn set_fullscreen_uses_sdk_slot_10() {
            assert_eq!(
                offset_of!(SwapChainVtbl, set_fullscreen_state),
                10 * size_of::<usize>()
            );
        }

        #[test]
        fn remaining_slots_match_sdk() {
            let slot = size_of::<usize>();
            assert_eq!(offset_of!(SwapChainVtbl, get_fullscreen_state), 11 * slot);
            assert_eq!(offset_of!(SwapChainVtbl, resize_buffers), 13 * slot);
            assert_eq!(offset_of!(SwapChainVtbl, _get_containing_output), 15 * slot);
        }

        #[test]
        fn rejects_background_or_wrong_fullscreen_state() {
            assert!(validate_state(false, 0, true, true).is_err());
            assert!(validate_state(true, 0, false, true).is_err());
            assert!(validate_state(true, 0, true, false).is_err());
            assert!(validate_state(true, -1, true, true).is_err());
        }

        #[test]
        fn accepts_verified_state_only() {
            assert!(validate_state(true, 0, true, true).is_ok());
            assert!(validate_state(true, 0, false, false).is_ok());
        }

        #[test]
        fn failed_or_occluded_present_is_not_counted() {
            for hr in [0x887a0005u32 as i32, 0x087a0001, 1] {
                let mut frames = 7;
                assert!(count_present(hr, &mut frames).is_err());
                assert_eq!(frames, 7);
            }
        }

        #[test]
        fn successful_present_is_counted() {
            let mut frames = 7;
            assert!(count_present(0, &mut frames).is_ok());
            assert_eq!(frames, 8);
        }

        #[test]
        fn rendering_and_tearing_slots_match_sdk() {
            let slot = size_of::<usize>();
            assert_eq!(offset_of!(SwapChainVtbl, get_buffer), 9 * slot);
            assert_eq!(offset_of!(DeviceVtbl, create_render_target_view), 9 * slot);
            assert_eq!(offset_of!(ContextVtbl, clear_render_target_view), 50 * slot);
            assert_eq!(offset_of!(FactoryVtbl, check_feature_support), 28 * slot);
        }

        #[test]
        fn scene_is_visible_animated_and_distinguishes_low_phase() {
            let high = scene_color(0.0, 300);
            assert_ne!(high, scene_color(0.5, 300));
            assert_ne!(high, scene_color(0.0, 60));
            for fps in [0, 60, 300, 600] {
                for t in 0..100 {
                    let color = scene_color(t as f32 / 10.0, fps);
                    assert!(color[..3].iter().any(|v| *v > 0.3));
                    assert!(color.iter().all(|v| (0.0..=1.0).contains(v)));
                }
            }
        }

        #[test]
        fn command_line_is_strict_and_accepts_manual_steps() {
            let args = [
                "borderless",
                "300",
                "30",
                "--wait-start",
                "--steps",
                "300:10,60:10,300:10",
            ]
            .map(String::from);
            let opts = Options::parse(&args).unwrap();
            assert!(opts.wait_start);
            assert_eq!(opts.steps.len(), 3);
            for bad in [
                vec!["borderless", "oops"],
                vec!["exclusive", "300", "0"],
                vec!["--steps"],
                vec!["--typo"],
            ] {
                assert!(
                    Options::parse(&bad.into_iter().map(String::from).collect::<Vec<_>>()).is_err()
                );
            }
        }

        #[test]
        fn accepts_frame_csv_option_with_spaces() {
            let args = [
                "borderless",
                "300",
                "--frame-csv",
                r"C:\bench logs\frames.csv",
            ]
            .map(String::from);
            assert_eq!(
                Options::parse(&args).unwrap().frame_csv,
                Some(PathBuf::from(r"C:\bench logs\frames.csv"))
            );
            for bad in [
                vec!["--frame-csv"],
                vec!["--frame-csv", "--wait-start"],
                vec!["--frame-csv", ""],
            ] {
                assert!(
                    Options::parse(&bad.into_iter().map(String::from).collect::<Vec<_>>()).is_err()
                );
            }
        }

        #[test]
        fn rendering_dispatch_clears_before_present_and_preserves_hresult() {
            #[repr(C)]
            struct FakeContext {
                vtable: *const ContextVtbl,
                cleared: bool,
            }
            #[repr(C)]
            struct FakeSwapchain {
                vtable: *const SwapChainVtbl,
                context: *const FakeContext,
                hr: Hr,
            }
            unsafe extern "system" fn clear(
                ctx: *mut c_void,
                target: *mut c_void,
                color: *const f32,
            ) {
                assert_eq!(target as usize, 0x1234);
                assert_eq!(
                    std::slice::from_raw_parts(color, 4),
                    &[0.05, 0.55, 0.65, 1.0]
                );
                (*(ctx as *mut FakeContext)).cleared = true;
            }
            unsafe extern "system" fn present(swap: *mut SwapChain, sync: u32, flags: u32) -> Hr {
                let swap = &*(swap as *const FakeSwapchain);
                assert!((*swap.context).cleared);
                assert_eq!(sync, 0);
                assert_eq!(flags, 0x200);
                swap.hr
            }
            unsafe extern "system" fn buffer(
                _: *mut SwapChain,
                _: u32,
                _: *const GUID,
                _: *mut *mut c_void,
            ) -> Hr {
                -1
            }
            unsafe extern "system" fn set(_: *mut SwapChain, _: i32, _: *mut c_void) -> Hr {
                -1
            }
            unsafe extern "system" fn get(
                _: *mut SwapChain,
                _: *mut i32,
                _: *mut *mut c_void,
            ) -> Hr {
                -1
            }
            unsafe extern "system" fn resize(
                _: *mut SwapChain,
                _: u32,
                _: u32,
                _: u32,
                _: u32,
                _: u32,
            ) -> Hr {
                -1
            }
            let context_vtable = ContextVtbl {
                _before_clear: [0; 50],
                clear_render_target_view: clear,
            };
            let swap_vtable = SwapChainVtbl {
                _qi: 0,
                _addref: 0,
                _release: 0,
                _set_private_data: 0,
                _set_private_data_interface: 0,
                _get_private_data: 0,
                _get_parent: 0,
                _get_device: 0,
                present,
                get_buffer: buffer,
                set_fullscreen_state: set,
                get_fullscreen_state: get,
                _get_desc: 0,
                resize_buffers: resize,
                _resize_target: 0,
                _get_containing_output: 0,
            };
            let mut context = FakeContext {
                vtable: &context_vtable,
                cleared: false,
            };
            let mut swap = FakeSwapchain {
                vtable: &swap_vtable,
                context: &context,
                hr: 0,
            };
            for hr in [0, 0x087a0001, 0x887a0005u32 as i32] {
                context.cleared = false;
                swap.hr = hr;
                let mut capture = Capture::with_capacity(1).unwrap();
                let result = unsafe {
                    render_present(
                        (&mut context as *mut FakeContext).cast(),
                        0x1234usize as *mut c_void,
                        (&mut swap as *mut FakeSwapchain).cast(),
                        &scene_color(0.0, 300),
                        0x200,
                        Some(&mut capture),
                        (1, 300),
                    )
                }
                .unwrap();
                assert_eq!(result, hr);
                assert_eq!(capture.len(), 1);
                let mut frames = 0;
                assert_eq!(count_present(result, &mut frames).is_ok(), hr == 0);
                assert_eq!(frames, u64::from(hr == 0));
            }
        }
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

    unsafe extern "system" fn wndproc(
        hwnd: HWND,
        msg: u32,
        wparam: WPARAM,
        lparam: LPARAM,
    ) -> LRESULT {
        match msg {
            WM_KILLFOCUS => {
                FOCUS_LOST.store(true, Ordering::Relaxed);
                DefWindowProcW(hwnd, msg, wparam, lparam)
            }
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

    #[allow(clippy::too_many_arguments)]
    fn report_interval(
        mode: &str,
        phase: usize,
        target: u32,
        frames: u64,
        seconds: f64,
        elapsed: Duration,
        present_flags: u32,
        partial: bool,
    ) {
        if seconds <= 0.0 || frames == 0 {
            return;
        }
        println!(
            "interval: elapsed={:.3} phase={} target={target} successful_present={frames} \
             interval_seconds={seconds:.6} present_rate={:.2} mode={mode} focus=true \
             exclusive={} tearing={} scene=animated_clear partial={partial} provisional=true",
            elapsed.as_secs_f64(),
            phase + 1,
            frames as f64 / seconds,
            mode == "exclusive",
            present_flags != 0
        );
    }

    pub fn run(options: Options) -> i32 {
        let mut csv = match options
            .frame_csv
            .as_ref()
            .map(|path| -> Result<_, String> {
                let capacity = capacity_for(&options.steps)?;
                let capture = Capture::with_capacity(capacity)?;
                let file = OpenOptions::new()
                    .write(true)
                    .create_new(true)
                    .open(path)
                    .map_err(|e| format!("cannot create frame CSV {}: {e}", path.display()))?;
                println!(
                    "frame_csv: path={} capacity={capacity} write_policy=memory_until_exit",
                    path.display()
                );
                Ok((file, capture))
            })
            .transpose()
        {
            Ok(csv) => csv,
            Err(error) => {
                eprintln!("INVALID: {error}");
                return 1;
            }
        };
        let code = run_window(&options, csv.as_mut().map(|(_, capture)| capture));
        if let Some((file, capture)) = csv {
            let mut writer = BufWriter::new(file);
            if let Err(error) =
                capture.write_csv(&mut writer, std::process::id(), matches!(code, 0 | 4))
            {
                eprintln!("INVALID: frame CSV write/flush failed: {error}; CSV may be incomplete");
                return 1;
            }
            println!(
                "frame_csv_complete: records={} run_valid={}",
                capture.len(),
                matches!(code, 0 | 4)
            );
        }
        if matches!(code, 0 | 4) {
            println!("sample complete: validity=valid target_met={} target_threshold=95percent metric=successful_present_calls_per_second scanout_mode=unverified", code == 0);
        }
        code
    }

    fn run_window(options: &Options, mut capture: Option<&mut Capture>) -> i32 {
        let mode = options.mode.as_str();
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
            let (x, y, mut w, mut h, style) = match mode {
                "exclusive" => (0, 0, sw, sh, WS_POPUP),
                "borderless" => (0, 0, sw, sh, WS_POPUP | WS_VISIBLE),
                _ => (80, 80, 1280, 720, WS_OVERLAPPEDWINDOW | WS_VISIBLE),
            };
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
            let foreground_requested = SetForegroundWindow(hwnd);
            println!(
                "startup: mode={mode} pid={} hwnd={hwnd:p} SetForegroundWindow={foreground_requested}; \
                 metric=successful_present_calls_per_second scanout_mode=unverified",
                std::process::id()
            );
            if let Err(error) = wait_for_start(hwnd, options.wait_start) {
                eprintln!("INVALID: {error}");
                DestroyWindow(hwnd);
                return 1;
            }

            let tearing_supported = supports_tearing();
            let (swap_flags, present_flags) = tearing_flags(tearing_supported, mode == "exclusive");
            println!(
                "environment: mode={mode} tearing_supported={tearing_supported} \
                 swap_flags={swap_flags:#x} present_flags={present_flags:#x} \
                 scene=animated_clear scanout_mode=unverified"
            );
            if mode != "exclusive" && !tearing_supported {
                println!("LIMITATION: tearing unavailable; DXGI/DWM may cap the submission rate; target FPS is not guaranteed");
            }

            let mut desc = DXGI_SWAP_CHAIN_DESC {
                mode_desc: DXGI_MODE_DESC {
                    width: if mode == "windowed" { 1280 } else { sw as u32 },
                    height: if mode == "windowed" { 720 } else { sh as u32 },
                    refresh_rate: DXGI_Rational {
                        numerator: 0,
                        denominator: 0,
                    },
                    format: DXGI_FORMAT_R8G8B8A8_UNORM,
                    scanline_ordering: 0,
                    scaling: 0,
                },
                sample_desc: DXGI_SAMPLE_DESC {
                    count: 1,
                    quality: 0,
                },
                usage: DXGI_USAGE_RENDER_TARGET_OUTPUT,
                buffer_count: 2,
                output_window: hwnd,
                windowed: 1,
                swap_effect: DXGI_SWAP_EFFECT_FLIP_DISCARD,
                flags: swap_flags,
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
            if hr != 0 || swapchain.is_null() || device.is_null() || context.is_null() {
                eprintln!("D3D11CreateDeviceAndSwapChain failed: hr={hr:#x}");
                release_com(swapchain.cast());
                release_com(context);
                release_com(device);
                DestroyWindow(hwnd);
                return 1;
            }

            let mut render_target = std::ptr::null_mut();
            // Keep every post-creation failure on the same fullscreen/COM cleanup path.
            let result = (|| -> Result<bool, String> {
                if mode == "exclusive" {
                    let hr = ((*(*swapchain).lp_vtbl).set_fullscreen_state)(
                        swapchain,
                        1,
                        std::ptr::null_mut(),
                    );
                    println!("SetFullscreenState(TRUE): hr={hr:#x}");
                    if hr != 0 {
                        return Err(format!("SetFullscreenState did not complete: hr={hr:#x}"));
                    }
                    // Flip-model fullscreen transitions require ResizeBuffers before presenting.
                    let hr = ((*(*swapchain).lp_vtbl).resize_buffers)(swapchain, 0, 0, 0, 0, 0);
                    if hr != 0 {
                        return Err(format!(
                            "ResizeBuffers after fullscreen transition failed: hr={hr:#x}"
                        ));
                    }
                }
                // Mode switching may temporarily transfer focus. Require a fresh stable gate.
                wait_for_start(hwnd, false)?;
                verify_window(swapchain, hwnd, mode == "exclusive", true)?;
                render_target = create_target(swapchain, device)?;

                let mut freq = 0i64;
                if QueryPerformanceFrequency(&mut freq) == 0 || freq <= 0 {
                    return Err("QueryPerformanceFrequency failed".into());
                }
                let measurement_qpc = qpc();
                if let Some(capture) = capture.as_deref_mut() {
                    capture.begin(freq, measurement_qpc);
                }
                let started = Instant::now();
                let mut frames: u64 = 0;
                let mut stat_window_start = qpc();
                let mut stat_window_frames: u64 = 0;
                let mut phase = 0usize;
                let mut phase_frames = 0u64;
                let mut phase_started = started;
                let mut phase_rates = Vec::new();
                println!("measurement_start: mode={mode} steps={:?} focus=true exclusive={} scene=animated_clear qpc={measurement_qpc} qpc_frequency={freq}", options.steps, mode == "exclusive");
                println!(
                    "phase_start: phase=1 target={} elapsed=0.000",
                    options.steps[0].fps
                );

                loop {
                    pump_messages()?;
                    verify_window(swapchain, hwnd, mode == "exclusive", false)?;
                    let elapsed = started.elapsed();
                    let next_phase = step_at(&options.steps, elapsed);
                    if next_phase != Some(phase) {
                        if phase_frames == 0 {
                            return Err(format!(
                                "phase {} had no successful Present calls",
                                phase + 1
                            ));
                        }
                        let now = qpc();
                        report_interval(
                            mode,
                            phase,
                            options.steps[phase].fps,
                            stat_window_frames,
                            (now - stat_window_start) as f64 / freq as f64,
                            elapsed,
                            present_flags,
                            true,
                        );
                        let phase_rate =
                            phase_frames as f64 / phase_started.elapsed().as_secs_f64();
                        phase_rates.push(phase_rate);
                        println!("phase_complete: phase={} target={} successful_present={} present_rate={phase_rate:.2} provisional=true",
                            phase + 1, options.steps[phase].fps, phase_frames);
                        let Some(next) = next_phase else {
                            if phase + 1 != options.steps.len() {
                                return Err("remaining phases were skipped by a stall".into());
                            }
                            break;
                        };
                        if next != phase + 1 {
                            return Err("a whole phase was skipped by a stall".into());
                        }
                        phase = next;
                        phase_frames = 0;
                        phase_started = Instant::now();
                        stat_window_frames = 0;
                        stat_window_start = now;
                        println!(
                            "phase_start: phase={} target={} elapsed={:.3}",
                            phase + 1,
                            options.steps[phase].fps,
                            elapsed.as_secs_f64()
                        );
                    }

                    let target_fps = options.steps[phase].fps;
                    let frame_start = qpc();
                    let color = scene_color(elapsed.as_secs_f32(), target_fps);
                    let hr = render_present(
                        context,
                        render_target,
                        swapchain,
                        &color,
                        present_flags,
                        capture.as_deref_mut(),
                        (phase + 1, target_fps),
                    )?;
                    let state = verify_window(swapchain, hwnd, mode == "exclusive", false);
                    if let Some(capture) = capture.as_deref_mut() {
                        capture.verify_last(state.is_ok());
                    }
                    state?;
                    count_present(hr, &mut frames)?;
                    stat_window_frames += 1;
                    phase_frames += 1;

                    let frame_budget_ticks = if target_fps > 0 {
                        freq / target_fps as i64
                    } else {
                        0
                    };
                    if frame_budget_ticks > 0 {
                        let mut last_focus_check = frame_start;
                        while qpc() - frame_start < frame_budget_ticks {
                            // Keep low-FPS pacing cancellable and latch transient focus loss.
                            let now = qpc();
                            if now - last_focus_check >= freq / 1000 {
                                pump_messages()?;
                                if GetForegroundWindow() != hwnd
                                    || FOCUS_LOST.load(Ordering::Relaxed)
                                {
                                    return Err("foreground lost during pacing; all sample intervals invalid".into());
                                }
                                last_focus_check = now;
                            }
                            std::hint::spin_loop();
                        }
                    }

                    let now = qpc();
                    if now - stat_window_start >= freq {
                        let dt = (now - stat_window_start) as f64 / freq as f64;
                        report_interval(
                            mode,
                            phase,
                            target_fps,
                            stat_window_frames,
                            dt,
                            started.elapsed(),
                            present_flags,
                            false,
                        );
                        let actual = stat_window_frames as f64 / dt;
                        stat_window_start = now;
                        stat_window_frames = 0;
                        let title = format!(
                            "fps-bench {:.1}/s target={} phase={} mode={} focus=1 exclusive={}",
                            actual,
                            target_fps,
                            phase + 1,
                            mode,
                            mode == "exclusive"
                        );
                        set_title(hwnd, &title);
                    }
                }

                verify_window(swapchain, hwnd, mode == "exclusive", true)?;
                if frames == 0 {
                    return Err("no successful Present calls collected".into());
                }
                let total = started.elapsed().as_secs_f64();
                let target_met = options
                    .steps
                    .iter()
                    .zip(&phase_rates)
                    .all(|(step, rate)| step.fps == 0 || *rate >= step.fps as f64 * 0.95);
                println!(
                    "measurement_end: {} successful Present calls in {:.1}s = {:.2}/s \
                 (mode={} target_met={} scanout_mode=unverified); awaiting cleanup",
                    frames,
                    total,
                    frames as f64 / total,
                    mode,
                    target_met
                );
                Ok(target_met)
            })();
            release_com(render_target);
            let restore_hr =
                ((*(*swapchain).lp_vtbl).set_fullscreen_state)(swapchain, 0, std::ptr::null_mut());
            release_com(swapchain.cast());
            release_com(context);
            release_com(device);
            if IsWindow(hwnd) != 0 {
                DestroyWindow(hwnd);
            }
            if let Err(ref error) = result {
                eprintln!("INVALID: {error}");
                if restore_hr != 0 {
                    eprintln!("cleanup: restoring windowed state also failed: hr={restore_hr:#x}");
                }
                return 1;
            }
            if restore_hr != 0 {
                eprintln!("INVALID: restoring windowed state failed: hr={restore_hr:#x}");
                return 1;
            }
            let target_met = result.unwrap();
            if target_met {
                0
            } else {
                4
            }
        }
    }
}

#[cfg(windows)]
fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let options = win::Options::parse(&args).unwrap_or_else(|error| {
        eprintln!("{error}\nusage: fps-bench [windowed|borderless|exclusive] [FPS] [SECONDS] [--wait-start] [--steps FPS:SECONDS,...] [--frame-csv PATH]");
        std::process::exit(2)
    });
    std::process::exit(win::run(options));
}

#[cfg(not(windows))]
fn main() {
    eprintln!("windows only");
    std::process::exit(3);
}
