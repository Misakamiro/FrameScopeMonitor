param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
. (Join-Path $RepoRoot 'tools/FrameScopeRelease.Common.ps1')
if(-not (Get-Command Get-ReleaseAssetNames -ErrorAction SilentlyContinue)){throw 'Missing authenticated full-installer asset support'}
$normal=@('FrameScope_2.0.0_x64-setup.exe','release-manifest.json','release-manifest.json.minisig')
$full=@('FrameScope_2.0.0_x64-full-setup.exe','full-installer-manifest.json','full-installer-manifest.json.minisig')
if(@(Get-ReleaseAssetNames '2.0.0' $normal).Count -ne 3){throw 'Normal release changed'}
if(@(Get-ReleaseAssetNames '2.0.0' ($normal+$full)).Count -ne 6){throw 'Complete full bundle rejected'}
foreach($names in @(,($normal+@($full[0])), ,($normal+@('unknown.exe')), ,($normal+$full+@('unknown.exe')))){
    $rejected=$false
    try{Get-ReleaseAssetNames '2.0.0' $names | Out-Null}catch{$rejected=$true}
    if(-not $rejected){throw 'Incomplete or unexpected full release files accepted'}
}
Write-Output 'Full installer release asset policy: PASS'
