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

Assert-Contract ([string]$lock.microsoftWebView2 -ceq '1.0.3967.48') "Microsoft.Web.WebView2 must remain locked to 1.0.3967.48 (found: $($lock.microsoftWebView2))."
Assert-Contract ([string]$lock.libreHardwareMonitorLib -ceq '0.9.6') "LibreHardwareMonitorLib must remain locked to 0.9.6 (found: $($lock.libreHardwareMonitorLib))."
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
Assert-Contract ($buildText.Contains('$lock.presentMon')) 'build.ps1 must verify the locked PresentMon file.'
Assert-Contract ($buildText.Contains('$lock.webView2StandaloneInstaller')) 'build.ps1 must verify the locked WebView2 standalone installer.'
Assert-Contract ($buildText.Contains('Resolved NuGet dependency')) 'build.ps1 must print resolved NuGet package versions.'
Assert-Contract ($buildText.Contains('Verified pinned file')) 'build.ps1 must print verified pinned-file hashes.'

$tokens = $null
$parseErrors = $null
$buildAst = [System.Management.Automation.Language.Parser]::ParseFile($buildPath, [ref]$tokens, [ref]$parseErrors)
Assert-Contract ($parseErrors.Count -eq 0) "build.ps1 must parse successfully (parse errors: $($parseErrors.Count))."

$assignments = @($buildAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.AssignmentStatementAst]
        }, $true))
$assignedSourceArrays = @{}
foreach ($assignment in $assignments) {
    if ($assignment.Left -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
        continue
    }

    $variableName = $assignment.Left.VariablePath.UserPath
    if ($variableName -match '(?i)Sources$') {
        $assignedSourceArrays[$variableName] = $true
    }
}

$buildCommands = @($buildAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.CommandAst] -and
                $node.GetCommandName() -eq 'Invoke-CSharpBuild'
        }, $true))
$usedSourceArrays = @{}
foreach ($command in $buildCommands) {
    $variables = @($command.FindAll({
                param($node)
                return $node -is [System.Management.Automation.Language.VariableExpressionAst]
            }, $true))
    foreach ($variable in $variables) {
        $variableName = $variable.VariablePath.UserPath
        if ($variableName -match '(?i)Sources$') {
            $usedSourceArrays[$variableName] = $true
        }
    }
}

foreach ($requiredArray in @('commonCoreSources', 'monitorSources', 'reportSources', 'samplerSources')) {
    Assert-Contract ($assignedSourceArrays.ContainsKey($requiredArray)) "build.ps1 must define `$$requiredArray."
    Assert-Contract ($usedSourceArrays.ContainsKey($requiredArray)) "build.ps1 must pass `$$requiredArray to Invoke-CSharpBuild."
}
Assert-Contract ($assignedSourceArrays.ContainsKey('packagingOnlySources')) 'build.ps1 must define the explicit $packagingOnlySources exemption array.'
Assert-Contract ($buildCommands.Count -gt 0) 'build.ps1 must compile targets through Invoke-CSharpBuild.'
Assert-Contract (([regex]::Matches($buildText, '(?im)^\s*&\s*\$csc\b')).Count -eq 1) 'build.ps1 must have exactly one direct csc invocation, inside Invoke-CSharpBuild.'

$coveredSources = @{}
$packagingOnlySources = @{}
$sourceStrings = @($buildAst.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                $node.Value -match '^(?:[.][\\/])?src[\\/].+[.]cs$'
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
    $normalizedPath = Normalize-SourcePath $sourceString.Value
    if ($arrayName -ieq 'packagingOnlySources') {
        $packagingOnlySources[$normalizedPath] = $true
    }
    elseif ($usedSourceArrays.ContainsKey($arrayName)) {
        $coveredSources[$normalizedPath] = $true
    }
}

$productionSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Recurse -File -Filter '*.cs' |
    ForEach-Object { Normalize-SourcePath $_.FullName.Substring($root.Length + 1) } |
    Sort-Object -Unique)
$uncoveredSources = @($productionSources | Where-Object {
        -not $coveredSources.ContainsKey($_) -and -not $packagingOnlySources.ContainsKey($_)
    })
Assert-Contract ($uncoveredSources.Count -eq 0) "Production C# sources are not covered by a compiled target or packagingOnlySources: $($uncoveredSources -join ', ')."

if ($script:failures.Count -gt 0) {
    foreach ($failure in $script:failures) {
        Write-Host "FAIL: $failure" -ForegroundColor Red
    }
    throw "FrameScope build contract failed with $($script:failures.Count) violation(s)."
}

Write-Host "PASS: FrameScope build contract verified $($productionSources.Count) production C# sources and all locked dependencies."
