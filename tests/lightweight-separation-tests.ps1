$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$legacyEntrypoints = @(
    'Install-GameLiteAutoTrigger.ps1',
    'Check-GameLiteAutoTrigger.ps1',
    'Remove-GameLiteAutoTrigger.ps1',
    'GameLiteSession.ps1',
    'Enter-GameLite.ps1',
    'Exit-GameLite.ps1',
    'Invoke-GameLiteSGuardThrottle.ps1',
    'Install-GameLiteAutoTrigger.cmd',
    'Check-GameLiteAutoTrigger.cmd',
    'Remove-GameLiteAutoTrigger.cmd'
)

function Assert-FrameScopeSeparation {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

foreach ($name in $legacyEntrypoints) {
    Assert-FrameScopeSeparation (-not (Test-Path -LiteralPath (Join-Path $root $name))) "Legacy GameLite entrypoint remains in FrameScope: $name"
}

$oldLightweightRoot = Join-Path $root 'scripts\lightweight'
$oldCoreScripts = @()
if (Test-Path -LiteralPath $oldLightweightRoot) {
    $oldCoreScripts = @(Get-ChildItem -LiteralPath $oldLightweightRoot -Filter '*.ps1' -ErrorAction SilentlyContinue)
}
Assert-FrameScopeSeparation ($oldCoreScripts.Count -eq 0) 'Old scripts\lightweight still contains GameLite core scripts.'

$buildText = Get-Content -Raw -LiteralPath (Join-Path $root 'build.ps1')
Assert-FrameScopeSeparation ($buildText -notmatch '(?i)GameLite|lightweight|AutoTrigger|SGuard|WMI') 'build.ps1 references GameLite automation.'

$testBuildText = Get-Content -Raw -LiteralPath (Join-Path $root 'tests\Build-FrameScopeTests.ps1')
Assert-FrameScopeSeparation ($testBuildText -notmatch '(?i)GameLite|lightweight|AutoTrigger|SGuard|WMI') 'tests\Build-FrameScopeTests.ps1 references GameLite automation.'

$forbiddenProductionAutomation = '(?i)GameLite|AutoTrigger|SGuard|Win32_Process(?:Start|Stop)Trace|__EventFilter|CommandLineEventConsumer'
$productionSourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter '*.cs')
$packagingSourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'packaging') -Recurse -File | Where-Object {
    $_.Extension -in @('.cs', '.ps1', '.cmd')
})

foreach ($file in @($productionSourceFiles + $packagingSourceFiles)) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    Assert-FrameScopeSeparation ($text -notmatch $forbiddenProductionAutomation) "FrameScope production file references GameLite automation or a process-trigger WMI primitive: $($file.FullName)"
}

[pscustomobject]@{
    Status = 'PASS'
    LegacyEntrypointsAbsent = $legacyEntrypoints.Count
    StandaloneProjectRequired = $false
    OldCoreScriptsRemaining = $oldCoreScripts.Count
    BuildIndependent = $true
    TestBuildIndependent = $true
    ProductionSourceFilesScanned = $productionSourceFiles.Count
    PackagingSourceFilesScanned = $packagingSourceFiles.Count
} | ConvertTo-Json -Depth 3
