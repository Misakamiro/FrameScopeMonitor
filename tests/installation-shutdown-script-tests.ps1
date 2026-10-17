param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('framescope-shutdown-script-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$desktopPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$results = @()
foreach ($scenario in @('missing', 'unknown-managed', 'wrong-capability', 'stop-failed', 'stop-timeout', 'output-limit')) {
    $case = Join-Path $fixture $scenario
    New-Item -ItemType Directory -Path $case | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'packaging\Install-FrameScopeMonitor.ps1') -Destination $case
    [IO.File]::WriteAllText((Join-Path $case 'payload.zip'), 'not-an-executable-payload')
    $executed = Join-Path $case 'old-main-executed.txt'
    $changed = Join-Path $case 'destructive-operation.txt'
    if ($scenario -ne 'missing') {
        $capability = if ($scenario -in @('stop-failed', 'stop-timeout', 'output-limit')) { 'framescope-installation-shutdown-v1' } else { 'unsupported' }
        $helperType = if ($scenario -eq 'unknown-managed') { '' } else {
            'internal static class FrameScopeInstallationShutdown { internal const string Capability = "' + $capability + '"; static FrameScopeInstallationShutdown(){System.IO.File.WriteAllText(@"' + $executed + '", "cctor-executed");} }'
        }
        $main = if ($scenario -in @('stop-failed', 'stop-timeout', 'output-limit')) {
            $budget = if ($scenario -eq 'output-limit') { 10000 } else { 1200 }
            $stop = if ($scenario -eq 'stop-timeout') { 'System.Threading.Thread.Sleep(10000);' } elseif ($scenario -eq 'output-limit') { 'System.Console.Write(new string((char)120, 1100000));' } else { 'System.Environment.ExitCode=2;' }
            'if(args[0]=="--shutdown-plan"){System.Console.WriteLine("{\"capability\":\"framescope-installation-shutdown-v1\",\"maximumBudgetMs\":' + $budget + '}");return;}' + $stop
        } else { 'System.IO.File.WriteAllText(@"' + $executed + '", "old-main");' }
        $source = Join-Path $case 'unknown.cs'
        [IO.File]::WriteAllText($source, $helperType + ' internal static class Unknown { public static void Main(string[] args){' + $main + '} }')
        & $csc /nologo /target:winexe ('/out:' + (Join-Path $case 'FrameScopeUninstaller.exe')) $source *> (Join-Path $EvidenceDirectory ($scenario + '-build.log'))
        if ($LASTEXITCODE -ne 0) { throw "Fixture helper compile failed: $scenario" }
    }
    $wrapper = Join-Path $case 'wrapper.ps1'
    $body = @'
$ErrorActionPreference='Stop'
$env:LOCALAPPDATA = $PSScriptRoot
function New-Item { [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'destructive-operation.txt'),'New-Item'); throw 'unexpected mutation' }
function Expand-Archive { [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'destructive-operation.txt'),'Expand-Archive'); throw 'unexpected mutation' }
function Start-Process { throw 'unexpected launch' }
try { & (Join-Path $PSScriptRoot 'Install-FrameScopeMonitor.ps1'); throw 'unexpected success' }
catch { Write-Output $_.Exception.Message; exit 7 }
'@
    [IO.File]::WriteAllText($wrapper, $body)
    $stdout = Join-Path $EvidenceDirectory ($scenario + '.log')
    $stderr = Join-Path $EvidenceDirectory ($scenario + '.err.log')
    $child = Start-Process -FilePath $desktopPowerShell -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $wrapper + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $child.WaitForExit(15000)) { throw "Fixture timeout: PID=$($child.Id), preserved" }
    $child.Refresh()
    $message = [IO.File]::ReadAllText($stdout)
    if ($child.ExitCode -ne 7 -or $message -match 'unexpected' -or (Test-Path -LiteralPath $executed) -or (Test-Path -LiteralPath $changed)) {
        throw "Production script safety fixture failed: $scenario exit=$($child.ExitCode) output=$message"
    }
    if ($scenario -eq 'stop-failed' -and $message -notmatch 'shutdown failed') { throw "Wrong failure boundary: $message" }
    if ($scenario -eq 'stop-timeout' -and $message -notmatch 'timed out after 1200 ms') { throw "Wrong timeout boundary: $message" }
    if ($scenario -eq 'output-limit' -and $message -notmatch 'per-stream limit') { throw "Wrong output boundary: $message" }
    if ($scenario -in @('stop-timeout', 'output-limit')) {
        $logMatch = [regex]::Match($message, 'Logs:\s+([^\r\n]+)\.\s*$')
        if (-not $logMatch.Success) { throw "Missing retained helper log path: $message" }
        $logRoot = $logMatch.Groups[1].Value
        $receipt = Get-Content -LiteralPath (Join-Path $logRoot 'stop-installation.receipt.json') -Raw | ConvertFrom-Json
        if (-not $receipt.Failure -or $null -eq $receipt.CleanupExitCode -or $receipt.CleanupFailure) { throw 'Own helper failure/cleanup evidence is incomplete.' }
        try {
            $remainingHelper = [Diagnostics.Process]::GetProcessById($receipt.Pid)
            try {
                if (-not $remainingHelper.HasExited -and $remainingHelper.StartTime.ToUniversalTime() -eq ([datetime]$receipt.StartTimeUtc).ToUniversalTime()) { throw 'Own helper remains alive after failed script.' }
            } finally { $remainingHelper.Dispose() }
        } catch [ArgumentException] { }
        if ((Get-Item -LiteralPath (Join-Path $logRoot 'stop-installation.stdout.log')).Length -gt 4194304) { throw 'Retained output escaped the bounded UTF-8 log size.' }
    }
    if ($scenario -in @('unknown-managed', 'wrong-capability') -and $message -notmatch 'capability is unsupported') { throw "Wrong capability boundary: $message" }
    $results += [pscustomobject]@{ Scenario = $scenario; ExitCode = $child.ExitCode; NoOldMain = $true; NoTypeInitializer = $true; NoMutation = $true; Output = $message.Trim() }
    Write-Output "PASS production script $scenario"
}
[pscustomobject]@{ Fixture = $fixture; ScriptHash = (Get-FileHash -LiteralPath (Join-Path $repo 'packaging\Install-FrameScopeMonitor.ps1')).Hash; Results = $results } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'script-receipt.json') -Encoding UTF8
