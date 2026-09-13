#requires -Version 5.1
<#
Audit-NinjaTraderData-v2.2.ps1 -- READ ONLY. No downloads/deletes/database writes.

FOCUSED CHECK (candidate filename mapping; independent exports recommended):
 .\Audit-NinjaTraderData-v2.2.ps1 -Symbols ES -From '2026-08-27' -To '2026-09-01'

IMPORTANT: filenames alone cannot establish their timezone or start/end label.
ET / End are inferred defaults from your supplied inventory and UI examples. Verify using a known file's actual contents
and NT's Historical Data timestamps. Then supply the verified values, e.g.:
 -NcdTimeZoneId 'Eastern Standard Time' -TickFileHourLabel End -ConfirmNcdMapping
MinuteFileDateLabel defaults to CloseDate; use BarStartDate only if independently
verified (this affects the midnight bar's daily container).
For ET filenames use 'Eastern Standard Time' on Windows. Do not equate the chart
or PC timezone with NCD storage timezone. Do not confirm solely to remove warnings.
Without confirmation, filename holes are marked VERIFY MAPPING THEN DOWNLOAD.

DEEP AUDIT (needed to inspect holes inside files):
 Export native NinjaTrader Last Tick AND 1-Minute text data covering requested
 dates AND warm-up dependencies. NinjaTrader exports are UTC, close-stamped for
 minutes. Do not supply 5-minute bars. Create manifest.csv:
 Contract,Interval,Path
 ES 09-26,Tick,C:\exports\ES-09-26-tick.Last.txt
 ES 09-26,Minute,C:\exports\ES-09-26-minute.Last.txt
 Add -ExportManifest 'C:\exports\manifest.csv'
 Partial export scope is reported as a gap; it is not proof the database is empty.
 Missing export files cause a clear error. Missing interval entries are UNVERIFIED.

WarmupSessions defaults to 3 prior weekdays per contract (configurable 0..30).
required-sessions.csv gives exact scope to export. This conservative lookback does
not guarantee EMA convergence or duplicate every strategy initialization setting.
Normal scope covers each session's previous 18:00 to 17:00 ET, including Sunday
for Monday. Built-in closures cover ES/NQ holidays in Sep 15 2025-Sep 11 2026.
Only the closed portion is excluded: pre-close and evening reopen data stays required.
Sources and exact ET times are saved in calendar-closures.csv each run.
-NoBuiltInClosures disables this preset. -ClosuresCsv appends verified intervals:
 StartET,EndET,Reason,Source
Use yyyy-MM-dd HH:mm. Source is optional for user CSVs. Never add a whole bank
holiday unless equity futures were actually closed for that entire interval.
AvailableFromET defaults to 2025-09-15 00:00 based on your reported provider limit.
Earlier missing data moves to unavailable-history.csv, but coverage remains
incomplete for sessions that depend on it. Use -AvailableFromET to change it.
The November 2025 CME outage is not auto-excluded without exact verified boundaries.

Optional ContractsCsv: Contract,From,To (yyyy-MM-dd). Defaults preserve your ES/NQ
research allocation, not official roll dates. Same-contract warm-up is required.
Optional -HashFiles -CompareInventory 'C:\AuditPC\inventory.csv'.
v2.2 changes: sourced closed-period exclusions; provider availability reporting;
fixed export-gap grouping by warmup/assigned basis; wider console summary.
ET/end inferred mapping; extra warm-up findings separated; positive
file coverage shown per session/contract. Content verification remains separate.
Outputs: coverage-summary.csv, file-session-summary.csv, warmup-review.csv, required-files.csv, required-sessions.csv, inventory.csv, sessions.csv,
exports.csv, gaps.csv, issues.csv, download-checklist.csv/txt, summary.txt.
New uniquely named report folder every run. No COMPLETE / READY certification.
Historical db/tick and db/minute only; db/replay Market Replay files not validated.
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
 [switch]$HashFiles,
 [ValidateSet('ES','NQ')][string[]]$Symbols = @('ES','NQ'),
 [ValidateRange(0,30)][int]$WarmupSessions = 3,
 [string]$NcdTimeZoneId = 'Eastern Standard Time',
 [ValidateSet('Start','End')][string]$TickFileHourLabel = 'End',
 [ValidateSet('CloseDate','BarStartDate')][string]$MinuteFileDateLabel = 'CloseDate',
 [switch]$ConfirmNcdMapping,
 [string]$ClosuresCsv,
 [switch]$NoBuiltInClosures,
 [datetime]$AvailableFromET = '2025-09-15 00:00' 
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Write-Host 'NinjaTrader Data Audit v2.2 - hourly tick checks + minute content gaps + overnight dependencies' -ForegroundColor Cyan
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
 $raw=@(foreach($symbol in $Symbols) {
  [pscustomobject]@{Contract="$symbol 12-25";From='2025-09-15';To='2025-12-12'}
  [pscustomobject]@{Contract="$symbol 03-26";From='2025-12-15';To='2026-03-13'}
  [pscustomobject]@{Contract="$symbol 06-26";From='2026-03-16';To='2026-06-12'}
  [pscustomobject]@{Contract="$symbol 09-26";From='2026-06-15';To='2026-09-11'}
  [pscustomobject]@{Contract="$symbol 12-26";From='2026-09-14';To='2026-12-11'}
  [pscustomobject]@{Contract="$symbol 03-27";From='2026-12-14';To='2027-03-12'}
 })
}
$contracts=@(foreach($r in $raw) {
 if ($r.Contract -notmatch '^(ES|NQ) \d{2}-\d{2}$') { throw "Invalid contract: $($r.Contract)" }
 $a=[datetime]::ParseExact($r.From,'yyyy-MM-dd',$ci); $b=[datetime]::ParseExact($r.To,'yyyy-MM-dd',$ci)
 if($a -gt $b) {throw "Reversed window: $($r.Contract)"}
 [pscustomobject]@{Contract=$r.Contract;Symbol=$r.Contract.Split(' ')[0];From=$a;To=$b}
})
$contracts=@($contracts | Where-Object {$_.Symbol -in $Symbols})
if($contracts.Count -eq 0){throw 'No selected contracts.'}
if (@($contracts.Contract | Select-Object -Unique).Count -ne $contracts.Count) {throw 'Duplicate contract rows.'}
foreach($symbol in $Symbols) {
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
    $fmt=switch($stamp.Length){8 {'yyyyMMdd'}; 12 {'yyyyMMddHHmm'}; 14 {'yyyyMMddHHmmss'}}
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
}
SaveCsv $inventory 'inventory.csv' @('Machine','RelativePath','Contract','Interval','PriceType','FileDate','RawStamp','Bytes','LastWriteUtc','SHA256','Problem')
if($CompareInventory) {
 $other=@{};foreach($r in (Import-Csv -LiteralPath $CompareInventory)){$other[$r.RelativePath]=$r}
 $here=@{};foreach($r in $inventory){$here[$r.RelativePath]=$r}
 $comparison=@(foreach($key in @(@($other.Keys)+@($here.Keys) | Sort-Object -Unique)) {
  $status=if(-not $here.ContainsKey($key)){'ONLY_OTHER'}elseif(-not $other.ContainsKey($key)){'ONLY_HERE'}elseif($here[$key].Problem -or $other[$key].Problem){'READ_OR_FORMAT_ISSUE'}elseif($here[$key].SHA256 -and $other[$key].SHA256){if($here[$key].SHA256 -eq $other[$key].SHA256){'HASH_MATCH'}else{'HASH_DIFF'}}elseif([long]$here[$key].Bytes -ne [long]$other[$key].Bytes){'SIZE_DIFF'}else{'SAME_SIZE_UNVERIFIED'}
  [pscustomobject]@{RelativePath=$key;Status=$status}
 })
 SaveCsv $comparison 'comparison.csv' @('RelativePath','Status')
}

# NCD storage mapping is explicit and is NEVER automatically certified from names.
# Confirm against a known hourly file and NT's Historical Data view before using
# -ConfirmNcdMapping. ET/end defaults match this supplied inventory; other installations need verification.
try {$storageZone=[TimeZoneInfo]::FindSystemTimeZoneById($NcdTimeZoneId)}
catch {
 if($NcdTimeZoneId -eq 'Eastern Standard Time'){$storageZone=[TimeZoneInfo]::FindSystemTimeZoneById('America/New_York')}
 else {throw "Unknown NcdTimeZoneId: $NcdTimeZoneId"}
}
$mappingStatus=if($ConfirmNcdMapping){'USER_CONFIRMED'}else{'INFERRED_MAPPING_NOT_CONTENT_VERIFIED'}
if(-not $ConfirmNcdMapping){Write-Warning 'NCD mapping is unconfirmed: hourly filename findings are candidates. Native UTC text exports provide the independent content check. See script help.'}
# Versioned ES/NQ equity schedule for this research period only.
# All timestamps below are ET, converted from the cited CT notices. These are
# CLOSED intervals, not dates on which we disable trading for an entire day.
# No outage interval or future-year holiday is guessed.
$calendarRaw=@()
if(-not $NoBuiltInClosures){
 $calendarRaw=@(@'
StartET,EndET,Reason,Source
2025-11-27 13:00,2025-11-27 18:00,Thanksgiving equity halt,https://www.ampfutures.com/news/holiday-trading-schedule-thanksgiving
2025-11-28 13:15,2025-11-28 17:00,Post-Thanksgiving equity early close,https://www.ampfutures.com/news/holiday-trading-schedule-thanksgiving
2025-12-24 13:15,2025-12-25 18:00,Christmas equity close and holiday,https://www.ampfutures.com/news/christmas-holiday-trading-schedule
2025-12-31 17:00,2026-01-01 18:00,New Year equity holiday,https://www.ampfutures.com/news/new-year-holiday-trading-schedule
2026-01-19 13:00,2026-01-19 18:00,MLK equity halt,https://www.ironbeam.com/dr-martin-luther-king-jr-holiday-trading-schedule/
2026-02-16 13:00,2026-02-16 18:00,Presidents Day equity halt,https://www.ampfutures.com/news/holiday-trading-schedule-presidents-day
2026-04-03 09:15,2026-04-03 17:00,Good Friday equity early close,https://community.optimusfutures.com/t/notice-good-friday-easter-holiday-schedule-april-3rd-2026/11698
2026-05-25 13:00,2026-05-25 18:00,Memorial Day equity halt,https://www.ironbeam.com/memorial-day-2026-futures-trading-hours/
2026-06-19 13:00,2026-06-19 17:00,Juneteenth equity early close,https://www.ironbeam.com/juneteenth-2026-futures-trading-schedule/
2026-07-03 13:00,2026-07-03 17:00,Independence Day equity early close,https://www.ironbeam.com/independence-day-2026-futures-trading-schedule/
2026-09-07 13:00,2026-09-07 18:00,Labor Day equity halt,https://edgeclear.com/exchange-holiday-hours/
'@ | ConvertFrom-Csv)
 if($From -lt [datetime]'2025-09-15' -or $To -gt [datetime]'2026-09-11'){
  Issue 'CalendarScopeReview' 'Built-in holiday coverage only 2025-09-15 through 2026-09-11. Add verified ClosuresCsv for other dates.'
 }
}
if($ClosuresCsv){$calendarRaw+=@(Import-Csv -LiteralPath $ClosuresCsv)}
$closures=@(foreach($r in $calendarRaw){
 $cs=[datetime]::ParseExact($r.StartET,'yyyy-MM-dd HH:mm',$ci)
 $ce=[datetime]::ParseExact($r.EndET,'yyyy-MM-dd HH:mm',$ci)
 if($ce -le $cs){throw 'Closure EndET must follow StartET.'}
 $source=if($r.PSObject.Properties.Name -contains 'Source'){$r.Source}else{'User supplied ClosuresCsv'}
 [pscustomobject]@{Start=$cs;End=$ce;Reason=$r.Reason;Source=$source}
})
$calendarReport=@($closures | ForEach-Object {[pscustomobject]@{StartET=$_.Start.ToString('yyyy-MM-dd HH:mm');EndET=$_.End.ToString('yyyy-MM-dd HH:mm');Reason=$_.Reason;Source=$_.Source}})
SaveCsv $calendarReport 'calendar-closures.csv' @('StartET','EndET','Reason','Source')
function IsClosed([datetime]$t){
 foreach($cl in $closures){if($t -ge $cl.Start -and $t -lt $cl.End){return $true}}
 return $false
}
function StorageTime([datetime]$t){
 $utc=[TimeZoneInfo]::ConvertTimeToUtc([datetime]::SpecifyKind($t,[DateTimeKind]::Unspecified),$et)
 return [TimeZoneInfo]::ConvertTimeFromUtc($utc,$storageZone)
}
$windows=[Collections.Generic.List[object]]::new()
foreach($c in $contracts){
 $a=if($c.From -gt $From){$c.From}else{$From};$b=if($c.To -lt $To){$c.To}else{$To}
 if($a -gt $b){continue}
 while($a.DayOfWeek -in @('Saturday','Sunday')){$a=$a.AddDays(1)}
 if($a -gt $b){continue}
 $begin=$a
 for($i=0;$i -lt $WarmupSessions;$i++){
  $begin=$begin.AddDays(-1)
  while($begin.DayOfWeek -in @('Saturday','Sunday')){$begin=$begin.AddDays(-1)}
 }
 for($d=$begin;$d -le $b;$d=$d.AddDays(1)){
  if($d.DayOfWeek -in @('Saturday','Sunday')){continue}
  $basis=if($d -lt $a){'WarmupDependency'}else{'AssignedTradingDay'}
  $windows.Add([pscustomobject]@{Contract=$c.Contract;DateET=$d.ToString('yyyy-MM-dd');Basis=$basis;Start=$d.AddDays(-1).AddHours(18);End=$d.AddHours(17)})
 }
}
SaveCsv $windows 'required-sessions.csv' @('Contract','DateET','Basis','Start','End')
# Healthy Last files only. A file is a container, NOT evidence of complete records.
$healthy=@{}
foreach($r in $inventory){
 if($r.PriceType -ne 'Last' -or $r.Problem){continue}
 if(($r.Interval -eq 'tick' -and $r.RawStamp.Length -ne 12) -or ($r.Interval -eq 'minute' -and $r.RawStamp.Length -ne 8)){
  Issue 'UnsupportedContainerName' $r.RelativePath;continue
 }
 $healthy[$r.RelativePath]=$true
}
$requirements=@{}
$sessionFiles=[Collections.Generic.List[object]]::new()
foreach($w in $windows){
 $windowKeys=@{tick=[Collections.Generic.HashSet[string]]::new();minute=[Collections.Generic.HashSet[string]]::new()}
 # Iterate minutes so partial-hour exchange closures and storage-day boundaries
 # do not accidentally suppress an entire required hour. Normal window is 23h.
 for($t=$w.Start;$t -lt $w.End;$t=$t.AddMinutes(1)){
  if(IsClosed $t){continue}
  $st=StorageTime $t
  $hour=$st.AddTicks(-($st.Ticks % [TimeSpan]::TicksPerHour))
  if($TickFileHourLabel -eq 'End'){$hour=$hour.AddHours(1)}
  $minuteClose=if($MinuteFileDateLabel -eq 'BarStartDate'){StorageTime $t}else{StorageTime ($t.AddMinutes(1))}
  foreach($interval in @('tick','minute')){
   $file=if($interval -eq 'tick'){$hour.ToString('yyyyMMddHHmm')+'.Last.ncd'}else{$minuteClose.ToString('yyyyMMdd')+'.Last.ncd'}
   $key="$interval/$($w.Contract)/$file"
   [void]$windowKeys[$interval].Add($key)
   if(-not $requirements.ContainsKey($key)){
    $requirements[$key]=[pscustomobject]@{Contract=$w.Contract;Interval=$interval;DataType='Last';ExpectedRelativePath=$key;RequiredFromET=$t;RequiredThroughET=$t.AddMinutes(1);FirstSessionET=$w.DateET;Basis=$w.Basis;Mapping=$mappingStatus;Status=$(if($healthy.ContainsKey($key)){'FILE_PRESENT_CONTENT_UNVERIFIED'}else{'FILE_ABSENT_OR_UNREADABLE'})}
   }else{
    $r=$requirements[$key]
    if($w.Basis -eq 'AssignedTradingDay'){$r.Basis='AssignedTradingDay'}
    if($t -lt $r.RequiredFromET){$r.RequiredFromET=$t}
    if($t.AddMinutes(1) -gt $r.RequiredThroughET){$r.RequiredThroughET=$t.AddMinutes(1)}
   }
  }
 }
 foreach($interval in @('tick','minute')){
  $found=0
  foreach($key in $windowKeys[$interval]){if($healthy.ContainsKey($key)){$found++}}
  $expected=$windowKeys[$interval].Count
  $sessionFiles.Add([pscustomobject]@{Contract=$w.Contract;SessionET=$w.DateET;Basis=$w.Basis;Interval=$interval;ExpectedFiles=$expected;PresentFiles=$found;MissingFiles=($expected-$found);Mapping=$mappingStatus;Status=$(if($expected -eq 0){'EXCLUDED_BY_CALENDAR'}elseif($found -eq $expected){'ALL_EXPECTED_FILES_PRESENT_CONTENT_UNVERIFIED'}else{'FILE_GAPS_REVIEW_MAPPING_AND_CALENDAR'})})
 }
}
SaveCsv $sessionFiles 'file-session-summary.csv' @('Contract','SessionET','Basis','Interval','ExpectedFiles','PresentFiles','MissingFiles','Mapping','Status')
$fileChecks=@($requirements.Values | Sort-Object -Property @('Contract','RequiredFromET','Interval'))
SaveCsv $fileChecks 'required-files.csv' @('Contract','Interval','DataType','ExpectedRelativePath','RequiredFromET','RequiredThroughET','FirstSessionET','Basis','Mapping','Status')
$actions=[Collections.Generic.List[object]]::new()
foreach($r in $fileChecks){
 if($r.Status -eq 'FILE_PRESENT_CONTENT_UNVERIFIED'){continue}
 $actions.Add([pscustomobject]@{Contract=$r.Contract;Interval=$r.Interval;Basis=$r.Basis;DataType='Last';FromET=$r.RequiredFromET;ThroughET=$r.RequiredThroughET;Reason='Missing or unreadable NCD container';Action=$(if($ConfirmNcdMapping){'DOWNLOAD / CHECK CALENDAR'}else{'VERIFY FILENAME MAPPING THEN DOWNLOAD'});File=$r.ExpectedRelativePath})
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
}

# Content verification runs independently of the assumed NCD filename mapping.
# Absence of an export is UNVERIFIED, never "complete" or a proven data hole.
foreach($w in $windows){
 foreach($interval in @('Tick','Minute')){
  $key="$($w.Contract)|$interval"
  $hasExport=$ExportManifest -and $buckets.ContainsKey($key)
  $expected=0;$present=0;$missing=0;$gapStart=$null;$lastEnd=$null
  for($t=$w.Start;$t -lt $w.End;$t=$t.AddMinutes(1)){
   if(IsClosed $t){continue}
   $expected++
   $close=$t.AddMinutes(1)
   if(-not $hasExport){continue}
   if($buckets[$key].Contains($close.Ticks)){$present++;continue}
   $missing++
   $window=if($t -lt ([datetime]$w.DateET).AddHours(3)){'Overnight'}elseif($t -lt ([datetime]$w.DateET).AddHours(9.5)){'Premarket'}else{'RTH/ETH context'}
   $gaps.Add([pscustomobject]@{Contract=$w.Contract;Interval=$interval;SessionET=$w.DateET;Basis=$w.Basis;Window=$window;MissingMinuteCloseET=$close.ToString('yyyy-MM-dd HH:mm:ss')})
  }
  $status=if($expected -eq 0){'EXCLUDED_BY_EXPLICIT_CALENDAR'}elseif(-not $hasExport){'NOT_EXPORTED_CONTENT_UNVERIFIED'}elseif($missing -gt 0){'GAPS_REVIEW_CALENDAR_EXPORT_SCOPE_AND_SOURCE'}else{'MINUTE_OCCUPANCY_PRESENT_NOT_ALL_TICKS_VERIFIED'}
  $sessions.Add([pscustomobject]@{Contract=$w.Contract;DateET=$w.DateET;Basis=$w.Basis;Interval=$interval;ExpectedMinutes=$expected;PresentMinutes=$(if($hasExport){$present}else{''});MissingMinutes=$(if($hasExport){$missing}else{''});Status=$status})
 }
}
# Coalesce consecutive missing minutes. Do not hide gaps inside existing NCDs.
foreach($group in ($gaps | Group-Object -Property @('Contract','Interval','Basis'))){
 $ordered=@($group.Group | Sort-Object MissingMinuteCloseET -Unique)
 $start=$null;$end=$null;$contract='';$interval=''
 foreach($g in $ordered){
  $t=[datetime]::ParseExact($g.MissingMinuteCloseET,'yyyy-MM-dd HH:mm:ss',$ci)
  if($null -ne $end -and $t -eq $end.AddMinutes(1)){$end=$t;continue}
  if($null -ne $start){$actions.Add([pscustomobject]@{Contract=$contract;Interval=$interval;Basis=$group.Group[0].Basis;DataType='Last';FromET=$start.AddMinutes(-1);ThroughET=$end;Reason='No valid exported records in minute buckets';Action='VERIFY EXPORT SCOPE / CALENDAR; RE-DOWNLOAD IF MISSING';File=''})}
  $start=$t;$end=$t;$contract=$g.Contract;$interval=$g.Interval
 }
 if($null -ne $start){$actions.Add([pscustomobject]@{Contract=$contract;Interval=$interval;Basis=$group.Group[0].Basis;DataType='Last';FromET=$start.AddMinutes(-1);ThroughET=$end;Reason='No valid exported records in minute buckets';Action='VERIFY EXPORT SCOPE / CALENDAR; RE-DOWNLOAD IF MISSING';File=''})}
}
SaveCsv $exportStats 'exports.csv' @('Contract','Interval','Path','Rows','BadRows','OutOfOrder','DuplicateMinuteBuckets','FirstUtc','LastUtc')
SaveCsv $sessions 'sessions.csv' @('Contract','DateET','Basis','Interval','ExpectedMinutes','PresentMinutes','MissingMinutes','Status')
SaveCsv $gaps 'gaps.csv' @('Contract','Interval','SessionET','Basis','Window','MissingMinuteCloseET')
$unavailable=@($actions | Where-Object {$_.ThroughET -le $AvailableFromET} | ForEach-Object {
 [pscustomobject]@{Contract=$_.Contract;Interval=$_.Interval;Basis=$_.Basis;FromET=$_.FromET.ToString('yyyy-MM-dd HH:mm');ThroughET=$_.ThroughET.ToString('yyyy-MM-dd HH:mm');Status='UNAVAILABLE_HISTORY_NOT_MARKET_CLOSURE';ExpectedRelativePath=$_.File}
})
SaveCsv $unavailable 'unavailable-history.csv' @('Contract','Interval','Basis','FromET','ThroughET','Status','ExpectedRelativePath')
# Keep the original missing coverage counts. An unavailable dependency still means
# the session is not fully supported, even though re-download cannot repair it.
$actions=@($actions | Where-Object {$_.ThroughET -gt $AvailableFromET})
$checklist=@($actions | Sort-Object -Property @('Contract','FromET','Interval') | ForEach-Object {
 [pscustomobject]@{Contract=$_.Contract;Basis=$_.Basis;DownloadFromET=$_.FromET.ToString('yyyy-MM-dd');DownloadThroughET=$_.ThroughET.ToString('yyyy-MM-dd');MissingFromET=$_.FromET.ToString('yyyy-MM-dd HH:mm');MissingThroughET=$_.ThroughET.ToString('yyyy-MM-dd HH:mm');SelectIntervals=$_.Interval;SelectDataType=$_.DataType;Action=$_.Action;Reason=$_.Reason;ExpectedRelativePath=$_.File}
})
$warmupChecklist=@($checklist | Where-Object Basis -eq 'WarmupDependency')
$checklist=@($checklist | Where-Object Basis -ne 'WarmupDependency')
SaveCsv $warmupChecklist 'warmup-review.csv' @('Contract','Basis','DownloadFromET','DownloadThroughET','MissingFromET','MissingThroughET','SelectIntervals','SelectDataType','Action','Reason','ExpectedRelativePath')
SaveCsv $checklist 'download-checklist.csv' @('Contract','DownloadFromET','DownloadThroughET','MissingFromET','MissingThroughET','SelectIntervals','SelectDataType','Action','Reason','ExpectedRelativePath')
SaveCsv $issues 'issues.csv' @('Scope','Detail')
$display=@(
 'MISSING DATA / REVIEW CHECKLIST - v2.2'
 'All download dates and missing intervals below are Eastern Time; convert if NT uses another display timezone.'
 'Required prior evenings (including Sunday for Monday) are included. Extra warm-up history is separate in warmup-review.csv.'
 "NCD storage mapping: $NcdTimeZoneId / $TickFileHourLabel / minute-$MinuteFileDateLabel / $mappingStatus"
 'Verified scheduled closures are excluded; see calendar-closures.csv. Remaining gaps are not automatically holidays.'
 "Unavailable history before $($AvailableFromET.ToString('yyyy-MM-dd HH:mm')) ET is separate in unavailable-history.csv. Coverage counts retain the missing dependencies."
 ''
)
if($checklist.Count){$display+=($checklist | Format-Table -Property @('Contract','MissingFromET','MissingThroughET','SelectIntervals','SelectDataType','Action') -AutoSize | Out-String -Width 230)}
else{$display+='No missing containers or exported minute buckets detected within configured scope. This is NOT a certification of every tick or of strategy readiness.'}
$display | Set-Content -LiteralPath (Join-Path $run 'download-checklist.txt') -Encoding UTF8
$display | ForEach-Object {Write-Host $_}
Write-Host 'FILE COVERAGE BY CONTRACT - assigned trading sessions only; content is still unverified' -ForegroundColor Cyan
$coverage=@(foreach($c in $contracts){
 $ticks=@($sessionFiles | Where-Object {$_.Contract -eq $c.Contract -and $_.Basis -eq 'AssignedTradingDay' -and $_.Interval -eq 'tick'})
 $minutes=@($sessionFiles | Where-Object {$_.Contract -eq $c.Contract -and $_.Basis -eq 'AssignedTradingDay' -and $_.Interval -eq 'minute'})
 if($ticks.Count -eq 0){continue}
 [pscustomobject]@{Contract=$c.Contract;Sessions=$ticks.Count;TickFilesAllPresent=@($ticks | Where-Object Status -eq 'ALL_EXPECTED_FILES_PRESENT_CONTENT_UNVERIFIED').Count;TickSessionsWithGaps=@($ticks | Where-Object {$_.MissingFiles -gt 0}).Count;MinuteFilesAllPresent=@($minutes | Where-Object Status -eq 'ALL_EXPECTED_FILES_PRESENT_CONTENT_UNVERIFIED').Count}
})
$coverage | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
SaveCsv $coverage 'coverage-summary.csv' @('Contract','Sessions','TickFilesAllPresent','TickSessionsWithGaps','MinuteFilesAllPresent')
Write-Host "Additional warm-up review rows: $($warmupChecklist.Count). These are not reassigned trading dates." -ForegroundColor Yellow
$unverified=@($sessions | Where-Object Status -eq 'NOT_EXPORTED_CONTENT_UNVERIFIED').Count
Write-Host "Sessions/intervals without content exports: $unverified. See sessions.csv; file presence is not completeness." -ForegroundColor Yellow
@"
NinjaTrader data audit v2.2 - $(Get-Date -Format o)
Database: $DbRoot
Requested ET trading dates: $($From.ToString('yyyy-MM-dd')) through $($To.ToString('yyyy-MM-dd'))
Symbols: $($Symbols -join ','). Warm-up sessions per contract: $WarmupSessions
Normal scope: previous calendar day 18:00 through session day 17:00 ET.
Weekend trading dates excluded. Sunday evenings included for Monday.
Built-in ES/NQ holiday intervals enabled: $(-not $NoBuiltInClosures). Additional closures: $ClosuresCsv.
See calendar-closures.csv for exact closed intervals and sources. Open holiday hours remain required.
AvailableFromET: $AvailableFromET. Missing earlier history is unavailable, NOT complete or closed.
November 2025 exchange outage remains review-only; exact outage boundaries are not assumed.
Full ETH context is checked conservatively, including 16:00-17:00 for indicators.
Warm-up is configurable, NOT proof of EMA convergence or exact live initialization.
NCD mapping: $NcdTimeZoneId / $TickFileHourLabel / minute-$MinuteFileDateLabel / $mappingStatus
ET/end mapping is inferred from supplied inventory, not certified from binary records.
required-files.csv lists every expected hourly tick and daily minute container.
Present files are CONTENT_UNVERIFIED; NCD binary contents are not decoded.
Native UTC Last exports provide separate one-minute occupancy and OHLC checks.
Exports supplied: $ExportManifest
Sessions/intervals without exports: $unverified
Missing/review checklist rows: $($checklist.Count)
Assigned-session file/export findings appear in download-checklist.csv/txt.
Extra warm-up findings appear separately in warmup-review.csv.
coverage-summary.csv and file-session-summary.csv show positive file coverage.
Absent ticks in a minute may mean inactivity, a halt, missing source data, or export
scope that did not include the window. Verify before treating it as a source hole.
One tick in a minute does not prove ALL ticks exist. Minute OHLC consistency with
independent sources, bid/ask completeness, and price truth are not certified.
Inspect exports.csv for invalid rows, out-of-order rows and duplicate minute bars.
A normal session bucket count does NOT override these integrity findings.
Last is a price type: required combinations are Tick+Last and Minute+Last.
Market Replay db/replay files and their contents are NOT audited by this script.
This audits historical db/tick and db/minute data, not strategy code or live feed.
Run with NT closed for stable byte/hash comparisons. No source file is modified.
Keep full-session data on the SAME contract at rollover, including prior evening.
Use ContractsCsv to match actual research allocations. No files are misplaced.
"@ | Set-Content -LiteralPath (Join-Path $run 'summary.txt') -Encoding UTF8
Write-Host "Reports: $run" -ForegroundColor Cyan
