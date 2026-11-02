#[path = "../../../tools/build_version.rs"]
mod build_version;
fn main() {
    build_version::emit("../../..");
    // Track the payload so a re-packed zip relinks the installer binary.
    println!("cargo:rerun-if-changed=payload.zip");

    #[cfg(target_os = "windows")]
    {
        use tauri_build::WindowsAttributes;
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
            "#);
        tauri_build::try_build(tauri_build::Attributes::new().windows_attributes(windows))
            .expect("Failed to run build script");
    }
    #[cfg(not(target_os = "windows"))]
    {
        tauri_build::build();
    }
}
