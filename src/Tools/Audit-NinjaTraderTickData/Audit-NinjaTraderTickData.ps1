#requires -Version 5.1
<#
READ-ONLY NinjaTrader data audit. Does not modify the NinjaTrader database.
Quick start: .\Audit-NinjaTraderData-v1.2.ps1
Compare: .\Audit-NinjaTraderData-v1.2.ps1 -HashFiles -CompareInventory 'C:\AuditPC\inventory.csv'
Focused check: .\Audit-NinjaTraderData-v1.2.ps1 -From '2025-10-01' -To '2025-10-01'
Optional deep audit: -ExportManifest 'C:\exports\manifest.csv'
Manifest columns: Contract,Interval,Path
Example row: ES 12-25,Minute,C:\exports\ES-12-25-minute.Last.txt
Another row: ES 12-25,Tick,C:\exports\ES-12-25-tick.Last.txt
Use native NinjaTrader Last exports (one-minute bars, not five-minute bars).
Exports are UTC. Convert to ET for session analysis; chart timezone stays ET.
Export the PREVIOUS evening too. Manifest can include several disjoint files.

Reports go into a NEW timestamped folder under OutputDirectory:
 inventory.csv: every NCD file, raw filename date, bytes, read errors, optional SHA256.
 dates.csv: Tick-Last vs Minute-Last inside assigned contract windows only.
 warmup-checks.csv: prior-evening context needed by the first audited session;
 separate from missing full-day downloads, not inferred from another contract.
 download-checklist.csv: every absent date with the download interval(s) to select.
 download-checklist.txt: the same readable checklist shown in the console.
 issues.csv: directory failures, unknown names, zero bytes, missing dates, window gaps.
 comparison.csv: union of PC/VPS relative filenames; hashes/bytes comparison.
 exports.csv: actual export rows, parse/order/duplicate-minute issues.
 sessions.csv: expected one-minute buckets with no data, by ET session window.
 gaps.csv: individual missing ET minutes (tick absence is not proof of missing trades).
 summary.txt: scope and limits.

A nonempty NCD is NOT proof of intraday completeness. NCD binary records are NOT
 decoded. Filename dates are NOT assumed to be ET session dates. No holiday is
 silently excluded. Default schedule checks weekdays, normal overnight/premarket/
 RTH windows only; exchange holidays/halts require review, not automatic repair.
Contract windows are YOUR research allocation, not exchange validity rules.
Files outside them are retained and are not 'misplaced'. Never move contracts.
Deep audit checks timestamp coverage and basic numeric integrity, not price truth
 or tick-by-tick equivalence. Optional hashes compare bytes, not economic identity.
#>
[CmdletBinding()]
param(
 [string]$DbRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8\db'),
 [string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTraderDataAudits'),
 [datetime]$From = '2025-09-15',
 [datetime]$To = [datetime]::MinValue,
 [string]$ContractsCsv,
 [string]$ExportManifest,
 [string]$CompareInventory,
 [switch]$HashFiles
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Write-Host 'NinjaTrader Data Audit v1.2 - assigned contract dates only; warm-up checks reported separately' -ForegroundColor Cyan
$ci = [Globalization.CultureInfo]::InvariantCulture
try { $et = [TimeZoneInfo]::FindSystemTimeZoneById('Eastern Standard Time') }
catch { $et = [TimeZoneInfo]::FindSystemTimeZoneById('America/New_York') }
if ($To -eq [datetime]::MinValue) {
 # Conservative default: previous ET weekday, never a partly completed today.
 $To = [TimeZoneInfo]::ConvertTimeFromUtc([datetime]::UtcNow,$et).Date.AddDays(-1)
 while ($To.DayOfWeek -in @('Saturday','Sunday')) { $To = $To.AddDays(-1) }
}
$From=$From.Date; $To=$To.Date
if ($From -gt $To) { throw 'From must be on or before To.' }
if (-not (Test-Path -LiteralPath $DbRoot -PathType Container)) { throw "Database folder not found: $DbRoot. Supply -DbRoot (including redirected Documents/OneDrive if applicable)." }
$run = Join-Path $OutputDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$issues = [Collections.Generic.List[object]]::new()
$inventory = [Collections.Generic.List[object]]::new()
$dates = [Collections.Generic.List[object]]::new()
$warmup = [Collections.Generic.List[object]]::new()
function Issue($scope,$detail) { $issues.Add([pscustomobject]@{Scope=$scope;Detail=$detail}) }
function SaveCsv($rows,$name,$columns) {
 $path=Join-Path $run $name
 if ($rows.Count -gt 0) { $rows | Select-Object -Property $columns | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8 }
 else { ('"'+($columns -join '","')+'"') | Set-Content -LiteralPath $path -Encoding UTF8 }
}
if ($ContractsCsv) { $raw=@(Import-Csv -LiteralPath $ContractsCsv) }
else {
 $raw=@(foreach($symbol in @('ES','NQ')) {
  [pscustomobject]@{Contract="$symbol 12-25";From='2025-09-15';To='2025-12-18'}
  [pscustomobject]@{Contract="$symbol 03-26";From='2025-12-19';To='2026-03-19'}
  [pscustomobject]@{Contract="$symbol 06-26";From='2026-03-20';To='2026-06-11'}
  [pscustomobject]@{Contract="$symbol 09-26";From='2026-06-12';To='2026-09-17'}
 })
}
$contracts=@(foreach($r in $raw) {
 if ($r.Contract -notmatch '^(ES|NQ) \d{2}-\d{2}$') { throw "Invalid contract: $($r.Contract)" }
 $a=[datetime]::ParseExact($r.From,'yyyy-MM-dd',$ci); $b=[datetime]::ParseExact($r.To,'yyyy-MM-dd',$ci)
 if($a -gt $b) {throw "Reversed window: $($r.Contract)"}
 [pscustomobject]@{Contract=$r.Contract;Symbol=$r.Contract.Split(' ')[0];From=$a;To=$b}
})
if (@($contracts.Contract | Select-Object -Unique).Count -ne $contracts.Count) {throw 'Duplicate contract rows.'}
foreach($symbol in @('ES','NQ')) {
 $s=@($contracts | Where-Object Symbol -eq $symbol | Sort-Object From)
 for($i=1;$i -lt $s.Count;$i++) { if($s[$i].From -le $s[$i-1].To) {throw "Overlapping research windows: $symbol"} }
 for($d=$From;$d -le $To;$d=$d.AddDays(1)) {
  if($d.DayOfWeek -in @('Saturday','Sunday')) {continue}
  if(@($s | Where-Object {$d -ge $_.From -and $d -le $_.To}).Count -eq 0) {Issue 'UnassignedDate' "$symbol $($d.ToString('yyyy-MM-dd')): update ContractsCsv; no active contract guessed."}
 }
}
$lookup=@{}
foreach($c in $contracts) {
 foreach($interval in @('tick','minute')) {
  $folder=Join-Path (Join-Path $DbRoot $interval) $c.Contract
  if(-not (Test-Path -LiteralPath $folder -PathType Container)) {Issue 'MissingFolder' $folder;continue}
  try { $files=@(Get-ChildItem -LiteralPath $folder -File -Filter '*.ncd' -ErrorAction Stop) }
  catch {Issue 'DirectoryReadError' "$folder : $_";continue}
  foreach($f in $files) {
   $date='';$price='Unknown';$stamp='';$problem='';$hash=''
   if($f.Name -match '^(?<stamp>\d{8}(?:\d{4}(?:\d{2})?)?)\.(?<price>Last|Bid|Ask)\.ncd$') {
    $stamp=$Matches.stamp; $price=$Matches.price
    $fmt=switch($stamp.Length){8 {'yyyyMMdd'} 12 {'yyyyMMddHHmm'} 14 {'yyyyMMddHHmmss'}}
    try {$parsed=[datetime]::ParseExact($stamp,$fmt,$ci);$date=$parsed.ToString('yyyy-MM-dd')}
    catch {$problem='Invalid filename timestamp'}
   } else {$problem='Unrecognized filename format; retained for inspection'}
   if($f.Length -eq 0) {$problem+='; Zero bytes'}
   try {
    if($HashFiles) {$hash=(Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash}
    else { $stream=[IO.File]::Open($f.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite);try {[void]$stream.ReadByte()} finally {$stream.Dispose()} }
   } catch {$problem+="; Read failed: $_"}
   if($problem) {Issue 'FileIssue' "$($f.FullName): $problem"}
   $key="$($c.Contract)|$interval|$date"
   if($price -eq 'Last' -and $date -and -not $problem) {
    if(-not $lookup.ContainsKey($key)) {$lookup[$key]=0};$lookup[$key]++
   }
   $inventory.Add([pscustomobject]@{Machine=[Environment]::MachineName;RelativePath="$interval/$($c.Contract)/$($f.Name)";Contract=$c.Contract;Interval=$interval;PriceType=$price;FileDate=$date;RawStamp=$stamp;Bytes=$f.Length;LastWriteUtc=$f.LastWriteTimeUtc.ToString('o');SHA256=$hash;Problem=$problem})
  }
 }
 $a=if($c.From -gt $From){$c.From}else{$From};$b=if($c.To -lt $To){$c.To}else{$To}
 if($a -gt $b){continue}
 # Only assigned weekdays are included in date/checklist reports.
 # Inventory and export parsing retain Sunday evening records for Monday context.
 # A date belongs only to its configured research window in this report.
 # Warm-up needs are separate: a prior contract cannot substitute price levels.
 $firstSession=$a
 while($firstSession.DayOfWeek -in @('Saturday','Sunday')) {$firstSession=$firstSession.AddDays(1)}
 if($firstSession -le $b) {
  $warmup.Add([pscustomobject]@{
   Contract=$c.Contract
   FirstSessionET=$firstSession.ToString('yyyy-MM-dd')
   OvernightStartET=$firstSession.AddDays(-1).AddHours(18).ToString('yyyy-MM-dd HH:mm')
   OvernightEndET=$firstSession.AddHours(9.5).ToString('yyyy-MM-dd HH:mm')
   Status='CONTENT_CHECK_REQUIRED_NOT_A_WHOLE_DAY_DOWNLOAD'
   Note='Check same-contract overnight records; earlier history may also be needed for ATR/EMA and prior-day close. Inventory does not verify this interval.'
  })
 }
 for($d=$a;$d -le $b;$d=$d.AddDays(1)) {
  if($d.DayOfWeek -in @('Saturday','Sunday')) {continue}
  $label=$d.ToString('yyyy-MM-dd');$t=0;$m=0
  if($lookup.ContainsKey("$($c.Contract)|tick|$label")){$t=$lookup["$($c.Contract)|tick|$label"]}
  if($lookup.ContainsKey("$($c.Contract)|minute|$label")){$m=$lookup["$($c.Contract)|minute|$label"]}
  $status=if($t -gt 0 -and $m -gt 0){'FILES_PRESENT_CONTENT_UNVERIFIED'}elseif($t -gt 0){'MINUTE_LAST_ABSENT'}elseif($m -gt 0){'TICK_LAST_ABSENT'}else{'BOTH_ABSENT'}
  $basis='AssignedContractDate'
  $dates.Add([pscustomobject]@{Contract=$c.Contract;RawFileDate=$label;Basis=$basis;TickLastFiles=$t;MinuteLastFiles=$m;Status=$status})
  if($status -ne 'FILES_PRESENT_CONTENT_UNVERIFIED') {Issue $status "$($c.Contract) raw filename date $label ($basis); verify ET mapping and exchange calendar."}
 }
}
SaveCsv $inventory 'inventory.csv' @('Machine','RelativePath','Contract','Interval','PriceType','FileDate','RawStamp','Bytes','LastWriteUtc','SHA256','Problem')
SaveCsv $warmup 'warmup-checks.csv' @('Contract','FirstSessionET','OvernightStartET','OvernightEndET','Status','Note')
SaveCsv $dates 'dates.csv' @('Contract','RawFileDate','Basis','TickLastFiles','MinuteLastFiles','Status')
if($CompareInventory) {
 $other=@{};foreach($r in (Import-Csv -LiteralPath $CompareInventory)){$other[$r.RelativePath]=$r}
 $here=@{};foreach($r in $inventory){$here[$r.RelativePath]=$r}
 $comparison=@(foreach($key in @(@($other.Keys)+@($here.Keys) | Sort-Object -Unique)) {
  $status=if(-not $here.ContainsKey($key)){'ONLY_OTHER'}elseif(-not $other.ContainsKey($key)){'ONLY_HERE'}elseif($here[$key].Problem -or $other[$key].Problem){'READ_OR_FORMAT_ISSUE'}elseif($here[$key].SHA256 -and $other[$key].SHA256){if($here[$key].SHA256 -eq $other[$key].SHA256){'HASH_MATCH'}else{'HASH_DIFF'}}elseif([long]$here[$key].Bytes -ne [long]$other[$key].Bytes){'SIZE_DIFF'}else{'SAME_SIZE_UNVERIFIED'}
  [pscustomobject]@{RelativePath=$key;Status=$status}
 })
 SaveCsv $comparison 'comparison.csv' @('RelativePath','Status')
}
$exportStats=[Collections.Generic.List[object]]::new()
$sessions=[Collections.Generic.List[object]]::new()
$gaps=[Collections.Generic.List[object]]::new()
if($ExportManifest) {
 $buckets=@{}
 foreach($entry in (Import-Csv -LiteralPath $ExportManifest)) {
  if($entry.Interval -notin @('Tick','Minute')){throw 'Export interval must be Tick or Minute.'}
  if($entry.Contract -notin $contracts.Contract){throw "Export contract not in research windows: $($entry.Contract)"}
  $key="$($entry.Contract)|$($entry.Interval)"
  if(-not $buckets.ContainsKey($key)){$buckets[$key]=[Collections.Generic.HashSet[long]]::new()}
  $seen=$buckets[$key];$rows=0L;$bad=0L;$outOfOrder=0L;$duplicates=0L;$first=$null;$last=$null;$previous=$null
  $reader=[IO.StreamReader]::new($entry.Path)
  try {
   while($null -ne ($line=$reader.ReadLine())) {
    if([string]::IsNullOrWhiteSpace($line)){continue}
    $rows++
    try {
     $parts=$line.Split(';')
     if($entry.Interval -eq 'Minute' -and $parts.Length -ne 6){throw 'Expected minute OHLCV row'}
     if($entry.Interval -eq 'Tick' -and $parts.Length -notin @(3,5)){throw 'Expected native Last tick row'}
     $ts=$parts[0].Trim()
     $format=if($ts -match '^\d{8} \d{6} \d{7}$'){'yyyyMMdd HHmmss fffffff'}elseif($ts -match '^\d{8} \d{6}$'){'yyyyMMdd HHmmss'}else{throw 'Invalid timestamp'}
     $utc=[datetime]::SpecifyKind([datetime]::ParseExact($ts,$format,$ci),[DateTimeKind]::Utc)
     for($n=1;$n -lt $parts.Length;$n++) {
      $v=[double]::Parse($parts[$n],$ci)
      if([double]::IsNaN($v) -or [double]::IsInfinity($v) -or $v -lt 0){throw 'Invalid numeric value'}
     }
     if($entry.Interval -eq 'Minute') {
      if($utc.Second -ne 0){throw 'Minute export is not end-stamped on minute boundary'}
      $o=[double]::Parse($parts[1],$ci);$h=[double]::Parse($parts[2],$ci);$l=[double]::Parse($parts[3],$ci);$cl=[double]::Parse($parts[4],$ci)
      if($l -le 0 -or $h -lt $l -or $o -lt $l -or $o -gt $h -or $cl -lt $l -or $cl -gt $h){throw 'Invalid OHLC'}
     } elseif([double]::Parse($parts[1],$ci) -le 0){throw 'Invalid Last price'}
     if($null -ne $previous -and $utc -lt $previous){$outOfOrder++}
     $previous=$utc
     if($null -eq $first -or $utc -lt $first){$first=$utc}
     if($null -eq $last -or $utc -gt $last){$last=$utc}
     $local=[TimeZoneInfo]::ConvertTimeFromUtc($utc,$et)
     # Minute exports are close-stamped; tick occupancy uses next minute close.
     $bucket=$local.Ticks-($local.Ticks % [TimeSpan]::TicksPerMinute)
     if($entry.Interval -eq 'Tick'){$bucket += [TimeSpan]::TicksPerMinute}
     if(-not $seen.Add($bucket) -and $entry.Interval -eq 'Minute'){$duplicates++}
    } catch {
     $bad++
     if($bad -le 5){Issue 'ExportRowError' "$($entry.Path) line $rows : $_"}
    }
   }
  } finally {$reader.Dispose()}
  $exportStats.Add([pscustomobject]@{Contract=$entry.Contract;Interval=$entry.Interval;Path=$entry.Path;Rows=$rows;BadRows=$bad;OutOfOrder=$outOfOrder;DuplicateMinuteBuckets=$duplicates;FirstUtc=$(if($first){$first.ToString('o')}else{''});LastUtc=$(if($last){$last.ToString('o')}else{''})})
 }
 foreach($c in $contracts) {
  $a=if($c.From -gt $From){$c.From}else{$From};$b=if($c.To -lt $To){$c.To}else{$To}
  for($d=$a;$d -le $b;$d=$d.AddDays(1)) {
   if($d.DayOfWeek -in @('Saturday','Sunday')){continue}
   foreach($interval in @('Tick','Minute')) {
    $key="$($c.Contract)|$interval"
    if(-not $buckets.ContainsKey($key)){
     $sessions.Add([pscustomobject]@{Contract=$c.Contract;DateET=$d.ToString('yyyy-MM-dd');Interval=$interval;Window='All';ExpectedMinutes=0;PresentMinutes=0;MissingMinutes=0;Status='NOT_EXPORTED'})
     continue
    }
    $windows=@(
     @{Name='Overnight';Start=$d.AddDays(-1).AddHours(18).AddMinutes(1);End=$d.AddHours(9.5)},
     @{Name='Premarket';Start=$d.AddHours(3).AddMinutes(1);End=$d.AddHours(9.5)},
     @{Name='RTH';Start=$d.AddHours(9.5).AddMinutes(1);End=$d.AddHours(16)}
    )
    foreach($w in $windows) {
     $expected=0;$present=0
     for($minute=$w.Start;$minute -le $w.End;$minute=$minute.AddMinutes(1)) {
      $expected++
      if($buckets[$key].Contains($minute.Ticks)){$present++}
      else {$gaps.Add([pscustomobject]@{Contract=$c.Contract;Interval=$interval;Window=$w.Name;MissingMinuteCloseET=$minute.ToString('yyyy-MM-dd HH:mm:ss')})}
     }
     $status=if($present -eq $expected){'BUCKETS_PRESENT_NOT_PRICE_VALIDATION'}else{'GAPS_REVIEW_CALENDAR_AND_EXPORT_SCOPE'}
     $sessions.Add([pscustomobject]@{Contract=$c.Contract;DateET=$d.ToString('yyyy-MM-dd');Interval=$interval;Window=$w.Name;ExpectedMinutes=$expected;PresentMinutes=$present;MissingMinutes=($expected-$present);Status=$status})
    }
   }
  }
 }
 SaveCsv $exportStats 'exports.csv' @('Contract','Interval','Path','Rows','BadRows','OutOfOrder','DuplicateMinuteBuckets','FirstUtc','LastUtc')
 SaveCsv $sessions 'sessions.csv' @('Contract','DateET','Interval','Window','ExpectedMinutes','PresentMinutes','MissingMinutes','Status')
 SaveCsv $gaps 'gaps.csv' @('Contract','Interval','Window','MissingMinuteCloseET')
}
SaveCsv $issues 'issues.csv' @('Scope','Detail')
@"
NinjaTrader audit - $(Get-Date -Format o)
Machine: $([Environment]::MachineName)
Database: $DbRoot
Requested dates: $($From.ToString('yyyy-MM-dd')) through $($To.ToString('yyyy-MM-dd'))
Files inventoried: $($inventory.Count). Issues/review items: $($issues.Count).
Hashing: $HashFiles. Export manifest: $ExportManifest

Required combinations: Tick + Last, Minute + Last. Bid/Ask are inventoried if
present, not required by this strategy. Day bars and market-replay files are
outside this audit. Last is a price type, not a third interval.
NCD files are checked for names/size/readability, NOT decoded or certified.
Filename dates are raw storage labels, not ET session dates. Weekday absence
is a review finding, not proof of a missing trading session. Holidays/halts
and early closes are NOT automatically removed. Verify the CME product calendar.
Only dates within each configured contract window appear in dates.csv and the
download checklist; Saturday/Sunday dates are excluded. Earlier context is
listed separately in warmup-checks.csv and needs content verification.
Sunday evening records remain in inventory and Monday overnight export checks.
Contract windows preserve your allocation (ES/NQ September 2026 capped at
2026-09-17); later dates are flagged unassigned. Supply ContractsCsv to extend.
Window overlap on disk is legitimate. No source file is moved or deleted.

Deep checks require native UTC NinjaTrader Last text exports. Export at least
the prior evening plus every requested day. Minute exports must be ONE minute.
Normal ET windows: overnight (18:00,09:30], premarket (03:00,09:30], RTH
(09:30,16:00]. Tick bins cover the preceding minute, not bar-close ticks.
Missing tick bins can be inactivity; missing minute bins can be exchange closures.
All findings need calendar/export-scope review. Premarket overlaps overnight.
Matching hashes mean identical file bytes, not complete/accurate market data.
Run on both machines with NinjaTrader closed for a stable comparison.
"@ | Set-Content -LiteralPath (Join-Path $run 'summary.txt') -Encoding UTF8
$downloadChecklist = @(
 foreach ($row in ($dates | Sort-Object Contract,RawFileDate)) {
  if ($row.Status -eq 'FILES_PRESENT_CONTENT_UNVERIFIED') { continue }
  $intervals = if ($row.TickLastFiles -eq 0 -and $row.MinuteLastFiles -eq 0) {
   'Tick + Minute'
  } elseif ($row.TickLastFiles -eq 0) { 'Tick' } else { 'Minute' }
  $action = 'DOWNLOAD / verify holiday'
  [pscustomobject]@{
   Contract = $row.Contract
   MissingDate = $row.RawFileDate
   SelectIntervals = $intervals
   SelectDataType = 'Last'
   Action = $action
  }
 }
)
SaveCsv $downloadChecklist 'download-checklist.csv' @('Contract','MissingDate','SelectIntervals','SelectDataType','Action')
$checklistText = @(
 'MISSING DATA - DOWNLOAD CHECKLIST'
 'Select the contract, listed date, interval(s), and Last in Historical Data > Download.'
 'Dates below are raw file dates. Check ET coverage on adjacent dates when downloading.'
 'Saturday/Sunday dates excluded. Holiday absence may be normal; verify the exchange calendar.'
 'Sunday evening data can still be required for Monday overnight ranges. Check issues.csv for read errors.'
 'No source data is changed and no download is submitted by this script.'
 ''
)
if ($downloadChecklist.Count -gt 0) {
 $checklistText += ($downloadChecklist | Format-Table Contract,MissingDate,SelectIntervals,SelectDataType,Action -AutoSize | Out-String -Width 180)
} else {
 $checklistText += 'No absent date-level files found in the audited windows. Intraday completeness remains unverified.'
}
$checklistText | Set-Content -LiteralPath (Join-Path $run 'download-checklist.txt') -Encoding UTF8
$checklistText | ForEach-Object { Write-Host $_ }
Write-Host 'First-session overnight context is listed separately in warmup-checks.csv; these are not full-day missing-data claims.' -ForegroundColor Yellow
Write-Host "Reports: $run" -ForegroundColor Cyan
Write-Host 'Start with download-checklist.csv. Intraday export gaps remain in gaps.csv when an ExportManifest is supplied.' -ForegroundColor Yellow
