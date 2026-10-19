//! Distinguish a user's native move loop from display/DPI/programmatic moves.
use crate::{overlay_display, overlay_panel, overlay_position};
use std::sync::atomic::{AtomicBool, Ordering};
use windows_sys::Win32::{
    Foundation::*,
    UI::{Shell::*, WindowsAndMessaging::*},
};

static MOVING: AtomicBool = AtomicBool::new(false);
static DIRTY: AtomicBool = AtomicBool::new(false);
static LAYOUT_READY: AtomicBool = AtomicBool::new(false);
const SUBCLASS_ID: usize = 0x465350;

pub fn prepare() {
    LAYOUT_READY.store(false, Ordering::SeqCst);
}

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    id: usize,
    _: usize,
) -> LRESULT {
    match message {
        WM_ENTERSIZEMOVE => {
            MOVING.store(true, Ordering::SeqCst);
        }
        WM_EXITSIZEMOVE => {
            let mut rect: RECT = std::mem::zeroed();
            if GetWindowRect(hwnd, &mut rect) != 0 {
                let display = overlay_display::resolve(
                    (
                        rect.left + (rect.right - rect.left) / 2,
                        rect.top + (rect.bottom - rect.top) / 2,
                    ),
                    None,
                );
                crate::record_vertical_position(
                    tauri::PhysicalPosition::new(rect.left, rect.top),
                    display.reference(rect.right - rect.left, rect.bottom - rect.top),
                );
                DIRTY.store(true, Ordering::SeqCst);
            }
            MOVING.store(false, Ordering::SeqCst);
        }
        WM_NCDESTROY => {
            RemoveWindowSubclass(hwnd, Some(window_proc), id);
            MOVING.store(false, Ordering::SeqCst);
            LAYOUT_READY.store(false, Ordering::SeqCst);
        }
        _ => {}
    }
    DefSubclassProc(hwnd, message, wparam, lparam)
}

pub fn install(window: &tauri::WebviewWindow) -> Result<(), String> {
    let (send, receive) = std::sync::mpsc::sync_channel(1);
    let owned = window.clone();
    window
        .run_on_main_thread(move || {
            let result = owned
                .hwnd()
                .map_err(|e| e.to_string())
                .and_then(|hwnd| unsafe {
                    if SetWindowSubclass(hwnd.0 as _, Some(window_proc), SUBCLASS_ID, 0) == 0 {
                        Err("无法监听竖排悬浮窗拖动".to_string())
                    } else {
                        Ok(())
                    }
                });
            let _ = send.send(result);
        })
        .map_err(|e| e.to_string())?;
    receive
        .recv_timeout(std::time::Duration::from_secs(2))
        .map_err(|e| e.to_string())?
}

pub fn layout_ready() {
    LAYOUT_READY.store(true, Ordering::SeqCst);
}
pub fn persist_pending(app: &tauri::AppHandle) {
    if DIRTY.swap(false, Ordering::SeqCst) {
        if let Err(error) = crate::save_native_settings(app) {
            DIRTY.store(true, Ordering::SeqCst);
            log::warn!("保存竖排悬浮窗位置失败: {error}");
        }
    }
}

/// Called on the window's owning thread, serial with its manual move messages.
pub unsafe fn reconcile(hwnd: HWND) {
    if MOVING.load(Ordering::SeqCst) {
        return;
    }
    let mut rect: RECT = std::mem::zeroed();
    if GetWindowRect(hwnd, &mut rect) == 0 {
        return;
    }
    let mut state = overlay_panel::CURRENT_SETTINGS
        .lock()
        .unwrap_or_else(|p| p.into_inner());
    let Some(settings) = state.as_mut() else {
        return;
    };
    let (Some(x), Some(y)) = (settings.vertical_position_x, settings.vertical_position_y) else {
        return;
    };
    let display = overlay_display::resolve((x, y), settings.vertical_position_reference.as_ref());
    let size = (rect.right - rect.left, rect.bottom - rect.top);
    if settings.vertical_position_reference.is_none()
        && LAYOUT_READY.load(Ordering::SeqCst)
        && overlay_position::can_adopt((x, y), display.bounds(), size)
    {
        settings.vertical_position_reference = Some(display.reference(size.0, size.1));
        DIRTY.store(true, Ordering::SeqCst);
    }
    let target = overlay_position::fit(
        (x, y),
        settings.vertical_position_reference.as_ref(),
        display.bounds(),
        size,
    );
    drop(state);
    if target != (rect.left, rect.top) {
        SetWindowPos(
            hwnd,
            std::ptr::null_mut(),
            target.0,
            target.1,
            0,
            0,
            SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE,
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn native_manual_move_commits_but_programmatic_move_and_destruction_do_not() {
        let _guard = overlay_panel::POSITION_TEST_LOCK.lock().unwrap();
        unsafe {
            // Hidden standalone HWND: exercise real comctl32 messages without
            // starting the application, a WebView, or changing display modes.
            let hwnd = CreateWindowExW(
                0,
                windows_sys::core::w!("STATIC"),
                windows_sys::core::w!("FrameScope position regression"),
                WS_POPUP,
                100,
                100,
                200,
                120,
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                windows_sys::Win32::System::LibraryLoader::GetModuleHandleW(std::ptr::null()),
                std::ptr::null(),
            );
            assert!(!hwnd.is_null());
            assert_ne!(
                SetWindowSubclass(hwnd, Some(window_proc), SUBCLASS_ID, 0),
                0
            );
            let before = overlay_panel::CURRENT_SETTINGS.lock().unwrap().clone();
            let mut settings = overlay_panel::OverlaySettings::default();
            settings.vertical_position_x = Some(100);
            settings.vertical_position_y = Some(100);
            *overlay_panel::CURRENT_SETTINGS.lock().unwrap() = Some(settings);
            DIRTY.store(false, Ordering::SeqCst);
            SetWindowPos(
                hwnd,
                std::ptr::null_mut(),
                140,
                160,
                0,
                0,
                SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE,
            );
            assert_eq!(
                overlay_panel::get_or_init_settings().vertical_position_x,
                Some(100)
            );
            assert!(!DIRTY.load(Ordering::SeqCst));
            SendMessageW(hwnd, WM_ENTERSIZEMOVE, 0, 0);
            assert!(MOVING.load(Ordering::SeqCst));
            SetWindowPos(
                hwnd,
                std::ptr::null_mut(),
                220,
                240,
                0,
                0,
                SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE,
            );
            reconcile(hwnd); // timer must never fight a live user move loop
            let mut rect: RECT = std::mem::zeroed();
            GetWindowRect(hwnd, &mut rect);
            assert_eq!((rect.left, rect.top), (220, 240));
            SendMessageW(hwnd, WM_EXITSIZEMOVE, 0, 0);
            let saved = overlay_panel::get_or_init_settings();
            assert_eq!(
                (saved.vertical_position_x, saved.vertical_position_y),
                (Some(220), Some(240))
            );
            let reference = saved.vertical_position_reference.clone().unwrap();
            assert_eq!(
                (reference.window_width, reference.window_height),
                (200, 120)
            );
            assert!(!reference.monitor.is_empty());
            assert!(DIRTY.load(Ordering::SeqCst));
            assert!(!MOVING.load(Ordering::SeqCst));
            SetWindowPos(
                hwnd,
                std::ptr::null_mut(),
                300,
                320,
                0,
                0,
                SWP_NOZORDER | SWP_NOSIZE | SWP_NOACTIVATE,
            );
            reconcile(hwnd);
            GetWindowRect(hwnd, &mut rect);
            assert_eq!((rect.left, rect.top), (220, 240));
            assert_eq!(
                overlay_panel::get_or_init_settings().vertical_position_reference,
                Some(reference)
            );
            DestroyWindow(hwnd);
            assert!(!MOVING.load(Ordering::SeqCst));
            assert_eq!(
                overlay_panel::get_or_init_settings().vertical_position_x,
                Some(220)
            );
            *overlay_panel::CURRENT_SETTINGS.lock().unwrap() = before;
            DIRTY.store(false, Ordering::SeqCst);
        }
    }
}
