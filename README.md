# FrameScope Monitor

FrameScope Monitor is a Windows game performance monitoring tool focused on short stutter diagnosis. It watches configured game processes, starts high-frequency capture when a game launches, stops when the game exits, and generates an interactive HTML report.

## Features

- Native WinForms launcher for selecting monitored processes.
- Automatic watcher mode: start the app, then launch a configured game.
- High-frequency process sampling for CPU, memory, disk IO, and process activity.
- PresentMon-based frame capture for FPS, 1% low, and 0.1% low analysis.
- Interactive HTML report with FPS, system metrics, process usage, and performance charts.
- Offline installer package published through GitHub Releases.

## Default Targets

The default configuration includes presets for:

- Counter-Strike 2
- Delta Force
- Neverness To Everness
- Valorant
- Cyberpunk 2077
- Battlefield 6
- Hogwarts Legacy
- OPUS Prism Peak

You can add more processes from the app UI.

## Install

Download `FrameScopeMonitor-Setup.exe` from the latest GitHub Release and run it.

The installer includes the app, PresentMon, and a portable Python runtime. Users do not need to install Python separately.

Default install path:

```text
%LOCALAPPDATA%\FrameScopeMonitor
```

## Build From Source

Run:

```powershell
.\build.ps1
```

The build script uses the .NET Framework compiler included with Windows and expects a portable Python runtime at:

```text
%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\python
```

If you only need the GUI executable, compile `FrameScopeNativeMonitor.cs` with .NET Framework 4.x references.

## Runtime Files

Core files:

- `FrameScopeMonitor.exe`
- `FrameScopeWatcher.ps1`
- `Monitor-CS2-HighFreq.ps1`
- `Generate-CS2-FrameScope-Interactive-Report.py`
- `tools\PresentMon-2.4.1-x64.exe`

Generated local files such as `framescope-runs`, `cs2-monitor-runs`, logs, configs, and packaged installers are intentionally ignored by git.
