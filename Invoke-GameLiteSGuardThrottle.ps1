$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$scriptPath = Join-Path $projectRoot 'gamelite-auto-lightweight\Invoke-GameLiteSGuardThrottle.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Missing standalone GameLite script: $scriptPath"
}

& $scriptPath @args
