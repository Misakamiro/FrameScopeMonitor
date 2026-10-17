#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[cfg(not(feature = "transaction-validation"))]
fn main() {
    match framescope_installer_lib::bootstrap_relaunch() {
        Ok(true) => std::process::exit(0),
        Ok(false) => (),
        Err(error) => { eprintln!("Installer relaunch failed: {error}"); std::process::exit(1); }
    }
    // Headless dispatch must run BEFORE the webview runtime: /S installs and
    // silent uninstalls never show a window at all.
    let action = framescope_installer_lib::parse_headless_args();
    let code = framescope_installer_lib::run_headless(action);
    if code != 0 || std::env::args().any(|a| a.eq_ignore_ascii_case("/S") || a.eq_ignore_ascii_case("/SILENT")) {
        if let Err(error)=framescope_installer_lib::cleanup_relaunch_copy(){eprintln!("Temporary installer cleanup incomplete: {error}");}
        std::process::exit(code);
    }
    framescope_installer_lib::run();
    if let Err(error)=framescope_installer_lib::cleanup_relaunch_copy(){eprintln!("Temporary installer cleanup incomplete: {error}");}
}

#[cfg(feature = "transaction-validation")]
fn main() {
    eprintln!("Validation feature build cannot run the installer; use the separate transaction-validation binary");
    std::process::exit(2);
}
