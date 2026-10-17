$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'This legacy installer requires Windows PowerShell Desktop for metadata-only helper verification.'
}

$payload = Join-Path $PSScriptRoot 'payload.zip'
if (-not (Test-Path -LiteralPath $payload)) {
    throw "payload.zip not found next to installer."
}

$appDir = Join-Path $env:LOCALAPPDATA 'FrameScopeMonitor'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FrameScope Monitor.lnk'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\FrameScope Monitor.lnk'

$helper = Join-Path $PSScriptRoot 'FrameScopeUninstaller.exe'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) {
    throw 'Verified stop-only helper is missing; refusing to overwrite the installation.'
}
$assembly = [Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes($helper))
$type = $assembly.GetType('FrameScopeInstallationShutdown', $false)
$field = if ($type) { $type.GetField('Capability', [Reflection.BindingFlags]'NonPublic,Static') }
if (-not $field -or $field.GetRawConstantValue() -ne 'framescope-installation-shutdown-v1') {
    throw 'Stop-only helper capability is unsupported; installation was not changed.'
}
$shutdownLogs = Join-Path ([IO.Path]::GetTempPath()) ('framescope-install-shutdown-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($shutdownLogs) | Out-Null

function Invoke-FrameScopeStopHelper {
    param([string]$Mode, [int]$BudgetMs)
    $encoding = New-Object Text.UTF8Encoding($false, $true)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $helper
    $info.WorkingDirectory = $PSScriptRoot
    $info.Arguments = $Mode + ' "' + $appDir + '"'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = $encoding
    $info.StandardErrorEncoding = $encoding
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    $stdout = New-Object Text.StringBuilder
    $stderr = New-Object Text.StringBuilder
    $streams = @()
    $record = [ordered]@{ Mode = $Mode; Executable = $helper; BudgetMs = $BudgetMs; Pid = $null; StartTimeUtc = $null; ExitCode = $null; Failure = $null }
    $name = $Mode.TrimStart('-')
    try {
        if (-not $process.Start()) { throw 'Stop-only helper could not start.' }
        $handle = $process.Handle # Keep the original process handle; never reopen a recycled PID.
        $record.Pid = $process.Id
        $record.StartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o')
        $timer = [Diagnostics.Stopwatch]::StartNew()
        foreach ($entry in @(@('stdout', $process.StandardOutput, $stdout), @('stderr', $process.StandardError, $stderr))) {
            $file = [IO.File]::Open((Join-Path $shutdownLogs ($name + '.' + $entry[0] + '.log')), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $writer = New-Object IO.StreamWriter($file, $encoding)
            $writer.AutoFlush = $true
            $buffer = New-Object char[] 4096
            $streams += [pscustomobject]@{ Reader = $entry[1]; Text = $entry[2]; Writer = $writer; Buffer = $buffer; Read = $entry[1].ReadAsync($buffer, 0, $buffer.Length); End = $false }
        }
        while ($true) {
            $drained = $false
            foreach ($stream in $streams) {
                if (-not $stream.End -and $stream.Read.IsCompleted) {
                    $drained = $true
                    $count = $stream.Read.GetAwaiter().GetResult()
                    if ($count -eq 0) { $stream.End = $true; continue }
                    $remaining = 1048576 - $stream.Text.Length
                    $accepted = [Math]::Min($remaining, $count)
                    if ($accepted -gt 0) {
                        $stream.Text.Append($stream.Buffer, 0, $accepted) | Out-Null
                        $stream.Writer.Write($stream.Buffer, 0, $accepted)
                    }
                    if ($accepted -lt $count) { throw 'Stop-only helper output exceeded the 1048576-character per-stream limit.' }
                    $stream.Read = $stream.Reader.ReadAsync($stream.Buffer, 0, $stream.Buffer.Length)
                }
            }
            if ($process.HasExited -and $streams[0].End -and $streams[1].End) { break }
            if ($timer.ElapsedMilliseconds -ge $BudgetMs) { throw "Stop-only helper timed out after $BudgetMs ms; installation was not changed." }
            if (-not $drained) { [Threading.Thread]::Sleep(10) }
        }
        $record.ExitCode = $process.ExitCode
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Stdout = $stdout.ToString(); Stderr = $stderr.ToString() }
    }
    catch {
        $record.Failure = $_.Exception.Message
        throw "$($record.Failure) Logs: $shutdownLogs."
    }
    finally {
        # Only this script's own helper handle is eligible for termination on timeout/output failure.
        # The target watcher, workers, report and hardware processes are never forcibly stopped here.
        if ($record.Pid -and -not $process.HasExited) {
            try {
                $process.Kill()
                if (-not $process.WaitForExit(3000)) { $record['CleanupFailure'] = 'Own helper did not exit within cleanup budget.' }
                else { $record['CleanupExitCode'] = $process.ExitCode }
            }
            catch { $record['CleanupFailure'] = $_.Exception.Message }
        }
        foreach ($stream in $streams) { $stream.Writer.Dispose() }
        [IO.File]::WriteAllText((Join-Path $shutdownLogs ($name + '.receipt.json')), ($record | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        $process.Dispose()
    }
}

$planResult = Invoke-FrameScopeStopHelper '--shutdown-plan' 30000
if ($planResult.ExitCode -ne 0) { throw "Installation shutdown plan failed ($($planResult.ExitCode)); logs preserved at $shutdownLogs. $($planResult.Stderr)" }
$plan = $planResult.Stdout | ConvertFrom-Json
if ($plan.capability -ne 'framescope-installation-shutdown-v1' -or $null -eq $plan.maximumBudgetMs -or
    [long]$plan.maximumBudgetMs -le 0 -or [long]$plan.maximumBudgetMs -gt 86400000) { throw "Invalid shutdown budget; logs preserved at $shutdownLogs." }
$stopResult = Invoke-FrameScopeStopHelper '--stop-installation' ([int]$plan.maximumBudgetMs)
if ($stopResult.ExitCode -ne 0) {
    throw "Installation shutdown failed ($($stopResult.ExitCode)); files and evidence were preserved. Logs: $shutdownLogs. $($stopResult.Stderr)"
}
$shutdown = $stopResult.Stdout | ConvertFrom-Json
if ($shutdown.status -notin @('stopped', 'stopped-with-warning')) { throw 'Shutdown did not confirm completion.' }
if ($shutdown.warning) { Write-Warning $shutdown.warning }

New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Expand-Archive -LiteralPath $payload -DestinationPath $appDir -Force

$exe = Join-Path $appDir 'FrameScopeMonitor.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "FrameScopeMonitor.exe was not installed correctly."
}

$shell = New-Object -ComObject WScript.Shell
foreach ($link in @($desktopLink, $startMenuLink)) {
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $appDir
    $shortcut.IconLocation = $exe
    $shortcut.Save()
}

Start-Process -FilePath $exe -WorkingDirectory $appDir
Write-Host "FrameScope Monitor installed to: $appDir"
