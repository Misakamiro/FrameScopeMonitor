# Export an independently password-protected offline copy. Never outputs passwords.
#requires -Version 7.0
param([Parameter(Mandatory)][string]$Destination,[string]$SigningDirectory=(Join-Path $env:LOCALAPPDATA 'FrameScope-ReleaseSigning'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'FrameScopeRelease.Common.ps1')
$signer=Get-ReleaseSigner $SigningDirectory
if(Test-Path -LiteralPath $Destination){throw 'Choose a new empty offline backup directory'}
$password=Read-Host 'Password for the offline key backup (store separately)' -AsSecureString
$confirm=Read-Host 'Repeat offline backup password' -AsSecureString
$old=ConvertTo-SecureString ((Get-Content -Raw -LiteralPath (Join-Path $SigningDirectory 'password.dpapi')).Trim())
$pointers=@()
try{
    $values=@(foreach($secret in @($old,$password,$confirm)){
        $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret);$pointers+=$pointer
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    })
    if($values[1].Length -lt 16 -or $values[1] -cne $values[2]){throw 'Passwords must match and contain at least 16 characters'}
    New-Item -ItemType Directory -Path $Destination | Out-Null
    Copy-Item -LiteralPath (Join-Path $SigningDirectory 'release.key'),(Join-Path $SigningDirectory 'release.pub') -Destination $Destination
    $psi=[Diagnostics.ProcessStartInfo]::new($signer);$psi.UseShellExecute=$false;$psi.CreateNoWindow=$true
    $psi.RedirectStandardInput=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($arg in @('-C','-s',(Join-Path $Destination 'release.key'))){$psi.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::Start($psi)
    $out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
    foreach($value in $values){$process.StandardInput.WriteLine($value)};$process.StandardInput.Close()
    if(-not $process.WaitForExit(60000)){$process.Kill();throw 'Offline key export timed out'}
    if($process.ExitCode -ne 0){throw ('Offline key export failed: '+$err.GetAwaiter().GetResult())}
    [IO.File]::WriteAllText((Join-Path $Destination 'README.txt'),'Encrypted Minisign key and public key. Keep the chosen password separately. The DPAPI file is not needed for this export. Restore only to a trusted signing machine.')
    Write-Output 'Independent encrypted key backup exported. No release was signed or published.'
}finally{
    $values=$null
    foreach($pointer in $pointers){[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)}
    if($process){$process.Dispose()}
}
