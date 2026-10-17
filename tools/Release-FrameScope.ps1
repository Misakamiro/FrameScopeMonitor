# PowerShell 7. Check is read-only; Build, Draft and Publish are explicit actions.
param(
    [ValidateSet('Check','Build','Draft','Publish')][string]$Action='Check',
    [string]$RepoRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory,
    [string]$SigningDirectory=(Join-Path $env:LOCALAPPDATA 'FrameScope-ReleaseSigning')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'FrameScopeRelease.Common.ps1')
$RepoRoot=(Resolve-Path -LiteralPath $RepoRoot).Path
$public=Join-Path $RepoRoot 'src/updater/release.pub'
$signer=Get-ReleaseSigner $SigningDirectory
if((Get-Content -LiteralPath $public)[1].Trim() -cne (Get-Content -LiteralPath (Join-Path $SigningDirectory 'release.pub'))[1].Trim()){throw 'Signing public key does not match the client'}
if($Action -in @('Check','Build')){
    & (Join-Path $RepoRoot 'tools/Sync-FrameScopeVersion.ps1') -RepoRoot $RepoRoot -Check
    $dirty=Invoke-ReleaseCommand git @('-C',$RepoRoot,'status','--porcelain')
    if($dirty.Count){throw 'Release requires committed, reviewed source; working tree is dirty'}
    $sha=(Invoke-ReleaseCommand git @('-C',$RepoRoot,'rev-parse','HEAD') -join '').Trim()
    $version=(Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'VERSION')).Trim()
    $changelog=Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'CHANGELOG.md')
    $changes=[regex]::Match($changelog,('(?ms)^## '+[regex]::Escape($version)+'\s*\r?\n(?<notes>.*?)(?=^## |\z)'))
    if(-not $changes.Success -or -not $changes.Groups['notes'].Value.Trim()){throw 'CHANGELOG is missing the target release section'}
    if($Action -eq 'Check'){Write-Output "Source preflight passed: $version + $sha (no build, upload or installation)";return}
    if(-not $OutputDirectory){throw 'Build requires a new OutputDirectory'}
    if(Test-Path -LiteralPath $OutputDirectory){throw 'Refusing to reuse a release directory'}
    $existing=@(& gh release view ('v'+$version) --repo $script:FrameScopeRepository --json tagName 2>$null)
    if($LASTEXITCODE -eq 0){throw 'This release version already exists; increment VERSION before building'}
    $refs=Invoke-ReleaseCommand git @('-C',$RepoRoot,'ls-remote','--tags','origin',('refs/tags/v'+$version))
    if($refs.Count){throw 'This public version tag already exists and cannot be replaced'}
    $savedJobs=$env:CARGO_BUILD_JOBS
    if(-not $savedJobs){$env:CARGO_BUILD_JOBS='1'}
    try {
        & (Join-Path $RepoRoot 'tools/Build-IsolatedInstaller.ps1') -RepoRoot $RepoRoot -OutputDirectory $OutputDirectory
    } finally {
        if($null -eq $savedJobs){Remove-Item Env:CARGO_BUILD_JOBS -ErrorAction SilentlyContinue}else{$env:CARGO_BUILD_JOBS=$savedJobs}
    }
    $source=Join-Path $OutputDirectory 'source'
    $result=Get-Content -Raw -LiteralPath (Join-Path $OutputDirectory 'isolated-build-result.json') | ConvertFrom-Json
    if(-not $result.inputsVerified -or $result.sourceHead -cne $sha){throw 'Isolated build provenance mismatch'}
    & (Join-Path $RepoRoot 'tools/Test-ReleasePayload.ps1') -Payload (Join-Path $source 'desktop-preview/installer/src-tauri/payload.zip') -Version $version -GitSha $sha
    $assets=Join-Path $OutputDirectory 'release';New-Item -ItemType Directory -Path $assets | Out-Null
    $name="FrameScope_${version}_x64-setup.exe"
    Copy-Item -LiteralPath (Join-Path $source "desktop-preview/src-tauri/target/release/bundle/installer/$name") -Destination (Join-Path $assets $name)
    $manifest=New-ReleaseManifest (Join-Path $assets $name) $version $sha (Join-Path $assets 'release-manifest.json')
    $password=ConvertTo-SecureString ((Get-Content -Raw -LiteralPath (Join-Path $SigningDirectory 'password.dpapi')).Trim())
    Invoke-Minisign $signer @('-S','-s',(Join-Path $SigningDirectory 'release.key'),'-m',(Join-Path $assets 'release-manifest.json')) $password
    Test-ReleaseFiles $assets $public $signer | Out-Null
    $notes="FrameScope $version`n`nSource: $sha`n`n"+$changes.Groups['notes'].Value.Trim()
    [IO.File]::WriteAllText((Join-Path $assets 'release-notes.md'),$notes,[Text.UTF8Encoding]::new($false))
    [ordered]@{version=$version;git_sha=$sha;manifest_sha256=(Get-FileHash (Join-Path $assets 'release-manifest.json')).Hash;installer_sha256=$manifest.installer.sha256;packaged=$true;published=$false;installed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'release-evidence.json')
    Write-Output "Signed local candidate ready: $assets. Nothing was uploaded or installed."
    return
}
if(-not $OutputDirectory){throw 'Draft/Publish requires the verified release directory'}
$assets=Join-Path $OutputDirectory 'release'
$manifest=Test-ReleaseFiles $assets $public $signer
$tag='v'+$manifest.version
if($Action -eq 'Draft'){
    # No clobber/force: existing tags and releases remain immutable.
    $refs=Invoke-ReleaseCommand git @('-C',$RepoRoot,'ls-remote','--tags','origin',('refs/tags/'+$tag))
    if($refs.Count){
        $tagSha=(Invoke-ReleaseCommand gh @('api',"repos/$script:FrameScopeRepository/commits/$tag",'--jq','.sha') -join '').Trim()
        if($tagSha -cne $manifest.git_sha){throw 'Tag exists for a different commit; public tags cannot be replaced'}
    }else{
        $localTag=@(& git -C $RepoRoot rev-parse --verify ($tag+'^{commit}') 2>$null)
        if($LASTEXITCODE -eq 0){if(($localTag -join '').Trim() -cne $manifest.git_sha){throw 'Local tag identifies another commit'}}
        else{Invoke-ReleaseCommand git @('-C',$RepoRoot,'tag','-a',$tag,$manifest.git_sha,'-m',("FrameScope "+$manifest.version)) | Out-Null}
        Invoke-ReleaseCommand git @('-C',$RepoRoot,'push','origin',("refs/tags/"+$tag)) | Out-Null
    }
    $existing=@(& gh release view $tag --repo $script:FrameScopeRepository --json isDraft,assets 2>$null)
    if($LASTEXITCODE -eq 0){
        $existing=($existing -join "`n")|ConvertFrom-Json
        if(-not $existing.isDraft){throw 'Release is already public; nothing will be overwritten'}
        $expectedNames=@($manifest.installer.name,'release-manifest.json','release-manifest.json.minisig')
        if(@($existing.assets.name|Where-Object{$_ -cnotin $expectedNames}).Count){throw 'Draft contains unexpected attachments'}
        # Resume an interrupted draft upload, verifying existing bytes first.
        $download=Join-Path ([IO.Path]::GetTempPath()) ('framescope-draft-verify-'+[guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $download | Out-Null
        foreach($name in @($existing.assets.name)){
            Invoke-ReleaseCommand gh @('release','download',$tag,'--repo',$script:FrameScopeRepository,'--pattern',$name,'--dir',$download)|Out-Null
            if((Get-FileHash (Join-Path $download $name)).Hash -ne (Get-FileHash (Join-Path $assets $name)).Hash){throw "Existing draft asset differs: $name"}
        }
        foreach($name in $expectedNames){if($name -cnotin @($existing.assets.name)){Invoke-ReleaseCommand gh @('release','upload',$tag,'--repo',$script:FrameScopeRepository,(Join-Path $assets $name))|Out-Null}}
        Test-RemoteRelease $assets $public $signer | ConvertTo-Json -Depth 5
        return
    }
    $arguments=@('release','create',$tag,'--repo',$script:FrameScopeRepository,'--verify-tag','--draft','--title',("FrameScope "+$manifest.version),'--notes-file',(Join-Path $assets 'release-notes.md'))
    if($manifest.channel -eq 'beta'){$arguments+='--prerelease'}
    $arguments+=@((Join-Path $assets $manifest.installer.name),(Join-Path $assets 'release-manifest.json'),(Join-Path $assets 'release-manifest.json.minisig'))
    Invoke-ReleaseCommand gh $arguments
    Test-RemoteRelease $assets $public $signer | ConvertTo-Json -Depth 5
}else{
    $remote=Test-RemoteRelease $assets $public $signer
    if(-not $remote.isDraft){throw 'Release is already public; no public assets will be overwritten'}
    Invoke-ReleaseCommand gh @('release','edit',$tag,'--repo',$script:FrameScopeRepository,'--draft=false')
    Write-Output "Published https://github.com/$script:FrameScopeRepository/releases/tag/$tag"
}
