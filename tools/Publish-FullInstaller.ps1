param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$RepoRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$SigningDirectory=(Join-Path $env:LOCALAPPDATA 'FrameScope-ReleaseSigning')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'FrameScopeRelease.Common.ps1')
$public=Join-Path $RepoRoot 'src/updater/release.pub'
$signer=Get-ReleaseSigner $SigningDirectory
$app=Test-ReleaseFiles $ReleaseDirectory $public $signer
$full=Test-FullReleaseFiles $ReleaseDirectory $app $public $signer
$tag='v'+$app.version
$release=(Invoke-ReleaseCommand gh @('release','view',$tag,'--repo',$script:FrameScopeRepository,'--json','isDraft,isPrerelease,assets,tagName,body') -join "`n") | ConvertFrom-Json
if($release.isDraft -or $release.tagName -cne $tag -or $release.isPrerelease -ne ($app.channel -eq 'beta')){throw 'Full bundle may only supplement the matching public app release'}
$remoteSha=(Invoke-ReleaseCommand gh @('api',"repos/$script:FrameScopeRepository/commits/$tag",'--jq','.sha') -join '').Trim()
if($remoteSha -cne $app.git_sha){throw 'Public app tag no longer matches the signed canonical installer'}
$bootstrapSha=(Invoke-ReleaseCommand gh @('api',"repos/$script:FrameScopeRepository/commits/$($full.bootstrap_git_sha)",'--jq','.sha') -join '').Trim()
if($bootstrapSha -cne $full.bootstrap_git_sha){throw 'Bootstrap source is not available at its authenticated public commit'}
$normal=@($app.installer.name,'release-manifest.json','release-manifest.json.minisig')
$extra=@($full.installer.name,'full-installer-manifest.json','full-installer-manifest.json.minisig')
if(@($normal | Where-Object {$_ -cnotin @($release.assets.name)}).Count -or
   @($release.assets.name | Where-Object {$_ -cnotin ($normal+$extra)}).Count){throw 'Unexpected or missing public release files'}
$download=Join-Path ([IO.Path]::GetTempPath()) ('framescope-full-publish-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $download | Out-Null
foreach($name in @($release.assets.name)){
    Invoke-ReleaseCommand gh @('release','download',$tag,'--repo',$script:FrameScopeRepository,'--pattern',$name,'--dir',$download) | Out-Null
    if((Get-FileHash -LiteralPath (Join-Path $download $name)).Hash -cne (Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory $name)).Hash){throw 'Existing public file differs; nothing will be overwritten'}
}
Test-ReleaseFiles $download $public $signer | Out-Null
foreach($name in $extra){
    if($name -cnotin @($release.assets.name)){
        Invoke-ReleaseCommand gh @('release','upload',$tag,'--repo',$script:FrameScopeRepository,(Join-Path $ReleaseDirectory $name)) | Out-Null
    }
}
Test-RemoteRelease $ReleaseDirectory $public $signer | Out-Null
$url="https://github.com/$script:FrameScopeRepository/releases/download/$tag/$($full.installer.name)"
if($release.body -notlike ('*'+$full.installer.name+'*')){
    $notes=Join-Path $download 'release-notes.md'
    $text=$release.body+"`n`n### WebView2 离线完整版`n`n[下载包含 WebView2 的完整安装包]($url)。适用于未安装 WebView2 的电脑；已有运行时会跳过依赖安装。内置的软件安装器与原标准版一致，软件内更新继续使用标准包。`n"
    [IO.File]::WriteAllText($notes,$text,[Text.UTF8Encoding]::new($false))
    Invoke-ReleaseCommand gh @('release','edit',$tag,'--repo',$script:FrameScopeRepository,'--notes-file',$notes) | Out-Null
}
Remove-VerifiedReleaseDownload $download
Write-Output "Published authenticated full installer: $url"
