$ErrorActionPreference = 'Continue'
$pass = @(); $fail = @()
foreach ($exe in Get-ChildItem 'tests' -Filter '*Tests.exe') {
  $p = Start-Process -FilePath $exe.FullName -PassThru -RedirectStandardOutput "$env:TEMP\fs-test-out.txt" -RedirectStandardError "$env:TEMP\fs-test-err.txt"
  if (-not $p.WaitForExit(120000)) {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    $fail += ($exe.Name + ' [TIMEOUT 120s]')
    continue
  }
  if ($p.ExitCode -eq 0) { $pass += $exe.Name } else {
    $tail = (Get-Content "$env:TEMP\fs-test-out.txt" -Tail 6 -ErrorAction SilentlyContinue) -join ' | '
    $fail += ($exe.Name + ' [exit ' + $p.ExitCode + '] ' + $tail.Substring(0, [Math]::Min(260, $tail.Length)))
  }
}
Write-Output ('PASS(' + $pass.Count + '): ' + (($pass | ForEach-Object { $_ -replace 'FrameScope|Tests\.exe','' }) -join ', '))
if ($fail.Count) { Write-Output ('FAIL(' + $fail.Count + '):'); $fail | ForEach-Object { Write-Output ('  ' + $_) } } else { Write-Output 'C# 全部测试套件通过' }
