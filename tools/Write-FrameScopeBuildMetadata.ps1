param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepoRoot).Path
$version = (Get-Content -Raw -LiteralPath (Join-Path $root 'VERSION')).Trim()
if ($version -notmatch '^(?<core>[0-9]+\.[0-9]+\.[0-9]+)(-beta\.[1-9][0-9]*)?$') {
    throw "Invalid VERSION: $version"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'obj\FrameScopeBuildMetadata.g.cs'
}
$fullOutput = [IO.Path]::GetFullPath($OutputPath)
$directory = Split-Path -Parent $fullOutput
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$assemblyVersion = "$($Matches.core).0"
$sha = $env:FRAMESCOPE_BUILD_SHA
if (-not $sha) { $sha = (& git -C $root rev-parse HEAD) }
if ($sha -notmatch '^[0-9a-f]{40}$') { throw 'Missing build Git SHA' }
$source = @"
using System.Reflection;
[assembly: AssemblyVersion("$assemblyVersion")]
[assembly: AssemblyFileVersion("$assemblyVersion")]
[assembly: AssemblyInformationalVersion("$version+$sha")]
internal static class FrameScopeProductInfo
{
    internal const string Version = "$version";
    internal const string GitSha = "$sha";
}
"@
[IO.File]::WriteAllText($fullOutput, $source, [Text.UTF8Encoding]::new($false))
$fullOutput
