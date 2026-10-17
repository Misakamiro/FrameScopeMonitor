param([Parameter(Mandatory)][string]$Payload,[Parameter(Mandatory)][string]$Version,[Parameter(Mandatory)][string]$GitSha)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
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
    foreach($required in @('FrameScope.exe','FrameScopeMonitor.exe','FrameScopeUninstaller.exe','FrameScopeHardwareService.exe','FrameScopeHardwareProvider.exe','FrameScopeReportGenerator.exe','FrameScopeProcessSampler.exe','FrameScopeSystemSampler.exe','Restore-FrameScope.cmd','LICENSE','LICENSE.EXTRA')){
        if(-not $seen.ContainsKey($required)){throw "Required component missing: $required"}
    }
    Write-Output "Payload verified: $($seen.Count) files, $Version, $GitSha"
} finally {$archive.Dispose()}
