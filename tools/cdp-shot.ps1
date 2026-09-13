# CDP probe 3: capture the renderer's own frame.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$http = New-Object System.Net.Http.HttpClient
$json = $http.GetStringAsync('http://127.0.0.1:9333/json/list').GetAwaiter().GetResult()
$page = ($json | ConvertFrom-Json) | Where-Object { $_.type -eq 'page' -and $_.url -match 'tauri' } | Select-Object -First 1
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).GetAwaiter().GetResult()

$script:id = 100
function Invoke-CdpRaw([string]$method, [hashtable]$p) {
  $script:id++
  $req = @{ id = $script:id; method = $method; params = $p } | ConvertTo-Json -Depth 5 -Compress
  $bytes = [Text.Encoding]::UTF8.GetBytes($req)
  [void]$ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
  $buf = New-Object byte[] (64MB)
  $sb = New-Object Text.StringBuilder
  do {
    $seg = [ArraySegment[byte]]::new($buf)
    $r = $ws.ReceiveAsync($seg, $ct).GetAwaiter().GetResult()
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
  } while (-not $r.EndOfMessage)
  return $sb.ToString()
}

$out = Invoke-CdpRaw 'Page.captureScreenshot' @{ format = 'png' }
$resp = $out | ConvertFrom-Json
if ($resp.result -and $resp.result.data) {
  [IO.File]::WriteAllBytes("$env:TEMP\fs-cdp-frame.png", [Convert]::FromBase64String($resp.result.data))
  Write-Output ("frame saved: " + (Get-Item "$env:TEMP\fs-cdp-frame.png").Length + " bytes -> $env:TEMP\fs-cdp-frame.png")
} else {
  Write-Output $out.Substring(0, [Math]::Min(400, $out.Length))
}
