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


## minisign-verify 0.2.5

Used by the FrameScope application and installer to verify signed update manifests.
Source: https://github.com/jedisct1/rust-minisign-verify

The complete upstream license, including its rust-crypto attribution, follows:

Copyright (c) 2019-2025 Frank Denis

Permission is hereby granted, free of charge, to any
person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the
Software without restriction, including without
limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software
is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice
shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

--

Code in the src/crypto folder is derived from the rust-crypto project:
https://github.com/DaGenix/rust-crypto

Original ISC license follows:

Copyright (c) 2006-2009 Graydon Hoare
Copyright (c) 2009-2013 Mozilla Foundation

Permission is hereby granted, free of charge, to any
person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the
Software without restriction, including without
limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software
is furnished to do so, subject to the following
conditions:

The above copyright notice and this permission notice
shall be included in all copies or substantial portions
of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
