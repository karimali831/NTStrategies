#!/usr/bin/env python3
"""Ninjex AI research v1: create leakage-controlled, session-aware ES/NQ ML features.

Read-only: consumes existing dated-contract four-timeframe OHLCV folders
from a basket audit JSON. No synthetic bars, trained models, trading
decisions or orders. Output is a separate derived CSV and provenance JSON.

Rows use ONLY closed bars at or before the 5-minute decision timestamp.
Future returns are labels, never predictors. Any missing 5m bar / daily
maintenance break invalidates labels that would cross that discontinuity.
Poorly reconciling 15m/60m source bars are quarantined rather than used.
"""
import argparse
import bisect
import csv
import datetime as dt
import json
import math
from collections import deque, defaultdict
from dataclasses import dataclass
from pathlib import Path
from zoneinfo import ZoneInfo

UTC = dt.timezone.utc
NY = ZoneInfo("America/New_York")
LONDON = ZoneInfo("Europe/London")
TOKYO = ZoneInfo("Asia/Tokyo")
FIVE = dt.timedelta(minutes=5)

FEATURE_COLUMNS = [
    "contract", "decisionUtc", "decisionEastern", "researchSplit",
    "sessionAsia", "sessionLondon", "sessionNewYork", "sessionLondonNewYorkOverlap",
    "easternWeekday", "easternMinuteOfDay", "minutesFromNewYorkOpen",
    "close", "atr14_5m", "ema20DistanceAtr", "ema50DistanceAtr",
    "ret3Atr", "ret12Atr", "efficiency12", "volumeVsPrior20",
    "prior12HighDistanceAtr", "prior12LowDistanceAtr",
    "close15", "body15Atr", "lastClosed15Utc", "close60", "body60Atr", "lastClosed60Utc",
    "fine1mComplete", "qualityFlags",
    "label30mCloseRetAtr", "label60mCloseRetAtr",
    "label30mMaxFavourableHighAtr", "label30mMaxAdverseLowAtr"
]


@dataclass(frozen=True)
class Bar:
    end: dt.datetime
    open: float
    high: float
    low: float
    close: float
    volume: float
    flags: str


def read_bars(folder, frame, contract):
    filename = folder / ("bars_" + str(frame) + "m.csv")
    out = []
    with filename.open(newline="", encoding="utf-8-sig") as stream:
        for line, row in enumerate(csv.DictReader(stream), 2):
            if row["contract"] != contract or int(row["frameMinutes"]) != frame:
                raise ValueError(f"{filename}:{line} mismatched instrument/timeframe")
            stamp = dt.datetime.fromisoformat(row["endUtc"].replace("Z", "+00:00"))
            if stamp.utcoffset() != dt.timedelta(0):
                stamp = stamp.astimezone(UTC)
            if out and stamp <= out[-1].end:
                raise ValueError(f"{filename}:{line} duplicated or reversed UTC")
            vals = [float(row[k]) for k in ("open", "high", "low", "close", "volume")]
            if not all(math.isfinite(v) for v in vals):
                raise ValueError(f"{filename}:{line} nonfinite OHLCV")
            out.append(Bar(stamp, *vals, row.get("flags", "")))
    return out


def true_aggregated(bars):
    return (bars[0].open, max(b.high for b in bars),
            min(b.low for b in bars), bars[-1].close,
            sum(b.volume for b in bars))


def validate_higher(higher_bars, lower_map, lower_minutes, high_minutes):
    """Reject partial/unmatched HTF bars (not fake them into full hours)."""
    valid = []
    rejected = []
    n = high_minutes // lower_minutes
    for bar in higher_bars:
        # The historical weekly/session shortened 60m bars can close at
        # odd minutes. We deliberately exclude them from v1 HTF features.
        if (bar.end.hour * 60 + bar.end.minute) % high_minutes:
            rejected.append((bar.end, "OFF_ALIGNED_PARTIAL"))
            continue
        start = bar.end - dt.timedelta(minutes=high_minutes)
        members = [lower_map.get(start + dt.timedelta(minutes=lower_minutes * step))
                   for step in range(1, n + 1)]
        if any(member is None for member in members):
            rejected.append((bar.end, "INCOMPLETE_LOWER_BARS"))
            continue
        observed = true_aggregated(members)
        expected = (bar.open, bar.high, bar.low, bar.close, bar.volume)
        if any(abs(a - b) > 0.00001 for a, b in zip(observed, expected)):
            rejected.append((bar.end, "OHLCV_MISMATCH"))
            continue
        valid.append(bar)
    return valid, rejected


def in_clock(time, start_minutes, stop_minutes):
    minute = time.hour * 60 + time.minute
    return start_minutes <= minute < stop_minutes


def session_flags(timestamp):
    """Exchange-independent research definitions in each city's real DST."""
    ny = timestamp.astimezone(NY)
    london = timestamp.astimezone(LONDON)
    tokyo = timestamp.astimezone(TOKYO)
    asia = in_clock(tokyo, 9 * 60, 17 * 60)
    uk = in_clock(london, 8 * 60, 16 * 60 + 30)
    us = in_clock(ny, 9 * 60 + 30, 16 * 60)
    ny_minute = ny.hour * 60 + ny.minute
    return ny, int(asia), int(uk), int(us), int(uk and us), ny_minute


def split_for(et_date):
    if et_date < dt.date(2025, 9, 1):
        return "train"
    if et_date < dt.date(2026, 6, 15):
        return "validation"
    return "holdout"


def ema_series(values, span):
    alpha = 2 / (span + 1)
    ema = None
    result = []
    for val in values:
        ema = val if ema is None else alpha * val + (1 - alpha) * ema
        result.append(ema)
    return result


def atr_series(bars, length=14):
    periods = deque()
    results = []
    running = 0.
    for idx, bar in enumerate(bars):
        prev = bars[idx - 1].close if idx else bar.close
        tr = max(bar.high - bar.low, abs(bar.high - prev), abs(bar.low - prev))
        periods.append(tr)
        running += tr
        if len(periods) > length:
            running -= periods.popleft()
        results.append(running / length if len(periods) == length else None)
    return results


def label_return(bars, index, n, atr):
    if index + n >= len(bars):
        return None
    for k in range(index + 1, index + n + 1):
        if bars[k].end - bars[k - 1].end != FIVE:
            return None
    future = bars[index + 1:index + n + 1]
    return ((future[-1].close - bars[index].close) / atr,
            (max(b.high for b in future) - bars[index].close) / atr,
            (min(b.low for b in future) - bars[index].close) / atr)


def format_float(value):
    return "" if value is None else format(value, ".10g")


def contract_rows(folder, contract, output, counters):
    minute = read_bars(folder, 1, contract)
    five = read_bars(folder, 5, contract)
    fifteen = read_bars(folder, 15, contract)
    hourly = read_bars(folder, 60, contract)
    times1 = {b.end for b in minute}
    times5 = {b.end: b for b in five}
    valid15, rejected15 = validate_higher(fifteen, times5, 5, 15)
    valid60, rejected60 = validate_higher(hourly, times5, 5, 60)
    counters["source5m"] += len(five)
    counters["rejected15mSource"] += len(rejected15)
    counters["rejected60mSource"] += len(rejected60)
    counters["rejectedPartial60m"] += sum(reason == "OFF_ALIGNED_PARTIAL"
                                        for _, reason in rejected60)
    counters["rejectedUnreconciled60m"] += sum(
        reason == "OHLCV_MISMATCH" for _, reason in rejected60)

    if not five:
        return
    atr = atr_series(five)
    ema20 = ema_series([b.close for b in five], 20)
    ema50 = ema_series([b.close for b in five], 50)
    valid15_times = [b.end for b in valid15]
    valid60_times = [b.end for b in valid60]
    prior_close = [b.close for b in five]
    gaps = [False] * len(five)
    for i in range(1, len(five)):
        gaps[i] = five[i].end - five[i - 1].end != FIVE
    recent_gap = deque()
    for i, bar in enumerate(five):
        if gaps[i]:
            recent_gap.append(i)
        while recent_gap and recent_gap[0] < i - 12:
            recent_gap.popleft()
        if i < 50 or atr[i] is None or atr[i] <= 0:
            counters["maskedIndicatorWarmup"] += 1
            continue
        if recent_gap:
            counters["maskedRecent5mGap"] += 1
            continue
        idx15 = bisect.bisect_right(valid15_times, bar.end) - 1
        idx60 = bisect.bisect_right(valid60_times, bar.end) - 1
        if idx15 < 0 or idx60 < 0:
            counters["maskedUnverifiedHigherFrame"] += 1
            continue
        b15, b60 = valid15[idx15], valid60[idx60]
        if (bar.end - b15.end > dt.timedelta(minutes=20) or
                bar.end - b60.end > dt.timedelta(minutes=70)):
            counters["maskedStaleHigherFrame"] += 1
            continue
        # All features below exist at decision time. Prior 12-bar range
        # deliberately excludes the current bar (including its high/low).
        prev12 = five[i-12:i]
        range_hi = max(b.high for b in prev12)
        range_lo = min(b.low for b in prev12)
        path = sum(abs(prior_close[j] - prior_close[j - 1])
                   for j in range(i - 11, i + 1))
        efficiency = abs(bar.close - prior_close[i - 12]) / path if path > 0 else 0.0
        avg_volume = sum(b.volume for b in five[i-20:i]) / 20
        fine_complete = all(bar.end - dt.timedelta(minutes=j) in times1
                            for j in range(5))
        ny, asia, london, us, overlap, minute_of_day = session_flags(bar.end)
        label6 = label_return(five, i, 6, atr[i])
        label12 = label_return(five, i, 12, atr[i])
        # Purge observations with outcomes in a later split, otherwise
        # the final training rows would peek into the validation period.
        current_split = split_for(ny.date())
        if i + 6 < len(five) and label6 is not None:
            if split_for(five[i+6].end.astimezone(NY).date()) != current_split:
                label6 = None
                counters["purgedCrossSplit30m"] += 1
        if i + 12 < len(five) and label12 is not None:
            if split_for(five[i+12].end.astimezone(NY).date()) != current_split:
                label12 = None
                counters["purgedCrossSplit60m"] += 1
        if label6 is None:
            counters["masked30mOutcome"] += 1
        if label12 is None:
            counters["masked60mOutcome"] += 1
        quality = []
        if not fine_complete:
            quality.append("SPARSE_1M_IN_PARENT")
            counters["flaggedSparseMinute5m"] += 1
        if "TIME_GAP" in bar.flags:
            quality.append("SOURCE_GAP_FLAG")
        data = {
            "contract": contract,
            "decisionUtc": bar.end.isoformat(),
            "decisionEastern": ny.isoformat(),
            "researchSplit": split_for(ny.date()),
            "sessionAsia": asia, "sessionLondon": london,
            "sessionNewYork": us, "sessionLondonNewYorkOverlap": overlap,
            "easternWeekday": ny.weekday(),
            "easternMinuteOfDay": minute_of_day,
            "minutesFromNewYorkOpen": minute_of_day - 570,
            "close": format_float(bar.close),
            "atr14_5m": format_float(atr[i]),
            "ema20DistanceAtr": format_float((bar.close - ema20[i]) / atr[i]),
            "ema50DistanceAtr": format_float((bar.close - ema50[i]) / atr[i]),
            "ret3Atr": format_float((bar.close - prior_close[i - 3]) / atr[i]),
            "ret12Atr": format_float((bar.close - prior_close[i - 12]) / atr[i]),
            "efficiency12": format_float(efficiency),
            "volumeVsPrior20": format_float(bar.volume / avg_volume) if avg_volume > 0 else "",
            "prior12HighDistanceAtr": format_float((bar.close - range_hi) / atr[i]),
            "prior12LowDistanceAtr": format_float((bar.close - range_lo) / atr[i]),
            "close15": format_float(b15.close),
            "lastClosed15Utc": b15.end.isoformat(),
            "body15Atr": format_float((b15.close - b15.open) / atr[i]),
            "close60": format_float(b60.close),
            "lastClosed60Utc": b60.end.isoformat(),
            "body60Atr": format_float((b60.close - b60.open) / atr[i]),
            "fine1mComplete": int(fine_complete),
            "qualityFlags": "|".join(quality) or "OK",
            "label30mCloseRetAtr": format_float(label6[0]) if label6 else "",
            "label60mCloseRetAtr": format_float(label12[0]) if label12 else "",
            "label30mMaxFavourableHighAtr": format_float(label6[1]) if label6 else "",
            "label30mMaxAdverseLowAtr": format_float(label6[2]) if label6 else "",
        }
        output.writerow(data)
        counters["outputRows"] += 1
        counters["split_" + data["researchSplit"]] += 1


def run(audit_path, output):
    report = json.loads(audit_path.read_text(encoding="utf-8-sig"))
    if report.get("schema") != "ninjex-basket-audit-v1":
        raise ValueError("Requires reviewed Ninjex 13-contract basket audit JSON")
    contracts = report.get("contracts", [])
    if not contracts:
        raise ValueError("No contracts in audit")
    if report.get("status") not in ("NEEDS_REVIEW", "PASS_WITH_QUALIFICATIONS"):
        raise ValueError("Unsupported basket audit status")
    if any(entry.get("result") in ("NOT_EXPORTED", "DUPLICATE_RUNS")
           for entry in contracts):
        raise ValueError("Cannot construct a research dataset from missing/duplicate raw contracts")
    counters = defaultdict(int)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=FEATURE_COLUMNS)
        writer.writeheader()
        for entry in contracts:
            if len(entry.get("folders", [])) != 1:
                raise ValueError("Missing/duplicate raw run for " + entry["contract"])
            folder = Path(entry["folders"][0])
            if not folder.is_dir():
                raise FileNotFoundError("Source raw folder unavailable: " + str(folder))
            manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8-sig"))
            if manifest["contract"] != entry["contract"] or manifest["datasetTag"] != report["datasetTag"]:
                raise ValueError("Dataset provenance mismatch for " + entry["contract"])
            contract_rows(folder, entry["contract"], writer, counters)
    manifest_path = output.with_suffix(".manifest.json")
    result = {
        "schema": "ninjex-ml-features-v1",
        "sourceAudit": str(audit_path),
        "sourceAuditStatus": report.get("status"),
        "datasetTag": report["datasetTag"],
        "sourceContracts": len(contracts),
        "features": FEATURE_COLUMNS,
        "counters": dict(sorted(counters.items())),
        "researchStatus": "PROVISIONAL_MASKED_NOT_TRAINED",
        "definitions": {
            "asia": "09:00-17:00 Asia/Tokyo local (may overlap other session flags)",
            "london": "08:00-16:30 Europe/London local (actual UK DST)",
            "newYork": "09:30-16:00 America/New_York local (actual US DST)",
            "labels": "30m/60m future close change / current ATR14; only if every future 5m bar is contiguous in UTC",
            "asOf": "15m and 60m CLOSED, independently reconstructed OHLCV and at/before 5m decision time",
            "splits": "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15",
            "sourceData": "Raw NinjaTrader OHLCV untouched. Missing 5m spans and their 12-bar warmup are masked.",
            "caveat": "No tick-level outcome path, trade simulation, calibrated probabilities or ML model yet.",
        }
    }
    manifest_path.write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--audit", required=True, type=Path,
                   help="13-contract es-full-basket-audit.json from local Research directory")
    p.add_argument("--output", required=True, type=Path,
                   help="Derived ML research CSV, NOT in a source raw export folder")
    a = p.parse_args()
    result = run(a.audit, a.output)
    print("[NinjexAI-ML] CSV:", a.output)
    print("[NinjexAI-ML] Manifest:", a.output.with_suffix(".manifest.json"))
    print("[NinjexAI-ML] Rows:", result["counters"].get("outputRows", 0))
    print("[NinjexAI-ML] Status:", result["researchStatus"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
