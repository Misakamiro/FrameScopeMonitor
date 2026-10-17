$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'tools/FrameScopeRelease.Common.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('framescope-release-tooling-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$exe=Join-Path $fixture 'FrameScope_2.0.0-beta.1_x64-setup.exe'
[IO.File]::WriteAllText($exe,'test bytes, never executed')
$manifest=New-ReleaseManifest $exe '2.0.0-beta.1' ('a'*40) (Join-Path $fixture 'release-manifest.json')
if($manifest.channel -ne 'beta' -or $manifest.installer.sha256 -ne (Get-FileHash $exe).Hash){throw 'Manifest binding failed'}
$rejected=$false
try{New-ReleaseManifest $exe '2.0.0-beta.1' ('a'*40) (Join-Path $fixture 'release-manifest.json')}catch{$rejected=$true}
if(-not $rejected){throw 'Existing manifest overwritten'}
foreach($version in @('1.2.0','2.0.0-beta.01','../2.0.0','2.0.0+dirty')){
    $rejected=$false;try{New-ReleaseManifest $exe $version ('a'*40) (Join-Path $fixture 'bad.json')}catch{$rejected=$true}
    if(-not $rejected){throw "Accepted invalid release version $version"}
}
$rejected=$false;try{New-ReleaseManifest $exe '2.0.0-beta.1' 'shortsha' (Join-Path $fixture 'bad.json')}catch{$rejected=$true}
if(-not $rejected){throw 'Accepted short SHA'}
# Exact fake payload exercises the same release validator without building software.
$payload=Join-Path $fixture 'payload';New-Item -ItemType Directory -Path $payload | Out-Null
$required=@('FrameScope.exe','FrameScopeMonitor.exe','FrameScopeUninstaller.exe','FrameScopeHardwareService.exe','FrameScopeHardwareProvider.exe','FrameScopeReportGenerator.exe','FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','Restore-FrameScope.cmd','LICENSE','LICENSE.EXTRA')
foreach($name in $required){[IO.File]::WriteAllText((Join-Path $payload $name),'fixture '+$name)}
[IO.File]::WriteAllText((Join-Path $payload 'FrameScopeBuildInfo.json'),'{"version":"2.0.0-beta.1","git_sha":"'+('a'*40)+'","channel":"beta"}')
[IO.File]::WriteAllText((Join-Path $payload 'FrameScopeHostCapabilities.json'),'{"hostSha256":"'+(Get-FileHash (Join-Path $payload 'FrameScope.exe')).Hash+'"}')
$files=@(Get-ChildItem -LiteralPath $payload -File | ForEach-Object {[ordered]@{path=$_.Name;length=$_.Length;sha256=(Get-FileHash $_.FullName).Hash}})
[IO.File]::WriteAllText((Join-Path $payload 'FrameScopePayloadManifest.json'),([ordered]@{schema=1;files=$files}|ConvertTo-Json -Depth 6))
$zip=Join-Path $fixture 'fixture.zip';[IO.Compression.ZipFile]::CreateFromDirectory($payload,$zip)
& (Join-Path $root 'tools/Test-ReleasePayload.ps1') -Payload $zip -Version '2.0.0-beta.1' -GitSha ('a'*40)
$rejected=$false
try{& (Join-Path $root 'tools/Test-ReleasePayload.ps1') -Payload $zip -Version '2.0.0-beta.2' -GitSha ('a'*40)}catch{$rejected=$true}
if(-not $rejected){throw 'Accepted wrong payload version'}
$signed=Join-Path $fixture 'signed';New-Item -ItemType Directory -Path $signed | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src/updater/tests/fixtures/release-manifest.json'),(Join-Path $root 'src/updater/tests/fixtures/release-manifest.json.minisig') -Destination $signed
[IO.File]::WriteAllText((Join-Path $signed 'FrameScope_2.0.0-beta.1_x64-setup.exe'),'fixture installer',[Text.UTF8Encoding]::new($false))
$signer=Get-ReleaseSigner (Join-Path $env:LOCALAPPDATA 'FrameScope-ReleaseSigning')
$public=Join-Path $root 'src/updater/tests/fixtures/test.pub'
Test-ReleaseFiles $signed $public $signer | Out-Null
[IO.File]::WriteAllText((Join-Path $signed 'FrameScope_2.0.0-beta.1_x64-setup.exe'),'tampered installer')
$rejected=$false;try{Test-ReleaseFiles $signed $public $signer | Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Accepted tampered signed package'}
Write-Output "Release tooling contracts: PASS; retained fixture $fixture"
