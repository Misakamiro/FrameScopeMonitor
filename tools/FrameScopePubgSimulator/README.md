# FrameScope PUBG Simulator

Purpose: validate the PUBG detection and FPS data chain without real PUBG installed.

This simulator does not prove real PUBG ETW/anti-cheat compatibility. It verifies:

- `TslGame.exe` process recognition.
- PUBG-like main window title recognition.
- PresentMon process-name capture arguments.
- Fake FPS CSV data entering `status.json`, `summary.json`, and generated report.
- No-frame diagnostic path when PresentMon writes a CSV header without rows.

Run from project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario spikes -DurationSeconds 3
```

Scenarios:

- `stable`: steady 60 FPS.
- `fluctuating`: wave-like frame time changes.
- `spikes`: low-FPS stalls plus high-FPS bursts.
- `no-data`: `presentmon.csv` header only, no frame rows.
- `missing-csv`: fake PresentMon exits without creating CSV.

Useful checks in output:

- `monitorExit` should be `0`.
- `reportExit` should be `0`.
- `presentMonCaptureMode` should be `process_name`.
- `presentMonCaptureTarget` should include `TslGame.exe;TslGame-Win64-Shipping.exe`.
- `targetHasMainWindow` should be `true`.
- `targetWindowTitle` should contain `PUBG`.
- `frameCaptureStatus` should be `captured` for data scenarios.
- `frameCaptureStatus` should be `no-presentmon-rows` or `no-presentmon-csv` for diagnostic scenarios.

Use `-NoInitialPid` to verify process/window discovery without the startup PID shortcut:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1 -Scenario fluctuating -DurationSeconds 2 -NoInitialPid
```
