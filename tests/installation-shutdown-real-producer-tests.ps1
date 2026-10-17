param([Parameter(Mandatory = $true)][string]$EvidenceDirectory, [Parameter(Mandatory = $true)][string]$ProducerBuildManifest)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('framescope-script-real-中文 空格-' + [guid]::NewGuid().ToString('N'))
$runtime = Join-Path $fixture 'FrameScopeMonitor'
$delivery = Join-Path $fixture 'delivery'
New-Item -ItemType Directory -Path $runtime,$delivery | Out-Null
$audit = Get-Content $ProducerBuildManifest -Raw | ConvertFrom-Json
$producer = $audit | Where-Object {$_.File -like '*\FrameScopeMonitor.exe'}
foreach ($source in $producer.Sources) { if ((Get-FileHash -LiteralPath $source.Path).Hash -ne $source.Hash) { throw 'Producer source drift' } }
if ((Get-FileHash -LiteralPath $producer.File).Hash -ne $producer.OutputHash) { throw 'Producer binary drift' }
Copy-Item -LiteralPath $producer.File -Destination $runtime
Copy-Item -LiteralPath (Join-Path $repo 'packaging\Install-FrameScopeMonitor.ps1') -Destination $delivery
[IO.File]::WriteAllText((Join-Path $delivery 'payload.zip'), 'fixture payload never expanded')
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$helper = Join-Path $delivery 'FrameScopeUninstaller.exe'
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.Web.Extensions.dll ('/out:' + $helper) (Join-Path $repo 'packaging\FrameScopeUninstaller.cs') (Join-Path $repo 'packaging\FrameScopeInstallationShutdown.cs') (Join-Path $repo 'src\core\FrameScopeWatcherShutdown.cs') (Join-Path $repo 'src\core\FrameScopeJsonFile.cs') *> (Join-Path $EvidenceDirectory 'real-helper-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Real helper compile failed' }
$configExe = Join-Path $fixture 'StopFixtureConfig.exe'
& $csc /nologo /target:exe /reference:System.Web.Extensions.dll ('/out:' + $configExe) (Join-Path $repo 'src\core\FrameScopeConfigStore.cs') (Join-Path $repo 'src\core\FrameScopeJsonFile.cs') (Join-Path $repo 'tests\FrameScopeInstallationStopFixtureConfig.cs') *> (Join-Path $EvidenceDirectory 'real-config-build.log')
if ($LASTEXITCODE -ne 0) { throw 'Fixture config compile failed' }
$target = 'FrameScopeQualityAbsent_' + [guid]::NewGuid().ToString('N')
$config = Join-Path $runtime 'quality-config.json'
& $configExe $config (Join-Path $fixture 'runs') $target
if ($LASTEXITCODE -ne 0 -or @(Get-Process -Name $target -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Fixture config isolation failed' }
$wrapper = Join-Path $delivery 'wrapper.ps1'
$body = @'
$ErrorActionPreference='Stop'
$env:LOCALAPPDATA = Split-Path -Parent $PSScriptRoot
function New-Item { Write-Output 'QUALITY_POST_STOP_GATE'; throw 'QUALITY_POST_STOP_GATE' }
function Expand-Archive { throw 'unexpected payload expansion' }
function Start-Process { throw 'unexpected product launch' }
try { & (Join-Path $PSScriptRoot 'Install-FrameScopeMonitor.ps1'); throw 'unexpected success' }
catch { Write-Output $_.Exception.Message; if ($_.Exception.Message -eq 'QUALITY_POST_STOP_GATE') {exit 7}; exit 8 }
'@
[IO.File]::WriteAllText($wrapper,$body)
$watcher = $null
$scriptChild = $null
$started=[datetime]::UtcNow
try {
    $watcher = Start-Process -FilePath (Join-Path $runtime 'FrameScopeMonitor.exe') -ArgumentList @('--watcher','--config',('"' + $config + '"'),'--diagnostic-output-root',('"' + (Join-Path $fixture 'diagnostics') + '"')) -WindowStyle Hidden -PassThru
    $statePath=Join-Path $runtime 'framescope-watcher-state.json'
    $deadline=[datetime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $state=if(Test-Path -LiteralPath $statePath){Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json}
    } while (($null -eq $state -or $state.WatcherPid -ne $watcher.Id -or $state.Phase -ne 'idle') -and [datetime]::UtcNow -lt $deadline)
    if ($null -eq $state -or $state.WatcherPid -ne $watcher.Id -or $state.Phase -ne 'idle') { throw 'Real watcher idle state missing' }
    $before=$state
    $before | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $EvidenceDirectory 'producer-before.json') -Encoding UTF8
    $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $stdout=Join-Path $EvidenceDirectory 'real-script.log'
    $stderr=Join-Path $EvidenceDirectory 'real-script.err.log'
    $scriptChild=Start-Process -FilePath $powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $wrapper + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $scriptChild.WaitForExit(60000)) {throw 'Own script child timeout'}
    $scriptChild.WaitForExit()
    $scriptChild.Refresh()
    if (-not $watcher.WaitForExit(5000)) {throw 'Own watcher did not actually exit'}
    $watcher.Refresh()
    $after=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $after | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $EvidenceDirectory 'producer-after.json') -Encoding UTF8
    Start-Sleep -Milliseconds 300
    $output=Get-Content -LiteralPath $stdout -Raw
    if($after.Phase -ne 'stopped' -or @($after.ActiveMonitors).Count -ne 0 -or $after.WatcherInstanceId -ne $before.WatcherInstanceId -or $after.WatcherStartTimeUnix -ne $before.WatcherStartTimeUnix -or $after.ConfigPath -ne $config -or $after.StopErrorCode -or $after.StopError -or -not $after.StopRequested){throw 'Real producer shutdown contract failed'}
    $products=@(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($runtime + '\',[StringComparison]::OrdinalIgnoreCase)})
    if($products.Count -ne 0){throw 'Owned runtime residual process'}
    $gateReached=$scriptChild.ExitCode -eq 7 -and $output -match 'QUALITY_POST_STOP_GATE'
    $confirmedFailure=$scriptChild.ExitCode -eq 8 -and $output -match 'Installation shutdown failed \(\)'
    [pscustomobject]@{Fixture=$fixture;TargetAbsent=$true;Producer=$producer.File;ProducerHash=$producer.OutputHash;ProducerSourcesVerified=$producer.Sources.Count;Helper=$helper;HelperHash=(Get-FileHash -LiteralPath $helper).Hash;ScriptHash=(Get-FileHash -LiteralPath (Join-Path $delivery 'Install-FrameScopeMonitor.ps1')).Hash;ScriptExit=$scriptChild.ExitCode;ScriptOutput=$output.Trim();WatcherPid=$watcher.Id;WatcherExit=$watcher.ExitCode;Token=$after.WatcherInstanceId;StartTime=$after.WatcherStartTimeUnix;Config=$config;StopRequested=$after.StopRequested;Stopped=$after.Phase -eq 'stopped';EmptyWorkers=@($after.ActiveMonitors).Count -eq 0;NoRuntimeResidual=$true;PostStopMutationGateReached=$gateReached;ConfirmedWinexeExitCodeFailure=$confirmedFailure;PayloadUnchanged=[IO.File]::ReadAllText((Join-Path $delivery 'payload.zip')) -eq 'fixture payload never expanded';ElapsedMs=([datetime]::UtcNow-$started).TotalMilliseconds} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $EvidenceDirectory 'real-producer-script-receipt.json') -Encoding UTF8
    if ($confirmedFailure) { Write-Output 'CONFIRMED P1: production Windows PowerShell script loses winexe exit code after successful real watcher shutdown'; exit 1 }
    if (-not $gateReached) {throw 'Unexpected script result'}
    Write-Output 'PASS real producer + production script metadata + stop-only + actual exit + pre-mutation gate'
} finally {
    foreach($owned in @($scriptChild,$watcher)){if($owned){$owned.Refresh();if(-not $owned.HasExited){$owned.Kill();$owned.WaitForExit(3000)|Out-Null};$owned.Dispose()}}
}
