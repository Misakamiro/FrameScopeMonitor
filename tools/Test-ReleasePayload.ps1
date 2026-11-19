param([Parameter(Mandatory)][string]$Payload,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$GitSha,[ValidateSet("Full","Cumulative")][string]$PackageMode)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
if(-not ('FrameScopePayloadManifestReader' -as [type])){
 Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class FrameScopePayloadManifestReader {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
 [DllImport("kernel32.dll", EntryPoint="FindResourceW", SetLastError=true)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
 [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr data);
 [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 public static string Read(string path) {
  IntPtr module=LoadLibraryEx(path,IntPtr.Zero,0x22);
  if(module==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
  try {
   IntPtr resource=FindResource(module,new IntPtr(1),new IntPtr(24));
   if(resource==IntPtr.Zero)throw new InvalidOperationException("Missing executable manifest");
   uint size=SizeofResource(module,resource);
   IntPtr data=LockResource(LoadResource(module,resource));
   if(data==IntPtr.Zero||size==0||size>1048576)throw new InvalidOperationException("Invalid executable manifest");
   byte[] bytes=new byte[size];Marshal.Copy(data,bytes,0,bytes.Length);
   return Encoding.UTF8.GetString(bytes).Trim('\uFEFF','\0');
  } finally {FreeLibrary(module);}
 }
}
'@
}
$archive=[IO.Compression.ZipFile]::OpenRead($Payload)
try {
    function Read-Entry([string]$Name){
        $entry=$archive.GetEntry($Name)
        if(-not $entry){throw "Payload missing $Name"}
        $stream=$entry.Open();$memory=[IO.MemoryStream]::new()
        try{$stream.CopyTo($memory);return ,$memory.ToArray()}finally{$stream.Dispose();$memory.Dispose()}
    }
    $manifest=[Text.Encoding]::UTF8.GetString((Read-Entry 'FrameScopePayloadManifest.json')).TrimStart([char]0xfeff) | ConvertFrom-Json
    $seen=@{}
    foreach($file in $manifest.files){
        if($file.path -match '(^|[\\/])\.\.([\\/]|$)|:|^[/\\]' -or $seen.ContainsKey($file.path)){throw 'Unsafe or duplicate payload path'}
        $seen[$file.path]=$true
        $bytes=Read-Entry $file.path
        $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
        if($bytes.Length -ne $file.length -or $hash -ne $file.sha256){throw "Payload identity mismatch: $($file.path)"}
    }
    if(@($archive.Entries|Where-Object{-not $_.FullName.EndsWith('/')}).Count -ne $seen.Count+1){throw 'Unexpected payload files'}
    $build=[Text.Encoding]::UTF8.GetString((Read-Entry 'FrameScopeBuildInfo.json')) | ConvertFrom-Json
    if($build.version -cne $Version -or $build.git_sha -cne $GitSha){throw 'Payload build version/SHA mismatch'}
    $marker=[Text.Encoding]::UTF8.GetString((Read-Entry 'FrameScopeHostCapabilities.json')).TrimStart([char]0xfeff) | ConvertFrom-Json
    $hostHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData((Read-Entry 'FrameScope.exe')))
    if($marker.hostSha256 -ne $hostHash){throw 'Host capability mismatch'}
    # Read the embedded PE resource without executing the payload. The compatibility
    # monitor keeps asInvoker for control/background commands and gates interactive entry in code.
    foreach($hostName in @('FrameScope.exe','FrameScopeMonitor.exe')){
        $taskManifestFile=Join-Path ([IO.Path]::GetTempPath()) ('framescope-manifest-'+[guid]::NewGuid().ToString('N')+'.exe')
        try{
            [IO.File]::WriteAllBytes($taskManifestFile,(Read-Entry $hostName))
            [xml]$taskXml=[FrameScopePayloadManifestReader]::Read($taskManifestFile)
            $taskLevel=$taskXml.SelectSingleNode('//*[local-name()="requestedExecutionLevel"]')
            $taskExpected=if($hostName -eq 'FrameScope.exe'){'requireAdministrator'}else{'asInvoker'}
            if(-not $taskLevel -or $taskLevel.level -cne $taskExpected -or $taskLevel.uiAccess -cne 'false'){throw "Invalid startup manifest: $hostName"}
        }finally{if(Test-Path -LiteralPath $taskManifestFile){Remove-Item -LiteralPath $taskManifestFile}}
    }
    foreach($required in @('FrameScope.exe','FrameScopeMonitor.exe','FrameScopeUninstaller.exe','FrameScopeHardwareService.exe','FrameScopeHardwareProvider.exe','FrameScopeReportGenerator.exe','FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','Restore-FrameScope.cmd','LICENSE','LICENSE.EXTRA')){
        if(-not $seen.ContainsKey($required)){throw "Required component missing: $required"}
    }
    if ($PackageMode) {
        $kind=[Text.Encoding]::UTF8.GetString((Read-Entry 'FrameScopePackage.json')) | ConvertFrom-Json
        if($kind.schema -ne 1 -or $kind.kind -cne $PackageMode.ToLowerInvariant()){throw 'Payload package kind mismatch'}
        $components=[Text.Encoding]::UTF8.GetString((Read-Entry 'FrameScopeComponents.json')) | ConvertFrom-Json
        if($components.schema -ne 1){throw 'Invalid installed component inventory'}
        $componentPaths=@{};$componentIds=@{}
        foreach($component in $components.components){
            if($componentIds.ContainsKey($component.id)){throw 'Duplicate installed component identity'}
            $componentIds[$component.id]=$true
            foreach($file in $component.files){
                if($componentPaths.ContainsKey($file.path)){throw 'Duplicate installed component path'}
                $componentPaths[$file.path]=$true
                if($PackageMode -eq 'Full'){
                    $payloadEntry=@($manifest.files | Where-Object {$_.path -ceq $file.path})
                    if($payloadEntry.Count -ne 1 -or $payloadEntry[0].length -ne $file.length -or $payloadEntry[0].sha256 -ine $file.sha256){throw 'Full payload differs from reusable component inventory'}
                } elseif($seen.ContainsKey($file.path)){throw 'Cumulative payload embeds reusable component bytes'}
            }
        }
        if($PackageMode -eq 'Cumulative' -and @($manifest.files | Where-Object {$_.path -match '^(toolbox/files/|dependencies/)'}).Count){throw 'Cumulative payload contains component-owned files'}
    }
    Write-Output "Payload verified: $($seen.Count) files, $Version, $GitSha"
} finally {$archive.Dispose()}
