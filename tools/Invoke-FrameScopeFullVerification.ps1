param(
    [Parameter(Mandatory = $true)]
    [string]$OriginalWorkspace,

    [string]$RepoRoot = '',

    [ValidateRange(1, 86400)]
    [int]$CheckTimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$originalRoot = $null
$originalBefore = $null
$branch = ''
$commit = ''
$nodeExe = ''
$powershellExe = ''
$dotnetExe = ''

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
$script:SimulationOwnershipPath = Join-Path $verificationRoot 'pubg-simulator-ownership.json'
$script:NativeTestCount = 0
$script:OriginalAfter = $null
$script:CurrentCheckExitCode = 0
$script:CurrentCheckName = ''
$script:CurrentCheckTimeoutSeconds = $CheckTimeoutSeconds
$script:CurrentCheckDeadlineUtc = [DateTime]::MaxValue
$script:CurrentCheckTimedOut = $false

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
        if ($LASTEXITCODE -ne 0) {
            $script:CurrentCheckExitCode = $LASTEXITCODE
            throw "git status failed in $Workspace"
        }
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

function Stop-OwnedProcessTree {
    param(
        [Diagnostics.Process]$Process,
        [int]$WaitMilliseconds = 5000
    )

    if ($null -eq $Process) { return }
    try { $Process.Refresh() }
    catch { return }
    if ($Process.HasExited) { return }

    & taskkill.exe /PID $Process.Id /T /F *> $null
    try { $Process.Refresh() }
    catch { return }
    if (-not $Process.HasExited) {
        try { $Process.Kill() }
        catch { }
    }
    if (-not $Process.WaitForExit($WaitMilliseconds)) {
        throw "Timed out waiting for owned process tree PID $($Process.Id) to exit."
    }
}

function Write-AvailableProcessOutput {
    param([IO.StreamReader]$Reader)
    while ($null -ne $Reader -and $Reader.Peek() -ge 0) {
        $Reader.ReadLine()
    }
}

function Invoke-External {
    param(
        [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $root
    )

    $remainingMilliseconds = [long]($script:CurrentCheckDeadlineUtc - [DateTime]::UtcNow).TotalMilliseconds
    if ($remainingMilliseconds -le 0) {
        $script:CurrentCheckTimedOut = $true
        $script:CurrentCheckExitCode = 124
        throw "Check '$($script:CurrentCheckName)' timed out after $($script:CurrentCheckTimeoutSeconds)s before starting $FilePath."
    }

    $exitCodePath = Join-Path $checksRoot ('process-' + [guid]::NewGuid().ToString('N') + '.exitcode')
    $payload = [ordered]@{
        filePath = $FilePath
        arguments = @($Arguments)
        workingDirectory = $WorkingDirectory
        exitCodePath = $exitCodePath
    }
    $payloadJson = $payload | ConvertTo-Json -Depth 4 -Compress
    $payloadBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payloadJson))
    $wrapper = @"
`$ErrorActionPreference = 'Continue'
`$childExitCode = 1
try {
    `$payloadJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$payloadBase64'))
    `$payload = `$payloadJson | ConvertFrom-Json
    Set-Location -LiteralPath ([string]`$payload.workingDirectory)
    `$childArguments = @(`$payload.arguments | ForEach-Object { [string]`$_ })
    & ([string]`$payload.filePath) @childArguments
    `$childExitCode = if (`$null -eq `$LASTEXITCODE) { 0 } else { [int]`$LASTEXITCODE }
}
catch {
    Write-Error `$_.Exception.Message
    `$childExitCode = 1
}
finally {
    `$exitCodeTemp = ([string]`$payload.exitCodePath) + '.tmp.' + [guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText(`$exitCodeTemp, `$childExitCode.ToString(), (New-Object Text.UTF8Encoding(`$false)))
        Move-Item -LiteralPath `$exitCodeTemp -Destination ([string]`$payload.exitCodePath) -Force
    }
    finally {
        Remove-Item -LiteralPath `$exitCodeTemp -Force -ErrorAction SilentlyContinue
    }
}
exit `$childExitCode
"@
    $encodedWrapper = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrapper))
    $stdoutPath = Join-Path $checksRoot ('process-' + [guid]::NewGuid().ToString('N') + '.stdout.log')
    $stderrPath = Join-Path $checksRoot ('process-' + [guid]::NewGuid().ToString('N') + '.stderr.log')
    $process = $null
    $stdoutReader = $null
    $stderrReader = $null
    try {
        $process = Start-Process -FilePath (Get-Command powershell.exe -ErrorAction Stop).Source `
            -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encodedWrapper) `
            -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $stdoutReader = New-Object IO.StreamReader([IO.File]::Open($stdoutPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))
        $stderrReader = New-Object IO.StreamReader([IO.File]::Open($stderrPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))

        while ($true) {
            Write-AvailableProcessOutput -Reader $stdoutReader
            Write-AvailableProcessOutput -Reader $stderrReader
            if ($process.WaitForExit(100)) { break }
            $remainingMilliseconds = [long]($script:CurrentCheckDeadlineUtc - [DateTime]::UtcNow).TotalMilliseconds
            if ($remainingMilliseconds -le 0) {
                $script:CurrentCheckTimedOut = $true
                $script:CurrentCheckExitCode = 124
                "TIMEOUT check=$($script:CurrentCheckName) timeoutSeconds=$($script:CurrentCheckTimeoutSeconds) ownedRootPid=$($process.Id)"
                Stop-OwnedProcessTree -Process $process
                Write-AvailableProcessOutput -Reader $stdoutReader
                Write-AvailableProcessOutput -Reader $stderrReader
                throw "Check '$($script:CurrentCheckName)' timed out after $($script:CurrentCheckTimeoutSeconds)s while running $FilePath."
            }
        }
        $process.WaitForExit()
        Write-AvailableProcessOutput -Reader $stdoutReader
        Write-AvailableProcessOutput -Reader $stderrReader
        if (-not (Test-Path -LiteralPath $exitCodePath -PathType Leaf)) {
            $script:CurrentCheckExitCode = 1
            throw "$FilePath did not publish child exit-code evidence."
        }
        $exitCodeText = [IO.File]::ReadAllText($exitCodePath).Trim()
        $exitCode = 0
        if (-not [int]::TryParse($exitCodeText, [ref]$exitCode)) {
            $script:CurrentCheckExitCode = 1
            throw "$FilePath published invalid child exit-code evidence: $exitCodeText"
        }
        if ($exitCode -ne 0) {
            $script:CurrentCheckExitCode = $exitCode
            throw "$FilePath exited with code $exitCode."
        }
    }
    finally {
        if ($null -ne $stdoutReader) { $stdoutReader.Dispose() }
        if ($null -ne $stderrReader) { $stderrReader.Dispose() }
        if ($null -ne $process) { $process.Dispose() }
        Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $exitCodePath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Check {
    param(
        [string]$Name,
        [scriptblock]$Action,
        [int]$TimeoutSeconds = $CheckTimeoutSeconds
    )

    $safeName = $Name -replace '[^A-Za-z0-9_.-]', '-'
    $logPath = Join-Path $checksRoot ($safeName + '.log')
    $checkStarted = [DateTime]::UtcNow
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $script:CurrentCheckExitCode = 0
    $script:CurrentCheckName = $Name
    $script:CurrentCheckTimeoutSeconds = $TimeoutSeconds
    $script:CurrentCheckDeadlineUtc = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $script:CurrentCheckTimedOut = $false
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
                timeoutSeconds = $TimeoutSeconds
                timedOut = $false
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
                exitCode = if ($script:CurrentCheckExitCode -ne 0) { $script:CurrentCheckExitCode } else { 1 }
                result = if ($script:CurrentCheckTimedOut) { 'timed-out' } else { 'failed' }
                timeoutSeconds = $TimeoutSeconds
                timedOut = $script:CurrentCheckTimedOut
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

function Get-RunningProcessExecutablePath {
    param([Diagnostics.Process]$Process)
    try {
        $Process.Refresh()
        if ($Process.HasExited) { return $null }
        return $Process.MainModule.FileName
    }
    catch {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter ("ProcessId = {0}" -f $Process.Id) -ErrorAction SilentlyContinue
        if ($null -ne $cim -and -not [string]::IsNullOrWhiteSpace([string]$cim.ExecutablePath)) {
            return [string]$cim.ExecutablePath
        }
        throw "Unable to resolve executable path for PID $($Process.Id)."
    }
}

function Assert-OwnedSimulatorProcessExited {
    param([string]$OwnershipPath)
    if ([string]::IsNullOrWhiteSpace($OwnershipPath) -or -not (Test-Path -LiteralPath $OwnershipPath -PathType Leaf)) {
        'No PUBG simulator ownership record was created.'
        return
    }

    $ownership = Get-Content -Raw -LiteralPath $OwnershipPath | ConvertFrom-Json
    $ownedPid = [int]$ownership.gamePid
    $expectedPath = [string]$ownership.gameExecutable
    if ($ownedPid -le 0 -or [string]::IsNullOrWhiteSpace($expectedPath)) {
        throw "Invalid PUBG simulator ownership record: $OwnershipPath"
    }

    $process = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        "Owned PUBG simulator process exited: PID=$ownedPid path=$expectedPath"
        return
    }

    $actualPath = Get-RunningProcessExecutablePath -Process $process
    if ([string]::IsNullOrWhiteSpace($actualPath)) {
        "Owned PUBG simulator process exited during verification: PID=$ownedPid path=$expectedPath"
        return
    }
    if (-not [string]::Equals([IO.Path]::GetFullPath($expectedPath), [IO.Path]::GetFullPath($actualPath), [StringComparison]::OrdinalIgnoreCase)) {
        "Owned PUBG simulator PID was reused by another executable: PID=$ownedPid expected=$expectedPath actual=$actualPath"
        return
    }
    throw "Owned PUBG simulator process remains: PID=$ownedPid path=$actualPath"
}

function Assert-NoResidualEtwSession {
    $output = @(Invoke-External -FilePath 'logman.exe' -Arguments @('query', '-ets'))
    $matches = @($output | Where-Object { [string]$_ -match '^\s*FrameScopeNativePresentMon_' })
    if ($matches.Count -gt 0) { throw ('Residual ETW sessions: ' + ($matches -join ', ')) }
    'No FrameScopeNativePresentMon_* ETW session remains.'
}

Write-Utf8NoBom -Path $summaryLog -Value ("FrameScope full verification`r`nrepoRoot=$root`r`nrequestedOriginalWorkspace=$OriginalWorkspace`r`nstartedAt=$($startedAt.ToString('o'))`r`n")

try {
    $originalRoot = (Resolve-Path -LiteralPath $OriginalWorkspace).Path
    if ([string]::Equals($root, $originalRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'OriginalWorkspace must be separate from the remediation worktree.'
    }
    $originalBefore = Get-GitStatusSnapshot -Workspace $originalRoot
    $branch = (& git -C $root branch --show-current | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read remediation branch.' }
    $commit = (& git -C $root rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read remediation commit.' }
    $nodeExe = Resolve-NodeExe
    $powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
    $dotnetExe = (Get-Command dotnet.exe -ErrorAction Stop).Source
    Add-Content -LiteralPath $summaryLog -Encoding UTF8 -Value ("branch=$branch`r`ncommit=$commit`r`noriginalStatusSha256=$($originalBefore.Sha256)")

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
            Invoke-External -FilePath $path
        }
        $script:NativeTestCount = $names.Count
        "Native test executables passed: $($names.Count)"
    }
    Invoke-Check -Name 'chart-sampling' -Action {
        Invoke-External -FilePath $nodeExe -Arguments @((Join-Path $root 'tests\chart-sampling-tests.js'))
    }
    Invoke-Check -Name 'lightweight-separation' -Action {
        $gameLiteRoot = Join-Path (Split-Path -Parent $originalRoot) 'gamelite-auto-lightweight'
        Invoke-External -FilePath $powershellExe -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $root 'tests\lightweight-separation-tests.ps1'),
            '-StandaloneProjectRoot', $gameLiteRoot
        )
    }
    Invoke-Check -Name 'render-probe-build' -Action {
        Invoke-External -FilePath $dotnetExe -Arguments @('build', (Join-Path $root 'tools\FrameScopeRenderProbe\FrameScopeRenderProbe.csproj'), '-c', 'Release', '--nologo')
    }
    Invoke-Check -Name 'pubg-fake-presentmon-simulator' -Action {
        $simulationOutput = Invoke-External -FilePath $powershellExe -Arguments @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $root 'tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1'),
            '-Scenario', 'stable',
            '-DurationSeconds', '4',
            '-OwnershipPath', $script:SimulationOwnershipPath
        )
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
        @{ Name = 'residual-processes'; Action = {
                Assert-NoResidualProcesses
                Assert-OwnedSimulatorProcessExited -OwnershipPath $script:SimulationOwnershipPath
            } },
        @{ Name = 'residual-etw-sessions'; Action = { Assert-NoResidualEtwSession } },
        @{ Name = 'original-workspace-integrity'; Action = {
                if ($null -eq $originalRoot -or $null -eq $originalBefore) {
                    throw 'Original workspace baseline was not initialized.'
                }
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
$originalAfter = $script:OriginalAfter
$originalSummary = [ordered]@{
    path = if ($null -ne $originalRoot) { $originalRoot } else { $OriginalWorkspace }
    beforeSha256 = if ($null -ne $originalBefore) { $originalBefore.Sha256 } else { $null }
    afterSha256 = if ($null -ne $originalAfter) { $originalAfter.Sha256 } else { $null }
    lineCount = if ($null -ne $originalAfter) { $originalAfter.LineCount } elseif ($null -ne $originalBefore) { $originalBefore.LineCount } else { $null }
    unchanged = ($null -ne $originalBefore -and $null -ne $originalAfter -and $originalBefore.Text -ceq $originalAfter.Text)
}
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
    error = if ($null -ne $failure) { $failure.Exception.Message } else { $null }
    originalWorkspace = $originalSummary
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
