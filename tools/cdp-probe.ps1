# CDP probe: inspect why the WebView2 page renders unstyled.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

# 1. list targets
$http = New-Object System.Net.Http.HttpClient
$json = $http.GetStringAsync('http://127.0.0.1:9333/json/list').GetAwaiter().GetResult()
$targets = $json | ConvertFrom-Json
$page = $targets | Where-Object { $_.type -eq 'page' -and $_.url -match 'tauri' } | Select-Object -First 1
if (-not $page) { Write-Output "no tauri page target found:"; $targets | ForEach-Object { Write-Output ("  " + $_.type + " " + $_.url) }; exit 1 }
Write-Output ("target: " + $page.url)

# 2. websocket evaluate
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).GetAwaiter().GetResult()

function Invoke-Cdp([string]$expr) {
  $req = @{ id = 1; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 5 -Compress
  $bytes = [Text.Encoding]::UTF8.GetBytes($req)
  $ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
  $buf = New-Object byte[] (4MB)
  $sb = New-Object Text.StringBuilder
  do {
    $seg = [ArraySegment[byte]]::new($buf)
    $r = $ws.ReceiveAsync($seg, $ct).GetAwaiter().GetResult()
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
  } while (-not $r.EndOfMessage)
  ($sb.ToString() | ConvertFrom-Json).result.result.value
}

Write-Output '=== styleSheets count ==='
Invoke-Cdp 'document.styleSheets.length'
Write-Output '=== styleSheets details ==='
Invoke-Cdp 'Array.from(document.styleSheets).map(s => { try { return (s.href || "inline") + " rules=" + s.cssRules.length } catch(e) { return (s.href || "inline") + " ERR " + e.message } }).join("\n")'
Write-Output '=== css resource timing ==='
Invoke-Cdp 'performance.getEntriesByType("resource").filter(r => /\.(css|js)/.test(r.name)).map(r => r.name.split("/").pop() + " size=" + r.transferSize + "/" + r.decodedBodySize + " dur=" + Math.round(r.duration)).join("\n")'
Write-Output '=== root children ==='
Invoke-Cdp 'document.getElementById("root") ? document.getElementById("root").children.length + " kids, first=" + (document.getElementById("root").firstElementChild ? document.getElementById("root").firstElementChild.className.toString().slice(0,80) : "none") : "no root"'
Write-Output '=== first card computed style ==='
Invoke-Cdp 'var el = document.querySelector("#root div div"); el ? JSON.stringify({cls: (el.className||"").toString().slice(0,60), display: getComputedStyle(el).display, pos: getComputedStyle(el).position, h: getComputedStyle(el).height, bg: getComputedStyle(el).backgroundColor, bf: getComputedStyle(el).backdropFilter}) : "none"'
Write-Output '=== body/html overflow + scrollHeight ==='
Invoke-Cdp 'JSON.stringify({bodyH: document.body.scrollHeight, docH: document.documentElement.scrollHeight, bodyOverflow: getComputedStyle(document.body).overflow, innerW: window.innerWidth, innerH: window.innerHeight})'
Write-Output '=== console errors hook ==='
Invoke-Cdp 'window.__fsErrors ? window.__fsErrors.join(" || ") : "none captured"'

$ws.Dispose()
