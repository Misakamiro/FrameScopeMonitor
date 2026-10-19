//! Win32 monitor lookup shared by the GDI and WebView overlays.
use crate::overlay_position::PositionReference;
use windows_sys::Win32::{
    Foundation::{BOOL, LPARAM, POINT, RECT},
    Graphics::Gdi::*,
};

pub struct Display {
    pub area: RECT,
    pub name: String,
}
impl Display {
    pub fn bounds(&self) -> (i32, i32, i32, i32) {
        (
            self.area.left,
            self.area.top,
            self.area.right - self.area.left,
            self.area.bottom - self.area.top,
        )
    }
    pub fn reference(&self, width: i32, height: i32) -> PositionReference {
        PositionReference {
            monitor: self.name.clone(),
            left: self.area.left,
            top: self.area.top,
            width: self.area.right - self.area.left,
            height: self.area.bottom - self.area.top,
            window_width: width,
            window_height: height,
        }
    }
}
unsafe fn info(monitor: HMONITOR) -> Option<Display> {
    let mut value: MONITORINFOEXW = std::mem::zeroed();
    value.monitorInfo.cbSize = std::mem::size_of::<MONITORINFOEXW>() as u32;
    if GetMonitorInfoW(monitor, &mut value.monitorInfo) == 0 {
        return None;
    }
    let end = value
        .szDevice
        .iter()
        .position(|&c| c == 0)
        .unwrap_or(value.szDevice.len());
    Some(Display {
        area: value.monitorInfo.rcWork,
        name: String::from_utf16_lossy(&value.szDevice[..end]),
    })
}
struct Search<'a> {
    name: &'a str,
    found: Option<Display>,
}
unsafe extern "system" fn find(monitor: HMONITOR, _: HDC, _: *mut RECT, data: LPARAM) -> BOOL {
    let search = &mut *(data as *mut Search<'_>);
    if let Some(display) = info(monitor) {
        if display.name == search.name {
            search.found = Some(display);
            return 0;
        }
    }
    1
}
pub fn resolve(point: (i32, i32), reference: Option<&PositionReference>) -> Display {
    unsafe {
        if let Some(reference) = reference {
            let mut search = Search {
                name: &reference.monitor,
                found: None,
            };
            EnumDisplayMonitors(
                std::ptr::null_mut(),
                std::ptr::null(),
                Some(find),
                &mut search as *mut _ as LPARAM,
            );
            if let Some(found) = search.found {
                return found;
            }
        }
        info(MonitorFromPoint(
            POINT {
                x: point.0,
                y: point.1,
            },
            MONITOR_DEFAULTTONEAREST,
        ))
        .unwrap_or_else(|| {
            use windows_sys::Win32::UI::WindowsAndMessaging::*;
            Display {
                area: RECT {
                    left: 0,
                    top: 0,
                    right: GetSystemMetrics(SM_CXSCREEN),
                    bottom: GetSystemMetrics(SM_CYSCREEN),
                },
                name: String::new(),
            }
        })
    }
}
