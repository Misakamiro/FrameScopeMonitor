// Elevated acceptance helper. It touches only a unique TEMP fixture's logon
// task; it never enables startup for the installed FrameScope executable.
#[cfg(windows)]
fn main() {
    use framescope_update::autostart::{self, State};
    let root = std::env::temp_dir().join(format!("framescope-logon-acceptance-{}-{}", std::process::id(),
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()));
    std::fs::create_dir(&root).unwrap();
    let exe = root.join("FrameScope.exe");
    std::fs::write(&exe, b"FrameScope inert task fixture; not executable").unwrap();
    let result = (|| -> Result<(), String> {
        let initial = autostart::state(&exe)?;
        if initial.exists { return Err("unexpected fixture task already exists".into()); }
        if !autostart::set(&exe, true)?.enabled { return Err("enable readback failed".into()); }
        autostart::restore(&exe, &State { exists: true, enabled: false })?;
        let disabled = autostart::state(&exe)?;
        if !disabled.exists || disabled.enabled { return Err("disabled-task rollback failed".into()); }
        autostart::set(&exe, false)?;
        autostart::restore(&exe, &initial)?;
        if autostart::state(&exe)?.exists { return Err("remove/rollback readback failed".into()); }
        Ok(())
    })();
    let cleanup = autostart::set(&exe, false).and_then(|state|
        if state.exists { Err("fixture task survived cleanup".into()) } else { Ok(()) });
    if cleanup.is_ok() { let _ = std::fs::remove_file(&exe); let _ = std::fs::remove_dir(&root); }
    println!("{}", serde_json::json!({"passed":result.is_ok() && cleanup.is_ok(),"result":result,"cleanup":cleanup,"fixture":exe}));
    if result.is_err() || cleanup.is_err() { std::process::exit(1); }
}

#[cfg(not(windows))]
fn main() { panic!("Windows acceptance helper"); }
