use windows_sys::Win32::{
    Foundation::HWND,
    System::LibraryLoader::GetModuleHandleW,
    UI::{
        HiDpi::{GetDpiForWindow, GetSystemMetricsForDpi},
        WindowsAndMessaging::{
            LoadImageW, SendMessageW, ICON_BIG, ICON_SMALL,
            IMAGE_ICON, LR_SHARED, SM_CXICON, SM_CXSMICON, SM_CYICON, SM_CYSMICON, WM_SETICON,
        },
    },
};

// Tauri decodes only the first ICO frame. Let Windows select both native
// window icon sizes from the complete executable resource instead.
pub fn apply(hwnd: HWND) -> std::io::Result<()> {
    unsafe {
        let module = GetModuleHandleW(std::ptr::null());
        let dpi = GetDpiForWindow(hwnd);
        for (kind, x, y) in [
            (ICON_SMALL, SM_CXSMICON, SM_CYSMICON),
            (ICON_BIG, SM_CXICON, SM_CYICON),
        ] {
            // tauri-winres uses 32512 for the application icon group.
            let icon = LoadImageW(
                module,
                32512usize as *const u16,
                IMAGE_ICON,
                GetSystemMetricsForDpi(x, dpi),
                GetSystemMetricsForDpi(y, dpi),
                LR_SHARED,
            );
            if icon.is_null() {
                return Err(std::io::Error::last_os_error());
            }
            SendMessageW(hwnd, WM_SETICON, kind as usize, icon as isize);
        }
    }
    Ok(())
}
