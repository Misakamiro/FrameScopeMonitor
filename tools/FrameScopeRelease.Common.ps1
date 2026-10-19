$ErrorActionPreference = 'Stop'
$script:FrameScopeRepository = 'Misakamiro/FrameScopeMonitor'
function Invoke-ReleaseCommand([string]$Executable,[string[]]$Arguments) {
    $result = @(& $Executable @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed ($LASTEXITCODE)" }
    return $result
}
function Get-ReleaseSigner([string]$SigningDirectory) {
    $tool=Join-Path $SigningDirectory 'tools/minisign.exe'
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'Minisign missing. Install the official 0.12 x64 binary in the protected signing directory tools folder.' }
    if ((Get-FileHash -LiteralPath $tool).Hash -ne '5535BE9E4E123831EBE6EF324AAFE9DDE507015C176191F9E20C3AD60567F9E1') { throw 'Unexpected Minisign binary hash' }
    return $tool
}
function Invoke-Minisign([string]$Tool,[string[]]$Arguments,[Security.SecureString]$Password) {
    $psi=[Diagnostics.ProcessStartInfo]::new($Tool)
    $psi.UseShellExecute=$false; $psi.CreateNoWindow=$true
    $psi.RedirectStandardInput=$true; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
    foreach($arg in $Arguments){$psi.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::Start($psi)
    $output=$process.StandardOutput.ReadToEndAsync(); $errorOutput=$process.StandardError.ReadToEndAsync()
    $pointer=[IntPtr]::Zero
    try {
        if($Password){
            $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
            $plain=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
            $process.StandardInput.WriteLine($plain)
        }
        $process.StandardInput.Close()
        if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Minisign timeout'}
        if($process.ExitCode -ne 0){throw ('Minisign failed: '+$errorOutput.GetAwaiter().GetResult())}
    } finally {
        $plain=$null
        if($pointer -ne [IntPtr]::Zero){[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)}
        $process.Dispose()
    }
}
function New-ReleaseManifest([string]$Installer,[string]$Version,[string]$GitSha,[string]$Output) {
    if($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.[1-9][0-9]*)?$' -or [int]$Version.Split('.')[0] -lt 2){throw 'Invalid 2.x release version'}
    if($GitSha -cnotmatch '^[a-f0-9]{40}$'){throw 'A full commit SHA is required'}
    $file=Get-Item -LiteralPath $Installer
    if($file.Name -cne "FrameScope_${Version}_x64-setup.exe" -or $file.Length -le 0 -or $file.Length -gt 2GB){throw 'Installer identity mismatch'}
    if(Test-Path -LiteralPath $Output){throw 'Refusing to overwrite release manifest'}
    $manifest=[ordered]@{schema=1;version=$Version;channel=$(if($Version.Contains('-')){'beta'}else{'stable'});git_sha=$GitSha;platform='windows-x86_64';
        installer=[ordered]@{name=$file.Name;size=$file.Length;sha256=(Get-FileHash -LiteralPath $Installer).Hash.ToLowerInvariant()}}
    [IO.File]::WriteAllText($Output,($manifest | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    return $manifest
}
function Test-ReleaseFiles([string]$Directory,[string]$PublicKey,[string]$Signer) {
    $manifestPath=Join-Path $Directory 'release-manifest.json'
    Invoke-Minisign $Signer @('-V','-q','-p',$PublicKey,'-m',$manifestPath,'-x',($manifestPath+'.minisig'))
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($manifest.schema -ne 1 -or $manifest.platform -cne 'windows-x86_64' -or $manifest.version -notmatch '^([2-9]|[1-9][0-9]+)\.\d+\.\d+(-beta\.[1-9][0-9]*)?$' -or
        $manifest.installer.name -cne "FrameScope_$($manifest.version)_x64-setup.exe" -or $manifest.git_sha -cnotmatch '^[a-f0-9]{40}$') {throw 'Invalid signed release identity'}
    if($manifest.channel -cne $(if($manifest.version.Contains('-')){'beta'}else{'stable'})){throw 'Signed channel mismatch'}
    $file=Get-Item -LiteralPath (Join-Path $Directory $manifest.installer.name)
    if($file.Length -ne $manifest.installer.size -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $manifest.installer.sha256){throw 'Release package hash or size mismatch'}
    return $manifest
}
function Test-RemoteRelease([string]$Directory,[string]$PublicKey,[string]$Signer) {
    $expected=Test-ReleaseFiles $Directory $PublicKey $Signer
    $tag='v'+$expected.version
    $release=(Invoke-ReleaseCommand gh @('release','view',$tag,'--repo',$script:FrameScopeRepository,'--json','isDraft,isPrerelease,assets,tagName,url') -join "`n") | ConvertFrom-Json
    if($release.tagName -cne $tag -or $release.isPrerelease -ne ($expected.channel -eq 'beta')){throw 'Remote channel or tag mismatch'}
    $sha=(Invoke-ReleaseCommand gh @('api',"repos/$script:FrameScopeRepository/commits/$tag",'--jq','.sha') -join '').Trim()
    if($sha -cne $expected.git_sha){throw 'Remote tag does not match the signed build SHA'}
    $names=@(Get-ReleaseAssetNames $expected.version @($release.assets.name))
    $download=Join-Path ([IO.Path]::GetTempPath()) ('framescope-release-verify-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $download | Out-Null
    Invoke-ReleaseCommand gh @('release','download',$tag,'--repo',$script:FrameScopeRepository,'--dir',$download) | Out-Null
    $actual=Test-ReleaseFiles $download $PublicKey $Signer
    if($actual.git_sha -cne $expected.git_sha -or $actual.version -cne $expected.version){throw 'Remote signed release differs from local candidate'}
    if($names.Count -eq 6){Test-FullReleaseFiles $download $actual $PublicKey $Signer | Out-Null}
    foreach($name in $names){if((Get-FileHash -LiteralPath (Join-Path $download $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $Directory $name)).Hash){throw "Remote file differs: $name"}}
    return $release
}

function Get-ReleaseAssetNames([string]$Version,[string[]]$Actual) {
    $normal=@("FrameScope_${Version}_x64-setup.exe",'release-manifest.json','release-manifest.json.minisig')
    $full=@("FrameScope_${Version}_x64-full-setup.exe",'full-installer-manifest.json','full-installer-manifest.json.minisig')
    $present=@($Actual | Where-Object {$_ -cin $full})
    if($present.Count -and $present.Count -ne 3){throw 'Incomplete authenticated full-installer attachment set'}
    $expected=@($normal)
    if($present.Count){$expected+= $full}
    if((@($Actual | Sort-Object) -join '|') -cne (@($expected | Sort-Object) -join '|')){throw 'Remote release asset set differs from the signed release'}
    return $expected | Sort-Object
}

function Test-FullReleaseFiles([string]$Directory,$AppManifest,[string]$PublicKey,[string]$Signer) {
    $path=Join-Path $Directory 'full-installer-manifest.json'
    Invoke-Minisign $Signer @('-V','-q','-p',$PublicKey,'-m',$path,'-x',($path+'.minisig'))
    $full=Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if($full.schema -ne 1 -or $full.kind -cne 'full-installer' -or $full.version -cne $AppManifest.version -or
       $full.platform -cne 'windows-x86_64' -or $full.app_git_sha -cne $AppManifest.git_sha -or
       $full.bootstrap_git_sha -cnotmatch '^[a-f0-9]{40}$' -or
       $full.installer.name -cne "FrameScope_$($full.version)_x64-full-setup.exe" -or
       $full.runtime_installer.name -cne 'MicrosoftEdgeWebView2RuntimeInstallerX64.exe' -or
       $full.runtime_installer.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $full.runtime_installer.size -le 0){throw 'Invalid authenticated full-installer identity'}
    foreach($field in @('name','size','sha256')){if($full.app_installer.$field -cne $AppManifest.installer.$field){throw 'Full installer is not bound to the canonical signed app package'}}
    $file=Get-Item -LiteralPath (Join-Path $Directory $full.installer.name)
    if($file.Length -ne $full.installer.size -or $file.Length -le 0 -or $file.Length -gt 2GB -or
       $full.installer.sha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $file.FullName).Hash -ine $full.installer.sha256){throw 'Full installer package hash or size differs'}
    return $full
}
