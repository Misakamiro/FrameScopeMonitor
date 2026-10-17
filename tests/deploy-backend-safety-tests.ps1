param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
if (-not $OutputDirectory) { throw 'Explicit isolated OutputDirectory is required.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$scriptPath=Join-Path (Split-Path -Parent $PSScriptRoot) 'tools/Deploy-PreviewBackend.ps1'
$code=Get-Content $scriptPath -Raw
if ($code -match 'Stop-Process|Get-Process\s+-Name') { throw 'Deployment still globally force-stops process names.' }
Write-Output 'DeployBackendSafetyTests: source stop scope PASS'
if ($PSVersionTable.PSEdition -eq 'Core') { throw 'Run runtime fixtures with Windows PowerShell Desktop.' }
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$fixtureRoot=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('fixtures-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$helperSource=Join-Path $fixtureRoot 'Helper.cs'
Set-Content -LiteralPath $helperSource @"
using System; using System.IO; using System.Threading;
internal static class FrameScopeInstallationShutdown { internal const string Capability = "framescope-installation-shutdown-v1"; }
internal static class FixtureHelper {
 public static int Main(string[] args) {
  string root=Environment.GetEnvironmentVariable("FRAMESCOPE_DEPLOY_FIXTURE");
  File.AppendAllText(Path.Combine(root,"calls.log"),args[0]+"|"+args[1]+Environment.NewLine);
  string mode=File.ReadAllText(Path.Combine(root,"mode.txt")).Trim();
  if(args[0]=="--shutdown-plan") { Console.WriteLine("{\"capability\":\"framescope-installation-shutdown-v1\",\"maximumBudgetMs\":1000}"); return 0; }
  if(mode=="timeout") { Thread.Sleep(10000); return 0; }
  if(mode=="overflow") { Console.Write(new string('x',1100000)); return 0; }
  if(mode=="fail") { Console.Error.WriteLine("fixture refusal"); return 2; }
  Console.WriteLine("{\"status\":\"stopped\"}"); return 0;
 }
}
"@ -Encoding UTF8
$fixtureExe=Join-Path $fixtureRoot 'FixtureHelper.exe'
& $csc /nologo /target:winexe "/out:$fixtureExe" $helperSource
if ($LASTEXITCODE -ne 0) { throw 'Fixture helper compilation failed.' }
function New-Case([string]$Name) {
 $root=Join-Path $fixtureRoot $Name
 $source=Join-Path $root 'source'; $target=Join-Path $root 'target'
 New-Item -ItemType Directory -Path "$source/tools",$target | Out-Null
 foreach($name in @('FrameScopeHardwareService.exe','FrameScopeHardwareProvider.exe','FrameScopeMonitor.exe','FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','FrameScopeReportGenerator.exe')) {
  Copy-Item $fixtureExe (Join-Path $source $name)
  Set-Content (Join-Path $target $name) 'old-target'
 }
 Set-Content (Join-Path $target 'FrameScope.exe') 'fixture host never executed'
 Set-Content (Join-Path $target 'framescope-config.json') 'user-settings'
 Set-Content (Join-Path $target 'unknown-user-file.txt') 'user-content'
 Set-Content (Join-Path $source 'Fixture.dll') 'runtime'
 Set-Content (Join-Path $target 'Fixture.dll') 'old-runtime'
 Copy-Item $fixtureExe (Join-Path $source 'tools/PresentMon-fixture.exe')
 $dependency=Get-Item (Join-Path $source 'Fixture.dll')
 @{runtimeFiles=@{'Fixture.dll'=@{length=$dependency.Length;sha256=(Get-FileHash $dependency.FullName).Hash}};presentMon=@{file='tools/PresentMon-fixture.exe';sha256=(Get-FileHash $fixtureExe).Hash}} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $source 'dependencies.lock.json')
 Set-Content (Join-Path $root 'mode.txt') 'success'
 return [pscustomobject]@{Root=$root;Source=$source;Target=$target;Evidence=(Join-Path $root 'evidence');Helper=$fixtureExe}
}
function Target-Hashes($Case) { return ((Get-ChildItem $Case.Target -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName+':'+(Get-FileHash $_.FullName).Hash }) -join '|') }
function Invoke-Case($Case,[bool]$ShouldSucceed) {
 $before=Target-Hashes $Case
 $env:FRAMESCOPE_DEPLOY_FIXTURE=$Case.Root
 $failed=$false
 try {
 $info=New-Object Diagnostics.ProcessStartInfo
 $info.FileName=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
 $info.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$scriptPath+'" -RepoRoot "'+$Case.Source+'" -TargetDirectory "'+$Case.Target+'" -StopHelperPath "'+$Case.Helper+'" -EvidenceDirectory "'+$Case.Evidence+'"'
 $info.UseShellExecute=$false; $info.CreateNoWindow=$true
 $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
 $process=New-Object Diagnostics.Process; $process.StartInfo=$info
 try {
  if (-not $process.Start()) { throw 'Fixture child failed to start.' }
  $outTask=$process.StandardOutput.ReadToEndAsync(); $errorTask=$process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit(20000)) { $process.Kill(); throw 'Fixture deployment exceeded outer timeout.' }
  $output=$outTask.GetAwaiter().GetResult(); $errors=$errorTask.GetAwaiter().GetResult()
  Set-Content (Join-Path $Case.Root 'child-output.log') ($output+$errors)
  if ($process.ExitCode -ne 0) { throw $errors }
 } finally { $process.Dispose() }
}
 catch { $failed=$true; $_.Exception.Message | Set-Content (Join-Path $Case.Root 'failure.txt') }
 finally { Remove-Item Env:FRAMESCOPE_DEPLOY_FIXTURE -ErrorAction SilentlyContinue }
 if ($failed -eq $ShouldSucceed) { throw "Unexpected outcome: $($Case.Root), failed=$failed" }
 if (-not $ShouldSucceed -and (Target-Hashes $Case) -ne $before) { throw 'Failed deployment changed target bytes.' }
 if ((Get-Content (Join-Path $Case.Target 'framescope-config.json') -Raw).Trim() -ne 'user-settings' -or (Get-Content (Join-Path $Case.Target 'unknown-user-file.txt') -Raw).Trim() -ne 'user-content') { throw 'User files changed.' }
 Write-Output ('PASS: '+[IO.Path]::GetFileName($Case.Root))
}
$case=New-Case 'missing-backend'; Remove-Item (Join-Path $case.Source 'FrameScopeReportGenerator.exe'); Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Missing backend reached stop helper.' }
$case=New-Case 'bad-runtime-hash'; Set-Content (Join-Path $case.Source 'Fixture.dll') 'corrupt'; Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Corrupt runtime reached stop helper.' }
$case=New-Case 'missing-presentmon'; Remove-Item (Join-Path $case.Source 'tools/PresentMon-fixture.exe'); Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Missing PresentMon reached stop helper.' }
$case=New-Case 'unknown-helper'; $case.Helper=Join-Path $case.Root 'unsupported.exe'; Set-Content $case.Helper 'unknown'; Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Unknown helper executed.' }
$case=New-Case 'unknown-managed-helper'
$unsupportedSource=Join-Path $case.Root 'Unknown.cs'
(Get-Content $helperSource -Raw).Replace('class FrameScopeInstallationShutdown','class UnsupportedMetadata') | Set-Content $unsupportedSource
$case.Helper=Join-Path $case.Root 'Unknown.exe'
& $csc /nologo /target:winexe (('/out:'+$case.Helper)) $unsupportedSource
if ($LASTEXITCODE -ne 0) { throw 'Unsupported fixture compilation failed.' }
Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Unknown managed helper Main executed.' }
$case=New-Case 'missing-runtime-checksum'
$badLock=Get-Content (Join-Path $case.Source 'dependencies.lock.json') -Raw | ConvertFrom-Json
$badLock.runtimeFiles.'Fixture.dll'.sha256=$null
$badLock | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $case.Source 'dependencies.lock.json')
Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Invalid checksum reached stop.' }
$case=New-Case 'payload-target-directory'
Remove-Item (Join-Path $case.Target 'FrameScopeMonitor.exe')
New-Item -ItemType Directory -Path (Join-Path $case.Target 'FrameScopeMonitor.exe') | Out-Null
Invoke-Case $case $false
if (Test-Path (Join-Path $case.Root 'calls.log')) { throw 'Target directory collision reached stop.' }
$case=New-Case 'stop-refused'; Set-Content (Join-Path $case.Root 'mode.txt') 'fail'; Invoke-Case $case $false
$case=New-Case 'stop-timeout'; Set-Content (Join-Path $case.Root 'mode.txt') 'timeout'; Invoke-Case $case $false
$case=New-Case 'output-overflow'; Set-Content (Join-Path $case.Root 'mode.txt') 'overflow'; Invoke-Case $case $false
$case=New-Case 'occupied-dll'; $locked=[IO.File]::Open((Join-Path $case.Target 'Fixture.dll'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
try { Invoke-Case $case $false } finally { $locked.Dispose() }
$case=New-Case 'success'; Invoke-Case $case $true
if ((Get-FileHash (Join-Path $case.Target 'FrameScopeMonitor.exe')).Hash -ne (Get-FileHash $fixtureExe).Hash) { throw 'Backend was not deployed.' }
$calls=Get-Content (Join-Path $case.Root 'calls.log')
if ($calls.Count -ne 2 -or $calls[0] -ne ('--shutdown-plan|'+$case.Target) -or $calls[1] -ne ('--stop-installation|'+$case.Target)) { throw 'Stop helper received a foreign target or wrong protocol.' }
Write-Output 'DeployBackendSafetyTests: 12 isolated runtime scenarios PASS'




