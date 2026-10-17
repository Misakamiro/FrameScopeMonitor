param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$owned = Join-Path ([IO.Path]::GetTempPath()) ('framescope-isolation-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $owned | Out-Null
Write-Host "Retained fixture: $owned"
$source = Join-Path $owned 'input'
New-Item -ItemType Directory -Path "$source\tools", "$source\packaging", "$source\src", "$source\desktop-preview\installer" -Force | Out-Null
$gitExe = (Get-Command git).Source
& $gitExe -C $source init --quiet
foreach ($name in @('tools\Build-IsolatedInstaller.ps1', 'tools\Verify-IsolatedBuildStage.ps1', 'desktop-preview\installer\pack-payload.ps1')) {
    Copy-Item -LiteralPath (Join-Path $RepoRoot $name) -Destination (Join-Path $source $name)
}
[IO.File]::WriteAllText("$source\src\dirty.cs", 'dirty current source')
$unicodeName = ([string][char]0x5B57) + '.cs'
[IO.File]::WriteAllText((Join-Path "$source\src" $unicodeName), 'unicode source fixture')
New-Item -ItemType Directory -Path "$source\src\hardware-service\src" -Force | Out-Null
[IO.File]::WriteAllText("$source\src\hardware-service\Cargo.toml", "[package]`nname = 'isolated_cargo_fixture'`nversion = '0.0.0'`nedition = '2021'`n")
[IO.File]::WriteAllText("$source\src\hardware-service\Cargo.lock", "version = 4`n[[package]]`nname = 'isolated_cargo_fixture'`nversion = '0.0.0'`n")
[IO.File]::WriteAllText("$source\src\hardware-service\src\lib.rs", '')
[IO.File]::WriteAllText("$source\.gitignore", "*.exe`nsrc/retained.cs`n")
[IO.File]::WriteAllText("$source\src\retained.cs", 'tracked source covered by a later ignore rule')
[IO.File]::WriteAllText("$source\tools\采集.exe", 'pinned present fixture')
[IO.File]::WriteAllText("$source\packaging\webview.exe", 'pinned webview fixture')
[IO.File]::WriteAllText("$source\packaging\PawnIO_setup.exe", 'explicit pawn fixture')
[IO.File]::WriteAllText("$source\framescope-config.json", 'PRIVATE USER DATA')
[ordered]@{ runtimeFiles = @{ 'fixture.dll' = @{} }; presentMon = @{ file = 'tools/采集.exe'; sha256 = (Get-FileHash "$source\tools\采集.exe").Hash };
    webView2StandaloneInstaller = @{ file = 'packaging/webview.exe'; sha256 = (Get-FileHash "$source\packaging\webview.exe").Hash } } |
    ConvertTo-Json -Depth 6 | Set-Content "$source\dependencies.lock.json" -Encoding UTF8
$lockPath = Join-Path $source 'dependencies.lock.json'
[IO.File]::WriteAllText($lockPath, [IO.File]::ReadAllText($lockPath, [Text.Encoding]::UTF8), [Text.UTF8Encoding]::new($false))
& $gitExe -C $source add -A
& $gitExe -C $source add --force -- src/retained.cs
# Only HEAD and the empty private clone boundary are synthetic. Enumeration,
# private-index staging, byte copying, provenance and verification use production code.
function git {
    if ($env:GIT_DIR -or $env:GIT_INDEX_FILE -or $env:CARGO_TARGET_DIR -or $env:CARGO_BUILD_TARGET_DIR) { throw 'Inherited output environment leaked into snapshot boundary' }
    if ($args -contains 'rev-parse') { $global:LASTEXITCODE = 0; return 'fixture-head-no-commit' }
    if ($args -contains 'clone') { & $gitExe init --quiet $args[-1]; return }
    & $gitExe @args
}
$saved = @{}
foreach ($name in @('GIT_DIR', 'GIT_INDEX_FILE', 'CARGO_TARGET_DIR', 'CARGO_BUILD_TARGET_DIR')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, (Join-Path $owned 'must-not-write'), 'Process')
}
try {
    $output = Join-Path $owned 'output'
    & "$source\tools\Build-IsolatedInstaller.ps1" -RepoRoot $source -OutputDirectory $output -SnapshotOnly
    $inventoryPath = Join-Path $output 'snapshot-inputs.json'
    $inventoryText = [IO.File]::ReadAllText($inventoryPath, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText($inventoryPath, $inventoryText, [Text.UTF8Encoding]::new($false))
    if ($env:CARGO_TARGET_DIR -ne (Join-Path $owned 'must-not-write')) { throw 'Parent environment was not restored' }
    if (Test-Path -LiteralPath (Join-Path $owned 'must-not-write')) { throw 'Build boundary wrote an inherited external output directory' }
    if (Test-Path "$output\source\framescope-config.json") { throw 'Snapshot copied real config' }
    if ((Get-FileHash "$source\src\dirty.cs").Hash -ne (Get-FileHash "$output\source\src\dirty.cs").Hash) { throw 'Dirty bytes were not copied' }
    if ((Get-FileHash "$source\src\retained.cs").Hash -ne (Get-FileHash "$output\source\src\retained.cs").Hash) { throw 'Tracked source covered by ignore rule disappeared from the snapshot' }
    if ((Get-Content "$output\source\.git\config" -Raw) -match 'worktree') { throw 'Snapshot is not a private Git repository' }
    & "$source\tools\Build-IsolatedInstaller.ps1" -RepoRoot $source -OutputDirectory $output -VerifyOnly
    $unicodePath = Join-Path "$source\src" $unicodeName
    [IO.File]::WriteAllText($unicodePath, 'unicode source changed')
    $rejected = $false
    try { & "$source\tools\Build-IsolatedInstaller.ps1" -RepoRoot $source -OutputDirectory $output -VerifyOnly } catch { $rejected = $true }
    if (-not $rejected) { throw 'Changed Unicode source accepted' }
    [IO.File]::WriteAllText($unicodePath, 'unicode source fixture')
    Write-Host 'PASS UTF-8 inventory without BOM preserves Unicode input and detects its hash change'
    Write-Host 'PASS private snapshot copies dirty source and explicit dependencies; excludes config; restores environment'
    [IO.File]::WriteAllText("$source\src\dirty.cs", 'changed during build')
    $rejected = $false
    try { & "$source\tools\Build-IsolatedInstaller.ps1" -RepoRoot $source -OutputDirectory $output -VerifyOnly } catch { $rejected = $true }
    if (-not $rejected) { throw 'Changed original source accepted' }
    [IO.File]::WriteAllText("$source\src\dirty.cs", 'dirty current source')
    Write-Host 'PASS original source drift blocks final pack'
    function Assert-ChangedInputRejected([string]$Name, [string]$Path, [switch]$Added, [switch]$Deleted) {
        $bytes = if (-not $Added) { [IO.File]::ReadAllBytes($Path) }
        try {
            if ($Added) { [IO.File]::WriteAllText($Path, 'unapproved source') }
            elseif ($Deleted) { Remove-Item -LiteralPath $Path }
            else { [byte[]]$changed = $bytes.Clone(); $changed[0] = $changed[0] -bxor 1; [IO.File]::WriteAllBytes($Path, $changed) }
            $rejected = $false
            try { & "$source\tools\Build-IsolatedInstaller.ps1" -RepoRoot $source -OutputDirectory $output -VerifyOnly } catch { $rejected = $true }
            if (-not $rejected) { throw "Changed input accepted: $Name" }
            Write-Host "PASS $Name"
        } finally {
            if ($Added) { Remove-Item -LiteralPath $Path -ErrorAction SilentlyContinue }
            else { [IO.File]::WriteAllBytes($Path, $bytes) }
        }
    }
    foreach ($root in @($source, "$output\source")) {
        $label = if ($root -eq $source) { 'original' } else { 'snapshot' }
        Assert-ChangedInputRejected "$label same-length ASCII one-byte change" "$root\src\dirty.cs"
        Assert-ChangedInputRejected "$label pinned Unicode dependency change" "$root\tools\采集.exe"
        Assert-ChangedInputRejected "$label added source" "$root\src\added.cs" -Added
        Assert-ChangedInputRejected "$label deleted source" "$root\src\dirty.cs" -Deleted
    }
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
        else { Set-Item -LiteralPath ('Env:' + $name) -Value $saved[$name] }
    }
    # Build only synthetic payload files inside the snapshot, never product binaries.
    $snapshot = "$output\source"
    $backend = "$snapshot\desktop-preview\src-tauri\resources\backend"
    $release = "$snapshot\desktop-preview\src-tauri\target\release"
    New-Item -ItemType Directory -Path $backend, $release, "$snapshot\desktop-preview\installer\src-tauri" -Force | Out-Null
    foreach ($name in @('FrameScopeHardwareService.exe', 'FrameScopeHardwareProvider.exe', 'FrameScopeMonitor.exe', 'FrameScopeProcessSampler.exe',
        'FrameScopeSystemSampler.exe', 'FrameScopeReportGenerator.exe', 'FrameScopeUninstaller.exe', 'LICENSE', 'LICENSE.EXTRA', 'PRIVACY.md',
        'THIRD-PARTY-NOTICES.md', 'README-FrameScopeMonitor.txt', 'FrameScopeBuildInfo.json', 'Restore-FrameScope.cmd', 'tools\采集.exe', 'fixture.dll')) {
        $path = Join-Path $backend $name
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path, "fixture $name")
    }
    foreach ($name in @('fonts\MiSansVF.ttf', 'dependencies\PawnIO\PawnIO_setup.exe')) {
        $path = "$snapshot\desktop-preview\src-tauri\resources\$name"
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($path, "fixture $name")
    }
    [IO.File]::WriteAllText("$release\FrameScope.exe", 'synthetic cargo host')
    $transform = Join-Path $owned 'host-transform.ps1'
    [IO.File]::WriteAllText($transform, 'param([string]$HostExecutable); [IO.File]::AppendAllText($HostExecutable, " transformed")')
    . "$snapshot\desktop-preview\installer\pack-payload.ps1" -RepoRoot $snapshot -FunctionsOnly
    & "$snapshot\tools\Verify-IsolatedBuildStage.ps1" -RepoRoot $snapshot
    New-Item -ItemType Directory -Path (Join-Path $output '.cargo') -Force | Out-Null
    $ancestorConfig = Join-Path $output '.cargo\config.toml'
    [IO.File]::WriteAllText($ancestorConfig, "[build]`n`"target-dir`" = '" + (Join-Path $owned 'must-not-write').Replace('\', '/') + "'")
    $rejected = $false
    try { & "$snapshot\tools\Verify-IsolatedBuildStage.ps1" -RepoRoot $snapshot } catch {
        if ($_ -notlike '*Cargo output escapes*') { throw }
        $rejected = $true
    }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $owned 'must-not-write'))) { throw 'Later ancestor configuration escaped the stage guard' }
    Remove-Item -LiteralPath $ancestorConfig
    Write-Host 'PASS each isolated stage rejects configuration changed after initial preflight'
    $proof = Invoke-CandidateHostTransform -Root $snapshot -Script $transform
    if ($proof.hostBeforeSha256 -eq $proof.hostAfterSha256) { throw 'Transform did not change host' }
    $nativeFailure = Join-Path $owned 'native-failure-transform.ps1'
    [IO.File]::WriteAllText($nativeFailure, 'param([string]$HostExecutable); & cmd.exe /c exit 37; Write-Output "transform finished"')
    $priorNativeExit = $global:LASTEXITCODE
    $rejected = $false
    try { Invoke-CandidateHostTransform -Root $snapshot -Script $nativeFailure | Out-Null } catch {
        if ($_ -notlike '*Candidate host transform failed*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Failed native transform accepted after successful output' }
    if ($global:LASTEXITCODE -ne $priorNativeExit) { throw 'Transform leaked its native exit code to the caller' }
    Write-Host 'PASS native transform failure rejected despite successful trailing output; caller exit state restored'
    $receipt = [ordered]@{ inputs = @(Get-PayloadInputs $snapshot); files = @(Get-PayloadFiles $snapshot); hostTransform = $proof }
    $receipt | ConvertTo-Json -Depth 6 | Set-Content "$release\payload-build-receipt.json" -Encoding UTF8
    $receiptPath = "$release\payload-build-receipt.json"
    [IO.File]::WriteAllText($receiptPath, [IO.File]::ReadAllText($receiptPath, [Text.Encoding]::UTF8), [Text.UTF8Encoding]::new($false))
    $originalPayload = "$source\desktop-preview\installer\src-tauri\payload.zip"
    New-Item -ItemType Directory -Path (Split-Path -Parent $originalPayload) -Force | Out-Null
    [IO.File]::WriteAllText($originalPayload, 'original payload sentinel must stay unchanged')
    $originalPayloadHash = (Get-FileHash -LiteralPath $originalPayload -Algorithm SHA256).Hash
    & "$source\desktop-preview\installer\pack-payload.ps1" -RepoRoot $source -OutputDirectory $output
    if ((Get-FileHash -LiteralPath $originalPayload -Algorithm SHA256).Hash -ne $originalPayloadHash) { throw 'Original entry replaced the original payload' }
    if (-not (Test-Path -LiteralPath "$snapshot\desktop-preview\installer\src-tauri\payload.zip" -PathType Leaf)) { throw 'Original entry did not produce the snapshot ZIP' }
    Write-Host 'PASS original pack entry with OutputDirectory writes only snapshot ZIP; original payload sentinel unchanged'
    Write-Host 'PASS candidate-only host transform binds marker, script hash and receipt before pack'
    [IO.File]::AppendAllText("$release\FrameScope.exe", ' unapproved replacement')
    $rejected = $false
    try { & "$snapshot\desktop-preview\installer\pack-payload.ps1" -RepoRoot $snapshot -OutputDirectory $output } catch { $rejected = $true }
    if (-not $rejected) { throw 'Post-receipt host replacement accepted' }
    Write-Host 'PASS host replacement after receipt is rejected'
    [IO.File]::WriteAllText("$release\FrameScope.exe", 'synthetic cargo host transformed')
    [IO.File]::WriteAllText($transform, 'param([string]$HostExecutable); [IO.File]::AppendAllText((Join-Path (Split-Path $HostExecutable -Parent) "..\..\resources\backend\FrameScopeMonitor.exe"), " invalid")')
    $rejected = $false
    try { Invoke-CandidateHostTransform -Root $snapshot -Script $transform | Out-Null } catch {
        if ($_ -notlike '*non-host payload*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Transform changed non-host payload without rejection' }
    Write-Host 'PASS transform cannot alter non-host payload'
    [IO.File]::WriteAllText("$backend\FrameScopeMonitor.exe", 'fixture FrameScopeMonitor.exe')
    [IO.File]::WriteAllText($transform, 'param([string]$HostExecutable); [IO.File]::AppendAllText((Join-Path (Split-Path $HostExecutable -Parent) "..\..\..\..\src\dirty.cs"), " invalid")')
    $rejected = $false
    try { Invoke-CandidateHostTransform -Root $snapshot -Script $transform | Out-Null } catch {
        if ($_ -notlike '*source inputs*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Transform changed source without rejection' }
    Write-Host 'PASS transform cannot alter source inputs'
    New-Item -ItemType Directory -Path "$source\.cargo" -Force | Out-Null
    [IO.File]::WriteAllText("$source\.cargo\config.toml", '[build]' + [Environment]::NewLine + 'target-dir = "C:/unsafe-external-output"')
    $rejected = $false
    try { & "$source\tools\Build-IsolatedInstaller.ps1" -RepoRoot $source -OutputDirectory (Join-Path $owned 'redirect-output') -SnapshotOnly } catch {
        if ($_ -notlike '*Output redirection*') { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw 'Snapshot Cargo output redirect accepted' }
    Write-Host 'PASS snapshot output-redirection config is rejected before build'
} finally {
    foreach ($name in $saved.Keys) {
        if ($null -eq $saved[$name]) { Remove-Item -LiteralPath ('Env:' + $name) -ErrorAction SilentlyContinue }
        else { Set-Item -LiteralPath ('Env:' + $name) -Value $saved[$name] }
    }
}
