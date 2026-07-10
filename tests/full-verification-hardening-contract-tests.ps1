param(
    [ValidateSet('all', 'timeout', 'owned-cleanup')]
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

$tests = [ordered]@{
    timeout = ${function:Test-PerCheckTimeout}
    'owned-cleanup' = ${function:Test-OwnedSimulatorCleanup}
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
