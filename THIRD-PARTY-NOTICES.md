# Third-Party Notices

## LibreHardwareMonitorLib 0.9.6

Used by `FrameScopeHardwareProvider.exe` (loaded via reflection at runtime) for
CPU/GPU/memory/motherboard/storage sensor readings.

- Project: LibreHardwareMonitor
- Source code: `https://github.com/LibreHardwareMonitor/LibreHardwareMonitor`
- License: MPL-2.0 (Mozilla Public License 2.0)
- Restored from NuGet at build time; exact version is pinned in `dependencies.lock.json`
  and `native-packages.lock.json` (`LibreHardwareMonitorLib`, `HidSharp`,
  `BlackSharp.Core`, `DiskInfoToolkit`, `RAMSPDToolkit-NDD`, plus their transitive
  `System.*` assemblies).

Driver-backed sensors (Vcore etc.) additionally rely on the PawnIO driver, which is
installed separately with user consent; its installer is not redistributed in the
current build.

## PresentMon 2.4.1

Used by the capture pipeline to record frame presentation intervals.

- Project: Intel PresentMon
- Source code: `https://github.com/GameTechDev/PresentMon`
- License: MIT
- Redistributed unchanged as `tools/PresentMon-2.4.1-x64.exe`; SHA-256 pinned in
  `dependencies.lock.json`.

## Microsoft WebView2

The WebView2 Runtime and SDK (`Microsoft.Web.WebView2`) are Microsoft products used
for the UI host. The Evergreen Runtime is installed or updated via the official
Microsoft installer; SDK assemblies are restored from NuGet with pinned versions in
`dependencies.lock.json`.

## Rust dependencies of FrameScopeHardwareService

`src/hardware-service` depends on `serde` and `serde_json` (MIT OR Apache-2.0),
resolved through `src/hardware-service/Cargo.lock`.

## Other dependencies

Other locked runtime dependencies and their exact package versions are recorded in
`dependencies.lock.json` and `native-packages.lock.json`.
