# Run with Windows PowerShell/.NET Framework. Loads assembly metadata/resources;
# never runs the installer entry point or installs either embedded component.
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$manifest=Get-Content -Raw -LiteralPath (Join-Path $ReleaseDirectory 'full-installer-manifest.json') | ConvertFrom-Json
$file=Join-Path $ReleaseDirectory $manifest.installer.name
$assembly=[Reflection.Assembly]::LoadFrom($file)
$names=@($assembly.GetManifestResourceNames() | Sort-Object)
if(($names -join '|') -cne 'FrameScope.MainInstaller|FrameScope.WebView2Installer'){throw 'Unexpected embedded component set'}
foreach($component in @(@{resource='FrameScope.MainInstaller';expected=$manifest.app_installer},@{resource='FrameScope.WebView2Installer';expected=$manifest.runtime_installer})){
    $stream=$assembly.GetManifestResourceStream($component.resource)
    $sha=[Security.Cryptography.SHA256]::Create()
    try{
        if($stream.Length -ne $component.expected.size){throw 'Embedded component length differs'}
        $hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant()
        if($hash -cne $component.expected.sha256){throw 'Embedded component hash differs'}
    }finally{$sha.Dispose();$stream.Dispose()}
}
$flags=[Reflection.BindingFlags]'Static, NonPublic, Public'
$product=$assembly.GetType('FrameScopeProductInfo',$true)
$binding=$assembly.GetType('FrameScopeFullBuildInfo',$true)
if($product.GetField('Version',$flags).GetRawConstantValue() -cne $manifest.version -or
   $product.GetField('GitSha',$flags).GetRawConstantValue() -cne $manifest.bootstrap_git_sha -or
   $binding.GetField('AppGitSha',$flags).GetRawConstantValue() -cne $manifest.app_git_sha -or
   $binding.GetField('AppSha256',$flags).GetRawConstantValue() -cne $manifest.app_installer.sha256 -or
   $binding.GetField('RuntimeSha256',$flags).GetRawConstantValue() -cne $manifest.runtime_installer.sha256){throw 'Compiled bootstrap identity or component bindings differ'}
$core=$assembly.GetType('FrameScopeFullInstallerCore',$true)
$available=$core.GetMethod('HasRuntime').Invoke($null,@())
[ordered]@{status='PASS';version=$manifest.version;bootstrapGitSha=$manifest.bootstrap_git_sha;appGitSha=$manifest.app_git_sha;
    resourceCount=$names.Count;embeddedHashesVerified=$true;localRuntimeAlreadyPresent=$available;
    installerEntryPointExecuted=$false;webViewInstalledByTest=$false} | ConvertTo-Json
