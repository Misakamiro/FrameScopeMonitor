param([string]$RepoRoot=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
. (Join-Path $RepoRoot 'tools/Build-IsolatedInstaller.ps1') -OutputDirectory (Join-Path $RepoRoot 'artifacts/unused-functions-only') -FunctionsOnly
. (Join-Path $RepoRoot 'desktop-preview/installer/pack-payload.ps1') -RepoRoot $RepoRoot -FunctionsOnly
$owned=Join-Path ([IO.Path]::GetTempPath()) ('framescope-storage-test-'+[guid]::NewGuid().ToString('N'))
$storage=Join-Path $owned 'builds'
New-Item -ItemType Directory -Path $storage -Force|Out-Null
function Reject([scriptblock]$action,[string]$message){
    $failure=$null
    try{& $action}catch{$failure=$_.Exception.Message}
    if(-not$failure -or $failure -notmatch $message){throw "Expected $message; got $failure"}
}
try{
    Assert-IsolatedBuildStorage -Directory (Join-Path $storage 'one') -StorageRoot $storage -FreeBytes 100GB
    Reject {Assert-IsolatedBuildStorage -Directory (Join-Path $owned 'outside') -StorageRoot $storage -FreeBytes 100GB} 'managed build root'
    Reject {Assert-IsolatedBuildStorage -Directory (Join-Path $storage 'nested/one') -StorageRoot $storage -FreeBytes 100GB} 'direct child'
    Reject {Assert-IsolatedBuildStorage -Directory (Join-Path $storage 'one') -StorageRoot $storage -FreeBytes 29GB} '30 GiB'
    New-Item -ItemType Directory -Path (Join-Path $storage 'old'),(Join-Path $storage 'failed-before-inventory')|Out-Null
    Reject {Assert-IsolatedBuildStorage -Directory (Join-Path $storage 'three') -StorageRoot $storage -FreeBytes 100GB} 'two retained build'
    if(Test-Path (Join-Path $storage 'three')){throw 'Rejected build created an output directory'}
    if(@(Get-ChildItem $storage -Directory).Count -ne 2){throw 'Preflight deleted existing builds'}
    if($IsWindows){
        $outside=Join-Path $owned 'outside';New-Item -ItemType Directory -Path $outside|Out-Null
        [IO.File]::WriteAllText((Join-Path $outside 'keep.txt'),'preserve')
        $link=Join-Path $owned 'linked-builds'
        New-Item -ItemType Junction -Path $link -Target $outside|Out-Null
        try{
            Reject {Assert-IsolatedBuildStorage -Directory (Join-Path $link 'one') -StorageRoot $link -FreeBytes 100GB} 'Reparse'
            if([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')) -ne 'preserve'){throw 'Junction target changed'}
        }finally{Remove-Item -LiteralPath $link -Force}
    }
    Write-Output 'PASS managed root, direct-child boundary, low disk, failed-build retention limit, reparse rejection, and non-mutating refusal'
}finally{
    Assert-PayloadPath -Path $owned -Root ([IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $owned -Recurse -Force
}
