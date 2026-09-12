//! Copied from NexBox c800dc575eb8832086ed4e4bfb223f0a008deed9 (GPL-3.0).
//! Preview adaptation: original GDI+ renderer, exclusively caller-supplied data.
use std::sync::atomic::{AtomicBool, AtomicPtr, Ordering};
use std::sync::Mutex;
use std::thread;
use std::time::Duration;
use tauri::{Emitter, Manager};

static OVERLAY_ACTIVE: AtomicBool = AtomicBool::new(false);
static OVERLAY_HANDLE: AtomicPtr<std::ffi::c_void> = AtomicPtr::new(std::ptr::null_mut());
static DRAG_MODE: AtomicBool = AtomicBool::new(false);
static POSITION_CHANGED: AtomicBool = AtomicBool::new(false);

/// Takes (clears) the drag-moved flag; true means CURRENT_SETTINGS now holds
/// a window position that has never been persisted.
pub fn take_position_changed() -> bool {
    POSITION_CHANGED.swap(false, std::sync::atomic::Ordering::SeqCst)
}
static OVERLAY_THREAD: Mutex<Option<thread::JoinHandle<()>>> = Mutex::new(None);

fn get_net_offset_ms() -> Option<i64> {
    CURRENT_HARDWARE_DATA.lock().unwrap().as_ref().and_then(|d| d.net_time_offset_ms)
}

pub fn collect_hardware_data() -> OverlayHardwareData {
    CURRENT_HARDWARE_DATA.lock().unwrap().clone().unwrap_or_default()
}

#[derive(serde::Serialize, serde::Deserialize, Clone, Debug)]
pub struct DisplayItem {
    pub id: String,
    pub label: String,
    pub enabled: bool,
}

pub type DisplayItems = Vec<DisplayItem>;

fn default_style() -> String {
    "default".to_string()
}

fn default_font() -> String {
    "Microsoft YaHei".to_string()
}

fn default_font_size() -> u32 {
    13
}

fn default_item_width() -> u32 {
    130
}

fn default_font_color() -> String {
    "#ffffff".to_string()
}

fn default_display_items() -> DisplayItems {
        vec![
            DisplayItem { id: "time".to_string(), label: "时间".to_string(), enabled: false },
            DisplayItem { id: "fps".to_string(), label: "FPS".to_string(), enabled: false },
            DisplayItem { id: "fps_1low".to_string(), label: "1% Low".to_string(), enabled: false },
            DisplayItem { id: "fps_01low".to_string(), label: "0.1% Low".to_string(), enabled: false },
            DisplayItem { id: "cpu_temp".to_string(), label: "CPU温度".to_string(), enabled: false },
            DisplayItem { id: "cpu_usage".to_string(), label: "CPU占用".to_string(), enabled: true },
            DisplayItem { id: "cpu_fan_speed".to_string(), label: "CPU风扇转速".to_string(), enabled: false },
            DisplayItem { id: "cpu_clock".to_string(), label: "CPU频率".to_string(), enabled: false },
            DisplayItem { id: "cpu_voltage".to_string(), label: "CPU电压".to_string(), enabled: false },
            DisplayItem { id: "cpu_power".to_string(), label: "CPU功耗".to_string(), enabled: false },
            DisplayItem { id: "gpu_temp".to_string(), label: "GPU温度".to_string(), enabled: true },
            DisplayItem { id: "gpu_usage".to_string(), label: "GPU占用".to_string(), enabled: true },
            DisplayItem { id: "gpu_fan_speed".to_string(), label: "GPU风扇转速".to_string(), enabled: false },
            DisplayItem { id: "gpu_power".to_string(), label: "GPU功耗".to_string(), enabled: false },
            DisplayItem { id: "gpu_clock".to_string(), label: "GPU频率".to_string(), enabled: false },
            DisplayItem { id: "gpu_voltage".to_string(), label: "GPU电压".to_string(), enabled: false },
            DisplayItem { id: "gpu_vram".to_string(), label: "GPU显存占用".to_string(), enabled: false },
            DisplayItem { id: "gpu_memory_clock".to_string(), label: "GPU显存频率".to_string(), enabled: false },
            DisplayItem { id: "memory_usage".to_string(), label: "内存占用".to_string(), enabled: true },
            DisplayItem { id: "ssd_temp".to_string(), label: "硬盘温度".to_string(), enabled: false },
            DisplayItem { id: "game_ping".to_string(), label: "游戏延迟".to_string(), enabled: true },
            DisplayItem { id: "delta_password".to_string(), label: "三角洲密码".to_string(), enabled: false },
        ]
}

#[derive(serde::Serialize, serde::Deserialize, Clone, Debug)]
pub struct CustomOverlayItem {
    pub id: String,
    pub text: String,
    pub color: String,
    #[serde(default)]
    pub enabled: bool,
}

#[derive(serde::Serialize, serde::Deserialize, Clone, Debug)]
pub struct OverlaySettings {
    #[serde(default = "default_display_items")]
    pub display_items: DisplayItems,
    #[serde(default)]
    pub custom_items: Vec<CustomOverlayItem>,
    pub opacity: u8,
    #[serde(default = "default_style")]
    pub style: String,
    #[serde(default = "default_font")]
    pub font: String,
    #[serde(default = "default_font_size")]
    pub font_size: u32,
    #[serde(default = "default_item_width")]
    pub item_width: u32,
    #[serde(default = "default_font_color")]
    pub font_color: String,
    #[serde(default)]
    pub position_x: Option<i32>,
    #[serde(default)]
    pub position_y: Option<i32>,
    /// 竖排悬浮框独立位置（与 Win32 悬浮框的 position_x/y 解耦，互不影响）
    #[serde(default)]
    pub vertical_position_x: Option<i32>,
    #[serde(default)]
    pub vertical_position_y: Option<i32>,
    /// 三角洲密码选中的地图名称列表（空 = 显示全部）
    #[serde(default)]
    pub delta_password_maps: Vec<String>,
}

impl Default for OverlaySettings {
    fn default() -> Self {
        Self {
            display_items: default_display_items(),
            custom_items: Vec::new(),
            opacity: 200,
            style: "default".to_string(),
            font: "Microsoft YaHei".to_string(),
            font_size: 13,
            item_width: 130,
            font_color: "#ffffff".to_string(),
            position_x: None,
            position_y: None,
            vertical_position_x: None,
            vertical_position_y: None,
            delta_password_maps: Vec::new(),
        }
    }
}

#[derive(serde::Serialize, serde::Deserialize, Clone, Debug)]
pub struct OverlayResult {
    pub success: bool,
    pub message: String,
}

/// 单个 GPU 的传感器数据
#[derive(Clone, serde::Serialize, serde::Deserialize, Default)]
#[serde(default, deny_unknown_fields)]
pub struct GpuSensorData {
    pub name: String,
    pub hardware_type: String,
    pub temperature: Option<f64>,
    pub usage: Option<u32>,
    pub fan_speed: Option<u32>,
    pub power: Option<u32>,
    pub clock: Option<u32>,
    pub memory_clock: Option<u32>,
    pub vram_used: Option<u32>,
    pub vram_total: Option<u32>,
    pub voltage: Option<f64>,
}

#[derive(Clone, serde::Serialize, serde::Deserialize)]
#[serde(default, deny_unknown_fields)]
pub struct OverlayHardwareData {
    fps: Option<u32>,
    fps_1low: Option<u32>,
    fps_01low: Option<u32>,
    cpu_usage: Option<u16>,
    cpu_temp: Option<f64>,
    cpu_clock: Option<u32>,
    gpu_temp: Option<f64>,
    gpu_usage: Option<u32>,
    memory_usage: Option<f64>,
    delta_password: Option<String>,
    game_ping: Option<u32>,
    cpu_fan_speed: Option<u32>,
    gpu_fan_speed: Option<u32>,
    gpu_power: Option<u32>,
    gpu_clock: Option<u32>,
    gpu_vram_used: Option<u32>,
    gpu_vram_total: Option<u32>,
    gpu_memory_clock: Option<u32>,
    cpu_voltage: Option<f64>,
    gpu_voltage: Option<f64>,
    cpu_power: Option<f64>,
    ssd_temp: Option<f64>,
    /// 网络标准时间偏移量（毫秒）：net_time = 本地时间 + 该偏移。None 表示尚未同步成功
    pub net_time_offset_ms: Option<i64>,
    /// 分核心 CPU 频率（MHz）。GDI 单行形态不渲染它，但字段必须存在——
    /// deny_unknown_fields 会拒绝服务快照里的未知键，缺了整个解码就炸。
    pub cpu_core_clocks: Vec<Option<f64>>,
    /// 分核心 CPU 电压（V）
    pub cpu_core_voltages: Vec<Option<f64>>,
    /// 所有 GPU 的传感器数据（支持多 GPU 切换）
    pub gpu_sensors: Vec<GpuSensorData>,
    /// 当前选中的 GPU 索引
    pub active_gpu_index: usize,
}

impl Default for OverlayHardwareData {
    fn default() -> Self {
        Self {
            fps: None,
            fps_1low: None,
            fps_01low: None,
            cpu_usage: None,
            cpu_temp: None,
            cpu_clock: None,
            gpu_temp: None,
            gpu_usage: None,
            memory_usage: None,
            delta_password: None,
            game_ping: None,
            cpu_fan_speed: None,
            gpu_fan_speed: None,
            gpu_power: None,
            gpu_clock: None,
            gpu_vram_used: None,
            gpu_vram_total: None,
            gpu_memory_clock: None,
            cpu_voltage: None,
            gpu_voltage: None,
            cpu_power: None,
            ssd_temp: None,
            net_time_offset_ms: None,
            cpu_core_clocks: Vec::new(),
            cpu_core_voltages: Vec::new(),
            gpu_sensors: Vec::new(),
            active_gpu_index: 0,
        }
    }
}

pub static CURRENT_SETTINGS: Mutex<Option<OverlaySettings>> = Mutex::new(None);
pub static CURRENT_HARDWARE_DATA: Mutex<Option<OverlayHardwareData>> = Mutex::new(None);
pub static SETTINGS_LOADED_FROM_STORE: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// 供托盘悬停面板读取的硬件数据快照（只读自常驻轮询缓存，不触发额外采样）。
#[derive(serde::Serialize, Clone, Default)]
pub struct HoverSnapshot {
    pub cpu_usage: Option<u16>,
    pub cpu_temp: Option<f64>,
    pub cpu_clock: Option<u32>,
    pub cpu_fan_speed: Option<u32>,
    pub cpu_voltage: Option<f64>,
    pub cpu_power: Option<f64>,
    pub gpu_temp: Option<f64>,
    pub gpu_usage: Option<u32>,
    pub gpu_clock: Option<u32>,
    pub gpu_fan_speed: Option<u32>,
    pub gpu_voltage: Option<f64>,
    pub gpu_power: Option<u32>,
    pub gpu_vram_used: Option<u32>,
    pub gpu_vram_total: Option<u32>,
    pub gpu_memory_clock: Option<u32>,
    pub memory_usage: Option<f64>,
    pub ssd_temp: Option<f64>,
}

/// 读取当前轮询缓存中的硬件数据（None 表示尚无数据，如启动早期）。
pub fn current_hover_snapshot() -> Option<HoverSnapshot> {
    CURRENT_HARDWARE_DATA.lock().unwrap().clone().map(|d| HoverSnapshot {
        cpu_usage: d.cpu_usage,
        cpu_temp: d.cpu_temp,
        cpu_clock: d.cpu_clock,
        cpu_fan_speed: d.cpu_fan_speed,
        cpu_voltage: d.cpu_voltage,
        cpu_power: d.cpu_power,
        gpu_temp: d.gpu_temp,
        gpu_usage: d.gpu_usage,
        gpu_clock: d.gpu_clock,
        gpu_fan_speed: d.gpu_fan_speed,
        gpu_voltage: d.gpu_voltage,
        gpu_power: d.gpu_power,
        gpu_vram_used: d.gpu_vram_used,
        gpu_vram_total: d.gpu_vram_total,
        gpu_memory_clock: d.gpu_memory_clock,
        memory_usage: d.memory_usage,
        ssd_temp: d.ssd_temp,
    })
}

/// 从持久化存储 (settings.json) 加载悬浮框设置到内存。
/// 仅在 CURRENT_SETTINGS 为空时尝试加载，确保快捷键触发的 toggle 使用已保存的设置而非默认值。
pub fn try_load_persisted_settings(app_handle: &tauri::AppHandle) {
    // 已经加载过就不再重复读取文件
    if SETTINGS_LOADED_FROM_STORE.load(Ordering::SeqCst) {
        return;
    }
    let mut lock = CURRENT_SETTINGS.lock().unwrap();
    if lock.is_some() {
        SETTINGS_LOADED_FROM_STORE.store(true, Ordering::SeqCst);
        return;
    }
    // 尝试从 settings.json 读取已保存的设置
    if let Ok(app_data_dir) = app_handle.path().app_data_dir() {
        let store_path = app_data_dir.join("preview-native-settings.json");
        if store_path.exists() {
            if let Ok(content) = std::fs::read_to_string(&store_path) {
                if let Ok(json) = serde_json::from_str::<serde_json::Value>(&content) {
                    if let Some(saved) = json.get("overlay-settings") {
                        if let Ok(settings) = serde_json::from_value::<OverlaySettings>(saved.clone()) {
                            log::info!("从持久化存储加载悬浮框设置成功");
                            *lock = Some(settings);
                        }
                    }
                }
            }
        }
    }
    // 如果加载失败，用默认值初始化（保持向后兼容）
    if lock.is_none() {
        *lock = Some(OverlaySettings::default());
    }
    SETTINGS_LOADED_FROM_STORE.store(true, Ordering::SeqCst);
}

pub fn get_or_init_settings() -> OverlaySettings {
    let mut settings_lock = CURRENT_SETTINGS.lock().unwrap();
    if settings_lock.is_none() {
        *settings_lock = Some(OverlaySettings::default());
    }
    settings_lock.as_ref().unwrap().clone()
}

#[cfg(target_os = "windows")]
mod win32 {
    use windows_sys::Win32::Foundation::*;
    use windows_sys::Win32::Graphics::Gdi::*;
    use windows_sys::Win32::Graphics::GdiPlus::*;
    use windows_sys::Win32::UI::WindowsAndMessaging::*;
    use windows_sys::Win32::System::LibraryLoader::GetModuleHandleW;
    use std::path::PathBuf;
    use std::ptr;
    use std::sync::Mutex;
    use std::result::Result::Ok;

    static GDIPLUS_TOKEN: Mutex<Option<usize>> = Mutex::new(None);
    static FONT_PATH: Mutex<Option<String>> = Mutex::new(None);

    fn find_misans_font_path() -> Option<PathBuf> {
        let font_name = "MiSansVF.ttf";
        // 1. 通过 exe 路径查找
        if let Ok(exe_path) = std::env::current_exe() {
            if let Some(parent) = exe_path.parent() {
                let candidates = [
                    parent.join("Fonts").join(font_name),
                    parent.join("fonts").join(font_name),
                    parent.join("..").join("Fonts").join(font_name),
                    parent.join("..").join("..").join("Fonts").join(font_name),
                    parent.join("..").join("..").join("..").join("Fonts").join(font_name),
                ];
                for path in &candidates {
                    if path.exists() {
                        return Some(path.canonicalize().unwrap_or_else(|_| path.clone()));
                    }
                }
            }
        }
        // 2. 编译时路径（开发环境）
        let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
        let candidates = [
            manifest_dir.join("..").join("Fonts").join(font_name),
            manifest_dir.join("..").join("public").join("fonts").join(font_name),
        ];
        for path in &candidates {
            if path.exists() {
                return Some(path.canonicalize().unwrap_or_else(|_| path.clone()));
            }
        }
        None
    }

    pub unsafe fn load_misans_font() {
        if let Some(path) = find_misans_font_path() {
            let path_str = path.to_string_lossy().to_string();
            let wide: Vec<u16> = path_str.encode_utf16().chain(std::iter::once(0)).collect();
            let result = AddFontResourceExW(wide.as_ptr(), FR_PRIVATE, ptr::null_mut());
            if result > 0 {
                if let Ok(mut lock) = FONT_PATH.lock() {
                    *lock = Some(path_str.clone());
                }
                log::info!("overlay: 已加载字体: {}", path_str);
            } else {
                log::warn!("overlay: 加载字体失败: {} (error: {})", path_str, GetLastError());
            }
        } else {
            log::info!("overlay: 未找到 MiSansVF.ttf 字体文件，使用系统默认字体");
        }
    }

    pub unsafe fn unload_misans_font() {
        if let Ok(mut lock) = FONT_PATH.lock() {
            if let Some(path) = lock.take() {
                let wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
                RemoveFontResourceExW(wide.as_ptr(), FR_PRIVATE, ptr::null_mut());
                log::info!("overlay: 已卸载 MiSans 字体");
            }
        }
    }

    pub unsafe fn init_gdiplus() -> bool {
        let mut token = GDIPLUS_TOKEN.lock().unwrap();
        if token.is_some() {
            return true;
        }

        let mut input = GdiplusStartupInput {
            GdiplusVersion: 1,
            DebugEventCallback: 0,
            SuppressBackgroundThread: 0,
            SuppressExternalCodecs: 0,
        };

        let mut token_value: usize = 0;
        let result = GdiplusStartup(&mut token_value, &mut input, ptr::null_mut());

        if result == 0 {
            *token = Some(token_value);
            // 加载 MiSans 字体供 GDI 渲染使用
            load_misans_font();
            true
        } else {
            log::error!("GDI+ 初始化失败: {}", result);
            false
        }
    }

    pub unsafe fn shutdown_gdiplus() {
        let mut token = GDIPLUS_TOKEN.lock().unwrap();
        if let Some(t) = token.take() {
            GdiplusShutdown(t);
            unload_misans_font();
        }
    }

    fn calculate_window_width(settings: &super::OverlaySettings) -> i32 {
        // 使用可配置的单项宽度
        let normal_item_width = settings.item_width as i32;
        // 默认密码项宽度（逻辑像素）
        let mut password_item_width = 220;
        if settings.display_items.iter().any(|item| item.id == "delta_password" && item.enabled) {
            if let Ok(lock) = super::CURRENT_HARDWARE_DATA.lock() {
                if let Some(ref data) = *lock {
                    if let Some(ref pwd) = data.delta_password {
                        unsafe {
                            // 使用屏幕 DC 和字体测量文本宽度，按 DPI 缩放
                            let screen_dc = GetDC(ptr::null_mut());
                            if !screen_dc.is_null() {
                                let dpi_x = GetDeviceCaps(screen_dc, 88);
                                let dpi_scale = dpi_x as f32 / 96.0;
                                let hfont = create_compatible_font(dpi_scale, &settings.font, settings.font_size);
                                if !hfont.is_null() {
                                    let val_w = measure_text_width(screen_dc, hfont, pwd);
                                    let est = val_w + (12.0 * dpi_scale) as i32 + 20;
                                                if est > password_item_width {
                                                    password_item_width = est;
                                                }
                                    DeleteObject(hfont as _);
                                }
                                ReleaseDC(ptr::null_mut(), screen_dc);
                            }
                        }
                    }
                }
            }
        }

        let mut width = 0i32;
        let mut enabled_count = 0i32;
        for item in &settings.display_items {
            if item.enabled {
                enabled_count += 1;
                match item.id.as_str() {
                    "delta_password" => { width += password_item_width; }
                    _ => { width += normal_item_width; }
                }
            }
        }

        // 自定义项宽度（各 150px 基础宽度）
        let custom_item_width = 150;
        let mut custom_count = 0i32;
        for custom in &settings.custom_items {
            if custom.enabled && !custom.text.is_empty() {
                width += custom_item_width;
                custom_count += 1;
            }
        }

        if width == 0 { return 200; }
        enabled_count += custom_count;
        let sep_count = if enabled_count > 1 { enabled_count - 1 } else { 0 };
        width + 32 + sep_count * 16
    }

    pub unsafe fn create_overlay_window(
        settings: &super::OverlaySettings,
    ) -> Result<HWND, String> {
        init_gdiplus();

        let h_instance = GetModuleHandleW(ptr::null());
        if h_instance.is_null() {
            return Err("无法获取模块句柄".to_string());
        }

        let class_name = windows_sys::core::w!("FrameScopeOverlayPanel");

        let wnd_class = WNDCLASSW {
            style: CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc: Some(window_proc),
            cbClsExtra: 0,
            cbWndExtra: 0,
            hInstance: h_instance,
            hIcon: LoadIconW(h_instance, IDI_APPLICATION),
            hCursor: LoadCursorW(h_instance, IDC_ARROW),
            hbrBackground: CreateSolidBrush(0),
            lpszMenuName: ptr::null(),
            lpszClassName: class_name,
        };

        if RegisterClassW(&wnd_class) == 0 {
            let error = GetLastError();
            if error != 1410 {
                return Err(format!("注册窗口类失败: {}", error));
            }
        }

        let screen_dc = GetDC(ptr::null_mut());
        let dpi_x = if screen_dc.is_null() { 96 } else { GetDeviceCaps(screen_dc, 88) };
        if !screen_dc.is_null() {
            ReleaseDC(ptr::null_mut(), screen_dc);
        }
        let dpi_scale = dpi_x as f32 / 96.0;

        let logical_width = {
            // Horizontal styles lay every enabled item out in ONE row. A
            // settings set configured for the vertical panel (20+ items) used
            // to produce a window wider than the desktop (measured 7865px
            // across two monitors) - clamp to the primary screen so the bar
            // stays visible; items past the clamp simply draw off-edge until
            // the user trims the item list.
            let max_logical = ((GetSystemMetrics(SM_CXSCREEN) as f32 / dpi_scale) as i32 - 40).max(200);
            calculate_window_width(settings).min(max_logical)
        };
        let base_height = if settings.style == "dynamic_island" { 36 } else { 28 };
        let logical_height = base_height + (settings.font_size.saturating_sub(13) * 2) as i32;
        let physical_width = (logical_width as f32 * dpi_scale) as i32;
        let physical_height = (logical_height as f32 * dpi_scale) as i32;

        // 使用保存的位置，或使用默认位置
        let (x, y) = if let (Some(px), Some(py)) = (settings.position_x, settings.position_y) {
            (px, py)
        } else {
            let screen_width = GetSystemMetrics(SM_CXSCREEN);
            let default_x = (screen_width - physical_width) / 2;
            let default_y = if settings.style == "dynamic_island" { 4 } else { 0 };
            (default_x, default_y)
        };

        let hwnd = CreateWindowExW(
            WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT,
            class_name,
            windows_sys::core::w!("FrameScope Overlay"),
            WS_POPUP,
            x,
            y,
            physical_width,
            physical_height,
            ptr::null_mut(),
            ptr::null_mut(),
            h_instance,
            ptr::null_mut(),
        );

        if hwnd.is_null() {
            return Err("创建窗口失败".to_string());
        }

        ShowWindow(hwnd, SW_SHOW);
        UpdateWindow(hwnd);

        Ok(hwnd)
    }

    pub unsafe fn destroy_overlay_window(hwnd: HWND) -> bool {
        if hwnd.is_null() {
            return false;
        }
        KillTimer(hwnd, 1);
        DestroyWindow(hwnd) != 0
    }

    unsafe fn create_compatible_font(dpi_scale: f32, font_name: &str, font_size: u32) -> HFONT {
        let font_height = -((font_size as f32) * dpi_scale).round() as i32;
        let wide_name: Vec<u16> = font_name.encode_utf16().chain(std::iter::once(0)).collect();
        CreateFontW(
            font_height,
            0,
            0,
            0,
            FW_NORMAL as i32,
            0,
            0,
            0,
            DEFAULT_CHARSET as u32,
            OUT_DEFAULT_PRECIS as u32,
            CLIP_DEFAULT_PRECIS as u32,
            CLEARTYPE_QUALITY as u32,
            (DEFAULT_PITCH | FF_DONTCARE) as u32,
            wide_name.as_ptr(),
        )
    }

    unsafe fn measure_text_width(hdc: HDC, hfont: HFONT, text: &str) -> i32 {
        let old_font = SelectObject(hdc, hfont as _);
        let wide: Vec<u16> = text.encode_utf16().chain(std::iter::once(0)).collect();
        let mut size = SIZE { cx: 0, cy: 0 };
        GetTextExtentPoint32W(hdc, wide.as_ptr(), (wide.len() - 1) as i32, &mut size);
        SelectObject(hdc, old_font);
        size.cx
    }

    struct DisplayItem {
        label: String,
        value: String,
        label_width: i32,
        value_width: i32,
        total_width: i32,
        custom_color: Option<u32>,
    }

    fn parse_hex_color(hex: &str) -> u32 {
        let hex = hex.trim_start_matches('#');
        if let Ok(val) = u32::from_str_radix(hex, 16) {
            // 前端使用 #RRGGBB 格式，GDI 颜色格式为 0x00BBGGRR
            let r = (val >> 16) & 0xFF;
            let g = (val >> 8) & 0xFF;
            let b = val & 0xFF;
            (b << 16) | (g << 8) | r
        } else {
            0x00FFFFFF
        }
    }

    /// 将 GDI 颜色 0x00BBGGRR 转换为不透明直线 alpha 的 ARGB（0xFFRRGGBB），供 GDI+ 文字使用。
    fn gdi_to_argb(gdi: u32) -> u32 {
        let r = gdi & 0xFF;
        let g = (gdi >> 8) & 0xFF;
        let b = (gdi >> 16) & 0xFF;
        0xFF000000 | (r << 16) | (g << 8) | b
    }

    fn build_display_items(
        settings: &super::OverlaySettings,
        data: &super::OverlayHardwareData,
    ) -> Vec<DisplayItem> {
        let mut items = Vec::new();
        for display_item in &settings.display_items {
            if !display_item.enabled {
                continue;
            }
            match display_item.id.as_str() {
                "time" => {
                    // 优先使用网络标准时间；未同步成功时回退到北京时间（UTC+8）
                    let now = if let Some(offset_ms) = super::get_net_offset_ms() {
                        let now_ms = std::time::SystemTime::now()
                            .duration_since(std::time::UNIX_EPOCH)
                            .map(|d| d.as_millis() as i64)
                            .unwrap_or(0)
                            + offset_ms;
                        chrono::DateTime::from_timestamp_millis(now_ms)
                            .map(|dt| dt.with_timezone(&chrono::FixedOffset::east_opt(8 * 3600).unwrap()))
                    } else {
                        Some(chrono::Utc::now().with_timezone(&chrono::FixedOffset::east_opt(8 * 3600).unwrap()))
                    };
                    if let Some(now) = now {
                        items.push(DisplayItem {
                            label: String::new(),
                            value: now.format("%H:%M:%S").to_string(),
                            label_width: 0, value_width: 0, total_width: 0, custom_color: None,
                        });
                    }
                }
                "cpu_usage" => {
                    let val = data.cpu_usage.map(|v| format!("{}%", v)).unwrap_or_else(|| "--%".to_string());
                    items.push(DisplayItem { label: "CPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_temp" => {
                    let val = data.gpu_temp.map(|v| format!("{:.0}°C", v)).unwrap_or_else(|| "--°C".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_usage" => {
                    let val = data.gpu_usage.map(|v| format!("{}%", v)).unwrap_or_else(|| "--%".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "memory_usage" => {
                    let val = data.memory_usage.map(|v| format!("{}%", v.round() as i32)).unwrap_or_else(|| "--%".to_string());
                    items.push(DisplayItem { label: "RAM".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "delta_password" => {
                    let val = data.delta_password.as_deref().unwrap_or("--").to_string();
                    items.push(DisplayItem { label: "".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "game_ping" => {
                    let val = data.game_ping.map(|v| format!("{}ms", v)).unwrap_or_else(|| "--ms".to_string());
                    items.push(DisplayItem { label: "PING".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "fps" => {
                    let (val, color) = match data.fps {
                        Some(v) => {
                            let c = if v < 30 {
                                0x000000FFu32
                            } else if v < 60 {
                                0x0000FFFFu32
                            } else {
                                0x0000FF00u32
                            };
                            (format!("{}", v), Some(c))
                        }
                        None => ("--".to_string(), None),
                    };
                    items.push(DisplayItem { label: "FPS".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: color });
                }
                "fps_1low" => {
                    let (val, color) = match data.fps_1low {
                        Some(v) => {
                            let c = if v < 30 {
                                0x000000FFu32
                            } else if v < 60 {
                                0x0000FFFFu32
                            } else {
                                0x0000FF00u32
                            };
                            (format!("{}", v), Some(c))
                        }
                        None => ("--".to_string(), None),
                    };
                    items.push(DisplayItem { label: "1%".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: color });
                }
                "fps_01low" => {
                    let (val, color) = match data.fps_01low {
                        Some(v) => {
                            let c = if v < 30 {
                                0x000000FFu32
                            } else if v < 60 {
                                0x0000FFFFu32
                            } else {
                                0x0000FF00u32
                            };
                            (format!("{}", v), Some(c))
                        }
                        None => ("--".to_string(), None),
                    };
                    items.push(DisplayItem { label: "0.1%".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: color });
                }
                "cpu_fan_speed" => {
                    let val = data.cpu_fan_speed.map(|v| format!("{}RPM", v)).unwrap_or_else(|| "--RPM".to_string());
                    items.push(DisplayItem { label: "CPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_fan_speed" => {
                    let val = data.gpu_fan_speed.map(|v| format!("{}RPM", v)).unwrap_or_else(|| "--RPM".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_power" => {
                    let val = data.gpu_power.map(|v| format!("{}W", v)).unwrap_or_else(|| "--W".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_clock" => {
                    let val = data.gpu_clock.map(|v| format!("{}MHz", v)).unwrap_or_else(|| "--MHz".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_vram" => {
                    // The hardware service already reports VRAM in GB (the
                    // NexBox upstream feed was MiB, hence the old /1024 here).
                    let val = match (data.gpu_vram_used, data.gpu_vram_total) {
                        (Some(used), Some(total)) => {
                            format!("{:.1}G/{:.1}G", used as f64, total as f64)
                        }
                        _ => "--G/--G".to_string(),
                    };
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_memory_clock" => {
                    let val = data.gpu_memory_clock.map(|v| format!("{}MHz", v)).unwrap_or_else(|| "--MHz".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "gpu_voltage" => {
                    let val = data.gpu_voltage.map(|v| format!("{:.3}V", v)).unwrap_or_else(|| "--V".to_string());
                    items.push(DisplayItem { label: "GPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "cpu_temp" => {
                    let val = data.cpu_temp.map(|v| format!("{:.0}°C", v)).unwrap_or_else(|| "--°C".to_string());
                    items.push(DisplayItem { label: "CPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "cpu_clock" => {
                    let val = data.cpu_clock.map(|v| format!("{}MHz", v)).unwrap_or_else(|| "--MHz".to_string());
                    items.push(DisplayItem { label: "CPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "cpu_voltage" => {
                    let val = data.cpu_voltage.map(|v| format!("{:.3}V", v)).unwrap_or_else(|| "--V".to_string());
                    items.push(DisplayItem { label: "CPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "cpu_power" => {
                    let val = data.cpu_power.map(|v| format!("{:.1}W", v)).unwrap_or_else(|| "--W".to_string());
                    items.push(DisplayItem { label: "CPU".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                "ssd_temp" => {
                    let val = data.ssd_temp.map(|v| format!("{:.0}°C", v)).unwrap_or_else(|| "--°C".to_string());
                    items.push(DisplayItem { label: "硬盘".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: None });
                }
                _ => {}
            }
        }
        for custom in &settings.custom_items {
            if custom.enabled && !custom.text.is_empty() {
                let color = parse_hex_color(&custom.color);
                items.push(DisplayItem {
                    label: String::new(),
                    value: custom.text.clone(),
                    label_width: 0,
                    value_width: 0,
                    total_width: 0,
                    custom_color: Some(color),
                });
            }
        }
        items
    }

    unsafe fn measure_and_layout_items(
        hdc: HDC,
        hfont: HFONT,
        items: &mut [DisplayItem],
        dpi_scale: f32,
    ) -> i32 {
        let gap = (10.0 * dpi_scale) as i32;
        let mut total = 0i32;
        for item in items.iter_mut() {
            item.label_width = measure_text_width(hdc, hfont, &item.label);
            item.value_width = measure_text_width(hdc, hfont, &item.value);
            if item.label.is_empty() {
                item.total_width = item.value_width;
            } else {
                item.total_width = item.label_width + gap + item.value_width;
            }
            total += item.total_width;
        }
        total
    }

    pub unsafe fn draw_overlay_content(
        hwnd: HWND,
        settings: &super::OverlaySettings,
        data: &super::OverlayHardwareData,
    ) {
        let dpi_scale = {
            let dc = GetDC(hwnd);
            let dpi = if dc.is_null() { 96 } else { GetDeviceCaps(dc, 88) };
            if !dc.is_null() {
                ReleaseDC(hwnd, dc);
            }
            dpi as f32 / 96.0
        };

        let hfont = create_compatible_font(dpi_scale, &settings.font, settings.font_size);
        if hfont.is_null() {
            eprintln!("[osd-draw] create_compatible_font failed");
            return;
        }

        let font_color = parse_hex_color(&settings.font_color);
        let temp_dc = GetDC(ptr::null_mut());
        let mut items = build_display_items(settings, data);
        let padding = (16.0 * dpi_scale) as i32;
        let item_gap = (16.0 * dpi_scale) as i32;
        let content_width = measure_and_layout_items(temp_dc, hfont, &mut items, dpi_scale);
        ReleaseDC(ptr::null_mut(), temp_dc);
        let sep_count = if items.len() > 1 { items.len() as i32 - 1 } else { 0 };
        let total_content_width = content_width + sep_count * item_gap + padding * 2;
        let logical_height = 28 + (settings.font_size.saturating_sub(13) * 2) as i32;
        let physical_height = (logical_height as f32 * dpi_scale) as i32;

        let dib_width = total_content_width;
        let dib_height = physical_height;

        let screen_dc = GetDC(ptr::null_mut());
        let mut bmi: BITMAPINFO = std::mem::zeroed();
        bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmiHeader.biWidth = dib_width;
        bmi.bmiHeader.biHeight = -dib_height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        let mut bits: *mut std::ffi::c_void = ptr::null_mut();
        let hbitmap = CreateDIBSection(screen_dc, &bmi, DIB_RGB_COLORS, &mut bits, ptr::null_mut(), 0);
        ReleaseDC(ptr::null_mut(), screen_dc);

        if hbitmap.is_null() {
            DeleteObject(hfont as _);
            return;
        }

        let mem_dc = CreateCompatibleDC(ptr::null_mut());
        let old_bmp = SelectObject(mem_dc, hbitmap as HGDIOBJ);

        let mut graphics: *mut GpGraphics = ptr::null_mut();
        if GdipCreateFromHDC(mem_dc, &mut graphics) != 0 {
            SelectObject(mem_dc, old_bmp);
            DeleteObject(hbitmap as HGDIOBJ);
            DeleteDC(mem_dc);
            DeleteObject(hfont as _);
            return;
        }

        GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);

        let mut clear_brush: *mut GpSolidFill = ptr::null_mut();
        GdipCreateSolidFill(0x00000001, &mut clear_brush);
        GdipFillRectangle(graphics, clear_brush as *mut GpBrush, 0.0, 0.0, dib_width as f32, dib_height as f32);
        GdipDeleteBrush(clear_brush as *mut GpBrush);

        // opacity>0 时才填充背景：opacity=0 时跳过，避免 GDI+ 预乘 alpha 把背景 rgb 归零，
        // 与纯黑文字 rgb=0 混淆（否则背景会被误判为文字而变不透明）。
        if settings.opacity > 0 {
            let bg_argb: u32 = ((settings.opacity as u32) << 24) | 0x00111111;
            let mut bg_brush: *mut GpSolidFill = ptr::null_mut();
            GdipCreateSolidFill(bg_argb, &mut bg_brush);
            GdipFillRectangle(graphics, bg_brush as *mut GpBrush, 0.0, 0.0, dib_width as f32, dib_height as f32);
            GdipDeleteBrush(bg_brush as *mut GpBrush);
        }
        GdipDeleteGraphics(graphics);

        let old_font = SelectObject(mem_dc, hfont as _);
        SetBkMode(mem_dc, TRANSPARENT as i32);

        let gap = (10.0 * dpi_scale) as i32;
        let mut current_x: i32 = padding;
        let win_height_i32 = dib_height;

        for (i, item) in items.iter().enumerate() {
            if i > 0 {
                current_x += item_gap;
            }

            if !item.label.is_empty() {
                let wide_label: Vec<u16> = item.label.encode_utf16().chain(std::iter::once(0)).collect();
                let mut label_rect = RECT {
                    left: current_x,
                    top: 0,
                    right: current_x + item.label_width,
                    bottom: win_height_i32,
                };
                SetTextColor(mem_dc, font_color);
                DrawTextW(
                    mem_dc,
                    wide_label.as_ptr(),
                    (wide_label.len() - 1) as i32,
                    &mut label_rect,
                    DT_RIGHT | DT_VCENTER | DT_SINGLELINE,
                );
            }

            let value_x = if item.label.is_empty() {
                current_x
            } else {
                current_x + item.label_width + gap
            };
            let wide_value: Vec<u16> = item.value.encode_utf16().chain(std::iter::once(0)).collect();
            let mut value_rect = RECT {
                left: value_x,
                top: 0,
                right: value_x + item.value_width,
                bottom: win_height_i32,
            };

            let mut color: u32 = font_color;
            if let Some(custom_color) = item.custom_color {
                color = custom_color;
            } else if !item.label.is_empty() && !item.value.contains("--")
                && (item.value.contains("°C") || item.value.contains('%') || item.value.bytes().all(|b| b.is_ascii_digit()))
            {
                let mut num_str = String::new();
                for ch in item.value.chars() {
                    if ch.is_ascii_digit() || ch == '.' {
                        num_str.push(ch);
                    } else if !num_str.is_empty() {
                        break;
                    }
                }
                if !num_str.is_empty() {
                    if let Ok(nf) = num_str.parse::<f32>() {
                        let nv = nf as i32;
                        if nv < 50 {
                            color = 0x0000FF00;
                        } else if nv < 80 {
                            color = 0x0000FFFF;
                        } else {
                            color = 0x000000FF;
                        }
                    }
                }
            }

            SetTextColor(mem_dc, color);
            DrawTextW(
                mem_dc,
                wide_value.as_ptr(),
                (wide_value.len() - 1) as i32,
                &mut value_rect,
                DT_LEFT | DT_VCENTER | DT_SINGLELINE,
            );

            current_x += item.total_width;
        }

        SelectObject(mem_dc, old_font);

        if !bits.is_null() {
            let pixels = std::slice::from_raw_parts_mut(
                bits as *mut u32,
                (dib_width * dib_height) as usize,
            );
            for pixel in pixels.iter_mut() {
                let alpha = (*pixel >> 24) & 0xFF;
                let rgb = *pixel & 0x00FFFFFF;
                // GDI 文字像素 alpha=0，清屏基准色为 0x000001 → rgb 为 0x000001 是空白、其余均为 GDI 文字
                // 旧逻辑 rgb != 0 漏掉了纯黑文字（rgb=0），这里用 rgb != 0x000001 同时覆盖黑字和彩色字
                if alpha == 0 && rgb != 0x000001 {
                    *pixel = 0xFF000000 | rgb;
                }
            }
        }

        let screen_width = GetSystemMetrics(SM_CXSCREEN);
        let default_x = (screen_width - dib_width) / 2;
        let use_x = settings.position_x.unwrap_or(default_x);
        let use_y = settings.position_y.unwrap_or(0);

        let ppt_dst = POINT { x: use_x, y: use_y };
        let psize = SIZE { cx: dib_width, cy: dib_height };
        let ppt_src = POINT { x: 0, y: 0 };
        let blend = BLENDFUNCTION {
            BlendOp: AC_SRC_OVER as u8,
            BlendFlags: 0,
            SourceConstantAlpha: 255,
            AlphaFormat: AC_SRC_ALPHA as u8,
        };

        UpdateLayeredWindow(
            hwnd,
            ptr::null_mut(),
            &ppt_dst,
            &psize,
            mem_dc,
            &ppt_src,
            0,
            &blend,
            ULW_ALPHA,
        );

        SelectObject(mem_dc, old_bmp);
        DeleteObject(hbitmap as HGDIOBJ);
        DeleteDC(mem_dc);
        DeleteObject(hfont as _);
    }

    pub unsafe fn draw_overlay_content_dynamic_island(
        hwnd: HWND,
        settings: &super::OverlaySettings,
        data: &super::OverlayHardwareData,
    ) {
        let dpi_scale = {
            let dc = GetDC(hwnd);
            let dpi = if dc.is_null() { 96 } else { GetDeviceCaps(dc, 88) };
            if !dc.is_null() {
                ReleaseDC(hwnd, dc);
            }
            dpi as f32 / 96.0
        };

        let hfont = create_compatible_font(dpi_scale, &settings.font, settings.font_size);
        if hfont.is_null() {
            eprintln!("[osd-island] create_compatible_font failed");
            return;
        }

        let font_color = parse_hex_color(&settings.font_color);
        let padding = (16.0 * dpi_scale) as i32;
        let item_gap = (16.0 * dpi_scale) as i32;
        let text_gap = (10.0 * dpi_scale) as i32;

        // 用 GDI+ 测量文字宽度（与绘制 GdipDrawString 同一文字引擎），
        // 避免测宽与绘制度量不一致导致文字被裁剪/换行。字体句柄复用给绘制。
        let mut font_handle: *mut GpFont = ptr::null_mut();
        let (dib_width, dib_height, layout_items);
        {
            let temp_dc = GetDC(ptr::null_mut());
            let old_temp_font = SelectObject(temp_dc, hfont as _);
            let mut layout_items_tmp = build_display_items(settings, data);

            let mut mg: *mut GpGraphics = ptr::null_mut();
            let gdiplus_ok = !temp_dc.is_null()
                && GdipCreateFromHDC(temp_dc, &mut mg) == 0 && !mg.is_null()
                && GdipCreateFontFromDC(temp_dc, &mut font_handle) == 0 && !font_handle.is_null();

            let sep_count = if layout_items_tmp.len() > 1 { layout_items_tmp.len() as i32 - 1 } else { 0 };
            let mut content_width: i32 = -1;
            if gdiplus_ok {
                let mut fmt: *mut GpStringFormat = ptr::null_mut();
                if GdipCreateStringFormat(StringFormatFlagsNoWrap as i32, 0, &mut fmt) == 0 && !fmt.is_null() {
                    let mut c = 0i32;
                    for item in layout_items_tmp.iter_mut() {
                        let measure_w = |s: &str| -> i32 {
                            if s.is_empty() {
                                return 0;
                            }
                            let wide: Vec<u16> = s.encode_utf16().chain(std::iter::once(0)).collect();
                            let arect = RectF { X: 0.0, Y: 0.0, Width: 100000.0, Height: 100000.0 };
                            let mut brect = RectF { X: 0.0, Y: 0.0, Width: 0.0, Height: 0.0 };
                            if GdipMeasureString(
                                mg,
                                wide.as_ptr(),
                                (wide.len() - 1) as i32,
                                font_handle,
                                &arect,
                                fmt,
                                &mut brect,
                                ptr::null_mut(),
                                ptr::null_mut(),
                            ) != 0 {
                                return 0;
                            }
                            brect.Width.ceil() as i32
                        };
                        item.label_width = measure_w(&item.label);
                        item.value_width = measure_w(&item.value);
                        item.total_width = if item.label.is_empty() {
                            item.value_width
                        } else {
                            item.label_width + text_gap + item.value_width
                        };
                        c += item.total_width;
                    }
                    content_width = c;
                    // 记录布局有效，供后续 DIB 宽度计算
                    GdipDeleteStringFormat(fmt);
                }
            }

            // GDI+ 测量失败时退回 GDI 测量（保持兼容）
            let content_width = if content_width >= 0 {
                content_width
            } else {
                measure_and_layout_items(temp_dc, hfont, &mut layout_items_tmp, dpi_scale)
            };

            let logical_height = 36 + (settings.font_size.saturating_sub(13) * 2) as i32;
            let tmp_dib_width = content_width + sep_count * item_gap + padding * 2;
            let tmp_dib_height = (logical_height as f32 * dpi_scale) as i32;

            SelectObject(temp_dc, old_temp_font);
            ReleaseDC(ptr::null_mut(), temp_dc);
            if !mg.is_null() {
                GdipDeleteGraphics(mg);
            }

            dib_width = tmp_dib_width.max(2);
            dib_height = tmp_dib_height.max(2);
            layout_items = layout_items_tmp;
        }

        // --- Create 32-bit ARGB DIB section (like crosshair) ---
        let screen_dc = GetDC(ptr::null_mut());
        let mut bmi: BITMAPINFO = std::mem::zeroed();
        bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmiHeader.biWidth = dib_width;
        bmi.bmiHeader.biHeight = -dib_height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        let mut bits: *mut std::ffi::c_void = ptr::null_mut();
        let hbitmap = CreateDIBSection(screen_dc, &bmi, DIB_RGB_COLORS, &mut bits, ptr::null_mut(), 0);
        ReleaseDC(ptr::null_mut(), screen_dc);

        if hbitmap.is_null() {
            DeleteObject(hfont as _);
            return;
        }

        let mem_dc = CreateCompatibleDC(ptr::null_mut());
        let old_bmp = SelectObject(mem_dc, hbitmap as HGDIOBJ);

        // --- GDI+ anti-aliased rounded rect background ---
        let mut graphics: *mut GpGraphics = ptr::null_mut();
        if GdipCreateFromHDC(mem_dc, &mut graphics) != 0 {
            SelectObject(mem_dc, old_bmp);
            DeleteObject(hbitmap as HGDIOBJ);
            DeleteDC(mem_dc);
            DeleteObject(hfont as _);
            return;
        }

        GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);

        // Clear to fully transparent（alpha=0）。GDI+ 按预乘 alpha 写入，透明区 rgb 归零，
        // 圆角外区域保持全透明，配合末尾逆预乘不会再出现黑边/不透明圆角。
        let mut clear_brush: *mut GpSolidFill = ptr::null_mut();
        GdipCreateSolidFill(0x00000000, &mut clear_brush);
        GdipFillRectangle(graphics, clear_brush as *mut GpBrush, 0.0, 0.0, dib_width as f32, dib_height as f32);
        GdipDeleteBrush(clear_brush as *mut GpBrush);

        // Draw rounded rect with GDI+ (proper per-pixel alpha anti-aliasing)。
        // 半透明胶囊背景，alpha 由 opacity 决定；最终统一逆预乘输出直线 alpha。
        if settings.opacity > 0 {
            let bg_argb: u32 = ((settings.opacity as u32) << 24) | 0x00111111;
            let corner_r = dib_height as f32 * 0.5;
            let mut bg_brush: *mut GpSolidFill = ptr::null_mut();
            GdipCreateSolidFill(bg_argb, &mut bg_brush);

            let mut path: *mut GpPath = ptr::null_mut();
            GdipCreatePath(FillModeAlternate, &mut path);
            if !path.is_null() {
                let w = dib_width as f32;
                let h = dib_height as f32;
                let r = corner_r;
                GdipAddPathArc(path, 0.0, 0.0, r * 2.0, r * 2.0, 180.0, 90.0);
                GdipAddPathLine(path, r, 0.0, w - r, 0.0);
                GdipAddPathArc(path, w - r * 2.0, 0.0, r * 2.0, r * 2.0, 270.0, 90.0);
                GdipAddPathLine(path, w, r, w, h - r);
                GdipAddPathArc(path, w - r * 2.0, h - r * 2.0, r * 2.0, r * 2.0, 0.0, 90.0);
                GdipAddPathLine(path, w - r, h, r, h);
                GdipAddPathArc(path, 0.0, h - r * 2.0, r * 2.0, r * 2.0, 90.0, 90.0);
                GdipAddPathLine(path, 0.0, h - r, 0.0, r);
                GdipClosePathFigure(path);
                GdipFillPath(graphics, bg_brush as *mut GpBrush, path);
                GdipDeletePath(path);
            }
            GdipDeleteBrush(bg_brush as *mut GpBrush);
        }

        // --- 文字使用 GDI+ 绘制（自带正确 alpha，可支持纯黑文字，不再依赖 rgb 哨兵）---
        // 字体复用当前 DC 中已选中的兼容字体，保证字号/字体与旧实现一致。
        let old_font = SelectObject(mem_dc, hfont as _);
        // font_handle 已在测量阶段创建，复用同一句柄保证绘制度量一致
        if !font_handle.is_null() {
            let mut string_format: *mut GpStringFormat = ptr::null_mut();
            // StringFormatFlagsNoWrap：禁止自动换行，文字始终单行显示（对齐旧 GDI DT_SINGLELINE 行为）
            if GdipCreateStringFormat(StringFormatFlagsNoWrap as i32, 0, &mut string_format) == 0 && !string_format.is_null() {
                // 灰度反锯齿：避免 ClearType 彩色边缘在透明分层窗口产生色边
                GdipSetTextRenderingHint(graphics, TextRenderingHintAntiAliasGridFit);
                GdipSetStringFormatLineAlign(string_format, StringAlignmentCenter);

                let gap = text_gap;
                let mut current_x: i32 = padding;

                for (i, item) in layout_items.iter().enumerate() {
                    if i > 0 {
                        current_x += item_gap;
                    }

                    // label：右对齐
                    if !item.label.is_empty() {
                        let mut brush_handle: *mut GpSolidFill = ptr::null_mut();
                        if GdipCreateSolidFill(gdi_to_argb(font_color), &mut brush_handle) == 0 && !brush_handle.is_null() {
                            let rect_f = RectF {
                                X: current_x as f32,
                                Y: 0.0,
                                Width: item.label_width as f32,
                                Height: dib_height as f32,
                            };
                            GdipSetStringFormatAlign(string_format, StringAlignmentFar);
                            let wide: Vec<u16> = item.label.encode_utf16().chain(std::iter::once(0)).collect();
                            GdipDrawString(
                                graphics,
                                wide.as_ptr(),
                                (wide.len() - 1) as i32,
                                font_handle,
                                &rect_f,
                                string_format,
                                brush_handle as *mut GpBrush,
                            );
                            GdipDeleteBrush(brush_handle as *mut GpBrush);
                        }
                    }

                    // value：左对齐
                    let value_x = if item.label.is_empty() {
                        current_x
                    } else {
                        current_x + item.label_width + gap
                    };

                    let mut color: u32 = font_color;
                    if let Some(custom_color) = item.custom_color {
                        color = custom_color;
                    } else if !item.label.is_empty() && !item.value.contains("--")
                        && (item.value.contains("°C") || item.value.contains('%') || item.value.bytes().all(|b| b.is_ascii_digit()))
                    {
                        let mut num_str = String::new();
                        for ch in item.value.chars() {
                            if ch.is_ascii_digit() || ch == '.' {
                                num_str.push(ch);
                            } else if !num_str.is_empty() {
                                break;
                            }
                        }
                        if !num_str.is_empty() {
                            if let Ok(nf) = num_str.parse::<f32>() {
                                let nv = nf as i32;
                                if nv < 50 {
                                    color = 0x0000FF00;
                                } else if nv < 80 {
                                    color = 0x0000FFFF;
                                } else {
                                    color = 0x000000FF;
                                }
                            }
                        }
                    }

                    let mut brush_handle: *mut GpSolidFill = ptr::null_mut();
                    if GdipCreateSolidFill(gdi_to_argb(color), &mut brush_handle) == 0 && !brush_handle.is_null() {
                        let rect_f = RectF {
                            X: value_x as f32,
                            Y: 0.0,
                            Width: item.value_width as f32,
                            Height: dib_height as f32,
                        };
                        GdipSetStringFormatAlign(string_format, StringAlignmentNear);
                        let wide: Vec<u16> = item.value.encode_utf16().chain(std::iter::once(0)).collect();
                        GdipDrawString(
                            graphics,
                            wide.as_ptr(),
                            (wide.len() - 1) as i32,
                            font_handle,
                            &rect_f,
                            string_format,
                            brush_handle as *mut GpBrush,
                        );
                        GdipDeleteBrush(brush_handle as *mut GpBrush);
                    }

                    current_x += item.total_width;
                }
                GdipDeleteStringFormat(string_format);
            }
        }
        SelectObject(mem_dc, old_font);

        GdipDeleteGraphics(graphics);
        if !font_handle.is_null() {
            GdipDeleteFont(font_handle);
        }

        // 对半透明像素做 alpha 逆预乘：GDI+ 按预乘 alpha 写入，
        // 而 UpdateLayeredWindow(AC_SRC_ALPHA) 需要直线 alpha，否则半透明边缘会偏暗、发黑。
        // alpha==0（透明/圆角外）与 alpha==255（纯不透明）保持不动。
        if !bits.is_null() {
            let pixels = std::slice::from_raw_parts_mut(
                bits as *mut u32,
                (dib_width * dib_height) as usize,
            );
            for pixel in pixels.iter_mut() {
                let v = *pixel;
                let a = (v >> 24) & 0xFF;
                if a > 0 && a < 255 {
                    let ar = a as u32;
                    let r = ((v >> 16) & 0xFF) as u32 * 255 / ar;
                    let g = ((v >> 8) & 0xFF) as u32 * 255 / ar;
                    let b = (v & 0xFF) as u32 * 255 / ar;
                    *pixel = (a as u32) << 24 | (r.clamp(0, 255) << 16) | (g.clamp(0, 255) << 8) | b.clamp(0, 255);
                }
            }
        }

        // --- Position and composite via UpdateLayeredWindow ---
        let screen_width = GetSystemMetrics(SM_CXSCREEN);
        let default_x = (screen_width - dib_width) / 2;
        let use_x = settings.position_x.unwrap_or(default_x);
        let use_y = settings.position_y.unwrap_or(4);

        let ppt_dst = POINT { x: use_x, y: use_y };
        let psize = SIZE { cx: dib_width, cy: dib_height };
        let ppt_src = POINT { x: 0, y: 0 };
        let blend = BLENDFUNCTION {
            BlendOp: AC_SRC_OVER as u8,
            BlendFlags: 0,
            SourceConstantAlpha: 255,
            AlphaFormat: AC_SRC_ALPHA as u8,
        };

        let ulw_ret = UpdateLayeredWindow(
            hwnd,
            ptr::null_mut(),
            &ppt_dst,
            &psize,
            mem_dc,
            &ppt_src,
            0,
            &blend,
            ULW_ALPHA,
        );
        if ulw_ret == 0 {
            eprintln!("[osd-island] UpdateLayeredWindow failed err={} size={}x{}", GetLastError(), psize.cx, psize.cy);
        }

        SelectObject(mem_dc, old_bmp);
        DeleteObject(hbitmap as HGDIOBJ);
        DeleteDC(mem_dc);
        DeleteObject(hfont as _);
    }

    /// One full redraw from the current settings + hardware snapshot. Shared
    /// by the 500ms timer and the WM_APP hot-repaint path.
    unsafe fn repaint_overlay(hwnd: HWND) {
        let data = super::collect_hardware_data();
        *super::CURRENT_HARDWARE_DATA.lock().unwrap() = Some(data.clone());
        let settings = super::get_or_init_settings();
        if settings.style == "dynamic_island" {
            draw_overlay_content_dynamic_island(hwnd, &settings, &data);
        } else {
            draw_overlay_content(hwnd, &settings, &data);
        }
    }

    pub unsafe extern "system" fn window_proc(
        hwnd: HWND,
        msg: u32,
        wparam: WPARAM,
        lparam: LPARAM,
    ) -> LRESULT {
        match msg {
            WM_PAINT => {
                let mut ps = PAINTSTRUCT {
                    hdc: ptr::null_mut(),
                    fErase: 0,
                    rcPaint: RECT {
                        left: 0,
                        top: 0,
                        right: 0,
                        bottom: 0,
                    },
                    fRestore: 0,
                    fIncUpdate: 0,
                    rgbReserved: [0u8; 32],
                };
                BeginPaint(hwnd, &mut ps);
                EndPaint(hwnd, &ps);
                0
            }
            WM_TIMER => {
                // 定时器 0.5s 刷新
                SetTimer(hwnd, 1, 500, None);
                repaint_overlay(hwnd);
                0
            }
            msg if msg == super::OVERLAY_WM_REPAINT => {
                // Hot settings apply (opacity slider on GDI styles): redraw
                // with the just-stored settings, no window teardown.
                repaint_overlay(hwnd);
                0
            }
            WM_NCHITTEST => {
                // 拖动模式下返回 HTCAPTION 允许拖动
                if super::DRAG_MODE.load(std::sync::atomic::Ordering::SeqCst) {
                    HTCAPTION as LRESULT
                } else {
                    DefWindowProcW(hwnd, msg, wparam, lparam)
                }
            }
            WM_EXITSIZEMOVE => {
                // 拖动结束后只保存位置，不退出拖动模式
                // 退出由前端按钮控制，避免样式切换导致位置重置
                if super::DRAG_MODE.load(std::sync::atomic::Ordering::SeqCst) {
                    // 获取当前窗口位置
                    let mut rect = RECT { left: 0, top: 0, right: 0, bottom: 0 };
                    GetWindowRect(hwnd, &mut rect);

                    // 保存位置到设置
                    {
                        let mut settings_lock = super::CURRENT_SETTINGS.lock().unwrap();
                        if let Some(ref mut settings) = *settings_lock {
                            settings.position_x = Some(rect.left);
                            settings.position_y = Some(rect.top);
                        }
                    }

                    // 标记位置已变更
                    super::POSITION_CHANGED.store(true, std::sync::atomic::Ordering::SeqCst);
                }
                0
            }
            WM_DESTROY => {
                PostQuitMessage(0);
                0
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }
}

// 设置拖动模式
pub fn set_drag_mode(enabled: bool) {
    DRAG_MODE.store(enabled, Ordering::SeqCst);

    #[cfg(target_os = "windows")]
    unsafe {
        let hwnd = OVERLAY_HANDLE.load(Ordering::SeqCst);
        if !hwnd.is_null() {
            use windows_sys::Win32::UI::WindowsAndMessaging::*;

            // 获取当前窗口样式
            let ex_style = GetWindowLongW(hwnd, GWL_EXSTYLE) as u32;

            if enabled {
                // 进入拖动模式：移除 WS_EX_TRANSPARENT
                let new_style = ex_style & !WS_EX_TRANSPARENT;
                SetWindowLongW(hwnd, GWL_EXSTYLE, new_style as i32);
            } else {
                // 退出拖动模式：恢复 WS_EX_TRANSPARENT
                let new_style = ex_style | WS_EX_TRANSPARENT;
                SetWindowLongW(hwnd, GWL_EXSTYLE, new_style as i32);
            }

            // 刷新窗口样式
            SetWindowPos(
                hwnd,
                HWND_TOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED,
            );
        }
    }
}

#[cfg(target_os = "windows")]
pub fn start_overlay(settings: OverlaySettings) -> Result<OverlayResult, String> {
    use windows_sys::Win32::UI::WindowsAndMessaging::*;

    // swap, not load+store: the check-then-act let two concurrent starts
    // both pass the gate and double-create the overlay window.
    if OVERLAY_ACTIVE.swap(true, Ordering::SeqCst) {
        return Ok(OverlayResult {
            success: true,
            message: "悬浮框已处于启用状态".to_string(),
        });
    }

    {
        let mut settings_lock = CURRENT_SETTINGS.lock().unwrap();
        *settings_lock = Some(settings.clone());
    }

    let (ready_tx, ready_rx) = std::sync::mpsc::sync_channel(1);
    let worker = thread::spawn(move || {
        unsafe {
            match win32::create_overlay_window(&settings) {
                std::result::Result::Ok(hwnd) => {
                    OVERLAY_HANDLE.store(hwnd, Ordering::SeqCst);
                    let _ = ready_tx.send(Ok(()));

                    // OWN the data before drawing: the inline
                    // `CURRENT_HARDWARE_DATA.lock().clone()` used to keep the
                    // MutexGuard alive until the END of the draw call, and
                    // build_display_items' "time" branch re-locks the same
                    // mutex via get_net_offset_ms() - a same-thread
                    // non-reentrant deadlock that froze the whole overlay
                    // operation lock whenever the GDI styles started with the
                    // clock item enabled.
                    let data_snapshot = CURRENT_HARDWARE_DATA.lock().unwrap().clone().unwrap_or_default();
                    if settings.style == "dynamic_island" {
                        win32::draw_overlay_content_dynamic_island(hwnd, &settings, &data_snapshot);
                    } else {
                        win32::draw_overlay_content(hwnd, &settings, &data_snapshot);
                    }

                    SetTimer(hwnd, 1, 500, None);

                    let mut msg: MSG = std::mem::zeroed();
                    // Labeled so WM_QUIT leaves the whole pump: breaking only
                    // the inner PeekMessage loop used to spin the outer loop
                    // every 50ms after a quit request.
                    'pump: while OVERLAY_ACTIVE.load(Ordering::SeqCst) {
                        while PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, PM_REMOVE) != 0 {
                            if msg.message == WM_QUIT {
                                break 'pump;
                            }
                            TranslateMessage(&msg);
                            DispatchMessageW(&msg);
                        }

                        if !OVERLAY_ACTIVE.load(Ordering::SeqCst) {
                            break;
                        }

                        thread::sleep(Duration::from_millis(50));
                    }

                    win32::destroy_overlay_window(hwnd);
                    OVERLAY_HANDLE.store(std::ptr::null_mut(), Ordering::SeqCst);
                }
                std::result::Result::Err(e) => {
                    log::error!("创建悬浮框窗口失败: {}", e);
                    let _ = ready_tx.send(Err(e.to_string()));
                    OVERLAY_ACTIVE.store(false, Ordering::SeqCst);
                }
            }
        }

    });
    *OVERLAY_THREAD.lock().unwrap() = Some(worker);
    match ready_rx.recv_timeout(Duration::from_secs(3)) {
        Ok(result) => result?,
        Err(error) => { let _ = stop_overlay(); return Err(error.to_string()); }
    }

    Ok(OverlayResult {
        success: true,
        message: "悬浮框已启动".to_string(),
    })
}

#[cfg(target_os = "windows")]
pub fn stop_overlay() -> Result<OverlayResult, String> {
    use windows_sys::Win32::UI::WindowsAndMessaging::PostMessageW;
    use windows_sys::Win32::UI::WindowsAndMessaging::WM_CLOSE;

    if !OVERLAY_ACTIVE.load(Ordering::SeqCst) {
        return Ok(OverlayResult {
            success: true,
            message: "悬浮框已处于关闭状态".to_string(),
        });
    }

    OVERLAY_ACTIVE.store(false, Ordering::SeqCst);

    unsafe {
        let hwnd = OVERLAY_HANDLE.load(Ordering::SeqCst);
        if !hwnd.is_null() {
            PostMessageW(hwnd, WM_CLOSE, 0, 0);
        }
    }
    if let Some(worker) = OVERLAY_THREAD.lock().unwrap().take() { let _ = worker.join(); }

    Ok(OverlayResult {
        success: true,
        message: "悬浮框已关闭".to_string(),
    })
}

#[cfg(not(target_os = "windows"))]
pub fn start_overlay(_settings: OverlaySettings) -> Result<OverlayResult, String> {
    Err("此功能仅支持 Windows 系统".to_string())
}

#[cfg(not(target_os = "windows"))]
pub fn stop_overlay() -> Result<OverlayResult, String> {
    Err("此功能仅支持 Windows 系统".to_string())
}

/// Check if Win32 overlay is active.
pub fn is_overlay_active() -> bool {
    OVERLAY_ACTIVE.load(Ordering::SeqCst)
}

pub fn update_settings(settings: OverlaySettings) -> Result<(), String> {
    // Stop a running GDI panel, store the new settings, and STOP there.
    // Restarting is the caller's job (start_selected knows the NEW style):
    // this function used to auto-relaunch the GDI window, which the vertical
    // switch path then immediately stopped again (GDI->vertical flashed a
    // full teardown+rebuild) and which made every opacity-slider tick on a
    // GDI style destroy+recreate the window (visible flicker).
    let active = is_overlay_active();
    if active { stop_overlay()?; }
    *CURRENT_SETTINGS.lock().unwrap() = Some(settings);
    let _ = active;
    Ok(())
}

/// WM_APP message the overlay wndproc repaints on: lets the settings page
/// hot-apply GDI style changes (opacity slider) without tearing the window
/// down - the old update_settings rebuild path made the bar blink on every
/// slider tick.
pub const OVERLAY_WM_REPAINT: u32 = 0x8000 + 7; // WM_APP + 7

pub fn request_overlay_repaint() -> bool {
    #[cfg(target_os = "windows")]
    {
        use windows_sys::Win32::UI::WindowsAndMessaging::PostMessageW;
        let hwnd = OVERLAY_HANDLE.load(std::sync::atomic::Ordering::SeqCst);
        if hwnd.is_null() {
            return false;
        }
        unsafe { PostMessageW(hwnd, OVERLAY_WM_REPAINT, 0, 0) != 0 }
    }
    #[cfg(not(target_os = "windows"))]
    {
        false
    }
}

pub fn publish_hardware_data(data: OverlayHardwareData) {
    *CURRENT_HARDWARE_DATA.lock().unwrap() = Some(data);
}

