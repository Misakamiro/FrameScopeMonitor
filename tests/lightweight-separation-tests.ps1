param(
    [string]$StandaloneProjectRoot = $env:FRAMESCOPE_GAMELITE_ROOT
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($StandaloneProjectRoot)) {
    $StandaloneProjectRoot = Join-Path (Split-Path -Parent $root) 'gamelite-auto-lightweight'
}
$newProjectRoot = [IO.Path]::GetFullPath($StandaloneProjectRoot)
$scriptNames = @(
    'Install-GameLiteAutoTrigger.ps1',
    'Check-GameLiteAutoTrigger.ps1',
    'Remove-GameLiteAutoTrigger.ps1',
    'GameLiteSession.ps1',
    'Enter-GameLite.ps1',
    'Exit-GameLite.ps1',
    'Invoke-GameLiteSGuardThrottle.ps1'
)

function Assert-GameLiteBridgeTest {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-GameLiteParse {
    param([string]$Path)

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors -and $errors.Count -gt 0) {
        throw ("PowerShell parse failed for {0}: {1}" -f $Path, ($errors | ForEach-Object { $_.Message }) -join '; ')
    }
}

Assert-GameLiteBridgeTest (Test-Path -LiteralPath $newProjectRoot) "Standalone GameLite project is missing: $newProjectRoot"

foreach ($name in $scriptNames) {
    $wrapperPath = Join-Path $root $name
    $standalonePath = Join-Path $newProjectRoot $name
    Assert-GameLiteBridgeTest (Test-Path -LiteralPath $wrapperPath) "Compatibility wrapper is missing: $name"
    Assert-GameLiteBridgeTest (Test-Path -LiteralPath $standalonePath) "Standalone GameLite script is missing: $standalonePath"

    Test-GameLiteParse -Path $wrapperPath
    Test-GameLiteParse -Path $standalonePath

    $wrapperText = Get-Content -Raw -LiteralPath $wrapperPath
    Assert-GameLiteBridgeTest ($wrapperText -match 'gamelite-auto-lightweight') "Wrapper $name does not point to the standalone GameLite project."
    Assert-GameLiteBridgeTest ($wrapperText -match '&\s+\$scriptPath\s+@args') "Wrapper $name does not forward @args."
    Assert-GameLiteBridgeTest ($wrapperText -notmatch 'scripts\\lightweight') "Wrapper $name still points at the old scripts\lightweight implementation."
}

foreach ($name in @('Install-GameLiteAutoTrigger.cmd', 'Check-GameLiteAutoTrigger.cmd', 'Remove-GameLiteAutoTrigger.cmd')) {
    $cmdText = Get-Content -Raw -LiteralPath (Join-Path $root $name)
    Assert-GameLiteBridgeTest ($cmdText -match '%\*') "CMD launcher $name does not forward parameters."
}

$oldLightweightRoot = Join-Path $root 'scripts\lightweight'
$oldCoreScripts = @()
if (Test-Path -LiteralPath $oldLightweightRoot) {
    $oldCoreScripts = @(Get-ChildItem -LiteralPath $oldLightweightRoot -Filter '*.ps1' -ErrorAction SilentlyContinue)
}
Assert-GameLiteBridgeTest ($oldCoreScripts.Count -eq 0) 'Old scripts\lightweight still contains GameLite core scripts.'

$buildText = Get-Content -Raw -LiteralPath (Join-Path $root 'build.ps1')
Assert-GameLiteBridgeTest ($buildText -notmatch '(?i)GameLite|lightweight|AutoTrigger|SGuard|WMI') 'build.ps1 references GameLite automation.'

$testBuildText = Get-Content -Raw -LiteralPath (Join-Path $root 'tests\Build-FrameScopeTests.ps1')
Assert-GameLiteBridgeTest ($testBuildText -notmatch '(?i)GameLite|lightweight|AutoTrigger|SGuard|WMI') 'tests\Build-FrameScopeTests.ps1 references GameLite automation.'

$forbiddenProductionAutomation = '(?i)GameLite|AutoTrigger|SGuard|Win32_Process(?:Start|Stop)Trace|__EventFilter|CommandLineEventConsumer'
$productionSourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter '*.cs')
$packagingSourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'packaging') -Recurse -File | Where-Object {
    $_.Extension -in @('.cs', '.ps1', '.cmd')
})

foreach ($file in @($productionSourceFiles + $packagingSourceFiles)) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    Assert-GameLiteBridgeTest ($text -notmatch $forbiddenProductionAutomation) "FrameScope production file references GameLite automation or a process-trigger WMI primitive: $($file.FullName)"
}

[pscustomobject]@{
    Status = 'PASS'
    CompatibilityWrappers = $scriptNames.Count
    CmdLaunchersForwardArgs = 3
    StandaloneProject = $newProjectRoot
    OldCoreScriptsRemaining = $oldCoreScripts.Count
    BuildIndependent = $true
    TestBuildIndependent = $true
    ProductionSourceFilesScanned = $productionSourceFiles.Count
    PackagingSourceFilesScanned = $packagingSourceFiles.Count
} | ConvertTo-Json -Depth 3
