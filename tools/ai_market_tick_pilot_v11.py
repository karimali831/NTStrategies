#!/usr/bin/env python3
"""V11: streaming, read-only NinjaTrader Last tick export -> true tick bars.

No trade prediction, no outcome labelling or backtest. Historical native
NinjaTrader Last text records: UTC yyyyMMdd HHmmss [fffffff];price;size
(with optional two extra numeric columns whose meaning is NOT assumed).
One source text line == one observed LAST update, not volume contracts.
Only original TRAIN timestamps earlier than 2025-09-01 Eastern are
decoded. No raw .ncd binary decoder; use NinjaTrader's official text
export. This pilot is single file/dated contract and source chunks must
NOT be stitched together without chronology/provenance checks.
"""
import argparse
import csv
import datetime as dt
import hashlib
import json
import math
import re
from collections import deque
from decimal import Decimal, InvalidOperation
from pathlib import Path

from zoneinfo import ZoneInfo

UTC = dt.timezone.utc
NY = ZoneInfo("America/New_York")
CUTOFF_ET = dt.datetime(2025, 9, 1, tzinfo=NY)
CUTOFF_UTC = CUTOFF_ET.astimezone(UTC)
TICK = Decimal("0.25")
NATIVE_TS = re.compile(r"^(\d{8}) (\d{6})(?: (\d{7}))?$")
CONTRACT_RE = re.compile(r"^ES \d{2}-\d{2}$")
MAX_GAP_SECONDS = 1200
FIELDS = (
    "schema","contract","barIndex","tickCount","endUtc","endEastern",
    "open","high","low","close","volume","durationSeconds","rangeTicks",
    "bodyTicks","ema21","emaSlope5","atr14","sourceFile"
)


def number(value, name, line):
    try:
        n = Decimal(value.strip())
    except (InvalidOperation, ValueError):
        raise ValueError(f"line {line}: invalid numeric {name}")
    if not n.is_finite():
        raise ValueError(f"line {line}: nonfinite {name}")
    return n


def parse_tick(line, index):
    """Return UTC timestamp, 100ns residual, price, reported trade size."""
    fields = line.strip().split(";")
    if len(fields) not in (3, 5):
        raise ValueError(f"line {index}: expected 3 or 5 Last tick fields")
    match = NATIVE_TS.fullmatch(fields[0].strip())
    if not match:
        raise ValueError(f"line {index}: invalid native UTC timestamp")
    prefix, clock, frac = match.groups()
    try:
        basic = dt.datetime.strptime(prefix + " " + clock, "%Y%m%d %H%M%S")
    except ValueError:
        raise ValueError(f"line {index}: invalid calendar UTC timestamp")
    tenth = 0
    if frac is not None:
        basic = basic.replace(microsecond=int(frac[:6]))
        tenth = int(frac[6])
    timestamp = basic.replace(tzinfo=UTC)
    price = number(fields[1], "Last price", index)
    size = number(fields[2], "volume", index)
    if price <= 0 or price % TICK:
        raise ValueError(f"line {index}: ES Last price not positive tick-aligned")
    if size <= 0 or size != size.to_integral_value():
        raise ValueError(f"line {index}: Last size must be positive integer")
    for extra in fields[3:]:
        if number(extra, "unclassified extra", index) < 0:
            raise ValueError(f"line {index}: negative extra")
    return timestamp, tenth, price, int(size), len(fields)


class TickBarBuilder:
    """Build 2000 RECORD tick bars, reset across >20m source gaps.

    Features use only the completed current tick bar and historical
    completed tick bars, never future OHLC or outcome labels.
    """
    def __init__(self, ticks_per_bar, contract, filename):
        if ticks_per_bar < 2:
            raise ValueError("ticks_per_bar must be at least 2")
        self.ticks_per_bar = ticks_per_bar
        self.contract = contract
        self.filename = filename
        self.clear_partial()
        self.close_history = deque(maxlen=6)
        self.tr_history = deque(maxlen=14)
        self.previous_close = None
        self.ema = None
        self.number = 0
        self.completed = 0
        self.resets = 0
        self.partial_discarded = 0

    def clear_partial(self):
        self.n = 0
        self.volume = 0
        self.open = self.high = self.low = self.close = None
        self.first_time = self.last_time = None

    def reset_gap(self):
        self.partial_discarded += self.n
        self.clear_partial()
        self.close_history.clear()
        self.tr_history.clear()
        self.previous_close = None
        self.ema = None
        self.resets += 1

    def append(self, timestamp, price, size):
        if self.n == 0:
            self.first_time = timestamp
            self.open = self.high = self.low = price
        self.last_time = timestamp
        self.high = max(self.high, price)
        self.low = min(self.low, price)
        self.close = price
        self.volume += size
        self.n += 1
        if self.n < self.ticks_per_bar:
            return None
        self.number += 1
        self.completed += 1
        current = float(self.close)
        previous = self.ema
        self.ema = current if self.ema is None else self.ema + (
            2.0 / 22.0) * (current - self.ema)
        self.close_history.append(self.ema)
        tr = float(self.high - self.low)
        if self.previous_close is not None:
            tr = max(tr, abs(float(self.high) - self.previous_close),
                     abs(float(self.low) - self.previous_close))
        self.tr_history.append(tr)
        slope = (self.close_history[-1] - self.close_history[0]
                 if len(self.close_history) == 6 else None)
        et = self.last_time.astimezone(NY)
        row = {
            "schema":"ninjex-2000tickbar-pilot-v11",
            "contract":self.contract,
            "barIndex":self.number,
            "tickCount":self.n,
            "endUtc":self.last_time.isoformat(),
            "endEastern":et.isoformat(),
            "open":str(self.open), "high":str(self.high),
            "low":str(self.low), "close":str(self.close),
            "volume":self.volume,
            "durationSeconds":round((self.last_time-self.first_time).total_seconds(),6),
            "rangeTicks":int((self.high-self.low)/TICK),
            "bodyTicks":float((self.close-self.open)/TICK),
            "ema21":round(self.ema,6),
            "emaSlope5":round(slope,6) if slope is not None else "",
            "atr14":round(sum(self.tr_history)/14,6)
                if len(self.tr_history)==14 else "",
            "sourceFile":self.filename,
        }
        self.previous_close = current
        self.clear_partial()
        return row


def ingest(source, contract, output, ticks_per_bar=2000, limit_rows=None):
    if not CONTRACT_RE.fullmatch(contract):
        raise ValueError("Pilot requires dated ES contract like ES 12-24")
    if limit_rows is not None and limit_rows < 1:
        raise ValueError("limit_rows must be positive")
    if not source.is_file():
        raise ValueError("Native Last export text file not found")
    if source.suffix.lower() == ".ncd":
        raise ValueError("Binary .ncd is NOT supported; export Tick Last to text in NinjaTrader")
    output.mkdir(parents=True, exist_ok=True)
    safe = contract.replace(" ","_")
    csv_path = output / f"{safe}-{ticks_per_bar}tick-train-v11.csv"
    report_path = output / f"{safe}-{ticks_per_bar}tick-audit-v11.json"
    # Never publish an incomplete CSV as a successful tick-bar artifact.
    partial_csv_path = output / f"{safe}-{ticks_per_bar}tick-train-v11.partial"
    builder=TickBarBuilder(ticks_per_bar,contract,source.name)
    count=0
    short_rows=0
    full_rows=0
    last_key=None
    last_dt=None
    end_guard=False
    pilot_limit=False
    largest_gap=0.
    prefix_hash=hashlib.sha256()
    first_utc=None
    last_utc=None
    # Source is assumed chronological. On a boundary timestamp, check
    # the timestamp ONLY, never price/size; later source bytes are not
    # parsed or included in the accepted-record prefix hash.
    with source.open("rb") as raw, partial_csv_path.open("w",newline="",encoding="utf-8") as dest:
        writer=csv.DictWriter(dest,fieldnames=FIELDS)
        writer.writeheader()
        for line_num, binary in enumerate(raw, 1):
            if not binary.strip():
                continue
            try:
                decoded=binary.decode("utf-8-sig")
            except UnicodeDecodeError:
                raise ValueError(f"line {line_num}: not UTF-8 native text")
            # Determine the date boundary BEFORE parsing later prices.
            stamp_field=decoded.split(";",1)[0].strip()
            ts_match=NATIVE_TS.fullmatch(stamp_field)
            if ts_match is None:
                raise ValueError(f"line {line_num}: unexpected native timestamp")
            prefix,clock,frac=ts_match.groups()
            try:
                base=dt.datetime.strptime(prefix+" "+clock,"%Y%m%d %H%M%S")
                instant=base.replace(tzinfo=UTC)
            except ValueError:
                raise ValueError(f"line {line_num}: invalid UTC calendar")
            if instant >= CUTOFF_UTC:
                end_guard=True
                break
            timestamp,tenth,price,size,field_count=parse_tick(decoded,line_num)
            stamp_key=(timestamp,tenth)
            if last_key is not None and stamp_key < last_key:
                raise ValueError(f"line {line_num}: reversed tick timestamp")
            if last_dt is not None:
                gap=(timestamp-last_dt).total_seconds()
                largest_gap=max(largest_gap,gap)
                if gap>MAX_GAP_SECONDS:
                    builder.reset_gap()
            if first_utc is None:
                first_utc=timestamp
            last_utc=timestamp
            last_dt=timestamp
            last_key=stamp_key
            prefix_hash.update(binary)
            count+=1
            short_rows+=int(field_count==3)
            full_rows+=int(field_count==5)
            row=builder.append(timestamp,price,size)
            if row is not None:
                writer.writerow(row)
            if limit_rows is not None and count>=limit_rows:
                pilot_limit=True
                break
    if count==0:
        partial_csv_path.unlink(missing_ok=True)
        raise ValueError("No TRAIN ticks in input; provide an older contract/sample")
    # Source reached a valid last observation; now atomically publish the
    # complete output. A parser failure leaves only the .partial artifact.
    partial_csv_path.replace(csv_path)
    report={
        "schema":"ninjex-ai-tick-pilot-v11",
        "status":"SOURCE_TICKS_DECODED_NO_TRADING_MODEL",
        "contract":contract,
        "source":str(source),
        "sourceRecordPrefixSha256":prefix_hash.hexdigest(),
        "recordPrefixHashNotFullFile":bool(end_guard or pilot_limit),
        "trainingBoundaryEt":"2025-09-01T00:00 America/New_York",
        "tickBarSizeRecords":ticks_per_bar,
        "ticksRead":count,
        "threeFieldRecords":short_rows,
        "fiveFieldRecordsUnclassifiedExtras":full_rows,
        "firstUtc":first_utc.isoformat(),
        "lastUtc":last_utc.isoformat(),
        "sourceMaxGapSeconds":round(largest_gap,3),
        "resetsForSourceGaps":builder.resets,
        "discardedPartialBarTicks":builder.partial_discarded+builder.n,
        "completeTickBars":builder.completed,
        "stoppedBeforeValidationOrHoldout":end_guard,
        "pilotRowLimitApplied":pilot_limit,
        "tickBarsCsv":str(csv_path),
        "warnings":[
            "NinjaTrader exported LAST record is NOT a verified bid/ask aggressor-trade stream.",
            "Native 5-field extras are deliberately UNCLASSIFIED until their provenance and meaning are verified.",
            "One exported Last record counts as one tick; volume is NOT used to choose bar size.",
            "Single-file pilot: does not join dated contracts, files or missing sessions, and does not audit exchange-day coverage.",
            "Tick bar EMA21 is not directly comparable to the earlier completed 5m EMA21.",
            "No strategy, ML inference, forward label, trade execution, or prop trailing drawdown is computed.",
            "No validation or final holdout prices were decoded. Only timestamp of first boundary row may be inspected."
        ],
    }
    report_path.write_text(json.dumps(report,indent=2),encoding="utf-8")
    return report


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--input",required=True,type=Path,help="native Last tick text export, not .ncd")
    p.add_argument("--contract",required=True,help="dated contract, e.g. ES 12-24")
    p.add_argument("--output-dir",required=True,type=Path)
    p.add_argument("--ticks-per-bar",type=int,default=2000)
    p.add_argument("--limit-rows",type=int,default=None,
                   help="optional pilot limit; explicitly marks report truncated")
    a=p.parse_args()
    r=ingest(a.input,a.contract,a.output_dir,a.ticks_per_bar,a.limit_rows)
    print("[NinjexAI-TICK] Last rows:",r["ticksRead"])
    print("[NinjexAI-TICK] Complete tick-count bars:",r["completeTickBars"])
    print("[NinjexAI-TICK] Output:",r["tickBarsCsv"])
    return 0


if __name__=="__main__":
    raise SystemExit(main())
