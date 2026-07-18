$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$documents = @(
    'AGENTS.md',
    'README.md',
    'docs/FrameScopeMonitor-Project-Overview.md',
    'docs/modules/backend-monitoring.md',
    'docs/modules/software-ui.md',
    'docs/modules/ui-interactions.md',
    'docs/modules/lightweight-script.md',
    'packaging/README-FrameScopeMonitor.txt'
)

$rootRepoPathAllowlist = @(
    'build.ps1',
    'VERSION',
    'dependencies.lock.json',
    'framescope-config.example.json',
    'AGENTS.md',
    'README.md',
    'CHANGELOG.md',
    '.gitignore',
    'Uninstall-FrameScopeMonitor.cmd'
)

$repoPathMappings = @{
    'Uninstall-FrameScopeMonitor.cmd' = 'packaging/Uninstall-FrameScopeMonitor.cmd'
}

$staleRules = @(
    @{ Name = 'deleted src/ui production tree'; Pattern = '(?im)^(?![^\r\n]*(?:\u5df2(?:\u7ecf)?(?:\u79fb\u9664|\u5220\u9664)|\u4e0d\u518d|\u4e0d\u8981\u6062\u590d|\u5386\u53f2|removed|deleted|no\s+longer|historical))(?=[^\r\n]*(?:^|[^A-Za-z0-9_])src[\\/]ui(?:[\\/]|\b))[^\r\n]*$' },
    @{ Name = 'removed DataGridView UI guidance'; Pattern = '(?im)^(?![^\r\n]*(?:\u5df2(?:\u7ecf)?(?:\u79fb\u9664|\u5220\u9664)|\u4e0d\u518d|\u4e0d\u8981\u6062\u590d|\u5386\u53f2|removed|deleted|no\s+longer|historical))(?=[^\r\n]*DataGridView)[^\r\n]*$' },
    @{ Name = 'old WinForms production UI guidance'; Pattern = '(?im)^(?![^\r\n]*(?:\u5df2(?:\u7ecf)?(?:\u79fb\u9664|\u5220\u9664)|\u4e0d\u518d|\u4e0d\u8981\u6062\u590d|\u5386\u53f2|removed|deleted|no\s+longer|historical))(?=[^\r\n]*WinForms)(?=[^\r\n]*(?:\u4e3b\u754c\u9762|\u751f\u4ea7\s*UI|production\s*UI|\u9875\u9762|\u63a7\u4ef6|\u4e8b\u4ef6\u7ed1\u5b9a|page layout|control binding))(?=[^\r\n]*(?:\u4ecd|\u7ee7\u7eed|\u7528\u4e8e|\u7528\u4f5c|\u4f5c\u4e3a|remain|still|used\s+for|\bis\b))[^\r\n]*$' },
    @{ Name = 'mojibake or replacement text'; Pattern = '(?:\uFFFD|\u00C2|\u00C3|\u00E2\u20AC|\u00F0\u0178)' },
    @{ Name = 'independent per-target sampling guidance'; Pattern = '(?im)^(?![^\r\n]*(?:\u4e0d(?:\u80fd|\u53ef|\u5141\u8bb8|\u652f\u6301|\u518d)|\u4e0d\u662f|\u4ec5[^\r\n]{0,20}\u517c\u5bb9|\u7edf\u4e00|\u5f52\u4e00\u5316|cannot|can\s+not|\bnot\b|no\s+longer|compatib|normaliz))(?=[^\r\n]*(?:\u6bcf(?:\u4e2a|\u4e00(?:\u4e2a)?)\u76ee\u6807|\u6309\u76ee\u6807|\u5404(?:\u4e2a)?\u76ee\u6807|per[- ]target))(?=[^\r\n]*(?:\u72ec\u7acb|\u5355\u72ec|\u5206\u522b|independent|separate))(?=[^\r\n]*(?:SampleIntervalMs|ProcessSampleIntervalMs|SlowSampleIntervalMs|\u91c7\u6837(?:\u7387|\u95f4\u9694)|sampling\s+(?:rate|interval)))(?=[^\r\n]*(?:\u53ef|\u53ef\u4ee5|\u5141\u8bb8|\u652f\u6301|\u914d\u7f6e|\u8bbe\u7f6e|\bcan\b|\bmay\b|configur|\bset\b))[^\r\n]*$' }
)

$staleMustMatchSelfTests = @(
    [regex]::Unescape('\u65e7 WinForms \u4e3b\u754c\u9762\u4ecd\u7528\u4e8e\u751f\u4ea7 UI'),
    [regex]::Unescape('\u5f53\u524d\u751f\u4ea7\u754c\u9762\u7ee7\u7eed\u4f7f\u7528 src/ui/FrameScopeUiState.cs'),
    [regex]::Unescape('Targets \u9875\u9762\u7ee7\u7eed\u4f7f\u7528 DataGridView'),
    [regex]::Unescape('\u65e7WinForms\u4e3b\u754c\u9762\u4ecd\u7528\u4e8e\u751f\u4ea7UI'),
    [regex]::Unescape('\u6bcf\u4e2a\u76ee\u6807\u53ef\u72ec\u7acb\u914d\u7f6e\u91c7\u6837\u7387'),
    [regex]::Unescape('\u5404\u76ee\u6807\u53ef\u5206\u522b\u8bbe\u7f6e\u91c7\u6837\u7387'),
    [regex]::Unescape('\u6bcf\u4e00\u76ee\u6807\u5747\u53ef\u5355\u72ec\u8bbe\u7f6e\u91c7\u6837\u95f4\u9694'),
    [regex]::Unescape('\u91c7\u6837\u95f4\u9694\u53ef\u4ee5\u6309\u76ee\u6807\u5355\u72ec\u8bbe\u7f6e'),
    'Per-target sampling rate can be configured independently.'
)

$staleMustNotMatchSelfTests = @(
    [regex]::Unescape('\u65e7 WinForms \u4e3b\u754c\u9762\u5df2\u79fb\u9664'),
    [regex]::Unescape('src/ui \u5df2\u5220\u9664\uff0c\u4e0d\u8981\u6062\u590d'),
    [regex]::Unescape('DataGridView \u662f\u5386\u53f2\u5b9e\u73b0\uff0c\u5df2\u79fb\u9664'),
    [regex]::Unescape('\u65e7WinForms\u4e3b\u754c\u9762\u5df2\u79fb\u9664'),
    [regex]::Unescape('\u6bcf\u4e2a\u76ee\u6807\u4e0d\u80fd\u72ec\u7acb\u914d\u7f6e\u91c7\u6837\u95f4\u9694'),
    'Legacy per-target sampling intervals are not independently configurable.',
    'Microsoft.Web.WebView2.WinForms.dll is a host dependency.'
)

$pathExtractionSelfTests = @(
    @{ Line = 'See `docs/modules/software-ui.md` for current guidance.'; Expected = @('docs/modules/software-ui.md') },
    @{ Line = 'powershell -File .\tools\Test-CurrentDocumentation.ps1 -Mode strict'; Expected = @('tools/Test-CurrentDocumentation.ps1') },
    @{ Line = 'Run ./tests/lightweight-separation-tests.ps1 -StandaloneProjectRoot C:\external'; Expected = @('tests/lightweight-separation-tests.ps1') },
    @{ Line = 'Build with build.ps1 and compare VERSION plus dependencies.lock.json.'; Expected = @('build.ps1', 'VERSION', 'dependencies.lock.json') },
    @{ Line = 'Template: framescope-config.example.json.'; Expected = @('framescope-config.example.json') },
    @{ Line = 'Use --output artifacts/report.json and -StandaloneProjectRoot C:\external.'; Expected = @() },
    @{ Line = 'Run Uninstall-FrameScopeMonitor.cmd to uninstall.'; Expected = @('Uninstall-FrameScopeMonitor.cmd') }
)

$generatedPrefixes = @(
    'src/frontend/dist/',
    'tools/generated/',
    'tests/generated/',
    'packaging/generated/'
)

function Test-IsGeneratedDescription {
    param([string]$Line, [string]$RepoPath)

    if ($Line -notmatch '(?i)(generated|build output|after build|\u751f\u6210\u7269|\u6784\u5efa\u4ea7\u7269|\u6784\u5efa\u540e)') {
        return $false
    }
    foreach ($prefix in $generatedPrefixes) {
        if ($RepoPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Normalize-DocumentedRepoPath {
    param([string]$Value)

    $candidate = ($Value.Trim() -split '\s+')[0]
    $candidate = $candidate.TrimEnd([char[]]",.;:!?)]}>")
    $candidate = $candidate -replace ':(?:line\s*)?\d+$', ''
    $candidate = $candidate -replace '#L\d+$', ''
    $candidate = $candidate -replace '^\.[\\/]', ''
    return $candidate.Replace('\', '/')
}

function Find-DocumentedRepoPathReferences {
    param([string]$Line)

    if ([string]::IsNullOrWhiteSpace($Line)) { return @() }
    $rootNames = ($rootRepoPathAllowlist | Sort-Object { $_.Length } -Descending | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $pattern = '(?i)(?<![A-Za-z0-9_.-])(?<path>(?:\.[\\/])?(?:(?:src|tools|tests|packaging|docs)[\\/][^\s`"''<>|,;:!?()\[\]{}\u3002\uff0c\uff1b\uff1a\uff01\uff1f\u3001]+|(?:' + $rootNames + ')))'
    $seen = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($match in [regex]::Matches($Line, $pattern)) {
        $candidate = Normalize-DocumentedRepoPath $match.Groups['path'].Value
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and $seen.Add($candidate)) {
            Write-Output $candidate
        }
    }
}

$failures = New-Object System.Collections.Generic.List[string]
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

foreach ($selfTestCase in $staleMustMatchSelfTests) {
    $caught = $false
    foreach ($rule in $staleRules) {
        if ([regex]::IsMatch($selfTestCase, $rule.Pattern)) {
            $caught = $true
            break
        }
    }
    if (-not $caught) {
        $failures.Add("stale must-match self-test was not caught: '$selfTestCase'")
    }
}

foreach ($selfTestCase in $staleMustNotMatchSelfTests) {
    foreach ($rule in $staleRules) {
        if ([regex]::IsMatch($selfTestCase, $rule.Pattern)) {
            $failures.Add("stale must-not-match self-test was caught by '$($rule.Name)': '$selfTestCase'")
            break
        }
    }
}

foreach ($selfTestCase in $pathExtractionSelfTests) {
    $actual = @(Find-DocumentedRepoPathReferences -Line $selfTestCase.Line)
    if (($actual -join '|') -ne ($selfTestCase.Expected -join '|')) {
        $failures.Add("path extraction self-test mismatch: '$($selfTestCase.Line)' expected '$($selfTestCase.Expected -join ',')' actual '$($actual -join ',')'")
    }
}

foreach ($relativeDocument in $documents) {
    $documentPath = Join-Path $root $relativeDocument
    if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) {
        $failures.Add("$relativeDocument`: missing current document")
        continue
    }

    try {
        $text = [IO.File]::ReadAllText($documentPath, $utf8)
    }
    catch {
        $failures.Add("$relativeDocument`: not valid UTF-8 ($($_.Exception.Message))")
        continue
    }

    foreach ($rule in $staleRules) {
        $match = [regex]::Match($text, $rule.Pattern)
        if ($match.Success) {
            $lineNumber = 1 + ([regex]::Matches($text.Substring(0, $match.Index), "`n")).Count
            $failures.Add("$relativeDocument`:$lineNumber`: $($rule.Name)")
        }
    }

    $lines = [regex]::Split($text, '\r?\n')
    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $line = $lines[$lineIndex]
        foreach ($repoPath in @(Find-DocumentedRepoPathReferences -Line $line)) {
            if ([string]::IsNullOrWhiteSpace($repoPath)) {
                $failures.Add("$relativeDocument`:$($lineIndex + 1): empty repository path reference")
                continue
            }
            if (Test-IsGeneratedDescription $line $repoPath) {
                continue
            }
            if ($repoPath -match '[<>]|\.\.(?:/|$)') {
                $failures.Add("$relativeDocument`:$($lineIndex + 1): unverifiable repository path '$repoPath'")
                continue
            }

            $resolvedRepoPath = $repoPath
            if ($repoPathMappings.ContainsKey($repoPath)) {
                $resolvedRepoPath = $repoPathMappings[$repoPath]
            }
            $nativePath = $resolvedRepoPath.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $candidatePath = Join-Path $root $nativePath
            if ($resolvedRepoPath.IndexOfAny([char[]]'*?[') -ge 0) {
                $matches = @(Get-ChildItem -Path $candidatePath -Force -ErrorAction SilentlyContinue)
                if ($matches.Count -eq 0) {
                    $failures.Add("$relativeDocument`:$($lineIndex + 1): wildcard repository path has no matches '$repoPath'")
                }
            }
            elseif (-not (Test-Path -LiteralPath $candidatePath)) {
                $failures.Add("$relativeDocument`:$($lineIndex + 1): repository path does not exist '$repoPath'")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Current documentation gate failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Output "PASS current documentation gate ($($documents.Count) documents)"
