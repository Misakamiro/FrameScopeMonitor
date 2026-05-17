$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found: $csc"
}

Push-Location $root
try {
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeMonitor.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Management.dll `
        /reference:System.Web.Extensions.dll `
        .\src\core\FrameScopeConfigStore.cs `
        .\src\core\FrameScopeCapturePlanner.cs `
        .\src\diagnostics\FrameScopeDiagnostics.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Models.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Sections.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Markdown.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Redaction.cs `
        .\src\diagnostics\FrameScopeDiagnostics.Retention.cs `
        .\src\diagnostics\FrameScopeDiagnostics.IO.cs `
        .\src\core\FrameScopeReportProgress.cs `
        .\src\ui\FrameScopeUiState.cs `
        .\src\ui\FrameScopeMotion.cs `
        .\src\ui\FrameScopeUiTheme.cs `
        .\src\ui\FrameScopeUiComponents.cs `
        .\src\ui\FrameScopeRoundedDrawing.cs `
        .\src\ui\FrameScopePanels.cs `
        .\src\ui\FrameScopeButtons.cs `
        .\src\ui\FrameScopeStatusControls.cs `
        .\src\ui\FrameScopeReferenceSidebar.cs `
        .\src\ui\FrameScopeReferenceSidebar.Navigation.cs `
        .\src\ui\FrameScopeReferenceSidebar.Drawing.cs `
        .\src\ui\FrameScopeReferenceSidebar.CompactDrawing.cs `
        .\src\ui\FrameScopeReferenceSidebar.ReferenceDrawing.cs `
        .\src\ui\FrameScopeReferenceSidebar.LogoDrawing.cs `
        .\src\ui\FrameScopeLiveChart.cs `
        .\src\ui\FrameScopeLiveData.cs `
        .\src\ui\FrameScopeLiveData.Csv.cs `
        .\src\ui\FrameScopeReportPage.cs `
        .\src\ui\FrameScopeReportPage.Layout.cs `
        .\src\ui\FrameScopeReportPage.Detail.cs `
        .\src\ui\FrameScopeReportPage.Actions.cs `
        .\src\app\FrameScopeNativeMonitor.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOrchestration.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOrchestration.Models.cs `
        .\src\app\FrameScopeNativeMonitor.ReportStatus.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOpen.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOpen.Browser.cs `
        .\src\app\FrameScopeNativeMonitor.ReportOpen.Status.cs `
        .\src\app\FrameScopeNativeMonitor.Watcher.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Models.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Paths.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Targets.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Tools.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.PresentMon.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.ChildProcesses.cs `
        .\src\app\FrameScopeNativeMonitor.MonitorSession.Status.cs `
        .\src\app\FrameScopeNativeMonitor.UiShell.cs `
        .\src\app\FrameScopeNativeMonitor.UiFields.cs `
        .\src\app\FrameScopeNativeMonitor.UiRouting.cs `
        .\src\app\FrameScopeNativeMonitor.UiVisualHelpers.cs `
        .\src\app\FrameScopeNativeMonitor.UiVisualCards.cs `
        .\src\app\FrameScopeNativeMonitor.UiVisualSections.cs `
        .\src\app\FrameScopeNativeMonitor.UiVisualButtons.cs `
        .\src\app\FrameScopeNativeMonitor.UiReportProgress.cs `
        .\src\app\FrameScopeNativeMonitor.UiScreenshots.cs `
        .\src\app\FrameScopeNativeMonitor.UiStatusDisplay.cs `
        .\src\app\FrameScopeNativeMonitor.UiInteractions.cs `
        .\src\app\FrameScopeNativeMonitor.UiHelpers.cs `
        .\src\app\FrameScopeNativeMonitor.UiConfigActions.cs `
        .\src\app\FrameScopeNativeMonitor.UiProcessPicker.cs `
        .\src\app\FrameScopeNativeMonitor.ProcessPickerDialog.cs `
        .\src\app\FrameScopeNativeMonitor.UiWatcherControls.cs `
        .\src\app\FrameScopeNativeMonitor.UiProcessCleanup.cs `
        .\src\app\FrameScopeNativeMonitor.UiStatusRefresh.cs `
        .\src\app\FrameScopeNativeMonitor.UiDiagnosticActions.cs `
        .\src\app\FrameScopeNativeMonitor.PageOverview.cs `
        .\src\app\FrameScopeNativeMonitor.PageSettings.cs `
        .\src\app\FrameScopeNativeMonitor.PageLive.cs `
        .\src\app\FrameScopeNativeMonitor.PageLive.Layout.cs `
        .\src\app\FrameScopeNativeMonitor.PageLive.Lifecycle.cs `
        .\src\app\FrameScopeNativeMonitor.PageLive.Log.cs `
        .\src\app\FrameScopeNativeMonitor.PageTargets.cs `
        .\src\app\FrameScopeNativeMonitor.PageTargets.Layout.cs `
        .\src\app\FrameScopeNativeMonitor.PageTargets.Grid.cs `
        .\src\app\FrameScopeNativeMonitor.PageTargets.Actions.cs `
        .\src\app\FrameScopeNativeMonitor.PageAbout.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeMonitor.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeProcessSampler.exe `
        .\src\monitoring\FrameScopeProcessSampler.cs `
        .\src\monitoring\FrameScopeProcessSampler.Models.cs `
        .\src\monitoring\FrameScopeProcessSampler.Selection.cs `
        .\src\monitoring\FrameScopeProcessSampler.IO.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeProcessSampler.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeSystemSampler.exe `
        .\src\monitoring\FrameScopeSystemSampler.cs `
        .\src\monitoring\FrameScopeSystemSampler.Models.cs `
        .\src\monitoring\FrameScopeSystemSampler.PerfCounters.cs `
        .\src\monitoring\FrameScopeSystemSampler.Gpu.cs `
        .\src\monitoring\FrameScopeSystemSampler.Processes.cs `
        .\src\monitoring\FrameScopeSystemSampler.IO.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeSystemSampler.exe" }

    & $csc /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeReportGenerator.exe `
        /reference:System.Web.Extensions.dll `
        /reference:System.Management.dll `
        /reference:Microsoft.VisualBasic.dll `
        .\src\core\FrameScopeReportProgress.cs `
        .\src\reporting\FrameScopeReportGenerator.cs `
        .\src\reporting\FrameScopeReportGenerator.Models.cs `
        .\src\reporting\FrameScopeReportGenerator.Cli.cs `
        .\src\reporting\FrameScopeReportGenerator.Progress.cs `
        .\src\reporting\FrameScopeReportGenerator.Diagnostics.cs `
        .\src\reporting\FrameScopeReportGenerator.PresentMon.cs `
        .\src\reporting\FrameScopeReportGenerator.SystemData.cs `
        .\src\reporting\FrameScopeReportGenerator.ProcessData.cs `
        .\src\reporting\FrameScopeReportGenerator.Analysis.cs `
        .\src\reporting\FrameScopeReportGenerator.Metadata.cs `
        .\src\reporting\FrameScopeReportGenerator.Csv.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Layout.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Styles.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Sections.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.Scripts.cs `
        .\src\reporting\FrameScopeReportGenerator.Html.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeReportGenerator.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeUninstaller.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        .\packaging\FrameScopeUninstaller.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeUninstaller.exe" }

    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:FrameScopeLegacyCleanup.exe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.Management.dll `
        .\packaging\FrameScopeLegacyCleanup.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeLegacyCleanup.exe" }

    $dist = Join-Path $root 'dist'
    $payloadRoot = Join-Path $dist 'FrameScopeMonitor-payload'
    $sourceRoot = Join-Path $dist 'FrameScopeMonitor-installer-source'
    foreach ($path in @($payloadRoot, $sourceRoot)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    foreach ($file in @(
        'FrameScopeMonitor.exe',
        'FrameScopeProcessSampler.exe',
        'FrameScopeSystemSampler.exe',
        'FrameScopeReportGenerator.exe',
        'FrameScopeUninstaller.exe',
        'packaging\Uninstall-FrameScopeMonitor.cmd',
        'packaging\README-FrameScopeMonitor.txt'
    )) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $payloadRoot -Force
    }

    $payloadTools = Join-Path $payloadRoot 'tools'
    New-Item -ItemType Directory -Path $payloadTools -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'tools\PresentMon-2.4.1-x64.exe') -Destination $payloadTools -Force

    $payloadZip = Join-Path $sourceRoot 'payload.zip'
    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -Force

    $setupExe = Join-Path $dist 'FrameScopeMonitor-Setup.exe'
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
        /out:$setupExe `
        /reference:System.Windows.Forms.dll `
        /reference:System.Drawing.dll `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        /resource:$payloadZip,FrameScopePayload `
        .\packaging\FrameScopeSetupNative.cs
    if ($LASTEXITCODE -ne 0) { throw "csc failed: FrameScopeMonitor-Setup.exe" }

    $legacyCleanupExe = Join-Path $dist 'FrameScopeMonitor-LegacyCleanup.exe'
    $distReadme = Join-Path $dist 'README-FrameScopeMonitor.txt'
    Copy-Item -LiteralPath (Join-Path $root 'FrameScopeLegacyCleanup.exe') -Destination $legacyCleanupExe -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging\README-FrameScopeMonitor.txt') -Destination $distReadme -Force

    $releaseZip = Join-Path $dist 'FrameScopeMonitor-Installer.zip'
    if (Test-Path -LiteralPath $releaseZip) { Remove-Item -LiteralPath $releaseZip -Force }
    Compress-Archive -LiteralPath @($setupExe, $legacyCleanupExe, $distReadme) -DestinationPath $releaseZip -Force

    "Build complete: $setupExe"
}
finally {
    Pop-Location
}
