param(
    [switch]$ForceSnapshot,
    [string[]]$ExcludeProcessNames = @()
)

$ErrorActionPreference = 'Stop'

$statePath = Join-Path $PSScriptRoot 'game-lite-priority-state.json'

# Hard protection: never change priority for interactive clients or service
# chains that can affect overlays, pairing, messaging, inventory, notifications
# or background device links.
$protectedNames = @(
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

# Conservative list: only low-risk renderers/reporters remain eligible.
$targets = @(
    @{ Name = 'Codex'; Reason = 'Codex desktop process during games' },
    @{ Name = 'codex'; Reason = 'Codex helper process during games' },
    @{ Name = 'steam'; Reason = 'Steam main client, overlay web helpers stay protected' },
    @{ Name = 'EpicGamesLauncher'; Reason = 'Epic Games Launcher UI/background process' },
    @{ Name = 'EpicWebHelper'; Reason = 'Epic Games Launcher web helper' },
    @{ Name = 'RiotClientServices'; Reason = 'Riot Client background service process' },
    @{ Name = 'RiotClientUx'; Reason = 'Riot Client UI process' },
    @{ Name = 'RiotClientUxRender'; Reason = 'Riot Client renderer process' },
    @{ Name = 'Battle.net'; Reason = 'Battle.net launcher UI process' },
    @{ Name = 'Agent'; Reason = 'Battle.net update agent'; PathContains = @('Battle.net', 'Blizzard Entertainment') },
    @{ Name = 'EADesktop'; Reason = 'EA app desktop client' },
    @{ Name = 'EABackgroundService'; Reason = 'EA app background service process' },
    @{ Name = 'EALauncher'; Reason = 'EA app launcher helper' },
    @{ Name = 'UbisoftConnect'; Reason = 'Ubisoft Connect launcher' },
    @{ Name = 'UbisoftConnectWebCore'; Reason = 'Ubisoft Connect web helper' },
    @{ Name = 'XboxPcApp'; Reason = 'Xbox app UI process' },
    @{ Name = 'gamingservices'; Reason = 'Xbox Gaming Services process, priority only' },
    @{ Name = 'WeGame'; Reason = 'Tencent WeGame platform client' },
    @{ Name = 'tgp_daemon'; Reason = 'Tencent game platform daemon' },
    @{ Name = 'TenioDL'; Reason = 'Tencent game platform download helper' },
    @{ Name = 'HoYoPlay'; Reason = 'HoYoPlay launcher' },
    @{ Name = 'HYP'; Reason = 'HoYoPlay helper process' },
    @{ Name = 'KuroLauncher'; Reason = 'Kuro Games launcher' },
    @{ Name = 'KuroGameLauncher'; Reason = 'Kuro Games launcher helper' },
    @{ Name = 'KRLauncher'; Reason = 'Kuro Games launcher helper' },
    @{ Name = 'TapTap'; Reason = 'TapTap game platform client' },
    @{ Name = 'TapLauncher'; Reason = 'TapTap launcher helper' },
    @{ Name = 'GoldenFiled'; Reason = 'background utility with observed CPU spikes' },
    @{ Name = 'wallpaper64'; Reason = 'Wallpaper Engine renderer' },
    @{ Name = 'wallpaperservice32'; Reason = 'Wallpaper Engine service helper' },
    @{ Name = 'cloudmusic_reporter'; Reason = 'NetEase Cloud Music reporter helper, player core stays untouched' }
)

$excludeNames = @($ExcludeProcessNames | Where-Object { $_ } | ForEach-Object { $_.Trim() })
$excludeNames += $protectedNames
$excludeNames = @($excludeNames | Select-Object -Unique)
if ($excludeNames.Count -gt 0) {
    $targets = @($targets | Where-Object { $excludeNames -notcontains $_.Name })
}

$targetNames = $targets.Name

function Test-GameLiteTargetMatch {
    param($Process, $Target)

    if ($Process.ProcessName -ne $Target.Name) { return $false }
    if ($Target.PathContains) {
        $path = $null
        try { $path = [string]$Process.Path } catch {}
        if (-not $path) { return $false }
        foreach ($fragment in @($Target.PathContains)) {
            if ($path.IndexOf([string]$fragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                return $true
            }
        }
        return $false
    }
    return $true
}

$running = foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
    if ($targetNames -notcontains $process.ProcessName) { continue }
    foreach ($target in $targets) {
        if (Test-GameLiteTargetMatch -Process $process -Target $target) {
            $process
            break
        }
    }
}

function Get-ProcessStartTimeText {
    param($Process)

    try {
        return $Process.StartTime.ToString('o')
    }
    catch {
        return $null
    }
}

$snapshotWritten = $false
if ($ForceSnapshot -or -not (Test-Path -LiteralPath $statePath)) {
    $snapshot = foreach ($process in $running) {
        [pscustomobject]@{
            Time          = (Get-Date).ToString('o')
            Id            = $process.Id
            ProcessName   = $process.ProcessName
            PriorityClass = [string]$process.PriorityClass
            StartTime     = Get-ProcessStartTimeText -Process $process
            Path          = $process.Path
        }
    }
    @($snapshot) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding UTF8
    $snapshotWritten = $true
}
else {
    $parsedSnapshot = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $snapshotList = New-Object System.Collections.ArrayList

    if ($parsedSnapshot -is [System.Array]) {
        foreach ($entry in $parsedSnapshot) {
            if ($entry) { [void]$snapshotList.Add($entry) }
        }
    }
    elseif ($parsedSnapshot) {
        [void]$snapshotList.Add($parsedSnapshot)
    }

    $knownKeys = @{}
    foreach ($entry in $snapshotList) {
        $knownKeys['{0}:{1}' -f $entry.Id, $entry.ProcessName] = $true
    }

    foreach ($process in $running) {
        $key = '{0}:{1}' -f $process.Id, $process.ProcessName
        if (-not $knownKeys.ContainsKey($key)) {
            [void]$snapshotList.Add([pscustomobject]@{
                Time          = (Get-Date).ToString('o')
                Id            = $process.Id
                ProcessName   = $process.ProcessName
                PriorityClass = [string]$process.PriorityClass
                StartTime     = Get-ProcessStartTimeText -Process $process
                Path          = $process.Path
            })
            $snapshotWritten = $true
        }
    }

    $snapshot = @($snapshotList)
    if ($snapshotWritten) {
        @($snapshot) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding UTF8
    }
}

$changes = foreach ($process in $running) {
    $before = [string]$process.PriorityClass
    $after = $before
    $status = 'unchanged'
    $errorMessage = $null

    try {
        if ($process.PriorityClass -notin @('Idle', 'BelowNormal')) {
            $process.PriorityClass = 'BelowNormal'
            $after = [string](Get-Process -Id $process.Id -ErrorAction Stop).PriorityClass
            $status = 'changed'
        }
    }
    catch {
        $status = 'failed'
        $errorMessage = $_.Exception.Message
    }

    [pscustomobject]@{
        Id            = $process.Id
        ProcessName   = $process.ProcessName
        Before        = $before
        After         = $after
        Status        = $status
        Reason        = ($targets | Where-Object { Test-GameLiteTargetMatch -Process $process -Target $_ } | Select-Object -First 1).Reason
        Error         = $errorMessage
    }
}

[pscustomobject]@{
    StatePath       = $statePath
    SnapshotWritten = $snapshotWritten
    SnapshotCount   = @($snapshot).Count
    Excluded        = @($excludeNames)
    ChangedCount    = @($changes | Where-Object { $_.Status -eq 'changed' }).Count
    Changes         = @($changes)
} | ConvertTo-Json -Depth 5
