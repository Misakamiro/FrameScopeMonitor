$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskWrapper=Join-Path $taskRoot 'packaging\Uninstall-FrameScopeMonitor.cmd'
$taskText=Get-Content -LiteralPath $taskWrapper -Raw
if($taskText -match '(?i)reg\s+delete|rmdir|\brd\s|\bdel\s|dp0FrameScopeUninstaller\.exe'){throw 'Unsafe legacy uninstall wrapper'}
if($taskText -notmatch 'FrameScope Installer\.exe' -or $taskText -notmatch '--uninstall'){throw 'Wrapper does not select the transactional entry'}
$taskReadme=Get-Content -LiteralPath (Join-Path $taskRoot 'packaging\README-FrameScopeMonitor.txt') -Raw
if($taskReadme -match '运行 Uninstall-FrameScopeMonitor.cmd 或 FrameScopeUninstaller.exe' -or -not $taskReadme.Contains('"FrameScope Installer.exe" --uninstall')){throw 'Readme still recommends legacy uninstall'}
$taskBase='C:\fs-build\b203\installer-safety-tests'
if(-not(Test-Path -LiteralPath $taskBase -PathType Container)){throw 'Use the existing b203 test slot'}
if((Get-Item -LiteralPath $taskBase -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Test slot is a reparse point'}
$taskFixture=Join-Path $taskBase ('entry-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFixture|Out-Null
$taskStub=Join-Path $taskFixture 'Stub.cs'
[IO.File]::WriteAllText($taskStub,'using System; using System.IO; class Stub { static void Main(string[] args) { File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"invoked.txt"),args); } }')
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($taskMode in @('normal','missing')){
    $taskCase=Join-Path $taskFixture $taskMode
    New-Item -ItemType Directory -Path $taskCase|Out-Null
    $taskExecutable=Join-Path $taskCase $(if($taskMode -eq 'normal'){'FrameScope Installer.exe'}else{'FrameScopeUninstaller.exe'})
    & $taskCsc /nologo /target:winexe /platform:x64 ('/out:'+$taskExecutable) $taskStub
    if($LASTEXITCODE){throw 'Cannot compile harmless dispatch stub'}
    $taskCmd=Join-Path $taskCase 'Uninstall.cmd'
    Copy-Item -LiteralPath $taskWrapper -Destination $taskCmd
    if((Get-FileHash $taskCmd).Hash -ne (Get-FileHash $taskWrapper).Hash){throw 'Wrapper copy differs'}
    $taskStart=[Diagnostics.ProcessStartInfo]::new()
    $taskStart.FileName=Join-Path $env:WINDIR 'System32\cmd.exe'
    $taskStart.Arguments='/d /c ""'+$taskCmd+'""'
    $taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
    $taskProcess=[Diagnostics.Process]::Start($taskStart)
    if(-not $taskProcess.WaitForExit(10000)){throw 'Wrapper test timed out'}
    $taskMarker=Join-Path $taskCase 'invoked.txt'
    if($taskMode -eq 'normal'){
        $taskUntil=(Get-Date).AddSeconds(5)
        while(-not(Test-Path $taskMarker) -and (Get-Date) -lt $taskUntil){Start-Sleep -Milliseconds 20}
        if($taskProcess.ExitCode -ne 0 -or -not(Test-Path $taskMarker) -or (Get-Content $taskMarker -Raw).Trim() -ne '--uninstall'){throw 'Transactional dispatch failed'}
    }else{
        Start-Sleep -Milliseconds 200
        if($taskProcess.ExitCode -ne 1 -or (Test-Path $taskMarker)){throw 'Wrapper invoked the legacy helper or masked missing entry'}
    }
    Write-Output "PASS uninstall wrapper: $taskMode"
}
Write-Output ('Retained harmless dispatch fixtures: '+$taskFixture)
