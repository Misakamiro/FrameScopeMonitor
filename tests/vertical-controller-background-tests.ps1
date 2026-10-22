# Executes the production creation block against API call recorders. No HWND,
# browser, COM runtime, system configuration, or installed application is used.
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RepoRoot
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
$source = Get-Content -LiteralPath (Join-Path $RepoRoot 'desktop-preview/src-tauri/vendor/wry/src/webview2/mod.rs') -Raw -Encoding UTF8
$block = [regex]::Match($source, '(?s)let handler = CreateCoreWebView2CompositionControllerCompletedHandler::create\(.*?\}\)\);(?<creation>.*?)\s*let composition_controller = webview2_com::wait_with_pump\(rx\)\?\?;')
if (-not $block.Success) { throw 'Production composition controller creation block not found' }
$fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fixtures/vertical-controller-background.rs') -Raw -Encoding UTF8
if (-not $fixture.Contains('/* PRODUCTION_CREATION */')) { throw 'Fixture insertion point missing' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory 'vertical-controller-background.rs'
[IO.File]::WriteAllText($output, $fixture.Replace('/* PRODUCTION_CREATION */', $block.Groups['creation'].Value), [Text.UTF8Encoding]::new($false))
& rustc --edition=2021 --test -O $output -o (Join-Path $OutputDirectory 'vertical-controller-background-tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Controller API fixture compilation failed' }
& (Join-Path $OutputDirectory 'vertical-controller-background-tests.exe') --test-threads=1 --nocapture
if ($LASTEXITCODE -ne 0) { throw 'Composition controller background regression failed' }
