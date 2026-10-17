param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$failures = @()
$fixtureBase = Join-Path ([IO.Path]::GetTempPath()) ('framescope-packaging-test-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $fixtureBase 'source'
New-Item -ItemType Directory -Path $fixture | Out-Null
Write-Host "Retained fixture: $fixture"

function Test-Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Host "PASS $Name" }
    catch { $script:failures += $Name; Write-Host "FAIL ${Name}: $_" }
}
function Assert-True($Value, [string]$Message) {
    if (-not $Value) { throw $Message }
}

foreach ($relative in @('tools\Package-Installer.ps1', 'desktop-preview\installer\build-installer.ps1')) {
    Test-Case "production protocol is enabled in $relative" {
        $global:packagingCargoArgs = @()
        function cargo { $global:packagingCargoArgs = @($args) }
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $RepoRoot $relative), [ref]$tokens, [ref]$errors)
        $command = $ast.FindAll({ param($node)
            $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'cargo'
        }, $true) | Where-Object { $_.Extent.Text -like '*tauri/custom-protocol*' } | Select-Object -First 1
        & ([scriptblock]::Create($command.Extent.Text))
        Assert-True ($global:packagingCargoArgs -contains 'tauri/custom-protocol') 'Cargo selected development mode for a production executable'
    }
}

Test-Case 'build entry never stops unrelated installed processes' {
    $global:packagingTestStops = [Collections.Generic.List[object]]::new()
    function Get-Process { [pscustomobject]@{ Id = 123; Path = 'C:\Program Files\FrameScope\FrameScopeMonitor.exe' } }
    function Stop-Process { param([Parameter(ValueFromPipeline)]$InputObject, [switch]$Force) process { $global:packagingTestStops.Add($InputObject) } }
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $RepoRoot 'build.ps1'), [ref]$tokens, [ref]$errors)
    $prefix = @($ast.EndBlock.Statements | Where-Object { $_.Extent.StartLineNumber -lt (
        $ast.EndBlock.Statements | Where-Object { $_.Extent.Text -like '$csc =*' } | Select-Object -First 1
    ).Extent.StartLineNumber })
    $root = $RepoRoot
    & ([scriptblock]::Create((($prefix | Where-Object { $_.Extent.Text -notlike '$root =*' }).Extent.Text -join "`n")))
    Assert-True ($global:packagingTestStops.Count -eq 0) 'Build stopped an executable outside the workspace'
}

Test-Case 'package entry never stops unrelated installed processes' {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $RepoRoot 'tools\Package-Installer.ps1'), [ref]$tokens, [ref]$errors)
    $stops = @($ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -in @('Stop-Process', 'taskkill', 'Kill')
    }, $true))
    Assert-True ($stops.Count -eq 0) 'Packaging contains a process termination command'
}

$installer = Join-Path $fixture 'desktop-preview\installer'
$release = Join-Path $fixture 'desktop-preview\src-tauri\target\release'
$resources = Join-Path $fixture 'desktop-preview\src-tauri\resources'
foreach ($dir in @("$installer\src-tauri", $release, "$resources\backend")) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $RepoRoot 'desktop-preview\installer\pack-payload.ps1') -Destination $installer
[IO.File]::WriteAllText("$release\FrameScope.exe", 'old UI fixture')
[IO.File]::WriteAllText("$resources\backend\FrameScopeMonitor.exe", 'backend fixture')
Test-Case 'packing rejects an old unverified host before replacing payload' {
    $output = "$installer\src-tauri\payload.zip"
    [IO.File]::WriteAllText($output, 'previous good payload')
    $before = (Get-FileHash -LiteralPath $output).Hash
    $rejected = $false
    try { & "$installer\pack-payload.ps1" -RepoRoot $fixture } catch { $rejected = $true }
    Assert-True $rejected 'Pack accepted an unverified host and incomplete backend'
    Assert-True ((Get-FileHash -LiteralPath $output).Hash -eq $before) 'Rejected packing replaced the previous payload'
}

. (Join-Path $RepoRoot 'desktop-preview\installer\pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly
& git -C $fixture init --quiet
if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize fixture repository' }
New-Item -ItemType Directory -Path "$fixture\src" -Force | Out-Null
[IO.File]::WriteAllText("$fixture\src\input.cs", 'current source')
[IO.File]::WriteAllText("$fixture\dependencies.lock.json", '{"runtimeFiles":{"fixture.dll":{}},"presentMon":{"file":"tools/PresentMon.exe"}}')
$required = @('FrameScopeHardwareService.exe', 'FrameScopeHardwareProvider.exe', 'FrameScopeMonitor.exe',
    'FrameScopeProcessSampler.exe', 'FrameScopeSystemSampler.exe', 'FrameScopeReportGenerator.exe',
    'LICENSE', 'LICENSE.EXTRA', 'PRIVACY.md', 'THIRD-PARTY-NOTICES.md', 'README-FrameScopeMonitor.txt',
    'fixture.dll', 'tools\PresentMon.exe', 'FrameScopeUninstaller.exe')
foreach ($name in $required) {
    $path = Join-Path "$resources\backend" $name
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, "fixture $name")
}
foreach ($name in @('fonts\MiSansVF.ttf', 'dependencies\PawnIO\PawnIO_setup.exe')) {
    $path = Join-Path $resources $name
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, "fixture $name")
}
$receiptPath = "$release\payload-build-receipt.json"
Test-Case 'missing capability marker rejects packing' {
    $rejected = $false
    try { Get-PayloadFiles -Root $fixture | Out-Null } catch { $rejected = $true }
    Assert-True $rejected 'Missing marker accepted'
}
Write-HostCapabilities -Root $fixture
Test-Case 'wrong host capability hash rejects packing' {
    $marker = "$resources\backend\FrameScopeHostCapabilities.json"
    [IO.File]::WriteAllText($marker, '{"schema":1,"requestExitProtocol":1,"hostSha256":"0000000000000000000000000000000000000000000000000000000000000000"}')
    $rejected = $false
    try { Get-PayloadFiles -Root $fixture | Out-Null } catch { $rejected = $true }
    Assert-True $rejected 'Wrong marker accepted'
    Write-HostCapabilities -Root $fixture
}
$receipt = [ordered]@{ inputs = @(Get-PayloadInputs -Root $fixture); files = @(Get-PayloadFiles -Root $fixture) }
Test-Case 'stale injected OSD artifacts cannot reenter the payload' {
    foreach ($name in @('FrameScopeInProcessOSD-D3D11.dll', 'FrameScopeInProcessOSD-D3D12.dll',
        'framescope-osd-inject.exe', 'THIRD-PARTY-NOTICES-hudhook.md')) {
        $obsolete = Join-Path "$resources\backend" $name
        [IO.File]::WriteAllText($obsolete, 'obsolete fixture')
        try {
            $rejected = $false
            try { Get-PayloadFiles -Root $fixture | Out-Null } catch { $rejected = $true }
            Assert-True $rejected "Stale injected OSD artifact accepted: $name"
        } finally { Remove-Item -LiteralPath $obsolete }
    }
}
$receipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
Test-Case 'verified payload packages flat layout and every manifest SHA matches' {
    & "$installer\pack-payload.ps1" -RepoRoot $fixture
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead("$installer\src-tauri\payload.zip")
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        Assert-True ($names -contains 'FrameScope.exe') 'Missing host'
        Assert-True ($names -contains 'FrameScopeHardwareService.exe') 'Backend was not flattened'
        Assert-True ($names -contains 'tools/PresentMon.exe') 'Missing tools'
        Assert-True ($names -contains 'Fonts/MiSansVF.ttf') 'Missing font'
        Assert-True ($names -contains 'dependencies/PawnIO/PawnIO_setup.exe') 'Missing driver payload'
        Assert-True ($names -contains 'FrameScopePayloadManifest.json') 'Missing manifest'
        Assert-True ($names -contains 'FrameScopeUninstaller.exe') 'Missing shutdown helper'
        Assert-True ($names -contains 'FrameScopeHostCapabilities.json') 'Missing host capability marker'
        foreach ($removed in @('FrameScopeInProcessOSD-D3D11.dll', 'FrameScopeInProcessOSD-D3D12.dll',
            'framescope-osd-inject.exe', 'THIRD-PARTY-NOTICES-hudhook.md')) {
            Assert-True ($names -notcontains $removed) "Obsolete injected OSD artifact is still packaged: $removed"
        }
    } finally { $zip.Dispose() }
}

Test-Case 'package build no longer compiles or stages injected OSD artifacts' {
    $source = Get-Content -LiteralPath (Join-Path $RepoRoot 'tools\Package-Installer.ps1') -Raw
    foreach ($removed in @('cargo build --release --locked --no-default-features --features dx11',
        'cargo build --release --locked --no-default-features --features dx12',
        '--bin framescope-osd-inject', 'FrameScopeInProcessOSD-D3D11.dll',
        'FrameScopeInProcessOSD-D3D12.dll', 'framescope-osd-inject.exe')) {
        Assert-True (-not $source.Contains($removed)) "Package build still references injected OSD artifact: $removed"
    }
}
Test-Case 'changed source invalidates receipt without replacing payload' {
    $before = (Get-FileHash -LiteralPath "$installer\src-tauri\payload.zip").Hash
    [IO.File]::WriteAllText("$fixture\src\input.cs", 'changed source')
    $rejected = $false
    try { & "$installer\pack-payload.ps1" -RepoRoot $fixture } catch { $rejected = $true }
    Assert-True $rejected 'Source change accepted with old receipt'
    Assert-True ((Get-FileHash -LiteralPath "$installer\src-tauri\payload.zip").Hash -eq $before) 'Changed source replaced payload'
    [IO.File]::WriteAllText("$fixture\src\input.cs", 'current source')
}
Test-Case 'changed binary invalidates receipt without replacing payload' {
    $before = (Get-FileHash -LiteralPath "$installer\src-tauri\payload.zip").Hash
    [IO.File]::WriteAllText("$release\FrameScope.exe", 'substituted UI')
    $rejected = $false
    try { & "$installer\pack-payload.ps1" -RepoRoot $fixture } catch { $rejected = $true }
    Assert-True $rejected 'Binary substitution accepted'
    Assert-True ((Get-FileHash -LiteralPath "$installer\src-tauri\payload.zip").Hash -eq $before) 'Changed binary replaced payload'
}
if ($failures.Count) { throw "$($failures.Count) packaging safety tests failed: $($failures -join ', ')" }
