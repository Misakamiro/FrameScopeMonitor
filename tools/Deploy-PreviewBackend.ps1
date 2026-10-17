# Deploy only the requested preview installation after identity-bound normal shutdown.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$TargetDirectory,
    [string]$StopHelperPath,
    [string]$EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Deployment requires Windows PowerShell Desktop for metadata-only helper verification.' }
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
if (-not $TargetDirectory) { $TargetDirectory = Join-Path $RepoRoot 'desktop-preview\src-tauri\target\release' }
$appDir = [IO.Path]::GetFullPath($TargetDirectory).TrimEnd('\')
if (-not $StopHelperPath) { $StopHelperPath = Join-Path $RepoRoot 'FrameScopeUninstaller.exe' }
if (-not $EvidenceDirectory) { $EvidenceDirectory = Join-Path ([IO.Path]::GetTempPath()) ('framescope-deploy-' + [guid]::NewGuid().ToString('N')) }
$stage = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $stage) { throw 'Deployment evidence directory must be new.' }
function Assert-OrdinaryPath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse path refused: $current" }
        }
        $parent = [IO.Path]::GetDirectoryName($current.TrimEnd('\'))
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
Assert-OrdinaryPath $appDir
Assert-OrdinaryPath $stage
if ($stage -eq $appDir -or $stage.StartsWith($appDir+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence directory cannot be inside deployment target.' }
if (-not (Test-Path -LiteralPath (Join-Path $appDir 'framescope-ui-preview.exe') -PathType Leaf) -and
    -not (Test-Path -LiteralPath (Join-Path $appDir 'FrameScope.exe') -PathType Leaf)) { throw "Host executable not found: $appDir" }
$lockPath=Join-Path $RepoRoot 'dependencies.lock.json'
Assert-OrdinaryPath $lockPath
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$entries = @()
foreach ($name in @('FrameScopeHardwareService.exe','FrameScopeHardwareProvider.exe','FrameScopeMonitor.exe',
    'FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','FrameScopeReportGenerator.exe')) {
    $entries += [pscustomobject]@{ Relative=$name; Source=(Join-Path $RepoRoot $name); Expected=$null; Length=$null }
}
foreach ($property in $lock.runtimeFiles.PSObject.Properties) {
    $name=$property.Name
    if ([IO.Path]::GetFileName($name) -ne $name -or $name -notmatch '\.dll$') { throw 'Invalid runtime payload filename.' }
    if ($name -notlike 'Microsoft.Web.*' -and $name -ne 'WebView2Loader.dll') {
        if ($property.Value.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $null -eq $property.Value.length -or [long]$property.Value.length -le 0) { throw 'Invalid locked runtime checksum or length.' }
        $entries += [pscustomobject]@{ Relative=$name; Source=(Join-Path $RepoRoot $name); Expected=$property.Value.sha256; Length=$property.Value.length }
    }
}
$pm=$lock.presentMon.file
if ($pm -notmatch '^tools/PresentMon[^/\\]*\.exe$') { throw 'Invalid locked PresentMon path.' }
if ($lock.presentMon.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid locked PresentMon checksum.' }
$entries += [pscustomobject]@{ Relative=$pm.Replace('/','\'); Source=(Join-Path $RepoRoot $pm); Expected=$lock.presentMon.sha256; Length=$null }
Assert-OrdinaryPath $StopHelperPath
if (-not (Test-Path -LiteralPath $StopHelperPath -PathType Leaf)) { throw 'Verified stop-only helper is missing; rebuild first.' }
$helperBytes = [IO.File]::ReadAllBytes($StopHelperPath)
$assembly = [Reflection.Assembly]::ReflectionOnlyLoad($helperBytes)
$type = $assembly.GetType('FrameScopeInstallationShutdown', $false)
$field = if ($type) { $type.GetField('Capability', [Reflection.BindingFlags]'NonPublic,Static') }
if (-not $field -or $field.GetRawConstantValue() -ne 'framescope-installation-shutdown-v1') { throw 'Unsupported stop-only helper; target unchanged.' }
# Validate the entire source set before starting any stop protocol or changing target files.
foreach ($entry in $entries) {
    Assert-OrdinaryPath $entry.Source
    if (-not (Test-Path -LiteralPath $entry.Source -PathType Leaf)) { throw "Backend payload missing: $($entry.Source)" }
    $file=Get-Item -LiteralPath $entry.Source
    if ($file.Length -le 0 -or ($null -ne $entry.Length -and $file.Length -ne $entry.Length)) { throw "Payload length invalid: $($entry.Relative)" }
    $hash=(Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash
    if ($entry.Expected -and $hash -ne $entry.Expected) { throw "Locked payload hash mismatch: $($entry.Relative)" }
    if ($entry.Relative -like '*.exe') {
        $stream=[IO.File]::OpenRead($entry.Source)
        try { if ($stream.ReadByte() -ne 77 -or $stream.ReadByte() -ne 90) { throw "Invalid executable payload: $($entry.Relative)" } } finally { $stream.Dispose() }
    }
    $entry | Add-Member NoteProperty Hash $hash
    Assert-OrdinaryPath (Join-Path $appDir $entry.Relative)
    if (Test-Path -LiteralPath (Join-Path $appDir $entry.Relative) -PathType Container) { throw 'Target payload path is a directory; target unchanged.' }
}
[IO.Directory]::CreateDirectory($stage) | Out-Null
$shutdownLogs=Join-Path $stage 'shutdown'
[IO.Directory]::CreateDirectory($shutdownLogs) | Out-Null
$helper=Join-Path $stage 'FrameScopeUninstaller.exe'
[IO.File]::WriteAllBytes($helper, $helperBytes)
foreach ($entry in $entries) {
    $staged=Join-Path (Join-Path $stage 'payload') $entry.Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($staged)) | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $staged
    if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ne $entry.Hash) { throw 'Source changed during staging; target unchanged.' }
}
$entries | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage 'source-receipt.json') -Encoding UTF8
function Invoke-FrameScopeStopHelper {
    param([string]$Mode, [int]$BudgetMs)
    $encoding = New-Object Text.UTF8Encoding($false, $true)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $helper
    $info.WorkingDirectory = $stage
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
if ($planResult.ExitCode -ne 0) { throw "Shutdown plan failed: $($planResult.Stderr)" }
$plan=$planResult.Stdout | ConvertFrom-Json
if ($plan.capability -ne 'framescope-installation-shutdown-v1' -or $null -eq $plan.maximumBudgetMs -or
    [long]$plan.maximumBudgetMs -le 0 -or [long]$plan.maximumBudgetMs -gt 86400000) { throw 'Invalid shutdown plan; target unchanged.' }
$stopResult=Invoke-FrameScopeStopHelper '--stop-installation' ([int]$plan.maximumBudgetMs)
if ($stopResult.ExitCode -ne 0) { throw "Shutdown failed; target unchanged. $($stopResult.Stderr)" }
$shutdown=$stopResult.Stdout | ConvertFrom-Json
if ($shutdown.status -notin @('stopped','stopped-with-warning')) { throw 'Shutdown did not confirm completion; target unchanged.' }
if ($shutdown.warning) { Write-Warning $shutdown.warning }
# Check every replacement before the first write; a loaded DLL causes a clean failure.
foreach ($entry in $entries) {
    $destination=Join-Path $appDir $entry.Relative
    Assert-OrdinaryPath $destination
    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        $stream=[IO.File]::Open($destination,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $stream.Dispose()
    }
    if ((Get-FileHash -LiteralPath (Join-Path (Join-Path $stage 'payload') $entry.Relative) -Algorithm SHA256).Hash -ne $entry.Hash) { throw 'Staged payload changed; target unchanged.' }
}
$changed=@()
try {
    foreach ($entry in $entries) {
        $destination=Join-Path $appDir $entry.Relative
        $backup=Join-Path (Join-Path $stage 'backup') $entry.Relative
        $existed=Test-Path -LiteralPath $destination -PathType Leaf
        if ($existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)) | Out-Null
            Copy-Item -LiteralPath $destination -Destination $backup
        }
        $changed += [pscustomobject]@{Destination=$destination;Backup=$backup;Existed=$existed}
        $changed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'replacement-log.json') -Encoding UTF8
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath (Join-Path (Join-Path $stage 'payload') $entry.Relative) -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $entry.Hash) { throw 'Deployed payload hash mismatch.' }
    }
}
catch {
    $originalFailure=$_
    $restoreFailures=@()
    for ($index=$changed.Count-1;$index -ge 0;$index--) {
        $item=$changed[$index]
        try {
            if ($item.Existed) { Copy-Item -LiteralPath $item.Backup -Destination $item.Destination -Force }
            elseif (Test-Path -LiteralPath $item.Destination -PathType Leaf) { Remove-Item -LiteralPath $item.Destination }
        } catch { $restoreFailures += $_.Exception.Message }
    }
    if ($restoreFailures.Count) { throw "Deployment and rollback failed. Backups retained: $stage. $($restoreFailures -join '; ')" }
    throw $originalFailure
}
Write-Output "Preview backend ready: $appDir. Evidence and backups: $stage"
