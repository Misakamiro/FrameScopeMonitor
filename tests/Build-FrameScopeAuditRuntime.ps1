param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stage = Join-Path ([IO.Path]::GetTempPath()) ('fsa-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($directory in @('tests', 'dist', 'assets\icon', 'src\frontend', 'packaging')) {
    New-Item -ItemType Directory -Path (Join-Path $stage $directory) | Out-Null
}
foreach ($file in @('VERSION', 'assets\icon\framescope-icon.ico', 'src\frontend\package.json', 'src\frontend\package-lock.json', 'packaging\FrameScopeSetupNative.cs')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $stage $file)
}
$metadata = & (Join-Path $root 'tools\Write-FrameScopeBuildMetadata.ps1') -RepoRoot $root -OutputPath (Join-Path $stage 'FrameScopeBuildMetadata.g.cs')
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'build.ps1'), [ref]$null, [ref]$null)
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$builds = @()
$targets = [ordered]@{
    'FrameScopeMonitor.exe' = 'monitorSources'
    'FrameScopeProcessSampler.exe' = 'samplerSources'
    'FrameScopeSystemSampler.exe' = 'systemSamplerSources'
    'FrameScopeReportGenerator.exe' = 'reportSources'
    'FrameScopeUninstaller.exe' = 'uninstallerSources'
    'FrameScopeLegacyCleanup.exe' = 'legacyCleanupSources'
    'dist\FrameScopeMonitor-Setup.exe' = 'setupSources'
    'dist\FrameScopeMonitor-Full-Setup.exe' = 'setupSources'
}
foreach ($target in $targets.GetEnumerator()) {
    $variableName = '$' + $target.Value
    $assignment = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq $variableName
    }, $true)
    if ($null -eq $assignment) { throw "Build source list not found: $variableName" }
    $sources = @($assignment.Right.FindAll({
        param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { Join-Path $root $_.Value })
    if ($sources.Count -eq 0 -or @($sources | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0) {
        throw "Invalid source list: $variableName"
    }
    $sources += @((Join-Path $root 'src\core\FrameScopeJsonFile.cs'), $metadata)
    $output = Join-Path $stage $target.Key
    $args = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', ('/out:' + $output))
    foreach ($reference in @('System.Core.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Management.dll',
        'System.Web.Extensions.dll', 'Microsoft.VisualBasic.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll',
        (Join-Path $root 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $root 'Microsoft.Web.WebView2.WinForms.dll'))) {
        $args += '/reference:' + $reference
    }
    $args += '/win32icon:' + (Join-Path $root 'assets\icon\framescope-icon.ico')
    $args += $sources
    $log = Join-Path $EvidenceDirectory ('compile-' + [IO.Path]::GetFileNameWithoutExtension($target.Key) + '.log')
    & $csc @args *> $log
    $code = $LASTEXITCODE
    $builds += [pscustomobject]@{
        File = $output; ExitCode = $code; Log = $log
        Scope = 'Compile-only PE metadata/icon fixture; no installer payload and never executed as an installer'
        Sources = @($sources | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 })
        OutputHash = if ($code -eq 0) { (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash } else { $null }
    }
    $builds | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'runtime-builds.json') -Encoding UTF8
    if ($code -ne 0) { throw "Fresh audit runtime compile failed: $($target.Key), see $log" }
}
$stage
