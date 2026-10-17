#[path = "../../tools/build_version.rs"]
mod build_version;
fn main() {
    build_version::emit("../..");
    println!("cargo:rerun-if-changed=icons/icon.ico");
    // requireAdministrator: the ETW frame capture, LibreHardwareMonitor
    // driver-backed sensors and the PawnIO driver all need an elevated
    // process; child processes (hardware service, watcher) inherit the token.
    // Structure mirrors Tauri's default windows-app-manifest.xml (the
    // Common-Controls dependency must stay); only the requested execution
    // level and PerMonitorV2 DPI awareness are added.
    let manifest = r#"<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
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
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>"#;
    tauri_build::try_build(
        tauri_build::Attributes::new()
            .windows_attributes(tauri_build::WindowsAttributes::new().app_manifest(manifest)),
    )
    .expect("failed to build the preview host");
}
