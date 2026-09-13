# CDP probe round 2: what does the DOM think is on screen?
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$http = New-Object System.Net.Http.HttpClient
$json = $http.GetStringAsync('http://127.0.0.1:9333/json/list').GetAwaiter().GetResult()
$targets = $json | ConvertFrom-Json
$page = $targets | Where-Object { $_.type -eq 'page' -and $_.url -match 'tauri' } | Select-Object -First 1
$ws = New-Object System.Net.WebSockets.ClientWebSocket
$ct = [System.Threading.CancellationToken]::None
$ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, $ct).GetAwaiter().GetResult()

function Invoke-Cdp([string]$expr) {
  $script:id++
  $req = @{ id = $script:id; method = 'Runtime.evaluate'; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } | ConvertTo-Json -Depth 5 -Compress
  $bytes = [Text.Encoding]::UTF8.GetBytes($req)
  $ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
  $buf = New-Object byte[] (8MB)
  $sb = New-Object Text.StringBuilder
  do {
    $seg = [ArraySegment[byte]]::new($buf)
    $r = $ws.ReceiveAsync($seg, $ct).GetAwaiter().GetResult()
    [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count))
  } while (-not $r.EndOfMessage)
  ($sb.ToString() | ConvertFrom-Json).result.result.value
}

$script:id = 10

Write-Output '=== elementFromPoint at centers ==='
Invoke-Cdp 'JSON.stringify([0.5,0.3,0.7].map(f => { var el = document.elementFromPoint(window.innerWidth*f, window.innerHeight*0.5); return {x: Math.round(window.innerWidth*f), tag: el ? el.tagName : null, cls: el ? (el.className||"").toString().slice(0,50) : null, text: el ? (el.textContent||"").slice(0,30) : null} }))'

Write-Output '=== wallpaper img rect+styles ==='
Invoke-Cdp 'var img = document.querySelector("img"); img ? JSON.stringify({src: img.src.split("/").pop(), rect: img.getBoundingClientRect().toJSON(), opacity: getComputedStyle(img).opacity, z: getComputedStyle(img).zIndex, pos: getComputedStyle(img).position, vis: getComputedStyle(img).visibility}) : "no img"'

Write-Output '=== all imgs ==='
Invoke-Cdp 'Array.from(document.images).slice(0,8).map(i => i.src.split("/").pop() + " rect=" + Math.round(i.getBoundingClientRect().width) + "x" + Math.round(i.getBoundingClientRect().height) + " complete=" + i.complete).join("\n")'

Write-Output '=== root kid rects ==='
Invoke-Cdp 'Array.from(document.getElementById("root").children).map(c => { var r = c.getBoundingClientRect(); return (c.className||"").toString().slice(0,40) + " " + Math.round(r.width) + "x" + Math.round(r.height) + "@" + Math.round(r.top) + " disp=" + getComputedStyle(c).display }).join("\n")'

Write-Output '=== hardware grid search ==='
Invoke-Cdp 'var t = Array.from(document.querySelectorAll("*")).filter(e => (e.textContent||"") === "NVIDIA GeForce RTX 3080"); t.length + " found; rect=" + (t[0] ? JSON.stringify(t[0].getBoundingClientRect().toJSON()) : "-") + " chain=" + (t[0] ? (function(e){var s=[];while(e && s.length<8){s.push(e.tagName+"."+(e.className||"").toString().slice(0,20));e=e.parentElement}return s.join(" < ")})(t[0]) : "-")'

Write-Output '=== overlay/covering elements ==='
Invoke-Cdp 'var card = document.evaluate("//*[text()=\"NVIDIA GeForce RTX 3080\"]", document, null, 9, null).singleNodeValue; if (card) { var els = document.elementsFromPoint(card.getBoundingClientRect().left + 5, card.getBoundingClientRect().top + 5); els.slice(0,5).map(e => e.tagName + "." + (e.className||"").toString().slice(0,30)).join(" | ") } else { "card not found" }'
