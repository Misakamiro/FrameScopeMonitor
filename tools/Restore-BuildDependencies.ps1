$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskManifest=Get-Content -LiteralPath (Join-Path $taskRoot 'build-dependencies.lock.json') -Raw -Encoding UTF8|ConvertFrom-Json
function Get-DependencyHash([string]$Path){
    $taskStream=[IO.File]::OpenRead($Path);$taskHasher=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($taskHasher.ComputeHash($taskStream)).Replace('-','')}
    finally{$taskStream.Dispose();$taskHasher.Dispose()}
}
$taskMissing=@()
foreach($taskEntry in $taskManifest.files){
    $taskPath=[IO.Path]::GetFullPath((Join-Path $taskRoot $taskEntry.file))
    if(-not $taskPath.StartsWith($taskRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Dependency escaped source directory'}
    if(Test-Path -LiteralPath $taskPath){
        if((Get-Item -LiteralPath $taskPath).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Dependency is a reparse point'}
        if((Get-DependencyHash $taskPath) -ne $taskEntry.sha256){throw ('Existing dependency differs; preserve it for inspection: '+$taskEntry.file)}
    }else{$taskMissing+=$taskEntry}
}
if(-not $taskMissing.Count){Write-Output 'Pinned build dependencies already verified';exit 0}
$taskCache=Join-Path $taskRoot 'obj\build-dependencies-source.zip'
New-Item -ItemType Directory -Path (Split-Path -Parent $taskCache) -Force|Out-Null
if(-not(Test-Path -LiteralPath $taskCache)){
    $taskPartial=$taskCache+'.partial'
    Invoke-WebRequest -Uri $taskManifest.archiveUrl -OutFile $taskPartial
    if((Get-DependencyHash $taskPartial) -ne $taskManifest.archiveSha256){throw 'Dependency source archive hash differs'}
    Move-Item -LiteralPath $taskPartial -Destination $taskCache
}
if((Get-DependencyHash $taskCache) -ne $taskManifest.archiveSha256){throw 'Cached dependency source archive hash differs'}
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$taskZip=[IO.Compression.ZipFile]::OpenRead($taskCache)
try{
    foreach($taskEntry in $taskMissing){
        $taskItem=$taskZip.GetEntry($taskEntry.file)
        if(-not $taskItem){throw ('Pinned dependency entry missing: '+$taskEntry.file)}
        $taskPath=[IO.Path]::GetFullPath((Join-Path $taskRoot $taskEntry.file))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($taskItem,$taskPath,$false)
        if((Get-DependencyHash $taskPath) -ne $taskEntry.sha256){throw ('Extracted dependency differs: '+$taskEntry.file)}
        Write-Output ('Restored pinned dependency: '+$taskEntry.file)
    }
}finally{$taskZip.Dispose()}
