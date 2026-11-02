param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RepoRoot=(Split-Path -Parent $PSScriptRoot),
    [string]$SigningDirectory=(Join-Path $env:LOCALAPPDATA 'FrameScope-ReleaseSigning')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'FrameScopeRelease.Common.ps1')
$RepoRoot=(Resolve-Path -LiteralPath $RepoRoot).Path
$ReleaseDirectory=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $OutputDirectory.StartsWith('C:\fs-build\',[StringComparison]::OrdinalIgnoreCase)){throw 'Full bootstrap builds must remain in the managed build storage'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Full bootstrap output must be a new private component build directory'}
$ancestor=Split-Path -Parent $OutputDirectory
while($ancestor){
    if((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Full bootstrap output ancestor is a reparse point'}
    $ancestor=Split-Path -Parent $ancestor
}
if(@(Invoke-ReleaseCommand git @('-C',$RepoRoot,'status','--porcelain')).Count){throw 'Full bootstrap requires committed source'}
$signer=Get-ReleaseSigner $SigningDirectory
$public=Join-Path $RepoRoot 'src/updater/release.pub'
$app=Test-ReleaseFiles $ReleaseDirectory $public $signer
$version=(Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'VERSION')).Trim()
if($app.version -cne $version){throw 'Wrapper product version differs from canonical release'}
$sha=(Invoke-ReleaseCommand git @('-C',$RepoRoot,'rev-parse','HEAD') -join '').Trim()
$lock=Get-Content -Raw -LiteralPath (Join-Path $RepoRoot 'dependencies.lock.json') | ConvertFrom-Json
$runtime=Join-Path $RepoRoot $lock.webView2StandaloneInstaller.file
$runtimeHash=(Get-FileHash -LiteralPath $runtime).Hash.ToLowerInvariant()
$signature=Get-AuthenticodeSignature -LiteralPath $runtime
if($runtimeHash -ine $lock.webView2StandaloneInstaller.sha256 -or $signature.Status -ne 'Valid' -or
   $signature.SignerCertificate.Subject -notmatch '(^|,\s*)CN=Microsoft Corporation(,|$)'){throw 'Microsoft offline runtime signature or pinned hash differs'}
$name="FrameScope_${version}_x64-full-setup.exe"
foreach($file in @($name,'full-installer-manifest.json','full-installer-manifest.json.minisig')){if(Test-Path -LiteralPath (Join-Path $ReleaseDirectory $file)){throw 'A public full variant must never be overwritten'}}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$inputs=@()
foreach($relative in @('packaging/FrameScopeFullInstaller.cs','packaging/FrameScopeFullInstaller.Core.cs','packaging/FrameScopeFullInstaller.manifest')){
    $source=Join-Path $RepoRoot $relative
    $target=Join-Path $OutputDirectory ([IO.Path]::GetFileName($relative))
    Copy-Item -LiteralPath $source -Destination $target
    $hash=(Get-FileHash -LiteralPath $source).Hash
    if((Get-FileHash -LiteralPath $target).Hash -cne $hash){throw 'Bootstrap source copy changed'}
    $inputs+= [ordered]@{path=$relative;sha256=$hash}
}
$metadata=Join-Path $OutputDirectory 'BuildMetadata.g.cs'
$savedSha=$env:FRAMESCOPE_BUILD_SHA
try{$env:FRAMESCOPE_BUILD_SHA=$sha;& (Join-Path $RepoRoot 'tools/Write-FrameScopeBuildMetadata.ps1') -RepoRoot $RepoRoot -OutputPath $metadata | Out-Null}
finally{if($null -eq $savedSha){Remove-Item Env:FRAMESCOPE_BUILD_SHA -ErrorAction SilentlyContinue}else{$env:FRAMESCOPE_BUILD_SHA=$savedSha}}
$binding=Join-Path $OutputDirectory 'FullBuildInfo.g.cs'
[IO.File]::WriteAllText($binding,@"
internal static class FrameScopeFullBuildInfo {
    public const string AppGitSha = "$($app.git_sha)";
    public const string AppSha256 = "$($app.installer.sha256)";
    public const string RuntimeSha256 = "$runtimeHash";
}
"@,[Text.UTF8Encoding]::new($false))
$exe=Join-Path $OutputDirectory $name
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/out:'+$exe) `
    ('/win32manifest:'+(Join-Path $OutputDirectory 'FrameScopeFullInstaller.manifest')) `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    ('/resource:'+(Join-Path $ReleaseDirectory $app.installer.name)+',FrameScope.MainInstaller') `
    ('/resource:'+$runtime+',FrameScope.WebView2Installer') `
    (Join-Path $OutputDirectory 'FrameScopeFullInstaller.cs') (Join-Path $OutputDirectory 'FrameScopeFullInstaller.Core.cs') $metadata $binding
if($LASTEXITCODE -ne 0){throw 'Full bootstrap compilation failed'}
foreach($input in $inputs){if((Get-FileHash -LiteralPath (Join-Path $RepoRoot $input.path)).Hash -cne $input.sha256){throw 'Bootstrap source changed while compiling'}}
if((Get-FileHash -LiteralPath $runtime).Hash -ine $runtimeHash){throw 'Runtime bytes changed while compiling'}
Test-ReleaseFiles $ReleaseDirectory $public $signer | Out-Null
$full=[ordered]@{schema=1;kind='full-installer';version=$version;platform='windows-x86_64';app_git_sha=$app.git_sha;bootstrap_git_sha=$sha;
    installer=[ordered]@{name=$name;size=(Get-Item -LiteralPath $exe).Length;sha256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant()};
    app_installer=$app.installer;runtime_installer=[ordered]@{name='MicrosoftEdgeWebView2RuntimeInstallerX64.exe';size=(Get-Item -LiteralPath $runtime).Length;sha256=$runtimeHash}}
$manifest=Join-Path $ReleaseDirectory 'full-installer-manifest.json'
Move-Item -LiteralPath $exe -Destination (Join-Path $ReleaseDirectory $name)
[IO.File]::WriteAllText($manifest,($full | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$password=ConvertTo-SecureString ((Get-Content -Raw -LiteralPath (Join-Path $SigningDirectory 'password.dpapi')).Trim())
Invoke-Minisign $signer @('-S','-s',(Join-Path $SigningDirectory 'release.key'),'-m',$manifest) $password
Test-FullReleaseFiles $ReleaseDirectory $app $public $signer | Out-Null
[ordered]@{bootstrapGitSha=$sha;appGitSha=$app.git_sha;inputs=$inputs;full=$full} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-evidence.json')
Write-Output "Authenticated offline full installer ready: $(Join-Path $ReleaseDirectory $name)"
