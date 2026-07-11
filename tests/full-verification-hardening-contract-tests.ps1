param(
    [ValidateSet('all', 'timeout', 'owned-cleanup', 'whole-run-finalization', 'workspace-fingerprint', 'simulation-exit-code', 'simulator-compile')]
    [string]$TestName = 'all'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$runnerPath = Join-Path $root 'tools\Invoke-FrameScopeFullVerification.ps1'
$simulatorPath = Join-Path $root 'tools\FrameScopePubgSimulator\Run-PubgSimulation.ps1'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('framescope-full-verification-contract-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw ("{0}: expected <{1}> but got <{2}>" -f $Message, $Expected, $Actual)
    }
}

function Get-ScriptAst {
    param([string]$Path)
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw ($errors | Out-String) }
    return $ast
}

function Import-ScriptFunctions {
    param([string]$Path)
    $ast = Get-ScriptAst -Path $Path
    $functions = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst]
            }, $true))
    foreach ($function in $functions) {
        $definition = $function.Extent.Text
        $qualified = $definition -replace ('(?i)^function\s+' + [regex]::Escape($function.Name)), ('function script:' + $function.Name)
        Invoke-Expression $qualified
    }
}

function Test-PerCheckTimeout {
    Import-ScriptFunctions -Path $runnerPath
    Assert-True ([bool](Get-Command Invoke-External -ErrorAction SilentlyContinue)) 'Invoke-External was not imported.'

    $runnerAst = Get-ScriptAst -Path $runnerPath
    $checkTimeoutParameter = @($runnerAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'CheckTimeoutSeconds' })
    Assert-Equal 1 $checkTimeoutParameter.Count 'full verifier exposes one configurable CheckTimeoutSeconds parameter'

    $directMainInvocations = New-Object Collections.Generic.List[string]
    $invokeChecks = @($runnerAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-Check'
            }, $true))
    foreach ($invokeCheck in $invokeChecks) {
        $action = @($invokeCheck.CommandElements | Where-Object { $_ -is [Management.Automation.Language.ScriptBlockExpressionAst] } | Select-Object -First 1)
        if ($action.Count -eq 0) { continue }
        foreach ($command in @($action[0].ScriptBlock.FindAll({
                        param($node)
                        $node -is [Management.Automation.Language.CommandAst] -and
                            $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand
                    }, $true))) {
            $directMainInvocations.Add($command.Extent.Text)
        }
    }
    Assert-Equal 0 $directMainInvocations.Count ('main checks still contain direct unbounded process calls: ' + ($directMainInvocations -join '; '))

    $fixturePath = Join-Path $tempRoot 'timeout-fixture.ps1'
    $descendantPidPath = Join-Path $tempRoot 'timeout-descendant.pid'
    [IO.File]::WriteAllText($fixturePath, @'
param([string]$DescendantPidPath)
Write-Output 'stream-before-timeout'
$child = Start-Process -FilePath (Get-Command powershell.exe).Source -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 30' -PassThru -WindowStyle Hidden
[IO.File]::WriteAllText($DescendantPidPath, $child.Id.ToString())
Start-Sleep -Seconds 4
'@)

    $script:root = $tempRoot
    $script:checksRoot = $tempRoot
    $script:summaryLog = Join-Path $tempRoot 'summary.log'
    $script:results = New-Object Collections.ArrayList
    [IO.File]::WriteAllText($script:summaryLog, '')
    $caught = $null
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try {
        Invoke-Check -Name 'contract-timeout' -TimeoutSeconds 1 -Action {
            Invoke-External -FilePath (Get-Command powershell.exe).Source -Arguments @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $fixturePath,
                '-DescendantPidPath', $descendantPidPath
            )
        } 6>$null
    }
    catch {
        $caught = $_
    }
    finally {
        $timer.Stop()
    }

    $descendantPid = if (Test-Path -LiteralPath $descendantPidPath) { [int](Get-Content -Raw -LiteralPath $descendantPidPath) } else { 0 }
    try {
        Assert-True ($null -ne $caught) 'bounded external process did not throw on timeout.'
        Assert-True ($timer.ElapsedMilliseconds -lt 3000) "timeout returned too late: $($timer.ElapsedMilliseconds)ms"
        Assert-True $script:CurrentCheckTimedOut 'timeout evidence flag was not set.'
        Assert-Equal 124 $script:CurrentCheckExitCode 'timeout exit code'
        Assert-Equal 1 $script:results.Count 'timeout check result count'
        Assert-Equal 'timed-out' $script:results[0].result 'timeout check result'
        Assert-Equal 124 $script:results[0].exitCode 'recorded timeout exit code'
        $checkLog = Get-Content -Raw -LiteralPath (Join-Path $tempRoot 'contract-timeout.log')
        Assert-True ($checkLog -match 'stream-before-timeout') 'child output was not streamed into the check log before timeout.'
        Assert-True ($checkLog -match 'TIMEOUT check=contract-timeout') 'check log does not contain timeout evidence.'
        Assert-True ($descendantPid -gt 0) 'timeout fixture did not publish its descendant PID.'
        Start-Sleep -Milliseconds 300
        Assert-True ($null -eq (Get-Process -Id $descendantPid -ErrorAction SilentlyContinue)) 'timeout left a descendant process running.'

        $exitFixturePath = Join-Path $tempRoot 'exit-fixture.ps1'
        [IO.File]::WriteAllText($exitFixturePath, "Write-Output 'real-exit-fixture'`r`nexit 37`r`n")
        $script:results = New-Object Collections.ArrayList
        $exitFailure = $null
        try {
            Invoke-Check -Name 'contract-real-exit' -TimeoutSeconds 5 -Action {
                Invoke-External -FilePath (Get-Command powershell.exe).Source -Arguments @(
                    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $exitFixturePath
                )
            } 6>$null
        }
        catch { $exitFailure = $_ }
        Assert-True ($null -ne $exitFailure) 'nonzero child exit did not fail its check.'
        Assert-Equal 37 $script:results[0].exitCode 'real child exit code'

        $script:results = New-Object Collections.ArrayList
        Invoke-Check -Name 'contract-text-output' -TimeoutSeconds 5 -Action {
            Invoke-External -FilePath (Get-Command powershell.exe).Source -Arguments @(
                '-NoProfile', '-Command', "[Console]::WriteLine('plain-output')"
            )
        } 6>$null
        $textOutputLog = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $tempRoot 'contract-text-output.log')
        Assert-True ($textOutputLog -match 'plain-output') 'successful child output was not captured.'
        Assert-True ($textOutputLog -notmatch '#< CLIXML') ('successful child output was polluted by CLIXML metadata: ' + $textOutputLog.Trim())
    }
    finally {
        if ($descendantPid -gt 0) { Stop-Process -Id $descendantPid -Force -ErrorAction SilentlyContinue }
    }
}

function Test-OwnedSimulatorCleanup {
    Import-ScriptFunctions -Path $runnerPath
    Import-ScriptFunctions -Path $simulatorPath
    Assert-True ([bool](Get-Command Stop-OwnedProcess -ErrorAction SilentlyContinue)) 'simulator does not define Stop-OwnedProcess.'
    Assert-True ([bool](Get-Command Assert-OwnedSimulatorProcessExited -ErrorAction SilentlyContinue)) 'runner does not define Assert-OwnedSimulatorProcessExited.'

    $simulatorAst = Get-ScriptAst -Path $simulatorPath
    $monitorTimeoutParameter = @($simulatorAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'MonitorTimeoutSeconds' })
    Assert-Equal 1 $monitorTimeoutParameter.Count 'simulator exposes one configurable MonitorTimeoutSeconds parameter'
    $unboundedMonitorWaits = @($simulatorAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
                    [string]$node.Member.Value -eq 'WaitForExit' -and
                    $node.Expression.Extent.Text -eq '$monitor' -and
                    $node.Arguments.Count -eq 0
            }, $true))
    Assert-Equal 0 $unboundedMonitorWaits.Count 'simulator monitor WaitForExit calls without timeout'

    $gameCleanupInFinally = @($simulatorAst.FindAll({
                param($node)
                if ($node -isnot [Management.Automation.Language.TryStatementAst] -or $null -eq $node.Finally) { return $false }
                return @($node.Finally.FindAll({
                            param($child)
                            $child -is [Management.Automation.Language.CommandAst] -and
                                $child.GetCommandName() -eq 'Stop-OwnedProcess' -and
                                $child.Extent.Text -match '\$game\b'
                        }, $true)).Count -gt 0
            }, $true))
    Assert-True ($gameCleanupInFinally.Count -gt 0) 'simulator does not clean its game PID from a finally block.'

    $runnerSource = Get-Content -Raw -LiteralPath $runnerPath
    $residualFunction = @((Get-ScriptAst -Path $runnerPath).FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-NoResidualProcesses'
            }, $true) | Select-Object -First 1)
    Assert-True ($residualFunction.Count -eq 1 -and $residualFunction[0].Extent.Text -notmatch "'TslGame'") 'runner residual guard must not scan all TslGame processes by name.'
    Assert-True ($runnerSource.Contains("'-OwnershipPath', " + '$script:SimulationOwnershipPath')) 'runner does not pass its known ownership record path to the simulator.'

    $powershellPath = (Get-Command powershell.exe).Source
    $owned = Start-Process -FilePath $powershellPath -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 30' -PassThru -WindowStyle Hidden
    $peer = Start-Process -FilePath $powershellPath -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 30' -PassThru -WindowStyle Hidden
    $ownershipPath = Join-Path $tempRoot 'simulator-ownership.json'
    try {
        $script:utf8NoBom = New-Object Text.UTF8Encoding($false)
        Write-OwnershipRecord -Path $ownershipPath -Record ([ordered]@{
                gamePid = $owned.Id
                gameExecutable = $powershellPath
            })

        $residualFailure = $null
        try { Assert-OwnedSimulatorProcessExited -OwnershipPath $ownershipPath }
        catch { $residualFailure = $_ }
        Assert-True ($null -ne $residualFailure) 'runner did not detect the exact owned PID/path while it was alive.'

        $mismatchFailure = $null
        try { Stop-OwnedProcess -Process $owned -ExpectedExecutablePath (Join-Path $tempRoot 'TslGame.exe') -WaitMilliseconds 2000 }
        catch { $mismatchFailure = $_ }
        Assert-True ($null -ne $mismatchFailure) 'owned cleanup did not reject an executable-path mismatch.'
        Assert-True ($null -ne (Get-Process -Id $owned.Id -ErrorAction SilentlyContinue)) 'path mismatch killed a process it did not own.'

        Stop-OwnedProcess -Process $owned -ExpectedExecutablePath $powershellPath -WaitMilliseconds 5000 | Out-Null
        Assert-True ($null -eq (Get-Process -Id $owned.Id -ErrorAction SilentlyContinue)) 'owned process did not exit after forced cleanup.'
        Assert-True ($null -ne (Get-Process -Id $peer.Id -ErrorAction SilentlyContinue)) 'cleanup killed an unrelated peer process.'

        Assert-OwnedSimulatorProcessExited -OwnershipPath $ownershipPath | Out-Null
    }
    finally {
        Stop-Process -Id $owned.Id -Force -ErrorAction SilentlyContinue
        Stop-Process -Id $peer.Id -Force -ErrorAction SilentlyContinue
    }
}

function Test-WholeRunFinalization {
    $runnerAst = Get-ScriptAst -Path $runnerPath
    $wholeRunParameter = @($runnerAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'WholeRunTimeoutSeconds' })
    Assert-Equal 1 $wholeRunParameter.Count 'full verifier exposes one configurable WholeRunTimeoutSeconds parameter'

    $unboundedExternalCalls = @($runnerAst.FindAll({
                param($node)
                if ($node -isnot [Management.Automation.Language.CommandAst] -or
                    $node.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Ampersand) {
                    return $false
                }
                return $node.CommandElements[0].Extent.Text -ne '$Action'
            }, $true))
    Assert-Equal 0 $unboundedExternalCalls.Count ('full verifier still has direct external calls: ' + (($unboundedExternalCalls | ForEach-Object { $_.Extent.Text }) -join '; '))

    $fixtureRoot = Join-Path $tempRoot 'whole-run-fixture-root'
    $originalRoot = Join-Path $tempRoot 'whole-run-original-root'
    New-Item -ItemType Directory -Path $fixtureRoot, $originalRoot -Force | Out-Null
    & git.exe -C $fixtureRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize whole-run fixture root.' }
    & git.exe -C $originalRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize whole-run original root.' }
    & git.exe -C $originalRoot config user.email 'framescope-contract@example.invalid'
    & git.exe -C $originalRoot config user.name 'FrameScope Contract'
    [IO.File]::WriteAllText((Join-Path $originalRoot 'baseline.txt'), "baseline`r`n")
    & git.exe -C $originalRoot add -- baseline.txt
    & git.exe -C $originalRoot commit --quiet -m 'fixture baseline'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to commit whole-run original fixture.' }

    $runnerSource = Get-Content -Raw -LiteralPath $runnerPath
    $mainPattern = '(?s)try \{\r?\n    \$originalRoot =.*?\r?\n\}\r?\ncatch \{\r?\n    \$failure = \$_\r?\n\}'
    $fixtureMain = @'
try {
    $originalRoot = (Resolve-Path -LiteralPath $OriginalWorkspace).Path
    $originalBefore = Get-GitWorkspaceFingerprint -Workspace $originalRoot
    $branch = 'contract'
    $commit = 'contract'
    $powershellExe = (Get-Command powershell.exe -ErrorAction Stop).Source
    Invoke-Check -Name 'whole-run-timeout-fixture' -TimeoutSeconds 10 -Action {
        Invoke-External -FilePath $powershellExe -Arguments @('-NoProfile', '-Command', 'Start-Sleep -Seconds 5')
    }
}
catch {
    $failure = $_
}
'@
    $fixtureSource = [regex]::Replace($runnerSource, $mainPattern, $fixtureMain.Replace('$', '$$'), 1)
    Assert-True ($fixtureSource -cne $runnerSource) 'Unable to replace full verifier main checks for the whole-run fixture.'
    $fixtureRunnerPath = Join-Path $tempRoot 'Invoke-FrameScopeWholeRunFixture.ps1'
    [IO.File]::WriteAllText($fixtureRunnerPath, $fixtureSource)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $fixtureRunnerPath `
                -OriginalWorkspace $originalRoot -RepoRoot $fixtureRoot `
                -CheckTimeoutSeconds 10 -WholeRunTimeoutSeconds 1 2>&1)
        $runnerExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    Assert-True ($runnerExit -ne 0) 'whole-run timeout fixture unexpectedly succeeded.'
    $resultLine = @($output | Where-Object { [string]$_ -match '^Result JSON:\s*(.+)$' } | Select-Object -Last 1)
    Assert-Equal 1 $resultLine.Count ('whole-run timeout result path output count; runner output=' + (($output | Out-String).Trim()))
    $resultPath = [regex]::Match([string]$resultLine[0], '^Result JSON:\s*(.+)$').Groups[1].Value.Trim()
    Assert-True (Test-Path -LiteralPath $resultPath -PathType Leaf) 'whole-run timeout did not write result.json.'
    $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    Assert-Equal 'failed' ([string]$result.result) 'whole-run timeout aggregate result'
    Assert-True ([bool]$result.wholeRunTimedOut) 'result.json did not preserve whole-run timeout evidence.'

    $checks = @($result.checks)
    $fixtureCheck = @($checks | Where-Object { $_.name -eq 'whole-run-timeout-fixture' })
    Assert-Equal 1 $fixtureCheck.Count 'whole-run timeout fixture check count'
    Assert-Equal 124 ([int]$fixtureCheck[0].exitCode) 'whole-run timeout fixture exit code'
    foreach ($guardName in @('residual-processes', 'residual-etw-sessions', 'original-workspace-integrity')) {
        Assert-Equal 1 @($checks | Where-Object { $_.name -eq $guardName }).Count "guard result count: $guardName"
    }
    Assert-Equal 0 @(Get-ChildItem -LiteralPath (Split-Path -Parent $resultPath) -Filter 'result.json.tmp.*' -File -ErrorAction SilentlyContinue).Count 'atomic result temp files remaining'
}

function Test-WorkspaceFingerprint {
    Import-ScriptFunctions -Path $runnerPath
    Assert-True ([bool](Get-Command Get-GitWorkspaceFingerprint -ErrorAction SilentlyContinue)) 'runner does not define Get-GitWorkspaceFingerprint.'

    $repo = Join-Path $tempRoot 'workspace-fingerprint-repo'
    New-Item -ItemType Directory -Path $repo -Force | Out-Null
    & git.exe -C $repo init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize workspace fingerprint fixture.' }
    & git.exe -C $repo config user.email 'framescope-contract@example.invalid'
    & git.exe -C $repo config user.name 'FrameScope Contract'

    [IO.File]::WriteAllText((Join-Path $repo '.gitignore'), "ignored.txt`r`n")
    [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), "tracked-clean`r`n")
    [IO.File]::WriteAllText((Join-Path $repo 'delete-staged.txt'), "delete-staged`r`n")
    [IO.File]::WriteAllText((Join-Path $repo 'delete-unstaged.txt'), "delete-unstaged`r`n")
    & git.exe -C $repo add -- .
    & git.exe -C $repo commit --quiet -m 'fixture baseline'
    if ($LASTEXITCODE -ne 0) { throw 'Unable to commit workspace fingerprint fixture.' }

    [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), "dirty-v1`r`n")
    [IO.File]::WriteAllText((Join-Path $repo 'untracked.txt'), "untracked-v1`r`n")
    [IO.File]::WriteAllText((Join-Path $repo 'ignored.txt'), "ignored-v1`r`n")

    $script:root = $repo
    $script:checksRoot = Join-Path $tempRoot 'workspace-fingerprint-processes'
    New-Item -ItemType Directory -Path $script:checksRoot -Force | Out-Null
    $script:CurrentCheckName = 'workspace-fingerprint-contract'
    $script:CurrentCheckTimeoutSeconds = 120
    $script:CurrentCheckDeadlineUtc = [DateTime]::UtcNow.AddSeconds(120)
    $script:CurrentCheckExitCode = 0
    $script:CurrentCheckTimedOut = $false
    $script:WholeRunDeadlineUtc = [DateTime]::UtcNow.AddSeconds(180)
    $script:IsFinalizing = $false

    $initial = Get-GitWorkspaceFingerprint -Workspace $repo
    $repeat = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-Equal $initial.Sha256 $repeat.Sha256 'repeated workspace fingerprint stability'
    Assert-Equal 1 ([int]$initial.UntrackedPathCount) 'ignored files excluded from untracked enumeration'

    [IO.File]::WriteAllText((Join-Path $repo 'ignored.txt'), "ignored-v2`r`n")
    $ignoredChanged = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-Equal $initial.Sha256 $ignoredChanged.Sha256 'ignored content does not affect workspace fingerprint'

    [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), "dirty-v2`r`n")
    $dirtyTrackedChanged = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-True ($initial.TrackedWorktreeSha256 -cne $dirtyTrackedChanged.TrackedWorktreeSha256) 'already-dirty tracked content change was not detected.'
    Assert-True ($initial.Sha256 -cne $dirtyTrackedChanged.Sha256) 'already-dirty tracked content did not change overall fingerprint.'

    [IO.File]::WriteAllText((Join-Path $repo 'untracked.txt'), "untracked-v2`r`n")
    $untrackedChanged = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-True ($dirtyTrackedChanged.UntrackedSha256 -cne $untrackedChanged.UntrackedSha256) 'existing untracked content change was not detected.'

    [IO.File]::WriteAllText((Join-Path $repo 'tracked.txt'), "staged-v3`r`n")
    & git.exe -C $repo add -- tracked.txt
    $stagedChanged = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-True ($untrackedChanged.IndexSha256 -cne $stagedChanged.IndexSha256) 'staged content change was not detected in index fingerprint.'

    & git.exe -C $repo rm --quiet -- delete-staged.txt
    $stagedDeletion = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-True ($stagedChanged.IndexSha256 -cne $stagedDeletion.IndexSha256) 'staged deletion was not detected in index fingerprint.'
    Assert-Equal 1 ([int]$stagedDeletion.MissingTrackedPathCount) 'staged deletion missing tracked count'

    Remove-Item -LiteralPath (Join-Path $repo 'delete-unstaged.txt') -Force
    $unstagedDeletion = Get-GitWorkspaceFingerprint -Workspace $repo
    Assert-Equal $stagedDeletion.IndexSha256 $unstagedDeletion.IndexSha256 'unstaged deletion must not change index fingerprint'
    Assert-True ($stagedDeletion.TrackedWorktreeSha256 -cne $unstagedDeletion.TrackedWorktreeSha256) 'unstaged deletion was not detected in tracked worktree fingerprint.'
    Assert-Equal 2 ([int]$unstagedDeletion.MissingTrackedPathCount) 'combined staged/unstaged deletion missing tracked count'

    foreach ($propertyName in @('HeadSha256', 'IndexSha256', 'TrackedWorktreeSha256', 'UntrackedSha256')) {
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$unstagedDeletion.$propertyName)) "missing fingerprint component: $propertyName"
    }

    $runnerSource = Get-Content -Raw -LiteralPath $runnerPath
    Assert-True ($runnerSource.Contains('beforeSha256 = if ($null -ne $originalBefore) { $originalBefore.Sha256 }')) 'result.json no longer records the before overall SHA.'
    Assert-True ($runnerSource.Contains('afterSha256 = if ($null -ne $originalAfter) { $originalAfter.Sha256 }')) 'result.json no longer records the after overall SHA.'
    Assert-True ($runnerSource -match '(?m)^\s+before = if \(' -and $runnerSource -match '(?m)^\s+after = if \(') 'result.json does not record before/after fingerprint components.'
}

function Test-SimulationExitCode {
    Import-ScriptFunctions -Path $runnerPath
    Assert-True ([bool](Get-Command Get-FirstNonZeroExitCode -ErrorAction SilentlyContinue)) 'runner does not define Get-FirstNonZeroExitCode.'
    Assert-True ([bool](Get-Command Assert-SimulationChildExitCodes -ErrorAction SilentlyContinue)) 'runner does not define Assert-SimulationChildExitCodes.'
    Assert-Equal 23 (Get-FirstNonZeroExitCode -MonitorExit 23 -ReportExit 41) 'monitor exit wins when both child exits are nonzero'
    Assert-Equal 41 (Get-FirstNonZeroExitCode -MonitorExit 0 -ReportExit 41) 'report exit used when monitor succeeds'
    Assert-Equal 0 (Get-FirstNonZeroExitCode -MonitorExit 0 -ReportExit 0) 'zero child exits remain zero'

    foreach ($case in @(
            @{ Monitor = 23; Report = 41; Expected = 23 },
            @{ Monitor = 0; Report = 41; Expected = 41 }
        )) {
        $script:CurrentCheckExitCode = 0
        $caught = $null
        try { Assert-SimulationChildExitCodes -MonitorExit $case.Monitor -ReportExit $case.Report }
        catch { $caught = $_ }
        Assert-True ($null -ne $caught) 'nonzero simulator child exits did not fail the check.'
        Assert-Equal $case.Expected $script:CurrentCheckExitCode 'simulator aggregate child exit code'
    }

    $script:CurrentCheckExitCode = 0
    Assert-SimulationChildExitCodes -MonitorExit 0 -ReportExit 0
    Assert-Equal 0 $script:CurrentCheckExitCode 'successful simulator child exits changed the aggregate exit code'

    $runnerAst = Get-ScriptAst -Path $runnerPath
    $simulatorChecks = @($runnerAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    $node.GetCommandName() -eq 'Invoke-Check' -and
                    $node.Extent.Text -match "pubg-fake-presentmon-simulator"
            }, $true))
    Assert-Equal 1 $simulatorChecks.Count 'PUBG simulator check count'
    Assert-True ($simulatorChecks[0].Extent.Text -match 'Assert-SimulationChildExitCodes') 'PUBG simulator check does not apply the real child exit-code assertion.'
}

function Test-SimulatorCompileDependencies {
    $outputRoot = Join-Path $tempRoot 'simulator-compile'
    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $simulatorPath `
            -CompileOnly -OutputRoot $outputRoot 2>&1)
    $exitCode = $LASTEXITCODE
    Assert-Equal 0 $exitCode ('simulator compile-only exit code; output=' + (($output | Out-String).Trim()))
    foreach ($name in @('TslGame.exe', 'FakePresentMon.exe')) {
        Assert-True (Test-Path -LiteralPath (Join-Path (Join-Path $outputRoot 'bin') $name) -PathType Leaf) "simulator compile-only output missing: $name"
    }
}

$tests = [ordered]@{
    timeout = ${function:Test-PerCheckTimeout}
    'owned-cleanup' = ${function:Test-OwnedSimulatorCleanup}
    'whole-run-finalization' = ${function:Test-WholeRunFinalization}
    'workspace-fingerprint' = ${function:Test-WorkspaceFingerprint}
    'simulation-exit-code' = ${function:Test-SimulationExitCode}
    'simulator-compile' = ${function:Test-SimulatorCompileDependencies}
}
$failures = New-Object Collections.Generic.List[string]
try {
    foreach ($entry in $tests.GetEnumerator()) {
        if ($TestName -ne 'all' -and $TestName -ne $entry.Key) { continue }
        try {
            & $entry.Value
            Write-Host ("PASS {0}" -f $entry.Key)
        }
        catch {
            $failures.Add(("FAIL {0}: {1}" -f $entry.Key, $_.Exception.Message))
            Write-Host $failures[$failures.Count - 1]
        }
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

'Full verification hardening contract tests passed.'
