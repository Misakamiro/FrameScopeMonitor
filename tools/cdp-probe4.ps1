# CDP probe 4: page visibility / focus / rAF activity
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$http = New-Object System.Net.Http.HttpClient
$json = $http.GetStringAsync('http://127.0.0.1:9333/json/list').GetAwaiter().GetResult()
$page = ($json | ConvertFrom-Json) | Where-Object { $_.type -eq 'page' -and $_.url -match 'tauri' } | Select-Object -First 1
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).GetAwaiter().GetResult()
$script:id = 200
function Invoke-Cdp([string]$expr) {
  $script:id++
  $req = @{ id = $script:id; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true } } | ConvertTo-Json -Depth 5 -Compress
  $bytes = [Text.Encoding]::UTF8.GetBytes($req)
  [void]$ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
  $buf = New-Object byte[] (8MB)
  $sb = New-Object Text.StringBuilder
  do {
    $seg = [ArraySegment[byte]]::new($buf)
    $r = $ws.ReceiveAsync($seg, $ct).GetAwaiter().GetResult()
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
  } while (-not $r.EndOfMessage)
  ($sb.ToString() | ConvertFrom-Json).result.result.value
}

Write-Output '=== visibility/focus ==='
Invoke-Cdp 'JSON.stringify({visibility: document.visibilityState, hasFocus: document.hasFocus(), hidden: document.hidden})'

Write-Output '=== rAF liveness (3s window) ==='
Invoke-Cdp 'new Promise(res => { var n = 0; var t0 = performance.now(); function f(){ n++; if (performance.now() - t0 < 3000) requestAnimationFrame(f); else res("rAF ticks in 3s: " + n) } requestAnimationFrame(f) })'

Write-Output '=== force a frame then check ==='
Invoke-Cdp 'document.body.style.backgroundColor = "#ff00ff"; "painted magenta bg"'
Start-Sleep -Seconds 2
Invoke-Cdp 'document.body.style.backgroundColor = ""; "reverted"'
