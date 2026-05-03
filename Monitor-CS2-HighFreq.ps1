param(
    [int]$WaitSeconds = 600,
    [int]$CaptureSeconds = 0,
    [int]$SampleIntervalMs = 80,
    [int]$SlowSampleIntervalMs = 1000,
    [string]$TargetProcessName = 'cs2',
    [string]$PresentMonExe = '',
    [string]$RunRoot = '',
    [string]$RunNamePrefix = ''
)

$ErrorActionPreference = 'Stop'

try {
    [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal'
}
catch {
}

if ($SampleIntervalMs -lt 50) { $SampleIntervalMs = 50 }
if ($SlowSampleIntervalMs -lt $SampleIntervalMs) { $SlowSampleIntervalMs = $SampleIntervalMs }

$root = $PSScriptRoot
$targetProcessBaseName = [IO.Path]::GetFileNameWithoutExtension($TargetProcessName)
if (-not $targetProcessBaseName) { $targetProcessBaseName = 'cs2' }
$presentMonProcessName = if ($TargetProcessName -match '\.exe$') { $TargetProcessName } else { "$targetProcessBaseName.exe" }

function ConvertTo-SafeName {
    param([string]$Name, [string]$Fallback = 'monitor')
    $text = if ($Name) { $Name } else { $Fallback }
    $safe = [Regex]::Replace($text, '[^\p{L}\p{Nd}\._-]+', '-').Trim('-')
    if (-not $safe) { $safe = $Fallback }
    return $safe
}

if (-not $RunRoot) {
    $RunRoot = Join-Path $root 'cs2-monitor-runs'
}
$runRoot = $RunRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$prefix = if ($RunNamePrefix) { ConvertTo-SafeName -Name $RunNamePrefix -Fallback $targetProcessBaseName } else { 'cs2-monitor' }
$runDir = Join-Path $runRoot ("$prefix-$stamp")
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$statusPath = Join-Path $runDir 'status.json'
$presentMonCsv = Join-Path $runDir 'presentmon.csv'
$presentMonStdout = Join-Path $runDir 'presentmon.stdout.log'
$presentMonStderr = Join-Path $runDir 'presentmon.stderr.log'
$presentMonInfoPath = Join-Path $runDir 'presentmon-info.json'
$samplesCsv = Join-Path $runDir 'system-samples.csv'
$processCsv = Join-Path $runDir 'process-samples.csv'
$topCsv = Join-Path $runDir 'topcpu-samples.csv'
$topIoCsv = Join-Path $runDir 'topio-samples.csv'
$alertsCsv = Join-Path $runDir 'sample-alerts.csv'
$eventsCsv = Join-Path $runDir 'event-samples.csv'
$summaryPath = Join-Path $runDir 'summary.json'
$reportLogPath = Join-Path $runDir 'report-generation.log'
$slowSamplerLogPath = Join-Path $runDir 'system-slow-sampler.log'
$errorPath = Join-Path $runDir 'monitor-error.txt'

$captureUntilTargetExit = ($CaptureSeconds -le 0)
$captureMode = if ($captureUntilTargetExit) { 'until-target-exit' } else { 'timed' }

$nvidiaSmiPath = (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue).Source
if (-not $nvidiaSmiPath -and (Test-Path 'C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe')) {
    $nvidiaSmiPath = 'C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe'
}

function Resolve-PresentMonPath {
    param([string]$RequestedPath)

    $candidates = New-Object System.Collections.Generic.List[string]
    if ($RequestedPath) { $candidates.Add($RequestedPath) }

    $toolsDir = Join-Path $root 'tools'
    $candidates.Add((Join-Path $toolsDir 'PresentMon-2.4.1-x64.exe'))
    if (Test-Path -LiteralPath $toolsDir) {
        Get-ChildItem -LiteralPath $toolsDir -Filter 'PresentMon*.exe' -ErrorAction SilentlyContinue |
            Sort-Object Name |
            ForEach-Object { $candidates.Add($_.FullName) }
    }
    $candidates.Add('C:\Program Files\NVIDIA Corporation\FrameViewSDK\bin\PresentMon_x64.exe')

    $seen = @{}
    foreach ($candidate in $candidates) {
        if (-not $candidate) { continue }
        try { $resolved = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path }
        catch { continue }

        $key = $resolved.ToLowerInvariant()
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        return $resolved
    }
    return $null
}

function Resolve-PythonPath {
    $portable = Join-Path $root 'runtime\python\python.exe'
    if (Test-Path -LiteralPath $portable) { return $portable }

    $cmd = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $bundled = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
    if (Test-Path -LiteralPath $bundled) { return $bundled }
    return $null
}

$presentMonPath = Resolve-PresentMonPath -RequestedPath $PresentMonExe

function Write-Status {
    param(
        [string]$Phase,
        [hashtable]$Extra = @{}
    )

    $obj = [ordered]@{
        Time                    = (Get-Date).ToString('o')
        Phase                   = $Phase
        RunDir                  = $runDir
        MonitorScript           = $PSCommandPath
        PresentMonCsv           = $presentMonCsv
        PresentMonExe           = $presentMonPath
        PresentMonOut           = $presentMonStdout
        PresentMonErr           = $presentMonStderr
        PresentMonInfo          = $presentMonInfoPath
        SamplesCsv              = $samplesCsv
        ProcessCsv              = $processCsv
        TopCpuCsv               = $topCsv
        TopIoCsv                = $topIoCsv
        AlertsCsv               = $alertsCsv
        EventsCsv               = $eventsCsv
        SummaryPath             = $summaryPath
        ReportLog               = $reportLogPath
        SlowSamplerLog          = $slowSamplerLogPath
        TargetProcess           = $presentMonProcessName
        CaptureMode             = $captureMode
        SampleIntervalMs        = $SampleIntervalMs
        SlowSampleIntervalMs    = $SlowSampleIntervalMs
        ProcessSamplingMode     = 'all-process-groups'
    }
    foreach ($key in $Extra.Keys) {
        $obj[$key] = $Extra[$key]
    }
    [pscustomobject]$obj | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statusPath -Encoding UTF8
}

function Convert-CsvField {
    param($Value)
    if ($null -eq $Value) { return '' }
    $text = [string]$Value
    if ($text -match '[,"\r\n]') {
        return '"' + $text.Replace('"', '""') + '"'
    }
    return $text
}

function Write-CsvLine {
    param(
        [IO.StreamWriter]$Writer,
        [object[]]$Values
    )

    $fields = foreach ($value in $Values) { Convert-CsvField $value }
    $Writer.WriteLine(($fields -join ','))
}

function New-CsvWriter {
    param(
        [string]$Path,
        [string[]]$Header
    )

    $encoding = [Text.UTF8Encoding]::new($false)
    $writer = [IO.StreamWriter]::new($Path, $false, $encoding)
    $writer.NewLine = "`r`n"
    Write-CsvLine -Writer $writer -Values $Header
    return $writer
}

function Safe-Number {
    param($Value)
    if ($null -eq $Value) { return $null }
    try { return [double]$Value } catch { return $null }
}

function Round-Nullable {
    param(
        $Value,
        [int]$Digits = 2
    )
    $number = Safe-Number $Value
    if ($null -eq $number) { return $null }
    return [Math]::Round($number, $Digits)
}

function Get-CounterValue {
    param([string]$Path)

    try {
        $sample = (Get-Counter -Counter $Path -ErrorAction Stop).CounterSamples | Select-Object -First 1
        if ($sample) { return [double]$sample.CookedValue }
    }
    catch {
    }
    return $null
}

function Get-SystemSlowSnapshot {
    $result = [ordered]@{
        AvailableMB           = $null
        DiskAvgSecPerTransfer = $null
        DiskBytesPerSec       = $null
        NetBytesPerSec        = $null
    }

    try {
        $counterPaths = @(
            '\Memory\Available MBytes',
            '\LogicalDisk(C:)\Avg. Disk sec/Transfer',
            '\LogicalDisk(C:)\Disk Bytes/sec',
            '\Network Interface(*)\Bytes Total/sec'
        )
        $samples = (Get-Counter -Counter $counterPaths -ErrorAction Stop).CounterSamples
        $netTotal = 0.0
        foreach ($sample in $samples) {
            $path = [string]$sample.Path
            if ($path -like '*\memory\available mbytes') {
                $result.AvailableMB = [double]$sample.CookedValue
            }
            elseif ($path -like '*\logicaldisk(c:)\avg. disk sec/transfer') {
                $result.DiskAvgSecPerTransfer = [double]$sample.CookedValue
            }
            elseif ($path -like '*\logicaldisk(c:)\disk bytes/sec') {
                $result.DiskBytesPerSec = [double]$sample.CookedValue
            }
            elseif ($path -like '*\network interface(*)\bytes total/sec') {
                $netTotal += [double]$sample.CookedValue
            }
        }
        $result.NetBytesPerSec = $netTotal
    }
    catch {
        $result.AvailableMB = Get-CounterValue '\Memory\Available MBytes'
        $result.DiskAvgSecPerTransfer = Get-CounterValue '\LogicalDisk(C:)\Avg. Disk sec/Transfer'
        $result.DiskBytesPerSec = Get-CounterValue '\LogicalDisk(C:)\Disk Bytes/sec'
    }

    return [pscustomobject]$result
}

function Get-GpuInfo {
    if (-not $nvidiaSmiPath) { return $null }

    try {
        $line = & $nvidiaSmiPath --query-gpu=utilization.gpu,utilization.memory,temperature.gpu,pstate,clocks.gr,clocks.mem,power.draw,memory.used,memory.total --format=csv,noheader,nounits 2>$null | Select-Object -First 1
        if (-not $line) { return $null }
        $parts = @($line -split ',' | ForEach-Object { $_.Trim() })
        return [pscustomobject]@{
            GpuUtilPct    = [double]$parts[0]
            GpuMemUtilPct = [double]$parts[1]
            GpuTempC      = [double]$parts[2]
            GpuPState     = $parts[3]
            GpuClockMHz   = [double]$parts[4]
            MemClockMHz   = [double]$parts[5]
            PowerW        = [double]$parts[6]
            VramUsedMiB   = [double]$parts[7]
            VramTotalMiB  = [double]$parts[8]
        }
    }
    catch {
        return $null
    }
}

function Get-SortedPercentile {
    param(
        [double[]]$SortedValues,
        [double]$P
    )
    if (-not $SortedValues -or $SortedValues.Count -eq 0) { return $null }
    $index = [Math]::Ceiling(($P / 100.0) * $SortedValues.Count) - 1
    if ($index -lt 0) { $index = 0 }
    if ($index -ge $SortedValues.Count) { $index = $SortedValues.Count - 1 }
    return [double]$SortedValues[$index]
}

function Analyze-PresentMonCsvStream {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ FrameSummary = $null; Notes = @('PresentMon CSV was not created.') }
    }

    $reader = $null
    try {
        $reader = [IO.StreamReader]::new($Path)
        $headerLine = $reader.ReadLine()
        if (-not $headerLine) {
            return [pscustomobject]@{ FrameSummary = $null; Notes = @('PresentMon CSV was empty.') }
        }

        $headers = @($headerLine -split ',')
        $ftColumn = $null
        foreach ($candidate in @('MsBetweenPresents', 'msBetweenPresents', 'MsBetweenDisplayChange', 'msBetweenDisplayChange')) {
            if ($headers -contains $candidate) { $ftColumn = $candidate; break }
        }
        $timeColumn = $null
        foreach ($candidate in @('TimeInDateTime', 'timeInDateTime', 'CPUStartDateTime', 'cpuStartDateTime')) {
            if ($headers -contains $candidate) { $timeColumn = $candidate; break }
        }
        $ftIndex = [array]::IndexOf($headers, $ftColumn)
        $timeIndex = if ($timeColumn) { [array]::IndexOf($headers, $timeColumn) } else { -1 }
        if ($ftIndex -lt 0) {
            return [pscustomobject]@{ FrameSummary = $null; Notes = @('PresentMon CSV existed but no usable frame-time column was found.') }
        }

        $frameTimes = [System.Collections.Generic.List[double]]::new()
        $topStutters = [System.Collections.Generic.List[object]]::new()
        $sum = 0.0
        $max = 0.0
        $spike20 = 0
        $spike33 = 0
        $spike50 = 0
        $spike100 = 0
        $index = 0

        while (($line = $reader.ReadLine()) -ne $null) {
            if (-not $line) { continue }
            $parts = $line.Split(',')
            if ($parts.Length -le $ftIndex) { $index++; continue }

            $ft = 0.0
            if (-not [double]::TryParse($parts[$ftIndex], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$ft)) {
                $index++
                continue
            }
            if ($ft -le 0 -or $ft -ge 10000) {
                $index++
                continue
            }

            $frameTimes.Add($ft)
            $sum += $ft
            if ($ft -gt $max) { $max = $ft }
            if ($ft -gt 20) { $spike20++ }
            if ($ft -gt 33.3) { $spike33++ }
            if ($ft -gt 50) { $spike50++ }
            if ($ft -gt 100) { $spike100++ }

            if ($ft -ge 33.3) {
                $timeValue = if ($timeIndex -ge 0 -and $parts.Length -gt $timeIndex) { $parts[$timeIndex] } else { $null }
                $item = [pscustomobject]@{
                    Index       = $index
                    Time        = $timeValue
                    FrameTimeMs = [Math]::Round($ft, 3)
                }
                if ($topStutters.Count -lt 30) {
                    $topStutters.Add($item)
                }
                else {
                    $minItem = $topStutters | Sort-Object FrameTimeMs | Select-Object -First 1
                    if ($item.FrameTimeMs -gt $minItem.FrameTimeMs) {
                        [void]$topStutters.Remove($minItem)
                        $topStutters.Add($item)
                    }
                }
            }
            $index++
        }

        if ($frameTimes.Count -eq 0) {
            return [pscustomobject]@{ FrameSummary = $null; Notes = @('PresentMon CSV existed but no usable frame-time values were found.') }
        }

        $sorted = $frameTimes.ToArray()
        [Array]::Sort($sorted)
        $avgMs = $sum / $frameTimes.Count
        $p95 = Get-SortedPercentile -SortedValues $sorted -P 95
        $p99 = Get-SortedPercentile -SortedValues $sorted -P 99
        $p999 = Get-SortedPercentile -SortedValues $sorted -P 99.9

        return [pscustomobject]@{
            FrameSummary = [ordered]@{
                FrameCount        = $frameTimes.Count
                FrameTimeColumn   = $ftColumn
                TimeColumn        = $timeColumn
                AverageFPS        = [Math]::Round(1000.0 / $avgMs, 2)
                AverageFrameMs    = [Math]::Round($avgMs, 3)
                P95FrameMs        = [Math]::Round($p95, 3)
                P99FrameMs        = [Math]::Round($p99, 3)
                P999FrameMs       = [Math]::Round($p999, 3)
                OnePercentLowFPS  = if ($p99 -gt 0) { [Math]::Round(1000.0 / $p99, 2) } else { $null }
                PointOneLowFPS    = if ($p999 -gt 0) { [Math]::Round(1000.0 / $p999, 2) } else { $null }
                MaxFrameMs        = [Math]::Round($max, 3)
                FramesOver20ms    = $spike20
                FramesOver33ms    = $spike33
                FramesOver50ms    = $spike50
                FramesOver100ms   = $spike100
                TopStutters       = @($topStutters | Sort-Object FrameTimeMs -Descending)
            }
            Notes = @()
        }
    }
    finally {
        if ($reader) { $reader.Close() }
    }
}

function Build-Summary {
    param(
        $PresentMonExitCode = $null,
        [bool]$PresentMonExitedEarly = $false,
        [bool]$PresentMonForcedStop = $false,
        [hashtable]$ReportResult = @{}
    )

    $frameAnalysis = Analyze-PresentMonCsvStream -Path $presentMonCsv
    $summary = [ordered]@{
        RunDir                 = $runDir
        MonitorScript          = $PSCommandPath
        PresentMonCsv          = $presentMonCsv
        PresentMonExe          = $presentMonPath
        PresentMonStdout       = $presentMonStdout
        PresentMonStderr       = $presentMonStderr
        PresentMonInfo         = $presentMonInfoPath
        PresentMonExitCode     = $PresentMonExitCode
        PresentMonExitedEarly  = $PresentMonExitedEarly
        PresentMonForcedStop   = $PresentMonForcedStop
        SamplesCsv             = $samplesCsv
        ProcessCsv             = $processCsv
        TopCpuCsv              = $topCsv
        TopIoCsv               = $topIoCsv
        AlertsCsv              = $alertsCsv
        EventsCsv              = $eventsCsv
        TargetProcess          = $presentMonProcessName
        CaptureMode            = $captureMode
        SampleIntervalMs       = $SampleIntervalMs
        SlowSampleIntervalMs   = $SlowSampleIntervalMs
        ProcessSamplingMode    = 'all-process-groups'
        FrameSummary           = if ($frameAnalysis.FrameSummary) { $frameAnalysis.FrameSummary } else { $null }
        PossibleCorrelates     = @()
        AlertSummary           = @()
        TopCpuProcesses        = @()
        TopIoProcesses         = @()
        Reports                = $ReportResult
        Notes                  = @($frameAnalysis.Notes)
    }

    if (Test-Path -LiteralPath $alertsCsv) {
        $alertRows = @(Import-Csv -LiteralPath $alertsCsv)
        $alertItems = @()
        foreach ($row in $alertRows) {
            if (-not $row.Alerts) { continue }
            $alertItems += @($row.Alerts -split ';' | Where-Object { $_ })
        }
        if ($alertItems.Count -gt 0) {
            $summary.PossibleCorrelates += "Alert samples: $($alertRows.Count)"
            $summary.AlertSummary = @(
                $alertItems |
                    Group-Object |
                    Sort-Object Count -Descending |
                    Select-Object -First 20 @{Name='Alert';Expression={$_.Name}}, Count
            )
        }
    }

    if (Test-Path -LiteralPath $topCsv) {
        $topRows = @(Import-Csv -LiteralPath $topCsv)
        $summary.TopCpuProcesses = @(
            $topRows |
                Where-Object { $null -ne (Safe-Number $_.CpuPct) } |
                Group-Object ProcessName |
                ForEach-Object {
                    $values = @($_.Group | ForEach-Object { Safe-Number $_.CpuPct } | Where-Object { $null -ne $_ })
                    [pscustomobject]@{
                        ProcessName = $_.Name
                        Samples     = $values.Count
                        AvgCpuPct   = if ($values.Count -gt 0) { [Math]::Round(($values | Measure-Object -Average).Average, 2) } else { $null }
                        MaxCpuPct   = if ($values.Count -gt 0) { [Math]::Round(($values | Measure-Object -Maximum).Maximum, 2) } else { $null }
                    }
                } |
                Sort-Object MaxCpuPct -Descending |
                Select-Object -First 30
        )
    }

    if (Test-Path -LiteralPath $topIoCsv) {
        $ioRows = @(Import-Csv -LiteralPath $topIoCsv)
        $summary.TopIoProcesses = @(
            $ioRows |
                Group-Object ProcessName |
                ForEach-Object {
                    $readValues = @($_.Group | ForEach-Object { Safe-Number $_.ReadMBps } | Where-Object { $null -ne $_ })
                    $writeValues = @($_.Group | ForEach-Object { Safe-Number $_.WriteMBps } | Where-Object { $null -ne $_ })
                    [pscustomobject]@{
                        ProcessName  = $_.Name
                        Samples      = $_.Count
                        MaxReadMBps  = if ($readValues.Count -gt 0) { [Math]::Round(($readValues | Measure-Object -Maximum).Maximum, 3) } else { $null }
                        MaxWriteMBps = if ($writeValues.Count -gt 0) { [Math]::Round(($writeValues | Measure-Object -Maximum).Maximum, 3) } else { $null }
                    }
                } |
                Sort-Object @{Expression='MaxReadMBps';Descending=$true}, @{Expression='MaxWriteMBps';Descending=$true} |
                Select-Object -First 30
        )
    }

    [pscustomobject]$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
}

function Invoke-ReportGeneration {
    $result = [ordered]@{
        Attempted = $false
        ExitCode  = $null
        ReportHtml = (Join-Path $runDir 'charts\framescope-interactive-report.html')
        PreviewPng = $null
        LogPath   = $reportLogPath
        Error     = $null
    }

    $reportScript = Join-Path $root 'Generate-CS2-FrameScope-Interactive-Report.py'
    $python = Resolve-PythonPath
    if (-not $python -or -not (Test-Path -LiteralPath $python)) {
        $result.Error = 'python.exe was not found.'
        return $result
    }
    if (-not (Test-Path -LiteralPath $reportScript)) {
        $result.Error = "Report script not found: $reportScript"
        return $result
    }

    $result.Attempted = $true
    try {
        $oldErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $output = & $python $reportScript $runDir 2>&1
        $ErrorActionPreference = $oldErrorActionPreference
        $result.ExitCode = $LASTEXITCODE
        $output | Set-Content -LiteralPath $reportLogPath -Encoding UTF8
        if ($LASTEXITCODE -ne 0) {
            $result.Error = "Report generation failed with exit code $LASTEXITCODE."
        }
    }
    catch {
        try { $ErrorActionPreference = $oldErrorActionPreference } catch {}
        $result.ExitCode = -1
        $result.Error = $_.Exception.Message
        $_ | Out-String | Set-Content -LiteralPath $reportLogPath -Encoding UTF8
    }
    return $result
}

function Start-SystemSlowSampler {
    param(
        [string]$Path,
        [string]$TargetBaseName,
        [int]$IntervalMs,
        [string]$NvidiaSmi
    )

    Start-Job -ArgumentList $Path, $TargetBaseName, $IntervalMs, $NvidiaSmi -ScriptBlock {
        param($Path, $TargetBaseName, $IntervalMs, $NvidiaSmi)

        function Convert-CsvField {
            param($Value)
            if ($null -eq $Value) { return '' }
            $text = [string]$Value
            if ($text -match '[,"\r\n]') { return '"' + $text.Replace('"', '""') + '"' }
            return $text
        }

        function Write-CsvLine {
            param([IO.StreamWriter]$Writer, [object[]]$Values)
            $fields = foreach ($value in $Values) { Convert-CsvField $value }
            $Writer.WriteLine(($fields -join ','))
        }

        function Get-CounterValue {
            param([string]$CounterPath)
            try {
                $sample = (Get-Counter -Counter $CounterPath -ErrorAction Stop).CounterSamples | Select-Object -First 1
                if ($sample) { return [double]$sample.CookedValue }
            }
            catch {}
            return $null
        }

        function Get-SystemSnapshot {
            $result = [ordered]@{
                TotalCpuPct           = $null
                CpuFrequencyMHz       = $null
                CpuPerformancePct     = $null
                AvailableMB           = $null
                DiskAvgSecPerTransfer = $null
                DiskBytesPerSec       = $null
                NetBytesPerSec        = $null
            }
            try {
                $counterPaths = @(
                    '\Processor(_Total)\% Processor Time',
                    '\Processor Information(_Total)\Processor Frequency',
                    '\Processor Information(_Total)\% Processor Performance',
                    '\Memory\Available MBytes',
                    '\LogicalDisk(C:)\Avg. Disk sec/Transfer',
                    '\LogicalDisk(C:)\Disk Bytes/sec',
                    '\Network Interface(*)\Bytes Total/sec'
                )
                $samples = (Get-Counter -Counter $counterPaths -ErrorAction Stop).CounterSamples
                $netTotal = 0.0
                foreach ($sample in $samples) {
                    $pathText = [string]$sample.Path
                    if ($pathText -like '*\processor(_total)\% processor time') { $result.TotalCpuPct = [double]$sample.CookedValue }
                    elseif ($pathText -like '*\processor information(_total)\processor frequency') { $result.CpuFrequencyMHz = [double]$sample.CookedValue }
                    elseif ($pathText -like '*\processor information(_total)\% processor performance') { $result.CpuPerformancePct = [double]$sample.CookedValue }
                    elseif ($pathText -like '*\memory\available mbytes') { $result.AvailableMB = [double]$sample.CookedValue }
                    elseif ($pathText -like '*\logicaldisk(c:)\avg. disk sec/transfer') { $result.DiskAvgSecPerTransfer = [double]$sample.CookedValue }
                    elseif ($pathText -like '*\logicaldisk(c:)\disk bytes/sec') { $result.DiskBytesPerSec = [double]$sample.CookedValue }
                    elseif ($pathText -like '*\network interface(*)\bytes total/sec') { $netTotal += [double]$sample.CookedValue }
                }
                $result.NetBytesPerSec = $netTotal
            }
            catch {
                $result.TotalCpuPct = Get-CounterValue '\Processor(_Total)\% Processor Time'
                $result.CpuFrequencyMHz = Get-CounterValue '\Processor Information(_Total)\Processor Frequency'
                $result.CpuPerformancePct = Get-CounterValue '\Processor Information(_Total)\% Processor Performance'
                $result.AvailableMB = Get-CounterValue '\Memory\Available MBytes'
                $result.DiskAvgSecPerTransfer = Get-CounterValue '\LogicalDisk(C:)\Avg. Disk sec/Transfer'
                $result.DiskBytesPerSec = Get-CounterValue '\LogicalDisk(C:)\Disk Bytes/sec'
            }
            return [pscustomobject]$result
        }

        function Get-GpuSnapshot {
            param([string]$NvidiaSmi)
            if (-not $NvidiaSmi -or -not (Test-Path -LiteralPath $NvidiaSmi)) { return $null }
            try {
                $line = & $NvidiaSmi --query-gpu=utilization.gpu,utilization.memory,temperature.gpu,pstate,clocks.gr,clocks.mem,power.draw,memory.used,memory.total --format=csv,noheader,nounits 2>$null | Select-Object -First 1
                if (-not $line) { return $null }
                $parts = @($line -split ',' | ForEach-Object { $_.Trim() })
                return [pscustomobject]@{
                    GpuUtilPct    = [double]$parts[0]
                    GpuMemUtilPct = [double]$parts[1]
                    GpuTempC      = [double]$parts[2]
                    GpuPState     = $parts[3]
                    GpuClockMHz   = [double]$parts[4]
                    MemClockMHz   = [double]$parts[5]
                    PowerW        = [double]$parts[6]
                    VramUsedMiB   = [double]$parts[7]
                    VramTotalMiB  = [double]$parts[8]
                }
            }
            catch { return $null }
        }

        $encoding = [Text.UTF8Encoding]::new($false)
        $writer = [IO.StreamWriter]::new($Path, $false, $encoding)
        $writer.NewLine = "`r`n"
        Write-CsvLine -Writer $writer -Values @('Time','SampleIndex','Cs2Running','TargetRunning','TotalCpuPct','CpuFrequencyMHz','CpuPerformancePct','AvailableMB','DiskAvgSecPerTransfer','DiskBytesPerSec','NetBytesPerSec','GpuUtilPct','GpuMemUtilPct','GpuTempC','GpuPState','GpuClockMHz','MemClockMHz','PowerW','VramUsedMiB','VramTotalMiB','ProcessCount')

        try {
            $sampleIndex = 0
            while ([bool](Get-Process -Name $TargetBaseName -ErrorAction SilentlyContinue)) {
                $now = Get-Date
                $system = Get-SystemSnapshot
                $gpu = Get-GpuSnapshot -NvidiaSmi $NvidiaSmi
                $processCount = @(Get-Process -ErrorAction SilentlyContinue).Count
                Write-CsvLine -Writer $writer -Values @(
                    $now.ToString('o'),
                    $sampleIndex,
                    [bool](Get-Process -Name cs2 -ErrorAction SilentlyContinue),
                    [bool](Get-Process -Name $TargetBaseName -ErrorAction SilentlyContinue),
                    $(if ($null -ne $system.TotalCpuPct) { [Math]::Round([double]$system.TotalCpuPct, 2) } else { $null }),
                    $(if ($null -ne $system.CpuFrequencyMHz) { [Math]::Round([double]$system.CpuFrequencyMHz, 0) } else { $null }),
                    $(if ($null -ne $system.CpuPerformancePct) { [Math]::Round([double]$system.CpuPerformancePct, 2) } else { $null }),
                    $(if ($null -ne $system.AvailableMB) { [Math]::Round([double]$system.AvailableMB, 1) } else { $null }),
                    $system.DiskAvgSecPerTransfer,
                    $system.DiskBytesPerSec,
                    $system.NetBytesPerSec,
                    $(if ($gpu) { $gpu.GpuUtilPct } else { $null }),
                    $(if ($gpu) { $gpu.GpuMemUtilPct } else { $null }),
                    $(if ($gpu) { $gpu.GpuTempC } else { $null }),
                    $(if ($gpu) { $gpu.GpuPState } else { $null }),
                    $(if ($gpu) { $gpu.GpuClockMHz } else { $null }),
                    $(if ($gpu) { $gpu.MemClockMHz } else { $null }),
                    $(if ($gpu) { $gpu.PowerW } else { $null }),
                    $(if ($gpu) { $gpu.VramUsedMiB } else { $null }),
                    $(if ($gpu) { $gpu.VramTotalMiB } else { $null }),
                    $processCount
                )
                $writer.Flush()
                $sampleIndex++
                Start-Sleep -Milliseconds $IntervalMs
            }
        }
        finally {
            $writer.Flush()
            $writer.Close()
        }
    }
}

$processWriter = $null
$topWriter = $null
$topIoWriter = $null
$alertsWriter = $null
$slowJob = $null
$presentMon = $null
$presentMonExitCode = $null
$presentMonExitedEarly = $false
$presentMonForcedStop = $false

try {
    if (-not $presentMonPath -or -not (Test-Path -LiteralPath $presentMonPath)) {
        throw "PresentMon not found. Expected portable copy under tools\PresentMon-2.4.1-x64.exe or NVIDIA FrameView SDK PresentMon."
    }

    $presentMonItem = Get-Item -LiteralPath $presentMonPath
    $presentMonHash = Get-FileHash -LiteralPath $presentMonPath -Algorithm SHA256
    [pscustomobject]@{
        Path            = $presentMonItem.FullName
        Length          = $presentMonItem.Length
        LastWriteTime   = $presentMonItem.LastWriteTime.ToString('o')
        FileVersion     = $presentMonItem.VersionInfo.FileVersion
        ProductVersion  = $presentMonItem.VersionInfo.ProductVersion
        ProductName     = $presentMonItem.VersionInfo.ProductName
        CompanyName     = $presentMonItem.VersionInfo.CompanyName
        SHA256          = $presentMonHash.Hash
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $presentMonInfoPath -Encoding UTF8

    Write-Status -Phase 'waiting-for-target' -Extra @{ WaitSeconds = $WaitSeconds; CaptureSeconds = $CaptureSeconds; CaptureUntilTargetExit = $captureUntilTargetExit }
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $targetProc = $null
    while ((Get-Date) -lt $deadline) {
        $targetProc = Get-Process -Name $targetProcessBaseName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($targetProc) { break }
        Start-Sleep -Milliseconds 200
    }

    if (-not $targetProc) {
        Write-Status -Phase 'timeout-waiting-for-target'
        exit 2
    }

    $startTime = Get-Date
    Write-Status -Phase 'starting-presentmon' -Extra @{ TargetPid = $targetProc.Id; StartTime = $startTime.ToString('o') }

    $pmArgs = @(
        '--process_name', $presentMonProcessName,
        '--output_file', $presentMonCsv,
        '--date_time',
        '--terminate_on_proc_exit',
        '--no_console_stats',
        '--stop_existing_session',
        '--session_name', 'CodexCS2PresentMon'
    )
    if (-not $captureUntilTargetExit) {
        $pmArgs += @('--timed', [string]$CaptureSeconds, '--terminate_after_timed')
    }

    $presentMon = Start-Process -FilePath $presentMonPath -ArgumentList $pmArgs -PassThru -WindowStyle Hidden -RedirectStandardOutput $presentMonStdout -RedirectStandardError $presentMonStderr

    $processWriter = New-CsvWriter -Path $processCsv -Header @('Time','SampleIndex','ElapsedMs','ProcessName','Count','CpuPct','WorkingSetMB','ReadMBps','WriteMBps','Priorities','Pids')
    $topWriter = New-CsvWriter -Path $topCsv -Header @('Time','SampleIndex','ElapsedMs','ProcessName','Id','CpuPct','WorkingSetMB')
    $topIoWriter = New-CsvWriter -Path $topIoCsv -Header @('Time','SampleIndex','ElapsedMs','ProcessName','Id','CpuPct','ReadMBps','WriteMBps','WorkingSetMB')
    $alertsWriter = New-CsvWriter -Path $alertsCsv -Header @('Time','SampleIndex','ElapsedMs','Alerts','TotalCpuPct','GpuUtilPct','GpuClockMHz','GpuTempC','AvailableMB','DiskLatencySec','TopCpuProcess','TopCpuPct','TopIoProcess','TopIoReadMBps','TopIoWriteMBps')
    $slowJob = Start-SystemSlowSampler -Path $samplesCsv -TargetBaseName $targetProcessBaseName -IntervalMs $SlowSampleIntervalMs -NvidiaSmi $nvidiaSmiPath

    $prevCpu = @{}
    $prevRead = @{}
    $prevWrite = @{}
    $prevTime = Get-Date
    $sampleIndex = 0
    $captureDeadline = if ($captureUntilTargetExit) { [DateTime]::MaxValue } else { (Get-Date).AddSeconds($CaptureSeconds) }
    $cachedSlow = [pscustomobject]@{ AvailableMB = $null; DiskAvgSecPerTransfer = $null; DiskBytesPerSec = $null; NetBytesPerSec = $null }
    $cachedGpu = $null
    $lastStatusWrite = [DateTime]::MinValue

    while ((Get-Date) -lt $captureDeadline) {
        $loopStart = Get-Date
        $targetStillRunning = [bool](Get-Process -Name $targetProcessBaseName -ErrorAction SilentlyContinue)
        if (-not $targetStillRunning) { break }

        if ($presentMon.HasExited -and $null -eq $presentMonExitCode) {
            $presentMonExitCode = $presentMon.ExitCode
            $presentMonExitedEarly = $true
        }

        $now = Get-Date
        $elapsed = [Math]::Max(0.001, ($now - $prevTime).TotalSeconds)
        $elapsedMsSinceStart = [Math]::Round(($now - $startTime).TotalMilliseconds, 1)
        $nowText = $now.ToString('o')
        $procs = @(Get-Process -ErrorAction SilentlyContinue)
        $cpuRows = New-Object System.Collections.Generic.List[object]

        foreach ($p in $procs) {
            $processId = [int]$p.Id
            $cpuNow = $null
            try { $cpuNow = [double]$p.CPU } catch {}
            $cpuPct = $null
            if ($null -ne $cpuNow -and $prevCpu.ContainsKey($processId)) {
                $deltaCpu = $cpuNow - [double]$prevCpu[$processId]
                if ($deltaCpu -ge 0) {
                    $cpuPct = ($deltaCpu / $elapsed / [Environment]::ProcessorCount) * 100.0
                }
            }
            if ($null -ne $cpuNow) { $prevCpu[$processId] = $cpuNow }

            $readMBps = $null
            $writeMBps = $null
            try {
                $readNow = [double]$p.IOReadBytes
                $writeNow = [double]$p.IOWriteBytes
                if ($prevRead.ContainsKey($processId)) { $readMBps = (($readNow - [double]$prevRead[$processId]) / $elapsed) / 1MB }
                if ($prevWrite.ContainsKey($processId)) { $writeMBps = (($writeNow - [double]$prevWrite[$processId]) / $elapsed) / 1MB }
                $prevRead[$processId] = $readNow
                $prevWrite[$processId] = $writeNow
            }
            catch {
            }

            $cpuRows.Add([pscustomobject]@{
                Id          = $processId
                ProcessName = $p.ProcessName
                CpuPct      = if ($null -ne $cpuPct) { [Math]::Round($cpuPct, 2) } else { $null }
                ReadMBps    = if ($null -ne $readMBps) { [Math]::Round($readMBps, 3) } else { $null }
                WriteMBps   = if ($null -ne $writeMBps) { [Math]::Round($writeMBps, 3) } else { $null }
                WorkingSet  = [double]$p.WorkingSet64
            }) | Out-Null
        }

        $groups = @{}
        foreach ($row in $cpuRows) {
            if (-not $row.ProcessName -or $row.ProcessName -eq 'Idle') { continue }
            $name = [string]$row.ProcessName
            if (-not $groups.ContainsKey($name)) {
                $groups[$name] = @{
                    Count      = 0
                    CpuPct     = 0.0
                    HasCpu     = $false
                    WorkingSet = 0.0
                    ReadMBps   = 0.0
                    WriteMBps  = 0.0
                    Pids       = New-Object System.Collections.ArrayList
                }
            }
            $entry = $groups[$name]
            $entry.Count++
            $entry.WorkingSet += [double]$row.WorkingSet
            if ($null -ne $row.CpuPct) {
                $entry.CpuPct += [double]$row.CpuPct
                $entry.HasCpu = $true
            }
            if ($null -ne $row.ReadMBps) { $entry.ReadMBps += [double]$row.ReadMBps }
            if ($null -ne $row.WriteMBps) { $entry.WriteMBps += [double]$row.WriteMBps }
            [void]$entry.Pids.Add([string]$row.Id)
        }

        $top = @($cpuRows | Where-Object { $null -ne $_.CpuPct -and $_.ProcessName -ne 'Idle' } | Sort-Object CpuPct -Descending | Select-Object -First 20)
        foreach ($row in $top) {
            Write-CsvLine -Writer $topWriter -Values @(
                $nowText, $sampleIndex, $elapsedMsSinceStart, $row.ProcessName, $row.Id, $row.CpuPct, [Math]::Round($row.WorkingSet / 1MB, 1)
            )
        }

        $topIo = @(
            $cpuRows |
                Where-Object {
                    $read = Safe-Number $_.ReadMBps
                    $write = Safe-Number $_.WriteMBps
                    (($null -ne $read -and $read -gt 0.01) -or ($null -ne $write -and $write -gt 0.01))
                } |
                Sort-Object @{Expression={ (Safe-Number $_.ReadMBps) + (Safe-Number $_.WriteMBps) }; Descending=$true} |
                Select-Object -First 20
        )
        foreach ($row in $topIo) {
            Write-CsvLine -Writer $topIoWriter -Values @(
                $nowText, $sampleIndex, $elapsedMsSinceStart, $row.ProcessName, $row.Id, $row.CpuPct, $row.ReadMBps, $row.WriteMBps, [Math]::Round($row.WorkingSet / 1MB, 1)
            )
        }

        foreach ($name in $groups.Keys) {
            $entry = $groups[$name]
            Write-CsvLine -Writer $processWriter -Values @(
                $nowText,
                $sampleIndex,
                $elapsedMsSinceStart,
                $name,
                $entry.Count,
                $(if ($entry.HasCpu) { [Math]::Round([double]$entry.CpuPct, 2) } else { $null }),
                [Math]::Round([double]$entry.WorkingSet / 1MB, 1),
                [Math]::Round([double]$entry.ReadMBps, 3),
                [Math]::Round([double]$entry.WriteMBps, 3),
                '',
                ($entry.Pids -join ';')
            )
        }

        $totalCpuPct = $null
        $cpuValues = @($cpuRows | ForEach-Object { Safe-Number $_.CpuPct } | Where-Object { $null -ne $_ })
        if ($cpuValues.Count -gt 0) {
            $totalCpuPct = [Math]::Min(100.0, [Math]::Max(0.0, ($cpuValues | Measure-Object -Sum).Sum))
        }

        $alerts = @()
        if ((Safe-Number $totalCpuPct) -gt 85) { $alerts += 'high-total-cpu' }
        if ((Safe-Number $cachedSlow.AvailableMB) -lt 2048) { $alerts += 'low-available-memory' }
        if ((Safe-Number $cachedSlow.DiskAvgSecPerTransfer) -gt 0.02) { $alerts += 'high-disk-latency' }
        if ($cachedGpu -and (Safe-Number $cachedGpu.GpuUtilPct) -ge 98) { $alerts += 'gpu-near-saturation' }
        if ($cachedGpu -and (Safe-Number $cachedGpu.GpuTempC) -ge 83) { $alerts += 'high-gpu-temperature' }
        if ($cachedGpu -and (Safe-Number $cachedGpu.GpuUtilPct) -gt 80 -and (Safe-Number $cachedGpu.GpuClockMHz) -lt 1200) { $alerts += 'gpu-clock-drop-under-load' }
        if ($top.Count -gt 0 -and (Safe-Number $top[0].CpuPct) -gt 25 -and $top[0].ProcessName -ne $targetProcessBaseName) { $alerts += 'background-cpu-spike' }
        if ($topIo.Count -gt 0 -and (((Safe-Number $topIo[0].ReadMBps) + (Safe-Number $topIo[0].WriteMBps)) -gt 100)) { $alerts += 'heavy-process-io' }

        if ($alerts.Count -gt 0) {
            Write-CsvLine -Writer $alertsWriter -Values @(
                $nowText,
                $sampleIndex,
                $elapsedMsSinceStart,
                ($alerts -join ';'),
                (Round-Nullable $totalCpuPct 2),
                $(if ($cachedGpu) { $cachedGpu.GpuUtilPct } else { $null }),
                $(if ($cachedGpu) { $cachedGpu.GpuClockMHz } else { $null }),
                $(if ($cachedGpu) { $cachedGpu.GpuTempC } else { $null }),
                (Round-Nullable $cachedSlow.AvailableMB 1),
                $cachedSlow.DiskAvgSecPerTransfer,
                $(if ($top.Count -gt 0) { $top[0].ProcessName } else { $null }),
                $(if ($top.Count -gt 0) { $top[0].CpuPct } else { $null }),
                $(if ($topIo.Count -gt 0) { $topIo[0].ProcessName } else { $null }),
                $(if ($topIo.Count -gt 0) { $topIo[0].ReadMBps } else { $null }),
                $(if ($topIo.Count -gt 0) { $topIo[0].WriteMBps } else { $null })
            )
        }

        if ($sampleIndex % 10 -eq 0) {
            $processWriter.Flush()
            $topWriter.Flush()
            $topIoWriter.Flush()
            $alertsWriter.Flush()
        }

        if (($now - $lastStatusWrite).TotalSeconds -ge 1) {
            Write-Status -Phase 'capturing' -Extra @{
                TargetPid             = $targetProc.Id
                PresentMonPid         = $presentMon.Id
                PresentMonExitedEarly = $presentMonExitedEarly
                PresentMonCsvExists   = (Test-Path -LiteralPath $presentMonCsv)
                SampleIndex           = $sampleIndex
                CurrentTotalCpuPct    = (Round-Nullable $totalCpuPct 2)
                CurrentAvailableMB    = (Round-Nullable $cachedSlow.AvailableMB 1)
                CurrentGpuUtilPct     = if ($cachedGpu) { $cachedGpu.GpuUtilPct } else { $null }
                CurrentGpuClockMHz    = if ($cachedGpu) { $cachedGpu.GpuClockMHz } else { $null }
                CurrentGpuTempC       = if ($cachedGpu) { $cachedGpu.GpuTempC } else { $null }
                CurrentAlerts         = $alerts
                CurrentTopCpu         = @($top | Select-Object -First 8 ProcessName, Id, CpuPct, WorkingSet)
                ProcessCount          = $procs.Count
                ProcessGroups         = $groups.Count
            }
            $lastStatusWrite = $now
        }

        $prevTime = $now
        $sampleIndex++
        $processingMs = ((Get-Date) - $loopStart).TotalMilliseconds
        $sleepMs = [Math]::Max(1, [int]($SampleIntervalMs - $processingMs))
        Start-Sleep -Milliseconds $sleepMs
    }

    foreach ($writer in @($processWriter, $topWriter, $topIoWriter, $alertsWriter)) {
        if ($writer) { $writer.Flush(); $writer.Close() }
    }
    $processWriter = $null
    $topWriter = $null
    $topIoWriter = $null
    $alertsWriter = $null

    if ($slowJob) {
        try {
            Wait-Job -Job $slowJob -Timeout 5 | Out-Null
            if ($slowJob.State -eq 'Running') { Stop-Job -Job $slowJob -ErrorAction SilentlyContinue }
            Receive-Job -Job $slowJob -ErrorAction SilentlyContinue | Out-String | Set-Content -LiteralPath $slowSamplerLogPath -Encoding UTF8
            Remove-Job -Job $slowJob -Force -ErrorAction SilentlyContinue
        }
        catch {
            $_ | Out-String | Set-Content -LiteralPath $slowSamplerLogPath -Encoding UTF8
        }
        $slowJob = $null
    }

    if (-not $presentMon.HasExited) {
        $waitedForPresentMon = $presentMon.WaitForExit(10000)
        if (-not $waitedForPresentMon) {
            $presentMonForcedStop = $true
            try { Stop-Process -Id $presentMon.Id -Force -ErrorAction SilentlyContinue } catch {}
            $presentMon.WaitForExit()
        }
    }
    if ($null -eq $presentMonExitCode) {
        try { $presentMon.Refresh() } catch {}
        try { $presentMonExitCode = $presentMon.ExitCode } catch {}
    }
    if ($null -eq $presentMonExitCode -and (Test-Path -LiteralPath $presentMonCsv)) {
        $presentMonExitCode = 0
    }

    $endTime = Get-Date
    $events = Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $startTime; EndTime = $endTime } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -match 'Display|nvlddmkm|WHEA|Disk|stornvme|storahci|storport|Ntfs|Tcpip|Netwtw|e1' -or $_.Id -in 4101,14,17,18,19,51,55,129,153,157,2004 }
    foreach ($event in $events) {
        [pscustomobject]@{
            TimeCreated  = $event.TimeCreated.ToString('o')
            ProviderName = $event.ProviderName
            Id           = $event.Id
            LevelDisplayName = $event.LevelDisplayName
            Message      = ($event.Message -replace '\s+', ' ').Trim()
        } | Export-Csv -LiteralPath $eventsCsv -NoTypeInformation -Append -Encoding UTF8
    }

    Build-Summary -PresentMonExitCode $presentMonExitCode -PresentMonExitedEarly $presentMonExitedEarly -PresentMonForcedStop $presentMonForcedStop
    $reportResult = Invoke-ReportGeneration
    Build-Summary -PresentMonExitCode $presentMonExitCode -PresentMonExitedEarly $presentMonExitedEarly -PresentMonForcedStop $presentMonForcedStop -ReportResult $reportResult

    Write-Status -Phase 'done' -Extra @{
        TargetPid             = $targetProc.Id
        ExitCode              = $presentMonExitCode
        SampleCount           = $sampleIndex
        PresentMonForcedStop  = $presentMonForcedStop
        PresentMonExitedEarly = $presentMonExitedEarly
        EndTime               = $endTime.ToString('o')
        SummaryPath           = $summaryPath
        ReportHtml            = $reportResult.ReportHtml
        ReportPreviewPng      = $reportResult.PreviewPng
        ReportError           = $reportResult.Error
    }

    exit 0
}
catch {
    foreach ($writer in @($processWriter, $topWriter, $topIoWriter, $alertsWriter)) {
        if ($writer) { try { $writer.Flush(); $writer.Close() } catch {} }
    }
    if ($slowJob) {
        try {
            Stop-Job -Job $slowJob -ErrorAction SilentlyContinue
            Receive-Job -Job $slowJob -ErrorAction SilentlyContinue | Out-String | Set-Content -LiteralPath $slowSamplerLogPath -Encoding UTF8
            Remove-Job -Job $slowJob -Force -ErrorAction SilentlyContinue
        }
        catch {}
    }
    $message = $_ | Out-String
    $message | Set-Content -LiteralPath $errorPath -Encoding UTF8
    Write-Status -Phase 'error' -Extra @{ Error = $message; ErrorPath = $errorPath }
    exit 1
}
