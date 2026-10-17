// Shared by the app and custom installer. VERSION remains authoritative.
pub fn emit(root: &str) {
    let root = std::path::Path::new(root);
    println!("cargo:rerun-if-changed={}", root.join("VERSION").display());
    let version = std::fs::read_to_string(root.join("VERSION")).expect("VERSION missing");
    let version = version.trim();
    assert_eq!(version, std::env::var("CARGO_PKG_VERSION").unwrap(), "Run Sync-FrameScopeVersion.ps1");
    let sha = std::env::var("FRAMESCOPE_BUILD_SHA").ok().or_else(|| {
        std::process::Command::new("git").args(["-C", root.to_str()?, "rev-parse", "HEAD"]).output().ok()
            .filter(|o| o.status.success()).map(|o| String::from_utf8_lossy(&o.stdout).trim().to_owned())
    }).expect("Git SHA missing (set FRAMESCOPE_BUILD_SHA for isolated source builds)");
    assert!(sha.len() == 40 && sha.bytes().all(|b| b.is_ascii_hexdigit()), "Invalid build SHA");
    println!("cargo:rerun-if-env-changed=FRAMESCOPE_BUILD_SHA");
    println!("cargo:rerun-if-changed={}", root.join(".git/HEAD").display());
    println!("cargo:rerun-if-changed={}", root.join(".git/refs/heads/codex/2.0").display());
    println!("cargo:rustc-env=FRAMESCOPE_VERSION={version}");
    println!("cargo:rustc-env=FRAMESCOPE_APP_VERSION={version}");
    println!("cargo:rustc-env=FRAMESCOPE_GIT_SHA={sha}");
}
