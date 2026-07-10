$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $root 'dependencies.lock.json'
$buildPath = Join-Path $root 'build.ps1'
$script:failures = New-Object 'System.Collections.Generic.List[string]'

function Assert-Contract {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        [void]$script:failures.Add($Message)
    }
}

function Normalize-SourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return (($Path -replace '^[.][\\/]+', '') -replace '/', '\').ToLowerInvariant()
}

function Get-ProductionCSharpSources {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $repositoryPath = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\')
    $sources = New-Object Collections.Generic.List[string]
    foreach ($productionRootName in @('src', 'packaging')) {
        $productionRoot = Join-Path $repositoryPath $productionRootName
        if (-not (Test-Path -LiteralPath $productionRoot -PathType Container)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $productionRoot -Recurse -File -Filter '*.cs') {
            $sources.Add((Normalize-SourcePath $file.FullName.Substring($repositoryPath.Length + 1)))
        }
    }
    return @($sources | Sort-Object -Unique)
}

function Get-UncoveredProductionCSharpSources {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$ProductionSources,

        [Parameter(Mandatory = $true)]
        [hashtable]$CoveredSources,

        [Parameter(Mandatory = $true)]
        [hashtable]$PackagingOnlySources
    )

    return @($ProductionSources | Where-Object {
            -not $CoveredSources.ContainsKey($_) -and -not $PackagingOnlySources.ContainsKey($_)
        } | Sort-Object -Unique)
}

function Assert-ProductionSourceCoverageSelfTest {
    $fixtureRoot = Join-Path $env:TEMP ('framescope-source-coverage-' + [guid]::NewGuid().ToString('N'))
    try {
        foreach ($directory in @('src', 'packaging', 'tests')) {
            New-Item -ItemType Directory -Path (Join-Path $fixtureRoot $directory) -Force | Out-Null
        }
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'src\covered.cs'), 'internal sealed class Covered { }')
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'packaging\missed.cs'), 'internal sealed class Missed { }')
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'tests\ignored.cs'), 'internal sealed class Ignored { }')

        $production = @(Get-ProductionCSharpSources -RepositoryRoot $fixtureRoot)
        $covered = @{ (Normalize-SourcePath 'src\covered.cs') = $true }
        $uncovered = @(Get-UncoveredProductionCSharpSources `
                -ProductionSources $production `
                -CoveredSources $covered `
                -PackagingOnlySources @{})
        Assert-Contract ($production.Count -eq 2) "Build-contract self-test must enumerate src and packaging production sources only (found: $($production -join ', '))."
        Assert-Contract (
            $uncovered.Count -eq 1 -and
            $uncovered[0] -ceq (Normalize-SourcePath 'packaging\missed.cs')
        ) "Build-contract self-test must reject a packaging source omitted from compiler inputs (found: $($uncovered -join ', '))."
    }
    finally {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Get-SourceArrayNamesFromBuildCommand {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.Language.CommandAst]$Command
    )

    $sourceArrayNames = @{}
    $elements = $Command.CommandElements
    for ($index = 1; $index -lt $elements.Count; $index++) {
        $parameter = $elements[$index]
        if ($parameter -isnot [System.Management.Automation.Language.CommandParameterAst] -or
            $parameter.ParameterName -ine 'Sources') {
            continue
        }

        $sourceArguments = @()
        if ($null -ne $parameter.Argument) {
            $sourceArguments += $parameter.Argument
        }
        for ($argumentIndex = $index + 1; $argumentIndex -lt $elements.Count; $argumentIndex++) {
            $argument = $elements[$argumentIndex]
            if ($argument -is [System.Management.Automation.Language.CommandParameterAst]) {
                break
            }
            $sourceArguments += $argument
        }

        foreach ($sourceArgument in $sourceArguments) {
            $variables = @($sourceArgument.FindAll({
                        param($node)
                        return $node -is [System.Management.Automation.Language.VariableExpressionAst]
                    }, $true))
            foreach ($variable in $variables) {
                $variableName = $variable.VariablePath.UserPath
                if ($variableName -match '(?i)Sources$') {
                    $sourceArrayNames[$variableName] = $true
                }
            }
        }
    }

    return @($sourceArrayNames.Keys | Sort-Object)
}

function Test-StaticSourceArrayDefinition {
    param([object[]]$Assignments)

    if ($Assignments.Count -ne 1) { return $false }
    $assignment = $Assignments[0]
    if ($assignment.Operator -ne [System.Management.Automation.Language.TokenKind]::Equals) { return $false }
    if ($assignment.Right -isnot [System.Management.Automation.Language.CommandExpressionAst] -or
        $assignment.Right.Expression -isnot [System.Management.Automation.Language.ArrayExpressionAst]) {
        return $false
    }

    $allowedNodeTypes = @(
        [System.Management.Automation.Language.CommandExpressionAst],
        [System.Management.Automation.Language.ArrayExpressionAst],
        [System.Management.Automation.Language.StatementBlockAst],
        [System.Management.Automation.Language.PipelineAst],
        [System.Management.Automation.Language.ArrayLiteralAst],
        [System.Management.Automation.Language.StringConstantExpressionAst]
    )
    foreach ($node in @($assignment.Right.FindAll({ param($candidate) return $true }, $true))) {
        $allowed = $false
        foreach ($allowedType in $allowedNodeTypes) {
            if ($allowedType.IsInstanceOfType($node)) {
                $allowed = $true
                break
            }
        }
        if (-not $allowed) { return $false }
    }
    return $true
}

function Get-RestoreNativeRuntimeVerificationCallCount {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.Language.ScriptBlockAst]$Ast
    )

    $restoreFunctions = @($Ast.FindAll({
                param($node)
                return $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ieq 'Restore-NativeDependencies'
            }, $true))
    if ($restoreFunctions.Count -ne 1) {
        return -1
    }

    $runtimeVerificationCalls = @($restoreFunctions[0].Body.FindAll({
                param($node)
                return $node -is [System.Management.Automation.Language.CommandAst] -and
                    $node.GetCommandName() -ieq 'Assert-LockedRuntimeFiles'
            }, $true))
    return $runtimeVerificationCalls.Count
}

function Test-LockedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [object]$Entry,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedFile,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256
    )

    $lockedFile = [string]$Entry.file
    $lockedSha256 = [string]$Entry.sha256
    Assert-Contract ($lockedFile -ceq $ExpectedFile) "$Name path must remain locked to $ExpectedFile (found: $lockedFile)."
    Assert-Contract ($lockedSha256 -ceq $ExpectedSha256) "$Name SHA256 must remain locked to $ExpectedSha256 (found: $lockedSha256)."

    $fullPath = Join-Path $root ($lockedFile -replace '/', '\')
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Assert-Contract $false "$Name locked file is missing: $lockedFile."
        return
    }

    $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash.ToUpperInvariant()
    Assert-Contract ($actualSha256 -ceq $lockedSha256) "$Name hash mismatch for ${lockedFile}: expected $lockedSha256, found $actualSha256."
}

Assert-Contract (Test-Path -LiteralPath $lockPath -PathType Leaf) 'dependencies.lock.json is required.'
Assert-Contract (Test-Path -LiteralPath $buildPath -PathType Leaf) 'build.ps1 is required.'

$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$buildText = Get-Content -Raw -LiteralPath $buildPath
$nativePackagesLockPath = Join-Path $root ([string]$lock.nuget.lockFile -replace '/', '\')

Assert-Contract ([string]$lock.microsoftWebView2 -ceq '1.0.3967.48') "Microsoft.Web.WebView2 must remain locked to 1.0.3967.48 (found: $($lock.microsoftWebView2))."
Assert-Contract ([string]$lock.libreHardwareMonitorLib -ceq '0.9.6') "LibreHardwareMonitorLib must remain locked to 0.9.6 (found: $($lock.libreHardwareMonitorLib))."
Assert-Contract ([string]$lock.nuget.source -ceq 'https://api.nuget.org/v3/index.json') "NuGet source must remain pinned to nuget.org (found: $($lock.nuget.source))."
Assert-Contract ([string]$lock.nuget.lockFile -ceq 'native-packages.lock.json') "Native packages lock path must remain native-packages.lock.json (found: $($lock.nuget.lockFile))."
Assert-Contract (Test-Path -LiteralPath $nativePackagesLockPath -PathType Leaf) 'native-packages.lock.json is required.'
$nativePackagesLock = Get-Content -Raw -LiteralPath $nativePackagesLockPath | ConvertFrom-Json
$nativeFrameworkLock = $nativePackagesLock.dependencies.'.NETFramework,Version=v4.7.2'
$nativeRidLock = $nativePackagesLock.dependencies.'.NETFramework,Version=v4.7.2/win-x64'
Assert-Contract ([string]$nativeFrameworkLock.'Microsoft.Web.WebView2'.resolved -ceq '1.0.3967.48') 'native-packages.lock.json must lock Microsoft.Web.WebView2 1.0.3967.48.'
Assert-Contract ([string]$nativeFrameworkLock.LibreHardwareMonitorLib.resolved -ceq '0.9.6') 'native-packages.lock.json must lock LibreHardwareMonitorLib 0.9.6.'
Assert-Contract ([string]$nativeRidLock.'Microsoft.Web.WebView2'.resolved -ceq '1.0.3967.48') 'native-packages.lock.json must lock the win-x64 WebView2 graph.'
Assert-Contract ([string]$nativeRidLock.LibreHardwareMonitorLib.resolved -ceq '0.9.6') 'native-packages.lock.json must lock the win-x64 LibreHardwareMonitor graph.'
Assert-Contract (@($lock.runtimeFiles.PSObject.Properties).Count -eq 16) 'dependencies.lock.json must lock all 16 shipped NuGet runtime files.'
foreach ($runtimeProperty in @($lock.runtimeFiles.PSObject.Properties)) {
    Assert-Contract ([long]$runtimeProperty.Value.length -gt 0) "Runtime file length must be positive: $($runtimeProperty.Name)."
    Assert-Contract ([string]$runtimeProperty.Value.sha256 -match '^[0-9A-F]{64}$') "Runtime file SHA256 is invalid: $($runtimeProperty.Name)."
}
Test-LockedFile -Name 'PresentMon' -Entry $lock.presentMon `
    -ExpectedFile 'tools/PresentMon-2.4.1-x64.exe' `
    -ExpectedSha256 'D74183E7AE630F72CD3690BE0373ECBFDC6CBB86578148AAB8FA2A7166068F34'
Test-LockedFile -Name 'WebView2 standalone installer' -Entry $lock.webView2StandaloneInstaller `
    -ExpectedFile 'packaging/MicrosoftEdgeWebView2RuntimeInstallerX64.exe' `
    -ExpectedSha256 '3A08103BED8A3D9AEFDFC9AC10A672EA69605163F2DCB08D76CFD3E0444511C9'

Assert-Contract (-not ($buildText -match '(?im)Select-Object\s+-Last\s+1')) 'build.ps1 must not select the latest locally installed package with Select-Object -Last 1.'
Assert-Contract (-not ($buildText -match '(?i)[.]nuget[\\/]packages')) 'build.ps1 must not resolve native dependencies from the user-wide NuGet package cache.'
Assert-Contract ($buildText.Contains("Join-Path `$root 'dependencies.lock.json'")) 'build.ps1 must read dependencies.lock.json from the repository root.'
Assert-Contract ($buildText.Contains('$webView2Version = [string]$lock.microsoftWebView2')) 'build.ps1 must resolve the WebView2 version from the lock manifest.'
Assert-Contract ($buildText.Contains('$libreHardwareMonitorVersion = [string]$lock.libreHardwareMonitorLib')) 'build.ps1 must resolve the LibreHardwareMonitor version from the lock manifest.'
Assert-Contract ($buildText.Contains("Join-Path `$root 'tools\.cache\nuget'")) 'build.ps1 must restore NuGet dependencies into tools\.cache\nuget.'
Assert-Contract ($buildText.Contains('<PackageReference Include="Microsoft.Web.WebView2" Version="[$webView2Version]" ExcludeAssets="all" />')) 'Microsoft.Web.WebView2 PackageReference must use the exact lock-derived version.'
Assert-Contract ($buildText.Contains('<PackageReference Include="LibreHardwareMonitorLib" Version="[$libreHardwareMonitorVersion]" />')) 'LibreHardwareMonitorLib PackageReference must use the exact lock-derived version.'
Assert-Contract ($buildText.Contains("Join-Path (Join-Path `$nugetCache 'microsoft.web.webview2') `$webView2Version")) 'build.ps1 must locate only the exact locked WebView2 package directory.'
Assert-Contract ($buildText.Contains("Join-Path (Join-Path `$nugetCache 'librehardwaremonitorlib') `$libreHardwareMonitorVersion")) 'build.ps1 must locate only the exact locked LibreHardwareMonitor package directory.'
Assert-Contract ($buildText.Contains('Assert-LockedFileHash')) 'build.ps1 must verify pinned file hashes before compiling.'
Assert-Contract ($buildText.Contains('<RestoreLockedMode>true</RestoreLockedMode>')) 'build.ps1 must enable NuGet locked restore mode.'
Assert-Contract ($buildText.Contains('--locked-mode')) 'build.ps1 must invoke dotnet restore in locked mode.'
Assert-Contract ($buildText.Contains('--source $NugetSource')) 'build.ps1 must pass the pinned NuGet source explicitly.'
Assert-Contract ($buildText.Contains("Copy-Item -LiteralPath `$PackagesLockPath -Destination (Join-Path `$temp 'packages.lock.json')")) 'build.ps1 must copy the committed native packages lock beside the restore project.'
Assert-Contract ($buildText.Contains('$lock.presentMon')) 'build.ps1 must verify the locked PresentMon file.'
Assert-Contract ($buildText.Contains('$lock.webView2StandaloneInstaller')) 'build.ps1 must verify the locked WebView2 standalone installer.'
Assert-Contract ($buildText.Contains('Resolved NuGet dependency')) 'build.ps1 must print resolved NuGet package versions.'
Assert-Contract ($buildText.Contains('Verified pinned file')) 'build.ps1 must print verified pinned-file hashes.'

Assert-ProductionSourceCoverageSelfTest

$tokens = $null
$parseErrors = $null
$buildAst = [System.Management.Automation.Language.Parser]::ParseFile($buildPath, [ref]$tokens, [ref]$parseErrors)
Assert-Contract ($parseErrors.Count -eq 0) "build.ps1 must parse successfully (parse errors: $($parseErrors.Count))."

$runtimeVerificationFixtureTokens = $null
$runtimeVerificationFixtureErrors = $null
$runtimeVerificationFixtureAst = [System.Management.Automation.Language.Parser]::ParseInput(
    @'
function Assert-LockedRuntimeFiles { }
function Restore-NativeDependencies {
    # Assert-LockedRuntimeFiles is intentionally not invoked.
    $message = 'Assert-LockedRuntimeFiles'
}
'@,
    [ref]$runtimeVerificationFixtureTokens,
    [ref]$runtimeVerificationFixtureErrors
)
$runtimeVerificationFixtureCallCount = Get-RestoreNativeRuntimeVerificationCallCount -Ast $runtimeVerificationFixtureAst
Assert-Contract (
    $runtimeVerificationFixtureErrors.Count -eq 0 -and
    $runtimeVerificationFixtureCallCount -ne 1
) "Build-contract self-test must reject a Restore-NativeDependencies body without an Assert-LockedRuntimeFiles call, even when declarations, strings, and comments retain that text (found: $runtimeVerificationFixtureCallCount)."

$runtimeVerificationCallCount = Get-RestoreNativeRuntimeVerificationCallCount -Ast $buildAst
Assert-Contract (
    $runtimeVerificationCallCount -eq 1
) "Restore-NativeDependencies must call Assert-LockedRuntimeFiles exactly once (found: $runtimeVerificationCallCount)."

$assignments = @($buildAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.AssignmentStatementAst]
        }, $true))
$sourceAssignments = @{}
foreach ($assignment in $assignments) {
    if ($assignment.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
        continue
    }

    $variableName = $assignment.Left.VariablePath.UserPath
    if ($variableName -match '(?i)Sources$') {
        if (-not $sourceAssignments.ContainsKey($variableName)) {
            $sourceAssignments[$variableName] = New-Object Collections.ArrayList
        }
        [void]$sourceAssignments[$variableName].Add($assignment)
    }
}
$validSourceAssignments = @{}
foreach ($variableName in $sourceAssignments.Keys) {
    $items = @($sourceAssignments[$variableName])
    $isStatic = Test-StaticSourceArrayDefinition -Assignments $items
    Assert-Contract $isStatic "Source array `$$variableName must have one static '=' array definition and no later overwrite, +=, or -= mutation."
    if ($isStatic) { $validSourceAssignments[$variableName] = $items[0] }
}

$buildCommands = @($buildAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -eq 'Invoke-CSharpBuild'
        }, $true))
$usedSourceArrays = @{}
foreach ($command in $buildCommands) {
    foreach ($variableName in @(Get-SourceArrayNamesFromBuildCommand -Command $command)) {
        $usedSourceArrays[$variableName] = $true
    }
}

$fixtureTokens = $null
$fixtureParseErrors = $null
$fixtureAst = [System.Management.Automation.Language.Parser]::ParseInput(
    'Invoke-CSharpBuild -References $monitorSources -Sources $commonCoreSources',
    [ref]$fixtureTokens,
    [ref]$fixtureParseErrors
)
$fixtureCommand = $fixtureAst.Find({
        param($node)
        return $node -is [System.Management.Automation.Language.CommandAst]
    }, $true)
$fixtureSourceArrays = @(Get-SourceArrayNamesFromBuildCommand -Command $fixtureCommand)
Assert-Contract (
    $fixtureParseErrors.Count -eq 0 -and
    $fixtureSourceArrays.Count -eq 1 -and
    $fixtureSourceArrays[0] -ceq 'commonCoreSources'
) "Build-contract self-test must bind source coverage only to -Sources arguments (found: $($fixtureSourceArrays -join ', '))."

$mutationTokens = $null
$mutationErrors = $null
$mutationAst = [System.Management.Automation.Language.Parser]::ParseInput(
    "`$fixtureSources = @('src\a.cs'); `$fixtureSources -= 'src\a.cs'",
    [ref]$mutationTokens,
    [ref]$mutationErrors
)
$mutationAssignments = @($mutationAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                $node.Left.VariablePath.UserPath -eq 'fixtureSources'
        }, $true))
Assert-Contract (
    $mutationErrors.Count -eq 0 -and
    -not (Test-StaticSourceArrayDefinition -Assignments $mutationAssignments)
) 'Build-contract self-test must reject overwritten or incrementally mutated source arrays.'

$indexedArrayTokens = $null
$indexedArrayErrors = $null
$indexedArrayAst = [System.Management.Automation.Language.Parser]::ParseInput(
    "`$fixtureSources = @('src\a.cs', 'src\b.cs')[0]",
    [ref]$indexedArrayTokens,
    [ref]$indexedArrayErrors
)
$indexedArrayAssignments = @($indexedArrayAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
                $node.Left.VariablePath.UserPath -eq 'fixtureSources'
        }, $true))
Assert-Contract (
    $indexedArrayErrors.Count -eq 0 -and
    -not (Test-StaticSourceArrayDefinition -Assignments $indexedArrayAssignments)
) 'Build-contract self-test must reject indexed or otherwise computed source arrays.'

foreach ($requiredArray in @(
        'commonCoreSources',
        'monitorSources',
        'reportSources',
        'samplerSources',
        'systemSamplerSources',
        'setupSources',
        'uninstallerSources',
        'legacyCleanupSources'
    )) {
    Assert-Contract ($validSourceAssignments.ContainsKey($requiredArray)) "build.ps1 must define `$$requiredArray as one static array."
    Assert-Contract ($usedSourceArrays.ContainsKey($requiredArray)) "build.ps1 must pass `$$requiredArray to Invoke-CSharpBuild."
}
Assert-Contract ($validSourceAssignments.ContainsKey('packagingOnlySources')) 'build.ps1 must define the explicit $packagingOnlySources exemption as one static array.'
Assert-Contract ($buildCommands.Count -gt 0) 'build.ps1 must compile targets through Invoke-CSharpBuild.'
Assert-Contract (([regex]::Matches($buildText, '(?im)^\s*&\s*\$csc\b')).Count -eq 1) 'build.ps1 must have exactly one direct csc invocation, inside Invoke-CSharpBuild.'

$coveredSources = @{}
$packagingOnlySources = @{}
$sourceStrings = @($buildAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                $node.Value -match '^(?:[.][\\/])?(?:src|packaging)[\\/].+[.]cs$'
        }, $true))
foreach ($sourceString in $sourceStrings) {
    $parent = $sourceString.Parent
    while ($null -ne $parent -and $parent -isnot [System.Management.Automation.Language.AssignmentStatementAst]) {
        $parent = $parent.Parent
    }
    if ($null -eq $parent -or $parent.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
        continue
    }

    $arrayName = $parent.Left.VariablePath.UserPath
    if (-not $validSourceAssignments.ContainsKey($arrayName) -or $validSourceAssignments[$arrayName] -ne $parent) {
        continue
    }
    $normalizedPath = Normalize-SourcePath $sourceString.Value
    if ($arrayName -ieq 'packagingOnlySources') {
        $packagingOnlySources[$normalizedPath] = $true
    }
    elseif ($usedSourceArrays.ContainsKey($arrayName)) {
        $coveredSources[$normalizedPath] = $true
    }
}

$productionSources = @(Get-ProductionCSharpSources -RepositoryRoot $root)
$uncoveredSources = @(Get-UncoveredProductionCSharpSources `
        -ProductionSources $productionSources `
        -CoveredSources $coveredSources `
        -PackagingOnlySources $packagingOnlySources)
Assert-Contract ($uncoveredSources.Count -eq 0) "Production C# sources are not covered by a compiled target or packagingOnlySources: $($uncoveredSources -join ', ')."

if ($script:failures.Count -gt 0) {
    foreach ($failure in $script:failures) {
        Write-Host "FAIL: $failure" -ForegroundColor Red
    }
    throw "FrameScope build contract failed with $($script:failures.Count) violation(s)."
}

Write-Host "PASS: FrameScope build contract verified $($productionSources.Count) production C# sources and all locked dependencies."
