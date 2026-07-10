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

$staleRules = @(
    @{ Name = 'deleted src/ui production tree'; Pattern = '(?i)(?:^|[^A-Za-z0-9_])src[\\/]ui(?:[\\/]|\b)' },
    @{ Name = 'removed DataGridView UI guidance'; Pattern = '(?i)\bDataGridView\b' },
    @{ Name = 'old WinForms production UI guidance'; Pattern = '(?i)(?:\u65e7|legacy)?\s*\bWinForms\b[^\r\n]{0,120}(?:\u4e3b\u754c\u9762|\u751f\u4ea7\s*UI|\u9875\u9762|\u63a7\u4ef6|\u4e8b\u4ef6\u7ed1\u5b9a|page layout|control binding)' },
    @{ Name = 'mojibake or replacement text'; Pattern = '(?:\uFFFD|\u00C2|\u00C3|\u00E2\u20AC|\u00F0\u0178)' },
    @{ Name = 'independent per-target sampling guidance'; Pattern = '(?im)^(?=[^\r\n]*(?:\u6bcf(?:\u4e2a|\u4e00(?:\u4e2a)?)\u76ee\u6807|\u6309\u76ee\u6807|per[- ]target))(?=[^\r\n]*(?:\u72ec\u7acb|\u5355\u72ec|independent))(?=[^\r\n]*(?:SampleIntervalMs|ProcessSampleIntervalMs|SlowSampleIntervalMs|\u91c7\u6837(?:\u7387|\u95f4\u9694)|sampling\s+(?:rate|interval)))[^\r\n]*$' }
)

$forbiddenPatternSelfTests = @(
    [regex]::Unescape('\u65e7 WinForms \u4e3b\u754c\u9762\u4ecd\u7528\u4e8e\u751f\u4ea7 UI'),
    [regex]::Unescape('\u6bcf\u4e2a\u76ee\u6807\u53ef\u72ec\u7acb\u914d\u7f6e\u91c7\u6837\u7387'),
    [regex]::Unescape('\u6bcf\u4e00\u76ee\u6807\u5747\u53ef\u5355\u72ec\u8bbe\u7f6e\u91c7\u6837\u95f4\u9694'),
    [regex]::Unescape('\u91c7\u6837\u95f4\u9694\u53ef\u4ee5\u6309\u76ee\u6807\u5355\u72ec\u8bbe\u7f6e'),
    'Per-target sampling rate can be configured independently.'
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
    return $candidate.Replace('\', '/')
}

$failures = New-Object System.Collections.Generic.List[string]
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)

foreach ($selfTestCase in $forbiddenPatternSelfTests) {
    $caught = $false
    foreach ($rule in $staleRules) {
        if ([regex]::IsMatch($selfTestCase, $rule.Pattern)) {
            $caught = $true
            break
        }
    }
    if (-not $caught) {
        $failures.Add("forbidden-pattern self-test was not caught: '$selfTestCase'")
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
        foreach ($match in [regex]::Matches($line, '`((?:src|tools|tests|packaging)[\\/][^`\r\n]+)`', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $repoPath = Normalize-DocumentedRepoPath $match.Groups[1].Value
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

            $nativePath = $repoPath.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $candidatePath = Join-Path $root $nativePath
            if ($repoPath.IndexOfAny([char[]]'*?[') -ge 0) {
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
