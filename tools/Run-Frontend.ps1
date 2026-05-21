param(
    [Parameter(Position = 0)]
    [ValidateSet('install', 'typecheck', 'test', 'build', 'verify', 'npm')]
    [string]$Command = 'verify',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$NpmArgs = @(),

    [string]$NodeExe = $env:FRAMESCOPE_NODE_EXE,

    [string]$NpmVersion = '10.9.2',

    [switch]$NoBootstrap
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$frontendRoot = Join-Path $repoRoot 'src\frontend'
$npmBootstrapRoot = Join-Path $repoRoot 'tools\.cache\frontend-npm'

function Test-NodeCandidate {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) { return $false }
    if (-not (Test-Path -LiteralPath $Candidate)) { return $false }

    try {
        $null = & $Candidate --version 2>$null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

function Resolve-NodeExe {
    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($NodeExe)) {
        $candidates.Add($NodeExe)
    }

    $codexNode = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
    $candidates.Add($codexNode)

    foreach ($commandInfo in Get-Command node -All -ErrorAction SilentlyContinue) {
        if (-not [string]::IsNullOrWhiteSpace($commandInfo.Source)) {
            $candidates.Add($commandInfo.Source)
        }
    }

    foreach ($candidate in $candidates) {
        if (Test-NodeCandidate -Candidate $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw 'No usable node.exe was found. Install Node.js 18+ or set FRAMESCOPE_NODE_EXE to a working node.exe path.'
}

function Get-NpmCliPath {
    param([string]$ResolvedNodeExe)

    $cachedCli = Join-Path $npmBootstrapRoot "npm-$NpmVersion\package\bin\npm-cli.js"
    if (Test-Path -LiteralPath $cachedCli) {
        return (Resolve-Path -LiteralPath $cachedCli).Path
    }

    $nodeRoot = (Resolve-Path -LiteralPath (Join-Path (Split-Path -Parent $ResolvedNodeExe) '..') -ErrorAction SilentlyContinue)
    if ($null -ne $nodeRoot) {
        $bundledCli = Join-Path $nodeRoot.Path 'node_modules\npm\bin\npm-cli.js'
        if (Test-Path -LiteralPath $bundledCli) {
            return (Resolve-Path -LiteralPath $bundledCli).Path
        }
    }

    if ($NoBootstrap) {
        throw "npm is unavailable and bootstrap is disabled. Install npm $NpmVersion or rerun without -NoBootstrap."
    }

    New-Item -ItemType Directory -Path $npmBootstrapRoot -Force | Out-Null

    $archive = Join-Path $npmBootstrapRoot "npm-$NpmVersion.tgz"
    $versionRoot = Join-Path $npmBootstrapRoot "npm-$NpmVersion"
    $uri = "https://registry.npmjs.org/npm/-/npm-$NpmVersion.tgz"

    if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
        throw 'npm bootstrap requires tar.exe. Install Node.js with npm, or use a Windows environment that includes tar.exe.'
    }

    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    } catch {
        # Older PowerShell hosts may already have a suitable TLS default.
    }

    Write-Host "Bootstrapping npm $NpmVersion from $uri"
    Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $archive

    if (Test-Path -LiteralPath $versionRoot) {
        Remove-Item -LiteralPath $versionRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null

    & tar.exe -xzf $archive -C $versionRoot
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed while extracting npm $NpmVersion with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $cachedCli)) {
        throw "npm bootstrap did not create expected CLI path: $cachedCli"
    }

    return (Resolve-Path -LiteralPath $cachedCli).Path
}

function Invoke-FrontendNpm {
    param([string[]]$Arguments)

    $env:npm_config_update_notifier = 'false'
    $env:npm_config_audit = 'false'
    $env:npm_config_fund = 'false'

    & $script:ResolvedNodeExe $script:NpmCliPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "npm $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $frontendRoot)) {
    throw "Frontend directory not found: $frontendRoot"
}

$script:ResolvedNodeExe = Resolve-NodeExe
$script:NpmCliPath = Get-NpmCliPath -ResolvedNodeExe $script:ResolvedNodeExe

$nodeDir = Split-Path -Parent $script:ResolvedNodeExe
$env:PATH = $nodeDir + [IO.Path]::PathSeparator + $env:PATH

Write-Host "Frontend root: $frontendRoot"
Write-Host "Node: $script:ResolvedNodeExe"
Write-Host "npm CLI: $script:NpmCliPath"

Push-Location $frontendRoot
try {
    switch ($Command) {
        'install' {
            Invoke-FrontendNpm -Arguments @('ci', '--cache', (Join-Path $npmBootstrapRoot 'cache'), '--prefer-offline')
        }
        'typecheck' {
            Invoke-FrontendNpm -Arguments @('run', 'typecheck')
        }
        'test' {
            Invoke-FrontendNpm -Arguments @('test')
        }
        'build' {
            Invoke-FrontendNpm -Arguments @('run', 'build')
        }
        'verify' {
            Invoke-FrontendNpm -Arguments @('ci', '--cache', (Join-Path $npmBootstrapRoot 'cache'), '--prefer-offline')
            Invoke-FrontendNpm -Arguments @('run', 'typecheck')
            Invoke-FrontendNpm -Arguments @('test')
            Invoke-FrontendNpm -Arguments @('run', 'build')
        }
        'npm' {
            if ($NpmArgs.Count -eq 0) {
                throw 'Command npm requires additional npm arguments, for example: tools\Run-Frontend.ps1 npm view vite version'
            }
            Invoke-FrontendNpm -Arguments $NpmArgs
        }
    }
}
finally {
    Pop-Location
}
