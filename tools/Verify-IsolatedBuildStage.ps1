param([Parameter(Mandatory = $true)][string]$RepoRoot)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
$directory = Split-Path -Parent $root
$inventory = Join-Path $directory 'snapshot-inputs.json'
if (-not (Test-Path -LiteralPath $inventory -PathType Leaf)) { return }
$proof = Get-Content -LiteralPath $inventory -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not [string]::Equals([IO.Path]::GetFullPath([string]$proof.snapshotRoot).TrimEnd('\'), $root, [StringComparison]::OrdinalIgnoreCase)) { return }
& (Join-Path $root 'tools\Build-IsolatedInstaller.ps1') -RepoRoot $root -OutputDirectory $directory -VerifyOnly
