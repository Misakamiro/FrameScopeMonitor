param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalWorkspace,

    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$originalRoot = (Resolve-Path -LiteralPath $OriginalWorkspace).Path
if ([string]::Equals($root, $originalRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OriginalWorkspace must be separate from the remediation worktree.'
}

$verificationRoot = Join-Path $root ('artifacts\verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$checksRoot = Join-Path $verificationRoot 'checks'
New-Item -ItemType Directory -Path $checksRoot -Force | Out-Null
$summaryLog = Join-Path $verificationRoot 'verification.log'
$resultPath = Join-Path $verificationRoot 'result.json'
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$results = New-Object Collections.ArrayList
$startedAt = [DateTime]::UtcNow
$failure = $null
$script:SimulationResult = $null
$script:NativeTestCount = 0

function Write-Utf8NoBom {
    param([string]$Path, [string]$Value)
    [IO.File]::WriteAllText($Path, $Value, $utf8NoBom)
}

function Get-Sha256Text {
    param([string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-GitStatusSnapshot {
    param([string]$Workspace)
    Push-Location $Workspace
    try {
        $lines = @(& git status --porcelain=v1 --untracked-files=all)
        if ($LASTEXITCODE -ne 0) { throw "git status failed in $Workspace" }
    }
    finally {
        Pop-Location
    }
    $text = if ($lines.Count -eq 0) { '' } else { ($lines -join "`n") + "`n" }
    return [pscustomobject]@{
        Text = $text
        Sha256 = Get-Sha256Text -Value $text
        LineCount = $lines.Count
    }
}

function Invoke-External {
    param(
        [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $root
    )
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "$FilePath exited with code $exitCode."
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-Check {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    $safeName = $Name -replace '[^A-Za-z0-9_.-]', '-'
    $logPath = Join-Path $checksRoot ($safeName + '.log')
    $checkStarted = [DateTime]::UtcNow
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action *>&1 | Out-File -LiteralPath $logPath -Encoding UTF8
        $timer.Stop()
        $checkEnded = [DateTime]::UtcNow
        [void]$results.Add([ordered]@{
                name = $Name
                startedAt = $checkStarted.ToString('o')
                endedAt = $checkEnded.ToString('o')
                durationMs = $timer.ElapsedMilliseconds
                exitCode = 0
                result = 'passed'
                log = Get-RelativeArtifactPath -Path $logPath
            })
        Add-Content -LiteralPath $summaryLog -Encoding UTF8 -Value ("PASS {0} {1}ms" -f $Name, $timer.ElapsedMilliseconds)
        Write-Host ("PASS {0} ({1}ms)" -f $Name, $timer.ElapsedMilliseconds)
    }
    catch {
        $timer.Stop()
        $checkEnded = [DateTime]::UtcNow
        ($_ | Format-List * -Force | Out-String) | Add-Content -LiteralPath $logPath -Encoding UTF8
        [void]$results.Add([ordered]@{
                name = $Name
                startedAt = $checkStarted.ToString('o')
                endedAt = $checkEnded.ToString('o')
                durationMs = $timer.ElapsedMilliseconds
                exitCode = 1
                result = 'failed'
                error = $_.Exception.Message
                log = Get-RelativeArtifactPath -Path $logPath
            })
        Add-Content -LiteralPath $summaryLog -Encoding UTF8 -Value ("FAIL {0} {1}ms {2}" -f $Name, $timer.ElapsedMilliseconds, $_.Exception.Message)
        Write-Host ("FAIL {0}: {1}" -f $Name, $_.Exception.Message)
        throw
    }
}

function Get-RelativeArtifactPath {
    param([string]$Path)
    $base = [Uri]([IO.Path]::GetFullPath($root).TrimEnd('\') + '\')
    $target = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($base.MakeRelativeUri($target).ToString()).Replace('\', '/')
}

function Resolve-NodeExe {
    $candidates = New-Object Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:FRAMESCOPE_NODE_EXE)) {
        $candidates.Add($env:FRAMESCOPE_NODE_EXE)
    }
    $candidates.Add((Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'))
    foreach ($candidate in Get-Command node -All -ErrorAction SilentlyContinue) {
        if (-not [string]::IsNullOrWhiteSpace($candidate.Source)) { $candidates.Add($candidate.Source) }
    }
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            & $candidate --version *> $null
            if ($LASTEXITCODE -eq 0) { return (Resolve-Path -LiteralPath $candidate).Path }
        }
        catch { }
    }
    throw 'No usable node.exe was found.'
}

function Assert-NoResidualProcesses {
    $names = @('FrameScopeMonitor', 'FrameScopeProcessSampler', 'FrameScopeSystemSampler', 'FrameScopeReportGenerator', 'FakePresentMon', 'PubgGameSimulator')
    $remaining = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName })
    if ($remaining.Count -gt 0) {
        throw ('Residual processes: ' + (($remaining | ForEach-Object { $_.ProcessName + ':' + $_.Id }) -join ', '))
    }
    'No FrameScope/FakePresentMon/PubgGameSimulator processes remain.'
}

function Assert-NoResidualEtwSession {
    $output = & logman.exe query -ets 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "logman query -ets failed with code $exitCode" }
    $matches = @($output | Where-Object { [string]$_ -match '^\s*FrameScopeNativePresentMon_' })
    if ($matches.Count -gt 0) { throw ('Residual ETW sessions: ' + ($matches -join ', ')) }
    'No FrameScopeNativePresentMon_* ETW session remains.'
}

$originalBefore = Get-GitStatusSnapshot -Workspace $originalRoot
$branch = (& git -C $root branch --show-current | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to read remediation branch.' }
$commit = (& git -C $root rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Unable to read remediation commit.' }
$nodeExe = Resolve-NodeExe
$powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
$dotnetExe = (Get-Command dotnet.exe -ErrorAction Stop).Source

Write-Utf8NoBom -Path $summaryLog -Value ("FrameScope full verification`r`nbranch=$branch`r`ncommit=$commit`r`nstartedAt=$($startedAt.ToString('o'))`r`n")

try {
    Invoke-Check -Name 'documentation' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\Test-CurrentDocumentation.ps1'))
    }
    Invoke-Check -Name 'build-contract' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\Test-FrameScopeBuildContract.ps1'))
    }
    Invoke-Check -Name 'frontend' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\Run-Frontend.ps1'), 'verify')
    }
    Invoke-Check -Name 'frontend-audit' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\Run-Frontend.ps1'), 'npm', 'audit', '--audit-level=high')
    }
    Invoke-Check -Name 'native-build' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'build.ps1'))
    }
    Invoke-Check -Name 'native-test-build' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\Build-FrameScopeTests.ps1'))
    }
    Invoke-Check -Name 'native-tests' -Action {
        $buildText = Get-Content -Raw -LiteralPath (Join-Path $root 'tests\Build-FrameScopeTests.ps1')
        $names = @([regex]::Matches($buildText, "-OutputName\s+'(FrameScope[^']+Tests\.exe)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if ($names.Count -eq 0) { throw 'No native test executables were declared.' }
        foreach ($name in $names) {
            $path = Join-Path (Join-Path $root 'tests') $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Native test executable missing: $name" }
            & $path
            if ($LASTEXITCODE -ne 0) { throw "$name failed with code $LASTEXITCODE" }
        }
        $script:NativeTestCount = $names.Count
        "Native test executables passed: $($names.Count)"
    }
    Invoke-Check -Name 'chart-sampling' -Action {
        Invoke-External -FilePath $nodeExe -Arguments @((Join-Path $root 'tests\chart-sampling-tests.js'))
    }
    Invoke-Check -Name 'lightweight-separation' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\lightweight-separation-tests.ps1'))
    }
    Invoke-Check -Name 'render-probe-build' -Action {
        Invoke-External -FilePath $dotnetExe -Arguments @('build', (Join-Path $root 'tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj'), '-c', 'Release', '--nologo')
    }
    Invoke-Check -Name 'pubg-fake-presentmon-simulator' -Action {
        $simulationOutput = & $powershellExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1') -Scenario stable -DurationSeconds 4
        if ($LASTEXITCODE -ne 0) { throw "PUBG simulator failed with code $LASTEXITCODE" }
        $script:SimulationResult = ($simulationOutput | Out-String | ConvertFrom-Json)
        if ([int]$script:SimulationResult.monitorExit -ne 0 -or [int]$script:SimulationResult.reportExit -ne 0) {
            throw 'PUBG simulator monitor/report exit code was nonzero.'
        }
        if (-not [bool]$script:SimulationResult.hasFrameData -or [string]$script:SimulationResult.reportKind -ne 'full') {
            throw 'PUBG simulator did not produce a full frame-data report.'
        }
        $script:SimulationResult | ConvertTo-Json -Depth 6
    }
    Invoke-Check -Name 'report-layout-probe' -Action {
        $reportPath = Join-Path ([string]$script:SimulationResult.runDir) 'charts\framescope-interactive-report.html'
        Invoke-External -FilePath $nodeExe -Arguments @(
            (Join-Path $root 'tools\Probe-ReportHtmlLayout.js'),
            '--report', $reportPath,
            '--diagnostic', $reportPath,
            '--out', (Join-Path $verificationRoot 'report-layout-probe')
        )
    }
    Invoke-Check -Name 'report-process-interaction-probe' -Action {
        $reportPath = Join-Path ([string]$script:SimulationResult.runDir) 'charts\framescope-interactive-report.html'
        Invoke-External -FilePath $nodeExe -Arguments @(
            (Join-Path $root 'tools\Probe-ReportProcessInteraction.js'),
            '--report', $reportPath,
            '--out', (Join-Path $verificationRoot 'report-process-probe'),
            '--label', 'final'
        )
    }
    Invoke-Check -Name 'frontend-large-list-probe' -Action {
        Invoke-External -FilePath $nodeExe -Arguments @(
            (Join-Path $root 'tools\Probe-FrontendLargeLists.js'),
            '--out', (Join-Path $verificationRoot 'frontend-large-list-probe'),
            '--label', 'final',
            '--runs', '1'
        )
    }
    Invoke-Check -Name 'package-parity' -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\Test-FrameScopePackages.ps1'))
    }
    Invoke-Check -Name 'git-diff-check' -Action {
        Invoke-External -FilePath 'git.exe' -Arguments @('-C', $root, 'diff', '--check', 'fd9a336..HEAD')
        Invoke-External -FilePath 'git.exe' -Arguments @('-C', $root, 'diff', '--check')
    }
}
catch {
    $failure = $_
}

foreach ($guard in @(
        @{ Name = 'residual-processes'; Action = { Assert-NoResidualProcesses } },
        @{ Name = 'residual-etw-sessions'; Action = { Assert-NoResidualEtwSession } },
        @{ Name = 'original-workspace-integrity'; Action = {
                $script:OriginalAfter = Get-GitStatusSnapshot -Workspace $originalRoot
                if ($originalBefore.Text -cne $script:OriginalAfter.Text) {
                    throw "Original workspace changed: before=$($originalBefore.Sha256) after=$($script:OriginalAfter.Sha256)"
                }
                "Original workspace unchanged: SHA256=$($script:OriginalAfter.Sha256) lines=$($script:OriginalAfter.LineCount)"
            } }
    )) {
    try {
        Invoke-Check -Name $guard.Name -Action $guard.Action
    }
    catch {
        if ($null -eq $failure) { $failure = $_ }
    }
}

$endedAt = [DateTime]::UtcNow
$originalAfter = if ($null -ne $script:OriginalAfter) { $script:OriginalAfter } else { Get-GitStatusSnapshot -Workspace $originalRoot }
$result = [ordered]@{
    schemaVersion = 1
    result = if ($null -eq $failure) { 'passed' } else { 'failed' }
    startedAt = $startedAt.ToString('o')
    endedAt = $endedAt.ToString('o')
    durationMs = [long]($endedAt - $startedAt).TotalMilliseconds
    repoRoot = $root
    branch = $branch
    commit = $commit
    node = $nodeExe
    nativeTestExecutables = $script:NativeTestCount
    originalWorkspace = [ordered]@{
        path = $originalRoot
        beforeSha256 = $originalBefore.Sha256
        afterSha256 = $originalAfter.Sha256
        lineCount = $originalAfter.LineCount
        unchanged = $originalBefore.Text -ceq $originalAfter.Text
    }
    simulation = $script:SimulationResult
    checks = @($results)
}
$resultTemp = $resultPath + '.tmp.' + [guid]::NewGuid().ToString('N')
try {
    Write-Utf8NoBom -Path $resultTemp -Value (($result | ConvertTo-Json -Depth 20) + [Environment]::NewLine)
    Move-Item -LiteralPath $resultTemp -Destination $resultPath -Force
}
finally {
    if (Test-Path -LiteralPath $resultTemp) { Remove-Item -LiteralPath $resultTemp -Force -ErrorAction SilentlyContinue }
}

Write-Host "Verification result: $($result.result)"
Write-Host "Result JSON: $resultPath"
if ($null -ne $failure) {
    throw $failure.Exception
}
