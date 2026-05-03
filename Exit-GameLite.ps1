$ErrorActionPreference = 'Stop'

$statePath = Join-Path $PSScriptRoot 'game-lite-priority-state.json'

if (-not (Test-Path -LiteralPath $statePath)) {
    [pscustomobject]@{
        StatePath = $statePath
        Status    = 'no-state-file'
        Message   = 'No saved game-lite priority snapshot was found.'
    } | ConvertTo-Json -Depth 3
    exit 0
}

$parsedSnapshot = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$snapshot = New-Object System.Collections.ArrayList

if ($parsedSnapshot -is [System.Array]) {
    foreach ($entry in $parsedSnapshot) {
        if ($entry) { [void]$snapshot.Add($entry) }
    }
}
elseif ($parsedSnapshot) {
    [void]$snapshot.Add($parsedSnapshot)
}

$restores = foreach ($entry in $snapshot) {
    $status = 'not-running'
    $after = $null
    $errorMessage = $null

    try {
        $process = Get-Process -Id $entry.Id -ErrorAction Stop

        if ($process.ProcessName -eq $entry.ProcessName) {
            $sameStartTime = $true
            if ($entry.PSObject.Properties.Name -contains 'StartTime' -and $entry.StartTime) {
                try {
                    $sameStartTime = ($process.StartTime.ToString('o') -eq [string]$entry.StartTime)
                }
                catch {
                    $sameStartTime = $true
                }
            }

            if ($sameStartTime) {
                $process.PriorityClass = $entry.PriorityClass
                $after = [string](Get-Process -Id $entry.Id -ErrorAction Stop).PriorityClass
                $status = 'restored'
            }
            else {
                $status = 'process-restarted'
            }
        }
        else {
            $status = 'pid-reused'
        }
    }
    catch [Microsoft.PowerShell.Commands.ProcessCommandException] {
        $status = 'not-running'
    }
    catch {
        $message = $_.Exception.Message
        if ($message -match 'Cannot find a process|process identifier') {
            $status = 'not-running'
        }
        else {
            $status = 'failed'
            $errorMessage = $message
        }
    }

    [pscustomobject]@{
        Id           = $entry.Id
        ProcessName  = $entry.ProcessName
        RestoredTo   = $entry.PriorityClass
        CurrentAfter = $after
        Status       = $status
        Error        = $errorMessage
    }
}

$failedCount = @($restores | Where-Object { $_.Status -eq 'failed' }).Count
$stateRemoved = $false

if ($failedCount -eq 0) {
    Remove-Item -LiteralPath $statePath -Force
    $stateRemoved = $true
}

[pscustomobject]@{
    StatePath     = $statePath
    StateRemoved  = $stateRemoved
    RestoredCount = @($restores | Where-Object { $_.Status -eq 'restored' }).Count
    NotRunning    = @($restores | Where-Object { $_.Status -eq 'not-running' }).Count
    Restores      = @($restores)
} | ConvertTo-Json -Depth 5
