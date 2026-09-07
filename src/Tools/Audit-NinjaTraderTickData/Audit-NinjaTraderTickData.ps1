param(
    [string]$TickRoot =
        "$env:USERPROFILE\Documents\NinjaTrader 8\db\tick",

    [string]$OutputCsv =
        "$env:USERPROFILE\Documents\NinjaTrader 8\db\tick\tick_data_audit.csv"
)

$ErrorActionPreference = "Stop"

# -------------------------------------------------------------------------
# Dynamic End Date Calculation for the current active contract
# -------------------------------------------------------------------------
$LastCompletedMarketDate = [datetime]::Today
if ($LastCompletedMarketDate.DayOfWeek -eq [System.DayOfWeek]::Saturday) {
    $LastCompletedMarketDate = $LastCompletedMarketDate.AddDays(-1)
}
elseif ($LastCompletedMarketDate.DayOfWeek -eq [System.DayOfWeek]::Sunday) {
    $LastCompletedMarketDate = $LastCompletedMarketDate.AddDays(-2)
}
else {
    $LastCompletedMarketDate = $LastCompletedMarketDate.AddDays(-1)
}

# -------------------------------------------------------------------------
# Official US Market Holidays (Full Closure Days for CME Equities)
# -------------------------------------------------------------------------
$marketHolidays = @{
    # 2025
    "2025-01-01" = "New Year's Day"
    "2025-01-20" = "Martin Luther King Jr. Day"
    "2025-02-17" = "Presidents' Day"
    "2025-04-18" = "Good Friday"
    "2025-05-26" = "Memorial Day"
    "2025-06-19" = "Juneteenth"
    "2025-07-04" = "Independence Day"
    "2025-09-01" = "Labor Day"
    "2025-11-27" = "Thanksgiving Day"
    "2025-12-25" = "Christmas Day"
    # 2026
    "2026-01-01" = "New Year's Day"
    "2026-01-19" = "Martin Luther King Jr. Day"
    "2026-02-16" = "Presidents' Day"
    "2026-04-03" = "Good Friday"
    "2026-05-25" = "Memorial Day"
    "2026-06-19" = "Juneteenth"
    "2026-07-03" = "Independence Day (Observed)"
    "2026-09-07" = "Labor Day"
    "2026-11-26" = "Thanksgiving Day"
    "2026-12-25" = "Christmas Day"
}

# -------------------------------------------------------------------------
# Contract windows (No overlaps allowed per instrument)
# -------------------------------------------------------------------------
$contracts = @(
    # NQ
    @{ Instrument = "NQ"; Contract = "NQ 12-25"; From = [datetime]"2025-09-15"; To = [datetime]"2025-12-18" },
    @{ Instrument = "NQ"; Contract = "NQ 03-26"; From = [datetime]"2025-12-19"; To = [datetime]"2026-03-19" },
    @{ Instrument = "NQ"; Contract = "NQ 06-26"; From = [datetime]"2026-03-20"; To = [datetime]"2026-06-11" },
    @{ Instrument = "NQ"; Contract = "NQ 09-26"; From = [datetime]"2026-06-12"; To = $LastCompletedMarketDate },

    # ES
    @{ Instrument = "ES"; Contract = "ES 12-25"; From = [datetime]"2025-09-15"; To = [datetime]"2025-12-18" },
    @{ Instrument = "ES"; Contract = "ES 03-26"; From = [datetime]"2025-12-19"; To = [datetime]"2026-03-19" },
    @{ Instrument = "ES"; Contract = "ES 06-26"; From = [datetime]"2026-03-20"; To = [datetime]"2026-06-11" },
    @{ Instrument = "ES"; Contract = "ES 09-26"; From = [datetime]"2026-06-12"; To = $LastCompletedMarketDate }
)

# -------------------------------------------------------------------------
# Phase 1: Validate Setup Boundaries
# -------------------------------------------------------------------------
Write-Host "Validating contract configurations for overlaps..." -ForegroundColor Cyan
Write-Host "Dynamic End Date set to: $($LastCompletedMarketDate.ToString('yyyy-MM-dd'))" -ForegroundColor Yellow

$instruments = "ES", "NQ"
foreach ($inst in $instruments) {
    $filtered = $contracts | Where-Object { $_.Instrument -eq $inst } | Sort-Object From
    for ($i = 0; $i -lt ($filtered.Count - 1); $i++) {
        if ($filtered[$i].To.Date -ge $filtered[$i+1].From.Date) {
            Write-Error "CRITICAL OVERLAP DETECTED for [$inst]: $($filtered[$i].Contract) ($($filtered[$i].To.ToString('yyyy-MM-dd'))) overlaps with $($filtered[$i+1].Contract) ($($filtered[$i+1].From.ToString('yyyy-MM-dd')))"
        }
    }
}

function Get-Weekdays {
    param([datetime]$From, [datetime]$To)
    $dates = New-Object System.Collections.Generic.List[datetime]
    for ($date = $From.Date; $date -le $To.Date; $date = $date.AddDays(1)) {
        $dateStr = $date.ToString("yyyy-MM-dd")
        if ($date.DayOfWeek -ne [System.DayOfWeek]::Saturday -and 
            $date.DayOfWeek -ne [System.DayOfWeek]::Sunday -and 
            -not $marketHolidays.ContainsKey($dateStr)) {
            $dates.Add($date)
        }
    }
    return $dates
}

function Get-TickDatesFromFolder {
    param([string]$Folder)
    if (-not (Test-Path $Folder)) { return @() }
    
    $foundDates = Get-ChildItem -Path $Folder -Filter "*.Last.ncd" -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            if ($_.Name -match '^(\d{4})(\d{2})(\d{2})\d{4}\.Last\.ncd$') {
                try { 
                    [datetime]::new([int]$matches[1], [int]$matches[2], [int]$matches[3]) 
                } catch {}
            }
        } | Sort-Object -Unique
    return @($foundDates)
}

function Find-CorrectContract {
    param([string]$Instrument, [datetime]$Date)
    $match = $contracts | Where-Object { $_.Instrument -eq $Instrument -and $Date.Date -ge $_.From.Date -and $Date.Date -le $_.To.Date }
    if ($match) { return $match.Contract }
    return "OUT_OF_BOUNDS / REMOVED"
}

$results = New-Object System.Collections.Generic.List[object]
$globalMisplacedFiles = New-Object System.Collections.Generic.List[object]
$globalMissingDates = New-Object System.Collections.Generic.List[object]

# -------------------------------------------------------------------------
# Phase 2: Audit Folders and Cross-Reference Dates
# -------------------------------------------------------------------------
foreach ($item in $contracts) {
    $folder = Join-Path $TickRoot $item.Contract
    $allTickDates = Get-TickDatesFromFolder -Folder $folder

    $tickDatesInWindow = New-Object System.Collections.Generic.List[datetime]
    $wrongContractAlerts = New-Object System.Collections.Generic.List[string]

    foreach ($date in $allTickDates) {
        if ($date.Date -ge $item.From.Date -and $date.Date -le $item.To.Date) {
            $tickDatesInWindow.Add($date)
        } else {
            $correctContract = Find-CorrectContract -Instrument $item.Instrument -Date $date
            $formattedDate = $date.ToString("yyyy-MM-dd")
            $wrongContractAlerts.Add("$formattedDate->$correctContract")
            
            $globalMisplacedFiles.Add([pscustomobject]@{
                CurrentFolder = $item.Contract
                FileDate      = $formattedDate
                TargetFolder  = $correctContract
            })
        }
    }

    $expectedWeekdays = @(Get-Weekdays -From $item.From -To $item.To)
    $tickDateLookup = @{}
    foreach ($date in $tickDatesInWindow) { $tickDateLookup[$date.ToString("yyyy-MM-dd")] = $true }

    $missingDates = @($expectedWeekdays | Where-Object { -not $tickDateLookup.ContainsKey($_.ToString("yyyy-MM-dd")) })
    
    # Log individual missing dates for dedicated visualization
    foreach ($mDate in $missingDates) {
        $globalMissingDates.Add([pscustomobject]@{
            Instrument = $item.Instrument
            Contract   = $item.Contract
            Date       = $mDate.ToString("yyyy-MM-dd")
            Status     = "Missing"
        })
    }

    $firstTick = if ($tickDatesInWindow.Count -gt 0) { $tickDatesInWindow[0] } else { $null }
    $lastTick = if ($tickDatesInWindow.Count -gt 0) { $tickDatesInWindow[$tickDatesInWindow.Count - 1] } else { $null }

    $coveragePercent = if ($expectedWeekdays.Count -gt 0) { [math]::Round(($tickDatesInWindow.Count / $expectedWeekdays.Count) * 100, 1) } else { 0 }

    $status = if (-not (Test-Path $folder)) { "FOLDER MISSING" }
              elseif ($allTickDates.Count -eq 0) { "NO TICK DATA" }
              elseif ($wrongContractAlerts.Count -gt 0) { "MISPLACED DATA DETECTED" }
              elseif ($missingDates.Count -eq 0) { "COMPLETE" }
              elseif ($coveragePercent -ge 90) { "MOSTLY COMPLETE" }
              else { "INCOMPLETE" }

    $result = [pscustomobject]@{
        Instrument          = $item.Instrument
        Contract            = $item.Contract
        ExpectedFrom        = $item.From.ToString("yyyy-MM-dd")
        ExpectedTo          = $item.To.ToString("yyyy-MM-dd")
        FirstTickDate       = if ($firstTick) { $firstTick.ToString("yyyy-MM-dd") } else { "" }
        LastTickDate        = if ($lastTick) { $lastTick.ToString("yyyy-MM-dd") } else { "" }
        ExpectedWeekdays    = $expectedWeekdays.Count
        ValidTickDaysFound  = $tickDatesInWindow.Count
        MisplacedDaysFound  = $wrongContractAlerts.Count
        MissingWeekdays     = $missingDates.Count
        CoveragePercent     = $coveragePercent
        Status              = $status
        MisplacedDatesMap   = ($wrongContractAlerts -join ", ")
        MissingDates        = ($missingDates | ForEach-Object { $_.ToString("yyyy-MM-dd") }) -join ", "
    }
    $results.Add($result)
}

# -------------------------------------------------------------------------
# Console Summary and Export
# -------------------------------------------------------------------------
Write-Host ""
Write-Host "============================================================"
Write-Host " NinjaTrader Tick Data Audit Report"
Write-Host "============================================================"

$results | Format-Table -Property Instrument, Contract, Status, ValidTickDaysFound, MisplacedDaysFound, MissingWeekdays, CoveragePercent

if ($globalMissingDates.Count -gt 0) {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Red
    Write-Host " MISSING DATES BREAKDOWN" -ForegroundColor Red
    Write-Host "============================================================" -ForegroundColor Red
    $globalMissingDates | Sort-Object Contract, Date | Format-Table -Property Instrument, Contract, Date, Status
}

