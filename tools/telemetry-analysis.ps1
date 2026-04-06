<#
.SYNOPSIS
    AICA v2.1 Telemetry Analysis — generates summary reports from JSONL telemetry data.

.DESCRIPTION
    Reads structured JSONL events produced by TelemetryLogger and outputs
    human-readable statistics for use during validation windows.

    Covers: tool call stats, M1 prune effectiveness, context resets,
    format step hit rate, skill injection distribution, session overview.

.PARAMETER Days
    Number of days of history to include (default 7).

.PARAMETER TelemetryPath
    Path to the telemetry directory (default $env:USERPROFILE\.AICA\telemetry).

.EXAMPLE
    .\telemetry-analysis.ps1
    .\telemetry-analysis.ps1 -Days 30
    .\telemetry-analysis.ps1 -Days 14 -TelemetryPath C:\custom\telemetry
#>
param(
    [int]$Days = 7,
    [string]$TelemetryPath = (Join-Path $env:USERPROFILE ".AICA\telemetry")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Load events ──────────────────────────────────────────────────────

if (-not (Test-Path $TelemetryPath)) {
    Write-Host "Telemetry directory not found: $TelemetryPath" -ForegroundColor Red
    exit 1
}

$cutoff = (Get-Date).AddDays(-$Days).ToString("yyyy-MM-dd")
$files = Get-ChildItem -Path $TelemetryPath -Filter "*.jsonl" |
    Where-Object { $_.BaseName.Substring(0, [Math]::Min(10, $_.BaseName.Length)) -ge $cutoff }

if ($files.Count -eq 0) {
    Write-Host "No telemetry files found in the last $Days days." -ForegroundColor Yellow
    exit 0
}

$events = [System.Collections.Generic.List[PSObject]]::new()
foreach ($file in $files) {
    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }
        try {
            $obj = $trimmed | ConvertFrom-Json
            $events.Add($obj)
        } catch {
            # skip malformed lines
        }
    }
}

if ($events.Count -eq 0) {
    Write-Host "No events found in $($files.Count) file(s)." -ForegroundColor Yellow
    exit 0
}

$totalEvents = $events.Count
$dateRange = "$cutoff .. $(Get-Date -Format 'yyyy-MM-dd')"

Write-Host ""
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "  AICA v2.1 Telemetry Report  |  $dateRange  |  $totalEvents events" -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host ""

# ── Helper ───────────────────────────────────────────────────────────

function Get-MetaValue($event, $key) {
    if ($null -eq $event.metadata) { return $null }
    if ($event.metadata.PSObject.Properties.Name -contains $key) {
        return $event.metadata.$key
    }
    return $null
}

# ── (a) Tool Call Statistics ─────────────────────────────────────────

$toolEvents = $events | Where-Object { $_.event_type -eq "tool_execution" }

if ($toolEvents.Count -gt 0) {
    Write-Host "--- (a) Tool Call Statistics ---" -ForegroundColor Green

    $toolGroups = $toolEvents | Group-Object -Property tool_name | Sort-Object Count -Descending
    $rows = foreach ($g in $toolGroups) {
        $total = $g.Count
        $successes = ($g.Group | Where-Object { (Get-MetaValue $_ "success") -eq $true }).Count
        $rate = if ($total -gt 0) { [math]::Round($successes / $total * 100, 1) } else { 0 }
        $durations = $g.Group | Where-Object { $null -ne $_.duration_ms } | ForEach-Object { $_.duration_ms }
        $avgMs = if ($durations.Count -gt 0) { [math]::Round(($durations | Measure-Object -Average).Average, 0) } else { "-" }
        [PSCustomObject]@{
            Tool       = $g.Name
            Calls      = $total
            SuccessRate = "${rate}%"
            AvgMs      = $avgMs
        }
    }
    $rows | Format-Table -AutoSize | Out-String | Write-Host

    # Top 5 slowest
    $top5 = $rows | Where-Object { $_.AvgMs -ne "-" } |
        Sort-Object { [int]$_.AvgMs } -Descending |
        Select-Object -First 5
    if ($top5.Count -gt 0) {
        Write-Host "  Top 5 Slowest Tools (avg ms):" -ForegroundColor DarkYellow
        $top5 | Format-Table Tool, AvgMs -AutoSize | Out-String | Write-Host
    }
} else {
    Write-Host "--- (a) Tool Call Statistics --- No tool_execution events." -ForegroundColor DarkGray
}

# ── (b) M1 Prune Effectiveness ──────────────────────────────────────

$pruneEvents = $events | Where-Object { $_.event_type -eq "prune_before_compaction" }

Write-Host "--- (b) M1 Prune Effectiveness ---" -ForegroundColor Green

if ($pruneEvents.Count -gt 0) {
    $tokensFreed = $pruneEvents | ForEach-Object { Get-MetaValue $_ "prune_tokens_freed" } |
        Where-Object { $null -ne $_ }
    $avgFreed = if ($tokensFreed.Count -gt 0) { [math]::Round(($tokensFreed | Measure-Object -Average).Average, 0) } else { "-" }

    $avoided = ($pruneEvents | Where-Object { (Get-MetaValue $_ "compaction_avoided") -eq $true }).Count
    $avoidRate = if ($pruneEvents.Count -gt 0) { [math]::Round($avoided / $pruneEvents.Count * 100, 1) } else { 0 }

    [PSCustomObject]@{
        PruneEvents       = $pruneEvents.Count
        AvgTokensFreed    = $avgFreed
        CompactionAvoided = "$avoided / $($pruneEvents.Count) (${avoidRate}%)"
    } | Format-Table -AutoSize | Out-String | Write-Host
} else {
    Write-Host "  No prune_before_compaction events." -ForegroundColor DarkGray
    Write-Host ""
}

# ── (c) Context Reset ───────────────────────────────────────────────

$resetEvents = $events | Where-Object { $_.event_type -eq "context_reset" }

Write-Host "--- (c) Context Reset ---" -ForegroundColor Green

if ($resetEvents.Count -gt 0) {
    $reasons = $resetEvents | ForEach-Object { Get-MetaValue $_ "trigger_reason" } |
        Where-Object { $null -ne $_ } |
        Group-Object | Sort-Object Count -Descending

    Write-Host "  Total resets: $($resetEvents.Count)"
    Write-Host "  Trigger reasons:"
    foreach ($r in $reasons) {
        Write-Host "    $($r.Name): $($r.Count)"
    }
    Write-Host ""
} else {
    Write-Host "  No context_reset events." -ForegroundColor DarkGray
    Write-Host ""
}

# ── (d) Format Step ─────────────────────────────────────────────────

$formatEvents = $events | Where-Object { $_.event_type -eq "format_step" }

Write-Host "--- (d) Format Step (M3) ---" -ForegroundColor Green

if ($formatEvents.Count -gt 0) {
    $changed = ($formatEvents | Where-Object { (Get-MetaValue $_ "format_changed") -eq $true }).Count
    $hitRate = [math]::Round($changed / $formatEvents.Count * 100, 1)
    $durations = $formatEvents | ForEach-Object { Get-MetaValue $_ "format_duration_ms" } |
        Where-Object { $null -ne $_ }
    $avgDur = if ($durations.Count -gt 0) { [math]::Round(($durations | Measure-Object -Average).Average, 0) } else { "-" }

    [PSCustomObject]@{
        FormatEvents   = $formatEvents.Count
        Changed        = "$changed (${hitRate}%)"
        AvgDurationMs  = $avgDur
    } | Format-Table -AutoSize | Out-String | Write-Host
} else {
    Write-Host "  No format_step events." -ForegroundColor DarkGray
    Write-Host ""
}

# ── (e) Skill Injection ─────────────────────────────────────────────

$skillEvents = $events | Where-Object { $_.event_type -eq "skill_injection" }

Write-Host "--- (e) Skill Injection (SK) ---" -ForegroundColor Green

if ($skillEvents.Count -gt 0) {
    Write-Host "  Total injections: $($skillEvents.Count)"
    Write-Host ""

    $byIntent = $skillEvents | ForEach-Object { Get-MetaValue $_ "intent" } |
        Where-Object { $null -ne $_ } |
        Group-Object | Sort-Object Count -Descending
    if ($byIntent.Count -gt 0) {
        Write-Host "  By intent:"
        foreach ($g in $byIntent) {
            Write-Host "    $($g.Name): $($g.Count)"
        }
        Write-Host ""
    }

    $bySkill = $skillEvents | ForEach-Object { Get-MetaValue $_ "skill_name" } |
        Where-Object { $null -ne $_ } |
        Group-Object | Sort-Object Count -Descending
    if ($bySkill.Count -gt 0) {
        Write-Host "  By skill:"
        foreach ($g in $bySkill) {
            Write-Host "    $($g.Name): $($g.Count)"
        }
        Write-Host ""
    }
} else {
    Write-Host "  No skill_injection events." -ForegroundColor DarkGray
    Write-Host ""
}

# ── (f) Session Overview ────────────────────────────────────────────

Write-Host "--- (f) Session Overview ---" -ForegroundColor Green

$sessions = $events | Where-Object { $null -ne $_.session_id -and $_.session_id.Length -gt 0 } |
    Group-Object -Property session_id

if ($sessions.Count -gt 0) {
    $toolCallsPerSession = foreach ($s in $sessions) {
        ($s.Group | Where-Object { $_.event_type -eq "tool_execution" }).Count
    }
    $avgToolCalls = [math]::Round(($toolCallsPerSession | Measure-Object -Average).Average, 1)

    # Session duration: diff between first and last event timestamp
    $durations = foreach ($s in $sessions) {
        $timestamps = $s.Group |
            Where-Object { $null -ne $_.timestamp } |
            ForEach-Object {
                try { [DateTime]::Parse($_.timestamp) } catch { $null }
            } |
            Where-Object { $null -ne $_ } |
            Sort-Object
        if ($timestamps.Count -ge 2) {
            ($timestamps[-1] - $timestamps[0]).TotalMinutes
        }
    }
    $avgDurationMin = if ($durations.Count -gt 0) {
        [math]::Round(($durations | Measure-Object -Average).Average, 1)
    } else { "-" }

    [PSCustomObject]@{
        TotalSessions      = $sessions.Count
        AvgToolCallsPerSes = $avgToolCalls
        AvgDurationMin     = $avgDurationMin
    } | Format-Table -AutoSize | Out-String | Write-Host
} else {
    Write-Host "  No sessions found (missing session_id)." -ForegroundColor DarkGray
    Write-Host ""
}

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "  Report complete. Use -Days N to adjust time window." -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
