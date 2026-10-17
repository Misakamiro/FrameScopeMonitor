param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot), [switch]$Check)
$ErrorActionPreference = 'Stop'
$version = (Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'VERSION')).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.[1-9][0-9]*)?$') { throw "Invalid product version: $version" }
$files = @('desktop-preview/package.json','desktop-preview/package-lock.json','desktop-preview/src-tauri/tauri.conf.json',
    'desktop-preview/src-tauri/Cargo.toml','desktop-preview/installer/src-tauri/tauri.conf.json','desktop-preview/installer/src-tauri/Cargo.toml',
    'src/frontend/package.json','src/frontend/package-lock.json')
foreach ($relative in $files) {
    $path = Join-Path $RepoRoot $relative
    $source = [IO.File]::ReadAllText($path)
    $pattern = if ($relative.EndsWith('.toml')) { '(?m)^(version = ")[^"]+("\r?$)' } else { '("version"\s*:\s*")[^"]+(")' }
    # lockfiles include dependency versions. Only root and packages[""] are product metadata.
    $count = if ($relative.EndsWith('package-lock.json')) { 2 } else { 1 }
    $regex = [regex]::new($pattern)
    $next = $regex.Replace($source,('${1}' + $version + '${2}'),$count)
    if ($next -ne $source) {
        if ($Check) { throw "Run tools/Sync-FrameScopeVersion.ps1: $relative" }
        [IO.File]::WriteAllText($path,$next,[Text.UTF8Encoding]::new($false))
    }
}
