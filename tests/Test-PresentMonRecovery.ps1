param([Parameter(Mandatory)][string]$RuntimeRoot, [Parameter(Mandatory)][string]$OutputDirectory, [string[]]$Cases = @())
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$fixture = Join-Path $OutputDirectory 'RecoveryTarget.exe'
$fixtureSource = Join-Path $OutputDirectory 'RecoveryTarget.cs'
@'
using System;using System.IO;using System.Diagnostics;using System.Threading;
class RecoveryTarget {
 static string Arg(string[] args,string key,string fallback){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
 static int Main(string[] args){
  if(Array.IndexOf(args,"--target-helper")>=0){Thread.Sleep(180000);return 0;}
  string session=Arg(args,"--session_name","");
  if(session.Length==0){Console.WriteLine("PresentMon 2.4.1 fixture");return 0;}
  string stop=Path.Combine(Path.GetTempPath(),session+".recovery-stop");
  if(Array.IndexOf(args,"--terminate_existing_session")>=0){File.WriteAllText(stop,"stop");return 0;}
  bool runtime=Array.IndexOf(args,"--v1_metrics")>=0;
  bool healthy=Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_FULL_FRAMES")=="1";
  bool produce=runtime||healthy;
  string mode=Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_CAPTURE_CASE");
  bool earlyExit=false;
  if(mode=="both-exit"||mode=="selected-exit"){
   string marker=Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_RUNTIME_MARKER");
   if(!runtime)earlyExit=mode=="both-exit";
   else if(!File.Exists(marker)){File.WriteAllText(marker,"initial-session");earlyExit=true;}
  }
  if(mode=="all-empty")produce=false;
  if(runtime&&mode=="empty-runtime-retry"){
   string marker=Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_RUNTIME_MARKER");
   if(!File.Exists(marker)){File.WriteAllText(marker,"initial-session-empty");produce=false;}
  }
  int target=int.Parse(Arg(args,"--process_id","0"));
  if(produce)Console.WriteLine(runtime?"Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents":"TimeInDateTime,MsBetweenPresents,Application,ProcessID,SwapChainAddress,PresentMode,AllowsTearing");
  var clock=Stopwatch.StartNew();int duration=int.Parse(Arg(args,"--timed","60"));
  while(clock.Elapsed.TotalSeconds<duration&&!File.Exists(stop)){
   if(earlyExit&&clock.Elapsed.TotalSeconds>=(mode=="selected-exit"?10:3))break;
   if(produce&&(runtime||mode!="stalled-full"||clock.Elapsed.TotalSeconds<2)){string time=DateTime.Now.ToString("o");Console.WriteLine(runtime?"RecoveryTarget.exe,"+target+",A,DXGI,0,0,0,"+time+",0.1,16":time+",16,RecoveryTarget.exe,"+target+",A,Hardware: Independent Flip,1");Console.Out.Flush();}
   Thread.Sleep(16);
  }
  if(File.Exists(stop))File.Delete(stop);
  return 0;
 }
}
'@ | Set-Content -LiteralPath $fixtureSource -Encoding UTF8
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 /target:exe ("/out:"+$fixture) $fixtureSource
if($LASTEXITCODE){throw 'Fixture compile failed'}
$target=$null
$previous=$env:FRAMESCOPE_TEST_FULL_FRAMES
$previousCase=$env:FRAMESCOPE_TEST_CAPTURE_CASE
$previousMarker=$env:FRAMESCOPE_TEST_RUNTIME_MARKER
$results=[Collections.Generic.List[object]]::new()
try {
    $target=Start-Process $fixture -ArgumentList '--target-helper' -PassThru -WindowStyle Hidden
    foreach($case in @(@{Name='both-exit';Seconds=16;Healthy='1';Expected='application-present-v1';MinimumRows=300},@{Name='selected-exit';Seconds=20;Healthy='0';Expected='application-present-v1';MinimumRows=400},@{Name='stalled-full';Seconds=12;Healthy='1';Expected='application-present-v1'},@{Name='publication-cadence';Seconds=6;Healthy='1';Expected='full';Poll='3000'},@{Name='empty-runtime-retry';Seconds=16;Healthy='0';Expected='application-present-v1';MinimumRows=70},@{Name='empty-full';Seconds=12;Healthy='0';Expected='application-present-v1'},@{Name='short-empty-full';Seconds=3;Healthy='0';Expected='application-present-v1'},@{Name='healthy-full';Seconds=3;Healthy='1';Expected='full'},@{Name='all-empty';Seconds=26;Healthy='0';Expected='full';MinimumRows=0;Empty=$true})) {
        if($Cases.Count -and $case.Name -notin $Cases){continue}
        $env:FRAMESCOPE_TEST_FULL_FRAMES=$case.Healthy
        $runRoot=Join-Path $OutputDirectory $case.Name
        New-Item -ItemType Directory -Path $runRoot -Force|Out-Null
        $env:FRAMESCOPE_TEST_CAPTURE_CASE=$case.Name
        $env:FRAMESCOPE_TEST_RUNTIME_MARKER=Join-Path $runRoot 'initial-runtime.marker'
        $poll=if($case.Poll){$case.Poll}else{'250'}
        $arguments=@('--monitor-session','--WaitSeconds','3','--CaptureSeconds',[string]$case.Seconds,
            '--ControlPollIntervalMs',$poll,'--RunRoot',$runRoot,'--RunNamePrefix','RuntimeRecovery',
            '--TargetProcessName','RecoveryTarget.exe','--InitialTargetPid',[string]$target.Id,
            '--PresentMonExe',$fixture,'--ProcessSamplerExe','C:\Program Files\FrameScope\FrameScopeProcessSampler.exe',
            '--SystemSamplerExe','C:\Program Files\FrameScope\FrameScopeSystemSampler.exe')
        $info=[Diagnostics.ProcessStartInfo]::new()
        $info.FileName=Join-Path $RuntimeRoot 'FrameScopeMonitor.exe'
        $info.Arguments=($arguments|ForEach-Object{'"'+$_+'"'}) -join ' '
        $info.WorkingDirectory=$RuntimeRoot;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
        $p=[Diagnostics.Process]::new();$p.StartInfo=$info;[void]$p.Start()
        $samples=[Collections.Generic.List[object]]::new();$deadline=[datetime]::UtcNow.AddSeconds(45)
        do{
            $run=Get-ChildItem $runRoot -Directory -Recurse|Where-Object{Test-Path (Join-Path $_.FullName 'live-frame-stats.json')}|Select-Object -First 1
            if($run){try{$v=Get-Content (Join-Path $run.FullName 'live-frame-stats.json') -Raw -Encoding UTF8|ConvertFrom-Json;if($v.phase -eq 'capturing' -and ($samples.Count -eq 0 -or $samples[$samples.Count-1].timestamp_ms -ne $v.timestamp_ms)){$samples.Add($v)}}catch{}}
        }while(-not $p.WaitForExit(100) -and [datetime]::UtcNow -lt $deadline)
        if(-not $p.HasExited){throw ('Worker deadline exceeded: '+$p.Id)}
        if($p.ExitCode -ne 0){throw ('Worker failed: '+$p.ExitCode+' evidence '+$runRoot)}
        $run=Get-ChildItem $runRoot -Directory -Recurse | Where-Object {Test-Path (Join-Path $_.FullName 'status.json')} | Select-Object -First 1
        if(-not $run){throw ('No run: '+$runRoot)}
        $status=Get-Content (Join-Path $run.FullName 'status.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $live=Get-Content (Join-Path $run.FullName 'live-frame-stats.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if($status.PresentMonProfile -ne $case.Expected){throw ('Wrong profile '+$status.PresentMonProfile+' expected '+$case.Expected+' evidence '+$run.FullName)}
        if((!$case.Empty -and $live.frame_count -le 0) -or $live.phase -ne 'completed'){throw ('No real final frames: '+$run.FullName)}
        $rows=@(Import-Csv (Join-Path $run.FullName 'presentmon.csv'))
        if($case.Name -in @('both-exit','selected-exit')){
            if($status.PresentMonRuntimeRetries -ne 1){throw 'An exited collector with prior frames must restart once'}
            $dates=@($rows|ForEach-Object{[datetime]$_.TimeInSeconds})
            if(($dates[-1]-$dates[0]).TotalSeconds -lt 12){throw 'Restart lost the prefix or never resumed actual frames'}
            $headerCount=@(Get-Content (Join-Path $run.FullName 'presentmon.csv')|Where-Object{$_ -like 'Application,ProcessID,*'}).Count
            if($headerCount -ne 1){throw 'Restart inserted another CSV header'}
        }
        $minimum=if($case.ContainsKey('MinimumRows')){$case.MinimumRows}else{$case.Seconds*30}
        if($rows.Count -lt $minimum){throw ('Beginning of capture lost or rows discarded: '+$rows.Count)}
        if($case.Empty -and ($rows.Count -ne 0 -or $live.fps_1low -or $live.fps_01low -or $live.fps)){throw 'Empty sources invented FPS data'}
        if($case.Empty -and $status.PresentMonRuntimeRetries -ne 2){throw 'No-event retry must stop at two attempts'}
        if($case.Name -eq 'publication-cadence'){
            for($index=1;$index -lt $samples.Count;$index++){if($samples[$index].timestamp_ms-$samples[$index-1].timestamp_ms -ge 2500){throw 'Three-second control polling starves the live FPS/Low publisher'}}
            if($samples.Count -lt 4){throw 'Live publications did not run independently of the slow control interval'}
        }
        if($case.Name -eq 'stalled-full' -and -not (Test-Path (Join-Path $run.FullName 'presentmon-full-attempt.csv'))){throw 'Original full frames were not preserved'}
        if($case.Name -eq 'empty-runtime-retry' -and $status.PresentMonRuntimeRetries -ne 1){throw 'Silent initial runtime session was not recreated once'}
        $samples.ToArray()|ConvertTo-Json -Depth 5|Set-Content (Join-Path $run.FullName 'test-live-observations.json') -Encoding UTF8
        $results.Add([pscustomobject]@{Case=$case.Name;Profile=$status.PresentMonProfile;Rows=$rows.Count;Run=$run.FullName})
        $p.Dispose()
    }
    $results | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'result.json') -Encoding UTF8
    'PASS: stalled full capture, short capture, healthy full capture, continuous raw frames, final publication'
} finally {
    $env:FRAMESCOPE_TEST_FULL_FRAMES=$previous
    $env:FRAMESCOPE_TEST_CAPTURE_CASE=$previousCase
    $env:FRAMESCOPE_TEST_RUNTIME_MARKER=$previousMarker
    if($target -and -not $target.HasExited){$target.Kill();[void]$target.WaitForExit(3000)}
}
