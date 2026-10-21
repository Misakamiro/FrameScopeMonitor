param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
$fixture=Join-Path $env:TEMP ('framescope-overlay-compat-'+[guid]::NewGuid().ToString('N'))
$appData=Join-Path $fixture 'profile'
$runtime=Join-Path $fixture 'runtime'
$settingsDir=Join-Path $appData 'com.framescope.ui-preview'
New-Item -ItemType Directory -Path $settingsDir,$runtime -Force | Out-Null
$scriptPath=Join-Path $fixture 'Collect-OverlayVisibility.ps1'
# Match the distributed script's encoding. All native window capture is
# replaced by this process-local stub: no window/game/UAC test is performed.
[IO.File]::WriteAllText($scriptPath,[IO.File]::ReadAllText((Join-Path $RepoRoot 'tools/Collect-OverlayVisibility.ps1'),[Text.Encoding]::UTF8),[Text.UTF8Encoding]::new($true))
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
public static class FrameScopeOverlayVisibility {
    public static object Capture() {
        return new Dictionary<string,object> {
            {"marker","retained-window-evidence"},
            {"processes",new object[]{new Dictionary<string,object> {
                {"name","FrameScope"},{"image",Path.Combine(Environment.GetEnvironmentVariable("FRAMESCOPE_DIAG_TEST_RUNTIME"),"FrameScope.exe")}
            }}}
        };
    }
}
'@
$savedAppData=$env:APPDATA
$savedRuntime=$env:FRAMESCOPE_DIAG_TEST_RUNTIME
function Assert-Result([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Write-Utf8([string]$Path,[string]$Text){[IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false))}
function Run-Case([string]$Name) {
    $output=Join-Path $fixture ($Name+'.json')
    & $scriptPath -Samples 1 -StartDelaySeconds 0 -OutputPath $output | Out-Null
    Assert-Result (Test-Path -LiteralPath $output) 'No diagnostic file was produced'
    $data=[IO.File]::ReadAllText($output,[Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-Result ($data.frames[0].marker -eq 'retained-window-evidence') 'Window evidence was lost'
    return $data
}
try {
    $env:APPDATA=$appData
    $env:FRAMESCOPE_DIAG_TEST_RUNTIME=$runtime
    $settings=Join-Path $settingsDir 'preview-native-settings.json'
    $build=Join-Path $runtime 'FrameScopeBuildInfo.json'
    $validSettings='{"overlay-settings":{"style":"竖排","opacity":138,"font":"处理器"}}'
    Write-Utf8 $settings $validSettings
    Write-Utf8 $build '{"version":"2.0.1-beta.1","note":"硬件信息"}'
    $valid=Run-Case 'utf8-without-bom'
    Assert-Result ($valid.overlaySettings.style -eq '竖排') 'UTF-8 settings were decoded incorrectly'
    Assert-Result ($valid.builds[0].build.note -eq '硬件信息') 'UTF-8 build information was decoded incorrectly'
    Assert-Result (@($valid.configurationReadErrors).Count -eq 0) 'Valid configuration was reported as broken'
    Write-Utf8 $settings '{"overlay-settings":'
    $badSettings=Run-Case 'broken-settings'
    Assert-Result (@($badSettings.configurationReadErrors).Count -eq 1) 'Settings failure was not recorded'
    Assert-Result ($badSettings.builds[0].build.version -eq '2.0.1-beta.1') 'Valid build information was lost'
    Write-Utf8 $build 'invalid-json'
    $both=Run-Case 'broken-settings-and-build'
    Assert-Result (@($both.configurationReadErrors).Count -eq 2) 'Both failures were not preserved'
    [IO.File]::WriteAllText($settings,$validSettings,[Text.UTF8Encoding]::new($true))
    Write-Utf8 $build '{"version":"2.0.1-beta.1"}'
    $bom=Run-Case 'utf8-with-bom'
    Assert-Result ($bom.overlaySettings.style -eq '竖排') 'UTF-8 BOM settings were decoded incorrectly'
    Write-Output "PASS: four offline compatibility cases; PowerShell $($PSVersionTable.PSVersion); fixture $fixture"
} finally {
    $env:APPDATA=$savedAppData
    $env:FRAMESCOPE_DIAG_TEST_RUNTIME=$savedRuntime
}
