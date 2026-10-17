mod installer;
mod install_transaction;
mod installation_system;
mod self_relaunch;

pub fn bootstrap_relaunch() -> Result<bool, String> { self_relaunch::bootstrap() }
pub fn validated_relaunch_root() -> Option<std::path::PathBuf> { self_relaunch::original_root() }
pub fn cleanup_relaunch_copy() -> Result<(), String> { self_relaunch::schedule_cleanup() }
#[cfg(feature = "transaction-validation")]
pub mod transaction_validation;

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
#[derive(Debug, PartialEq)]
pub enum HeadlessAction {
    None,
    Invalid(String),
    Install { dir: Option<String>, desktop_shortcut: bool, autostart: bool },
    Uninstall { delete_data: bool },
}

pub fn parse_headless_args() -> HeadlessAction {
    let args: Vec<String> = std::env::args().collect();
    parse_headless_args_from(&args, get_default_install_path)
}

fn parse_headless_args_from(args: &[String], default_dir: impl FnOnce() -> String) -> HeadlessAction {
    let uninstall = args.iter().any(|a| a == "--uninstall");
    let silent = args.iter().any(|a| a.eq_ignore_ascii_case("/S") || a.eq_ignore_ascii_case("/SILENT"));
    let mut dir = None;
    for arg in args.iter().skip(1) {
        if arg.get(..2).is_some_and(|prefix| prefix.eq_ignore_ascii_case("/D")) {
            if dir.is_some() || !arg.get(..3).is_some_and(|prefix| prefix.eq_ignore_ascii_case("/D=")) {
                return HeadlessAction::Invalid("Expected exactly one /D=<absolute installation directory>".into());
            }
            let value = arg[3..].trim_matches('"');
            if value.trim().is_empty() {
                return HeadlessAction::Invalid("Installation directory cannot be empty".into());
            }
            dir = Some(value.to_string());
        }
    }

    if uninstall {
        if silent {
            return HeadlessAction::Uninstall { delete_data: false };
        }
        // `--uninstall` without /S runs the uninstall UI, not headless.
        return HeadlessAction::None;
    }
    if silent {
        return HeadlessAction::Install {
            dir: Some(dir.unwrap_or_else(default_dir)),
            desktop_shortcut: false,
            autostart: false,
        };
    }
    HeadlessAction::None
}

#[cfg(test)]
mod headless_tests {
    use super::*;

    fn parse(arguments: &[&str]) -> HeadlessAction {
        let args = std::iter::once("installer.exe").chain(arguments.iter().copied())
            .map(str::to_string).collect::<Vec<_>>();
        parse_headless_args_from(&args, || r"C:\Custom Installation".into())
    }

    #[test]
    fn unicode_arguments_do_not_panic() {
        assert_eq!(parse(&["中文参数"]), HeadlessAction::None);
    }

    #[test]
    fn directory_flag_requires_equals_and_nonempty_value() {
        for invalid in ["/Dwrong", "/D", "/D=", "/D=\"\"", "/D:wrong"] {
            assert!(!matches!(parse(&["/S", invalid]), HeadlessAction::Install { .. }), "{invalid}");
        }
    }

    #[test]
    fn silent_default_uses_existing_custom_install_directory() {
        assert_eq!(parse(&["/S"]), HeadlessAction::Install {
            dir: Some(r"C:\Custom Installation".into()), desktop_shortcut: false, autostart: false,
        });
    }

    #[test]
    fn explicit_directory_and_silent_uninstall_keep_data() {
        assert_eq!(parse(&["/silent", "/d=C:\\Test Path"]), HeadlessAction::Install {
            dir: Some(r"C:\Test Path".into()), desktop_shortcut: false, autostart: false,
        });
        assert_eq!(parse(&["--uninstall", "/S"]), HeadlessAction::Uninstall { delete_data: false });
        assert_eq!(parse(&["--uninstall"]), HeadlessAction::None);
        assert!(matches!(parse(&["/S", "/D=C:\\one", "/D=C:\\two"]), HeadlessAction::Invalid(_)));
    }
}

pub fn run_headless(action: HeadlessAction) -> i32 {
    let result = match action {
        HeadlessAction::Install { dir, desktop_shortcut, autostart } => {
            let dir = dir.unwrap_or_else(get_default_install_path);
            install_headless(dir, desktop_shortcut, autostart)
        }
        HeadlessAction::Uninstall { delete_data } => uninstall_headless(delete_data),
        HeadlessAction::Invalid(error) => Err(error),
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
