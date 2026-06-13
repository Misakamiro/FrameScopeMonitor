# FrameScope build artifact sync after chart dropout and VID fix

Date: 2026-06-13
Status: PASS

## Scope

This report records the release-candidate artifact sync state for the chart dropout / bottom-spike fix and AMD CPU Core VID accuracy fix.

This report does not claim a new build, install, real-game run, BF6 test, GitHub push, or GitHub Release update by itself. It ties the current `dist` artifacts to the already validated implementation and installed-app evidence for this release window.

## Artifact hashes

The release-candidate artifacts in `dist` were checked against the publish-window hashes:

- `dist\FrameScopeMonitor-Setup.exe`: `8E3A301D7D2C4AC18FD2EA1F83BDDDE5FCFFB96985F303DAD09A25785B9CD5A3`
- `dist\FrameScopeMonitor-Full-Setup.exe`: `0C724E50BE1DC133BC39F188199810F4400340AD5540B656A8DAE2855ACC0901`
- `dist\FrameScopeMonitor-payload\FrameScopeMonitor.exe`: `EEA59166F2FEAB7A89DD3580A62481B520976BCD9D5FD0445A7D0B744FB3165C`
- `dist\FrameScopeMonitor-payload\frontend\assets\index-m2r1Gfgc.js`: `2DB69188D6FD4A6B2CA08379BFE38C89833C4188A427D3734B3719842BF302CE`

Conclusion: the installer, full installer, payload executable, and frontend JS hashes match the current release-window values.

## Linked validation

Primary implementation report:

- `docs\implementation-reports\2026-06-13-framescope-chart-dropout-and-cpu-vid-accuracy-fix-report.md`

Primary retest report:

- `docs\test-reports\2026-06-13-framescope-chart-dropout-and-cpu-vid-accuracy-fix-retest.md`

Installed update validation:

- `docs\test-reports\2026-06-13-framescope-local-install-update-validation-after-chart-dropout-and-vid-fix.md`

Installed WebView2 smoke clarification:

- `docs\test-reports\2026-06-13-framescope-installed-webview2-smoke-partial-clarification.md`

## Confirmed release behavior

- Invalid `0` / extremely low failed telemetry samples in voltage, frequency, temperature, and power charts are filtered or converted to null gaps instead of being drawn as bottom spikes.
- Downsampling preserves null gaps and does not reintroduce invalid zero spikes.
- Real low P-state values remain valid, including the observed GPU `225 MHz` sample.
- AMD LibreHardwareMonitor `0.4-0.7V` Core VID samples are rejected as unreliable low-range VID data.
- The user's expected around `1.08V` reading is CPU Voltage / Vcore, not per-core VID.
- Vcore is not substituted into CPU Core VID.
- `DATA.cpuVoltage`, `DATA.cpuVid`, and `bucketMs=1000` remain stable compatibility keys.

## Explicitly not done in this artifact-sync note

- Did not start a real game.
- Did not test BF6.
- Did not reinstall FrameScope.
- Did not rebuild or replace artifacts from this note alone.
- Did not update GitHub Release from this note alone.
- Did not touch GameLite or lightweight-script files.
