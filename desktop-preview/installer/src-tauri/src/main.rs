#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    // Headless dispatch must run BEFORE the webview runtime: /S installs and
    // silent uninstalls never show a window at all.
    let action = framescope_installer_lib::parse_headless_args();
    let code = framescope_installer_lib::run_headless(action);
    if code != 0 || std::env::args().any(|a| a.eq_ignore_ascii_case("/S") || a.eq_ignore_ascii_case("/SILENT")) {
        std::process::exit(code);
    }
    framescope_installer_lib::run()
}
