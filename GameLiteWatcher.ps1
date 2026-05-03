param(
    [int]$IntervalSeconds = 5
)

$ErrorActionPreference = 'Continue'

$root = $PSScriptRoot
$enterScript = Join-Path $root 'Enter-GameLite.ps1'
$exitScript = Join-Path $root 'Exit-GameLite.ps1'
$statePath = Join-Path $root 'game-lite-priority-state.json'
$logPath = Join-Path $root 'game-lite-watcher.log'

$protectedProcessNames = @(
    'steamwebhelper',
    'gameoverlayui64',
    'steamservice',
    'steamerrorreporter',
    'steamerrorreporter64',
    'O+Connect',
    'devicespace',
    'oplus_remote_ui',
    'oplus_remote_service',
    'OplusRemoteService',
    'pantaChannelService',
    'adb',
    'everything',
    'Everything',
    'QQ',
    'QQEX',
    'WeChat',
    'WeChatAppEx',
    'Weixin'
)

$gameProcesses = @(
    'bf6',
    'cs2',
    'Cyberpunk2077',
    'HogwartsLegacy',
    'OPUS_ Prism Peak',
    # Domestic game runtime processes. Launchers and platforms can be lowered by Enter-GameLite,
    # but active game runtimes and overlay-sensitive helpers stay excluded.
    'DeltaForceClient',
    'DeltaForceClient-Win64-Shipping',
    'HTGame',
    'VALORANT-Win64-Shipping'
)

function Write-GameLiteLog {
    param([string]$Message)
    $line = '{0} {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Message
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Get-ActiveGameProcess {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $gameProcesses -contains $_.ProcessName } |
        Select-Object Id, ProcessName
}

function Get-GameLiteExclusions {
    param([object[]]$ActiveGames)

    $activeNames = @($ActiveGames | ForEach-Object { $_.ProcessName })
    $exclusions = @($protectedProcessNames)

    if ($activeNames -contains 'cs2') {
        # CS2 is sensitive to short startup/map-load stalls. The global
        # protected list already keeps Steam, OPPO Connect, Everything and
        # chat/helper chains untouched.
    }

    $exclusions | Select-Object -Unique
}

Write-GameLiteLog ('watcher-start interval={0}s games={1}' -f $IntervalSeconds, ($gameProcesses -join ','))

while ($true) {
    try {
        $activeGames = @(Get-ActiveGameProcess)
        $hasGame = $activeGames.Count -gt 0
        $liteActive = Test-Path -LiteralPath $statePath

        if ($hasGame -and -not $liteActive) {
            $names = ($activeGames | ForEach-Object { '{0}:{1}' -f $_.ProcessName, $_.Id }) -join ','
            $exclusions = @(Get-GameLiteExclusions -ActiveGames $activeGames)
            $excludeText = if ($exclusions.Count -gt 0) { ' exclusions={0}' -f ($exclusions -join ',') } else { '' }

            Write-GameLiteLog ("game-detected enter-lite {0}{1}" -f $names, $excludeText)
            & $enterScript -ForceSnapshot -ExcludeProcessNames $exclusions | Out-String | ForEach-Object {
                if ($_.Trim()) { Write-GameLiteLog ("enter-result {0}" -f $_.Trim()) }
            }
        }
        elseif (-not $hasGame -and $liteActive) {
            Write-GameLiteLog 'no-game-detected exit-lite'
            & $exitScript | Out-String | ForEach-Object {
                if ($_.Trim()) { Write-GameLiteLog ("exit-result {0}" -f $_.Trim()) }
            }
        }
    }
    catch {
        Write-GameLiteLog ("watcher-error {0}" -f $_.Exception.Message)
    }

    Start-Sleep -Seconds $IntervalSeconds
}
