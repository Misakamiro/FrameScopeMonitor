mod installer;
mod install_transaction;
mod installation_system;
mod self_relaunch;
mod update_install;

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
    Update {dir:String,receipt:String},
    Restore {dir:String},
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
    if uninstall && args.iter().any(|a| a=="--update"||a=="--restore-previous") {return HeadlessAction::Invalid("Conflicting installer modes".into());}
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
    let update=args.iter().any(|a|a=="--update");
    let restore=args.iter().any(|a|a=="--restore-previous");
    if update||restore {
        if !silent||update&&restore{return HeadlessAction::Invalid("Update/recovery requires /S and one explicit mode".into());}
        let Some(dir)=dir.filter(|p|std::path::Path::new(p).is_absolute())else{return HeadlessAction::Invalid("Update/recovery requires /D=<current absolute installation directory>".into());};
        if restore {return HeadlessAction::Restore{dir};}
        let receipts:Vec<_>=args.iter().filter_map(|a|a.strip_prefix("--receipt=")).collect();
        if receipts.len()!=1||!std::path::Path::new(receipts[0]).is_absolute(){return HeadlessAction::Invalid("Update requires one absolute receipt path".into());}
        return HeadlessAction::Update{dir,receipt:receipts[0].into()};
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
    fn update_and_recovery_require_explicit_target_and_unambiguous_mode(){
        for args in [vec!["--update","/S"],vec!["--update","/S","/D=C:\\FrameScope"],vec!["--restore-previous","/S","/D=relative"],vec!["--uninstall","--update","/S"],vec!["--restore-previous","--update","/S","/D=C:\\FrameScope"]]{assert!(matches!(parse(&args),HeadlessAction::Invalid(_)));}
        assert!(matches!(parse(&["--update","/S","/D=C:\\FrameScope","--receipt=C:\\Cache\\receipt.json"]),HeadlessAction::Update{..}));
        assert!(matches!(parse(&["--restore-previous","/S","/D=C:\\FrameScope"]),HeadlessAction::Restore{..}));
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
    let update_target=match &action {HeadlessAction::Update{dir,..}|HeadlessAction::Restore{dir}=>Some(std::path::PathBuf::from(dir)),_=>None};
    let result = match action {
        HeadlessAction::Update{dir,receipt}=>update_install::perform_update(std::path::Path::new(&dir),std::path::Path::new(&receipt)),
        HeadlessAction::Restore{dir}=>update_install::restore_previous(std::path::Path::new(&dir)),
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
            if let Some(target)=update_target {if target.is_dir()&&crate::installer::validate_install_target(&target).is_ok(){
                let _=framescope_update::atomic_json(&target.join("FrameScopeUpdateResult.json"),&serde_json::json!({"success":false,"error":e}));
            }}
            eprintln!("framescope-installer: {e}");
            1
        }
    }
}
