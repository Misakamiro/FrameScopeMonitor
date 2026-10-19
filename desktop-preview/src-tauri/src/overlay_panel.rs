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
#[cfg(test)]
pub(crate) static POSITION_TEST_LOCK: Mutex<()> = Mutex::new(());

/// Takes (clears) the drag-moved flag; true means CURRENT_SETTINGS now holds
/// a window position that has never been persisted.
pub fn take_position_changed() -> bool {
    POSITION_CHANGED.swap(false, std::sync::atomic::Ordering::SeqCst)
}
static OVERLAY_THREAD: Mutex<Option<thread::JoinHandle<()>>> = Mutex::new(None);
#[cfg(feature = "uiaccess-acceptance")]
static RENDER_DIAGNOSTICS: Mutex<Option<serde_json::Value>> = Mutex::new(None);
#[cfg(feature = "uiaccess-acceptance")]
pub fn render_diagnostics() -> serde_json::Value {
    RENDER_DIAGNOSTICS.lock().unwrap_or_else(|p|p.into_inner()).clone().unwrap_or(serde_json::Value::Null)
}

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
    #[serde(default)]
    pub position_reference: Option<crate::overlay_position::PositionReference>,
    /// 竖排悬浮框独立位置（与 Win32 悬浮框的 position_x/y 解耦，互不影响）
    #[serde(default)]
    pub vertical_position_x: Option<i32>,
    #[serde(default)]
    pub vertical_position_y: Option<i32>,
    #[serde(default)]
    pub vertical_position_reference: Option<crate::overlay_position::PositionReference>,
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
            position_reference: None,
            vertical_position_x: None,
            vertical_position_y: None,
            vertical_position_reference: None,
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
    // Low metrics are fractional FPS values. Keeping them as integers here
    // silently truncated the service result before it reached the native OSD.
    fps_1low: Option<f64>,
    fps_01low: Option<f64>,
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
    /// 目标进程呈现模式（"composited"/"independent-flip"/"legacy-exclusive"）。
    /// GDI 单行形态不渲染它，但字段必须存在——deny_unknown_fields 会拒绝
    /// 服务快照里的未知键，缺了整个解码就炸。
    pub present_mode: Option<String>,
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
            present_mode: None,
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
    #[cfg(test)]
    mod tests {
        include!("overlay_panel_tests.rs");
    }

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
    static IN_SIZE_MOVE: super::AtomicBool = super::AtomicBool::new(false);

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

    unsafe fn monitor_work_area(settings: &super::OverlaySettings) -> RECT {
        crate::overlay_display::resolve((settings.position_x.unwrap_or(0),settings.position_y.unwrap_or(0)),
            settings.position_reference.as_ref()).area
    }

    fn fitted_position(settings: &super::OverlaySettings, area: RECT, width: i32, height: i32) -> POINT {
        let point = (settings.position_x.unwrap_or(area.left + (area.right - area.left - width) / 2),
            settings.position_y.unwrap_or(area.top + if settings.style == "dynamic_island" { 4 } else { 0 }));
        let (x,y) = crate::overlay_position::fit(point, settings.position_reference.as_ref(),
            (area.left,area.top,area.right-area.left,area.bottom-area.top),(width,height));
        POINT { x, y }
    }

    fn remember_position_reference(settings: &super::OverlaySettings, area: RECT, width: i32, height: i32) {
        let (Some(x),Some(y))=(settings.position_x,settings.position_y) else { return; };
        if settings.position_reference.is_some() || !crate::overlay_position::can_adopt((x,y),
            (area.left,area.top,area.right-area.left,area.bottom-area.top),(width,height)) { return; }
        let display=crate::overlay_display::resolve((x,y),None);
        if let Some(current)=super::CURRENT_SETTINGS.lock().unwrap().as_mut() {
            if current.position_x==Some(x) && current.position_y==Some(y) && current.position_reference.is_none() {
                current.position_reference=Some(display.reference(width,height));
                super::POSITION_CHANGED.store(true,super::Ordering::SeqCst);
            }
        }
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

        // The first measured repaint supplies the real dimensions.
        let area = monitor_work_area(settings);
        let base_height = if settings.style == "dynamic_island" { 36 } else { 28 };
        let logical_height = base_height + (settings.font_size.saturating_sub(13) * 2) as i32;
        let physical_width = ((200.0 * dpi_scale) as i32).min(area.right - area.left).max(1);
        let physical_height = ((logical_height as f32 * dpi_scale) as i32).min(area.bottom - area.top).max(1);
        let point = fitted_position(settings, area, physical_width, physical_height);

        let hwnd = CreateWindowExW(
            // WS_EX_NOACTIVATE：OSD 永不抢焦点（配合 WS_EX_TRANSPARENT），游戏中
            // 点击/切窗不会把焦点从游戏偷走
            WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT,
            class_name,
            windows_sys::core::w!("FrameScope Overlay"),
            WS_POPUP,
            point.x,
            point.y,
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
                            let c = if v < 30.0 {
                                0x000000FFu32
                            } else if v < 60.0 {
                                0x0000FFFFu32
                            } else {
                                0x0000FF00u32
                            };
                            (format!("{v:.1}"), Some(c))
                        }
                        None => ("--".to_string(), None),
                    };
                    items.push(DisplayItem { label: "1%".to_string(), value: val, label_width: 0, value_width: 0, total_width: 0, custom_color: color });
                }
                "fps_01low" => {
                    let (val, color) = match data.fps_01low {
                        Some(v) => {
                            let c = if v < 30.0 {
                                0x000000FFu32
                            } else if v < 60.0 {
                                0x0000FFFFu32
                            } else {
                                0x0000FF00u32
                            };
                            (format!("{v:.1}"), Some(c))
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

    struct ItemLayout {
        items: Vec<DisplayItem>,
        positions: Vec<(i32, i32)>,
        width: i32,
        height: i32,
        row_height: i32,
    }

    fn item_color(item: &DisplayItem, font_color: u32) -> u32 {
        if let Some(color) = item.custom_color {
            return color;
        }
        if !item.label.is_empty() && !item.value.contains("--")
            && (item.value.contains("°C") || item.value.contains('%') || item.value.bytes().all(|b| b.is_ascii_digit()))
        {
            let number: String = item.value.chars()
                .skip_while(|ch| !ch.is_ascii_digit() && *ch != '.')
                .take_while(|ch| ch.is_ascii_digit() || *ch == '.')
                .collect();
            if let Ok(value) = number.parse::<f32>() {
                return if value < 50.0 { 0x0000FF00 } else if value < 80.0 { 0x0000FFFF } else { 0x000000FF };
            }
        }
        font_color
    }

    fn fitting_prefix(text: &str, width: i32, measure: &impl Fn(&str) -> i32) -> Result<usize, String> {
        let mut end = 0;
        for (offset, ch) in text.char_indices() {
            let next = offset + ch.len_utf8();
            if measure(&text[..next]) > width {
                break;
            }
            end = next;
        }
        if end == 0 && !text.is_empty() {
            Err(format!("OSD glyph cannot fit available width {width} at the configured font size"))
        } else {
            Ok(end)
        }
    }

    // Keep a metric intact unless the metric alone is wider than a whole row.
    fn wrap_item(
        mut item: DisplayItem, width: i32, text_gap: i32,
        font_color: u32, measure: &impl Fn(&str) -> i32,
    ) -> Result<Vec<DisplayItem>, String> {
        if item.total_width <= width {
            return Ok(vec![item]);
        }
        item.custom_color = Some(item_color(&item, font_color));
        let mut parts = Vec::new();
        let mut label = item.label.as_str();
        let mut value = item.value.as_str();
        while !label.is_empty() || !value.is_empty() {
            let label_end = fitting_prefix(label, width, measure)?;
            let label_part = &label[..label_end];
            let label_width = measure(label_part);
            let gap = if label_part.is_empty() { 0 } else { text_gap };
            let remaining = width - label_width - gap;
            let value_end = if label_end == label.len() {
                fitting_prefix(value, remaining, measure).unwrap_or(0)
            } else { 0 };
            if label_part.is_empty() && value_end == 0 {
                return Err(format!("OSD value cannot fit available width {width} at the configured font size"));
            }
            let value_part = &value[..value_end];
            let value_width = measure(value_part);
            parts.push(DisplayItem {
                label: label_part.to_string(), value: value_part.to_string(),
                label_width, value_width,
                total_width: label_width + value_width + if value_part.is_empty() { 0 } else { gap },
                custom_color: item.custom_color,
            });
            label = &label[label_end..];
            value = &value[value_end..];
        }
        Ok(parts)
    }

    fn layout_measured_items(
        items: Vec<DisplayItem>, area: RECT, dpi_scale: f32, row_height: i32,
        font_color: u32, measure: impl Fn(&str) -> i32,
    ) -> Result<ItemLayout, String> {
        let padding = (16.0 * dpi_scale) as i32;
        let item_gap = (16.0 * dpi_scale) as i32;
        let text_gap = (10.0 * dpi_scale) as i32;
        let content_width = area.right - area.left - 2 * padding;
        if content_width <= 0 || row_height <= 0 {
            return Err("OSD work area has no room for text and padding".into());
        }
        let mut result = ItemLayout {
            items: Vec::new(), positions: Vec::new(),
            width: padding * 2, height: row_height, row_height,
        };
        let mut x = padding;
        let mut y = 0;
        for item in items {
            let oversized = item.total_width > content_width;
            let parts = wrap_item(item, content_width, text_gap, font_color, &measure)?;
            for (index, part) in parts.into_iter().enumerate() {
                if x > padding && (oversized || x + item_gap + part.total_width > padding + content_width)
                    || index > 0
                {
                    x = padding;
                    y += row_height;
                } else if x > padding {
                    x += item_gap;
                }
                result.positions.push((x, y));
                x += part.total_width;
                result.width = result.width.max(x + padding);
                result.items.push(part);
            }
        }
        result.height = y + row_height;
        if result.height > area.bottom - area.top {
            return Err(format!("OSD needs {}px height, work area has {}px; content was not clipped",
                result.height, area.bottom - area.top));
        }
        Ok(result)
    }

    unsafe fn measured_row_height(hdc: HDC, hfont: HFONT, dpi_scale: f32, island: bool) -> i32 {
        let old = SelectObject(hdc, hfont as _);
        let mut metrics: TEXTMETRICW = std::mem::zeroed();
        GetTextMetricsW(hdc, &mut metrics);
        SelectObject(hdc, old);
        let base = if island { 36.0 } else { 28.0 };
        (metrics.tmHeight + (4.0 * dpi_scale) as i32).max((base * dpi_scale) as i32)
    }

    unsafe fn measure_gdiplus_text(graphics: *mut GpGraphics, font: *mut GpFont, format: *mut GpStringFormat, text: &str) -> i32 {
        if text.is_empty() {
            return 0;
        }
        let wide: Vec<u16> = text.encode_utf16().collect();
        let area = RectF { X: 0.0, Y: 0.0, Width: 100000.0, Height: 100000.0 };
        let mut bounds: RectF = std::mem::zeroed();
        if GdipMeasureString(graphics, wide.as_ptr(), wide.len() as i32, font, &area, format,
            &mut bounds, ptr::null_mut(), ptr::null_mut()) != 0
        {
            return 0;
        }
        bounds.Width.ceil() as i32
    }

    #[cfg(feature = "uiaccess-acceptance")]
    unsafe fn record_render(hwnd: HWND, bits: *mut std::ffi::c_void, width: i32, height: i32, settings: &super::OverlaySettings, success: bool) {
        if bits.is_null() { return; }
        let pixels = std::slice::from_raw_parts(bits as *const u32, (width * height) as usize);
        let mut histogram = std::collections::BTreeMap::<u32,usize>::new();
        let mut invalid = 0;
        for pixel in pixels {
            *histogram.entry(*pixel).or_default() += 1;
            let alpha = pixel >> 24;
            if ((pixel >> 16) & 255) > alpha || ((pixel >> 8) & 255) > alpha || (pixel & 255) > alpha { invalid += 1; }
        }
        let dominant = histogram.iter().max_by_key(|(_,count)|*count).map(|(pixel,_)|*pixel).unwrap_or(0);
        *super::RENDER_DIAGNOSTICS.lock().unwrap_or_else(|p|p.into_inner()) = Some(serde_json::json!({
            "hwnd":hwnd as usize,"style":settings.style,"opacity":settings.opacity,"width":width,"height":height,
            "updated":success,"dominant_pixel":dominant,"invalid_premultiplied_pixels":invalid,
            "transparent_pixels":pixels.iter().filter(|pixel|**pixel >> 24 == 0).count(),"pixels":pixels.len()}));
    }

    fn repair_gdi_text_alpha(pixels: &mut [u32]) {
        for pixel in pixels.iter_mut() {
            let alpha = (*pixel >> 24) & 0xFF;
            let rgb = *pixel & 0x00FFFFFF;
            if alpha == 0 { *pixel = if rgb == 0x000001 { 0 } else { 0xFF000000 | rgb }; }
        }
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
        measure_and_layout_items(temp_dc, hfont, &mut items, dpi_scale);
        let area = monitor_work_area(settings);
        let row_height = measured_row_height(temp_dc, hfont, dpi_scale, false);
        let layout = layout_measured_items(items, area, dpi_scale, row_height, font_color,
            |text| measure_text_width(temp_dc, hfont, text));
        ReleaseDC(ptr::null_mut(), temp_dc);
        let layout = match layout {
            Ok(layout) => layout,
            Err(error) => {
                log::error!("{error}");
                DeleteObject(hfont as _);
                return;
            }
        };
        let dib_width = layout.width;
        let dib_height = layout.height;

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

        // An alpha-zero GDI+ brush does not initialize the DIB. Write the
        // transparent sentinel directly so blank pixels are not mistaken for text.
        if !bits.is_null() {
            std::slice::from_raw_parts_mut(bits as *mut u32, (dib_width * dib_height) as usize).fill(0x00000001);
        }

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
        for (item, &(current_x, current_y)) in layout.items.iter().zip(&layout.positions) {

            if !item.label.is_empty() {
                let wide_label: Vec<u16> = item.label.encode_utf16().chain(std::iter::once(0)).collect();
                let mut label_rect = RECT {
                    left: current_x,
                    top: current_y,
                    right: current_x + item.label_width,
                    bottom: current_y + layout.row_height,
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
                top: current_y,
                right: value_x + item.value_width,
                bottom: current_y + layout.row_height,
            };

            SetTextColor(mem_dc, item_color(item, font_color));
            DrawTextW(
                mem_dc,
                wide_value.as_ptr(),
                (wide_value.len() - 1) as i32,
                &mut value_rect,
                DT_LEFT | DT_VCENTER | DT_SINGLELINE,
            );
        }

        SelectObject(mem_dc, old_font);

        if !bits.is_null() {
            let pixels = std::slice::from_raw_parts_mut(
                bits as *mut u32,
                (dib_width * dib_height) as usize,
            );
            repair_gdi_text_alpha(pixels);
        }

        let ppt_dst = fitted_position(settings, area, dib_width, dib_height);
        let psize = SIZE { cx: dib_width, cy: dib_height };
        let ppt_src = POINT { x: 0, y: 0 };
        let blend = BLENDFUNCTION {
            BlendOp: AC_SRC_OVER as u8,
            BlendFlags: 0,
            SourceConstantAlpha: 255,
            AlphaFormat: AC_SRC_ALPHA as u8,
        };

        let updated = UpdateLayeredWindow(
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
        #[cfg(feature = "uiaccess-acceptance")]
        record_render(hwnd,bits,dib_width,dib_height,settings,updated != 0);
        if updated != 0 {
            remember_position_reference(settings, area, dib_width, dib_height);
        }

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
        let text_gap = (10.0 * dpi_scale) as i32;
        let area = monitor_work_area(settings);

        // 用 GDI+ 测量文字宽度（与绘制 GdipDrawString 同一文字引擎），
        // 避免测宽与绘制度量不一致导致文字被裁剪/换行。字体句柄复用给绘制。
        let mut font_handle: *mut GpFont = ptr::null_mut();
        let layout;
        {
            let temp_dc = GetDC(ptr::null_mut());
            let old_temp_font = SelectObject(temp_dc, hfont as _);
            let mut layout_items_tmp = build_display_items(settings, data);

            let mut mg: *mut GpGraphics = ptr::null_mut();
            let gdiplus_ok = !temp_dc.is_null()
                && GdipCreateFromHDC(temp_dc, &mut mg) == 0 && !mg.is_null()
                && GdipCreateFontFromDC(temp_dc, &mut font_handle) == 0 && !font_handle.is_null();

            let mut fmt: *mut GpStringFormat = ptr::null_mut();
            let mut measured = false;
            if gdiplus_ok {
                if GdipCreateStringFormat(StringFormatFlagsNoWrap as i32, 0, &mut fmt) == 0 && !fmt.is_null() {
                    GdipSetTextRenderingHint(mg, TextRenderingHintAntiAliasGridFit);
                    for item in layout_items_tmp.iter_mut() {
                        let measure_w = |s: &str| measure_gdiplus_text(mg, font_handle, fmt, s);
                        item.label_width = measure_w(&item.label);
                        item.value_width = measure_w(&item.value);
                        item.total_width = if item.label.is_empty() {
                            item.value_width
                        } else {
                            item.label_width + text_gap + item.value_width
                        };
                    }
                    measured = true;
                }
            }

            // GDI+ 测量失败时退回 GDI 测量（保持兼容）
            if !measured {
                measure_and_layout_items(temp_dc, hfont, &mut layout_items_tmp, dpi_scale);
            }
            let row_height = measured_row_height(temp_dc, hfont, dpi_scale, true);
            layout = layout_measured_items(layout_items_tmp, area, dpi_scale, row_height, font_color,
                |text| if measured { measure_gdiplus_text(mg, font_handle, fmt, text) }
                    else { measure_text_width(temp_dc, hfont, text) });
            if !fmt.is_null() {
                GdipDeleteStringFormat(fmt);
            }

            SelectObject(temp_dc, old_temp_font);
            ReleaseDC(ptr::null_mut(), temp_dc);
            if !mg.is_null() {
                GdipDeleteGraphics(mg);
            }
        }
        let layout = match layout {
            Ok(layout) => layout,
            Err(error) => {
                log::error!("{error}");
                if !font_handle.is_null() { GdipDeleteFont(font_handle); }
                DeleteObject(hfont as _);
                return;
            }
        };
        let dib_width = layout.width;
        let dib_height = layout.height;

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
            if !font_handle.is_null() { GdipDeleteFont(font_handle); }
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
            if !font_handle.is_null() { GdipDeleteFont(font_handle); }
            DeleteObject(hfont as _);
            return;
        }

        GdipSetSmoothingMode(graphics, SmoothingModeAntiAlias);

        // Clear to fully transparent（alpha=0）。GDI+ 按预乘 alpha 写入，透明区 rgb 归零，
        // 圆角外区域保持全透明，配合末尾逆预乘不会再出现黑边/不透明圆角。
        if !bits.is_null() {
            std::slice::from_raw_parts_mut(bits as *mut u32, (dib_width * dib_height) as usize).fill(0);
        }

        // Draw rounded rect with GDI+ (proper per-pixel alpha anti-aliasing)。
        // 半透明胶囊背景，alpha 由 opacity 决定；最终统一逆预乘输出直线 alpha。
        if settings.opacity > 0 {
            let bg_argb: u32 = ((settings.opacity as u32) << 24) | 0x00111111;
            let corner_r = layout.row_height.min(dib_width) as f32 * 0.5;
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
                for (item, &(current_x, current_y)) in layout.items.iter().zip(&layout.positions) {

                    // label：右对齐
                    if !item.label.is_empty() {
                        let mut brush_handle: *mut GpSolidFill = ptr::null_mut();
                        if GdipCreateSolidFill(gdi_to_argb(font_color), &mut brush_handle) == 0 && !brush_handle.is_null() {
                            let rect_f = RectF {
                                X: current_x as f32,
                                Y: current_y as f32,
                                Width: item.label_width as f32,
                                Height: layout.row_height as f32,
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

                    let color = item_color(item, font_color);

                    let mut brush_handle: *mut GpSolidFill = ptr::null_mut();
                    if GdipCreateSolidFill(gdi_to_argb(color), &mut brush_handle) == 0 && !brush_handle.is_null() {
                        let rect_f = RectF {
                            X: value_x as f32,
                            Y: current_y as f32,
                            Width: item.value_width as f32,
                            Height: layout.row_height as f32,
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
                }
                GdipDeleteStringFormat(string_format);
            }
        }
        SelectObject(mem_dc, old_font);

        GdipDeleteGraphics(graphics);
        if !font_handle.is_null() {
            GdipDeleteFont(font_handle);
        }

        // GDI+ already writes premultiplied BGRA, as required by
        // UpdateLayeredWindow(AC_SRC_ALPHA). Keep the translucent pixels intact.

        // --- Position and composite via UpdateLayeredWindow ---
        let ppt_dst = fitted_position(settings, area, dib_width, dib_height);
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
        #[cfg(feature = "uiaccess-acceptance")]
        record_render(hwnd,bits,dib_width,dib_height,settings,ulw_ret != 0);
        if ulw_ret == 0 {
            eprintln!("[osd-island] UpdateLayeredWindow failed err={} size={}x{}", GetLastError(), psize.cx, psize.cy);
        } else {
            remember_position_reference(settings, area, dib_width, dib_height);
        }

        SelectObject(mem_dc, old_bmp);
        DeleteObject(hbitmap as HGDIOBJ);
        DeleteDC(mem_dc);
        DeleteObject(hfont as _);
    }

    /// One full redraw from the current settings + hardware snapshot. Shared
    /// by the 500ms timer and the WM_APP hot-repaint path.
    unsafe fn repaint_overlay(hwnd: HWND) {
        if IN_SIZE_MOVE.load(super::Ordering::SeqCst) {
            return;
        }
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
                // 定时器 250ms 刷新（读数 4Hz，人眼足够；GDI 重绘是 CPU 大头，
                // 不跟宿主 10Hz 轮询跑）
                SetTimer(hwnd, 1, 250, None);
                // 问题 6 方案 A：周期性重申 TOPMOST。窗口创建时的 WS_EX_TOPMOST
                // 在游戏抢占前台/SetFullscreenState 后可能被永久压到下层——flip
                // model（独立翻转）下 DWM 仍在链路中，TOPMOST 本该有效；「Z 序
                // 从不重申」是独占全屏下 OSD 不显示的主因。SWP_NOACTIVATE 保证
                // 不从游戏手里抢焦点。
                SetWindowPos(
                    hwnd,
                    HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
                );
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
            WM_ENTERSIZEMOVE => {
                IN_SIZE_MOVE.store(true, super::Ordering::SeqCst);
                0
            }
            WM_EXITSIZEMOVE => {
                // 拖动结束后只保存位置，不退出拖动模式
                // 退出由前端按钮控制，避免样式切换导致位置重置
                if super::DRAG_MODE.load(std::sync::atomic::Ordering::SeqCst) {
                    // 获取当前窗口位置
                    let mut rect = RECT { left: 0, top: 0, right: 0, bottom: 0 };
                    if GetWindowRect(hwnd, &mut rect) == 0 {
                        IN_SIZE_MOVE.store(false, super::Ordering::SeqCst);
                        return 0;
                    }
                    let display=crate::overlay_display::resolve((rect.left+(rect.right-rect.left)/2,rect.top+(rect.bottom-rect.top)/2),None);

                    // 保存位置到设置
                    {
                        let mut settings_lock = super::CURRENT_SETTINGS.lock().unwrap();
                        if let Some(ref mut settings) = *settings_lock {
                            settings.position_x = Some(rect.left);
                            settings.position_y = Some(rect.top);
                            settings.position_reference=Some(display.reference(rect.right-rect.left,rect.bottom-rect.top));
                        }
                    }

                    // 标记位置已变更
                    super::POSITION_CHANGED.store(true, std::sync::atomic::Ordering::SeqCst);
                }
                IN_SIZE_MOVE.store(false, super::Ordering::SeqCst);
                repaint_overlay(hwnd);
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

                    SetTimer(hwnd, 1, 250, None);

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

/// Store new settings WITHOUT touching any running window. Companion to
/// request_overlay_repaint: the hot path must persist the new values into
/// CURRENT_SETTINGS *before* posting the repaint, or the wndproc redraws
/// with the stale ones (measured: opacity slider did nothing).
pub fn store_settings(settings: OverlaySettings) {
    *CURRENT_SETTINGS.lock().unwrap() = Some(settings);
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
