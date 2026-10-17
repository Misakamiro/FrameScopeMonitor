fn main() {
    // Track the payload so a re-packed zip relinks the installer binary.
    println!("cargo:rerun-if-changed=payload.zip");

    // Version comes from the main app so installer and app never diverge.
    let main_cargo = std::path::Path::new("../../src-tauri/Cargo.toml");
    if main_cargo.exists() {
        let content = std::fs::read_to_string(main_cargo).unwrap_or_default();
        for line in content.lines() {
            if let Some(ver) = line.strip_prefix("version = \"") {
                if let Some(end) = ver.find('"') {
                    println!("cargo:rustc-env=FRAMESCOPE_APP_VERSION={}", &ver[..end]);
                    break;
                }
            }
        }
    }

    #[cfg(target_os = "windows")]
    {
        use tauri_build::WindowsAttributes;
        // The optional transaction-validation binary only operates on guarded
        // TEMP fixtures. Normal installer builds retain requireAdministrator;
        // main.rs disables the installer entry in a validation-feature build.
        let validation = std::env::var_os("CARGO_FEATURE_TRANSACTION_VALIDATION").is_some();
        let windows = WindowsAttributes::new()
            // Iron rule from the main app: overriding the manifest MUST keep the
            // Tauri default Common-Controls dependency or SxS breaks (main app
            // lost hours to exactly this). requireAdministrator matches the
            // per-machine install layout (Program Files + HKLM ARP key).
            .app_manifest(r#"
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <dependency>
    <dependentAssembly>
      <assemblyIdentity
        type="win32"
        name="Microsoft.Windows.Common-Controls"
        version="6.0.0.0"
        processorArchitecture="*"
        publicKeyToken="6595b64144ccf1df"
        language="*"
      />
    </dependentAssembly>
  </dependency>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false"/>
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
            "#.replace("level=\"requireAdministrator\"", if validation { "level=\"asInvoker\"" } else { "level=\"requireAdministrator\"" }));
        tauri_build::try_build(tauri_build::Attributes::new().windows_attributes(windows))
            .expect("Failed to run build script");
    }
    #[cfg(not(target_os = "windows"))]
    {
        tauri_build::build();
    }
}
