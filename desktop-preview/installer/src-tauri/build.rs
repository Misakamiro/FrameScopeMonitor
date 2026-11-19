#[path = "../../../tools/build_version.rs"]
mod build_version;
fn main() {
    build_version::emit("../../..");
    println!("cargo:rerun-if-changed=payload.zip");
    let level=if std::env::var_os("CARGO_FEATURE_TRANSACTION_VALIDATION").is_some(){"asInvoker"}else{"requireAdministrator"};
    let manifest=format!(r#"<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0"><dependency><dependentAssembly><assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*" /></dependentAssembly></dependency><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="{level}" uiAccess="false" /></requestedPrivileges></security></trustInfo><application xmlns="urn:schemas-microsoft-com:asm.v3"><windowsSettings><dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware><dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness></windowsSettings></application></assembly>"#);
    // Keep the large offline payload out of Rust/LLVM constant evaluation.
    let payload=std::path::PathBuf::from(std::env::var_os("CARGO_MANIFEST_DIR").unwrap()).join("payload.zip");
    let resource=format!("202 RCDATA \"{}\"",payload.to_string_lossy().replace('\\',"/"));
    winres::WindowsResource::new().set_icon("icons/icon.ico").set_manifest(&manifest).append_rc_content(&resource)
        .compile().expect("native installer resources");
}
