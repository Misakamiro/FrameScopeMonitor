mod installer;

use installer::*;

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .invoke_handler(tauri::generate_handler![
            get_state,
            get_default_install_path,
            get_app_version,
            get_launch_mode,
            check_disk_space,
            check_webview2,
            is_app_running_cmd,
            install,
            uninstall,
            launch_app,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

/// Silent / headless entry points, dispatched from main() before any window
/// is created. NSIS-compatible flags: `/S` silent, `/D=path` install dir.
pub enum HeadlessAction {
    None,
    Install { dir: Option<String>, desktop_shortcut: bool, autostart: bool },
    Uninstall { delete_data: bool },
}

pub fn parse_headless_args() -> HeadlessAction {
    let args: Vec<String> = std::env::args().collect();
    let uninstall = args.iter().any(|a| a == "--uninstall");
    let silent = args.iter().any(|a| a.eq_ignore_ascii_case("/S") || a.eq_ignore_ascii_case("/SILENT"));
    let dir = args
        .iter()
        .find(|a| a.len() > 3 && a[..2].eq_ignore_ascii_case("/D"))
        .map(|a| a[3..].trim_matches('"').to_string());

    if uninstall {
        if silent {
            return HeadlessAction::Uninstall { delete_data: false };
        }
        // `--uninstall` without /S runs the uninstall UI, not headless.
        return HeadlessAction::None;
    }
    if silent {
        let default_dir = {
            // Mirror get_default_install_path without touching the registry
            // twice; reuse it via a plain computation.
            let pf = std::env::var("ProgramFiles").unwrap_or_else(|_| "C:\\Program Files".into());
            format!("{pf}\\{PRODUCT_NAME}")
        };
        return HeadlessAction::Install {
            dir: Some(dir.unwrap_or(default_dir)),
            desktop_shortcut: false,
            autostart: false,
        };
    }
    HeadlessAction::None
}

pub fn run_headless(action: HeadlessAction) -> i32 {
    let result = match action {
        HeadlessAction::Install { dir, desktop_shortcut, autostart } => {
            let dir = dir.unwrap_or_else(get_default_install_path);
            install_headless(dir, desktop_shortcut, autostart)
        }
        HeadlessAction::Uninstall { delete_data } => uninstall_headless(delete_data),
        HeadlessAction::None => return 0,
    };
    match result {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("framescope-installer: {e}");
            1
        }
    }
}
