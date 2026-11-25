#!/usr/bin/env python3
"""Ninjex AI historical OHLCV research data quality gate. Stdlib + tzdata on Windows."""
import argparse
import csv
import datetime as dt
import json
import math
from pathlib import Path
from zoneinfo import ZoneInfo

FRAMES = (1, 5, 15, 60)
UTC = dt.timezone.utc
ET = ZoneInfo("America/New_York")
REQUIRED = {"schemaVersion", "contract", "frameMinutes", "endUtc", "endEastern", "utcOffsetMinutes", "open", "high", "low", "close", "volume", "flags"}


def exchange_close(et, frame):
    """Normal ES ETH close schedule; early closures and holidays need explicit review."""
    dow, hh, mm = et.weekday(), et.hour, et.minute
    if dow == 5 or (dow == 6 and (hh < 18 or (hh == 18 and mm == 0))):
        return False
    if dow == 4 and (hh > 17 or (hh == 17 and mm > 0)):
        return False
    if dow in (0, 1, 2, 3) and (hh == 17 and mm > 0 or hh == 18 and mm == 0):
        return False
    if dow in (0, 1, 2, 3) and hh == 17 and mm > 0:
        return False
    return (hh * 60 + mm) % frame == 0


def auditable_slots(start, end, frame):
    # Iterating in UTC avoids fall-back duplicates in ET local time.
    utc = start.astimezone(UTC)
    end_utc = end.astimezone(UTC)
    while utc <= end_utc:
        if exchange_close(utc.astimezone(ET), frame):
            yield utc
        utc += dt.timedelta(minutes=frame)


def load_rows(path, frame, contract, report):
    rows = {}
    previous = None
    with path.open(encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if not REQUIRED.issubset(reader.fieldnames or []):
            report["errors"].append(f"{path.name}: missing CSV columns")
            return rows
        for number, row in enumerate(reader, 2):
            try:
                utc = dt.datetime.fromisoformat(row["endUtc"].replace("Z", "+00:00"))
                if utc.tzinfo is None:
                    raise ValueError("UTC timestamp has no offset")
                utc = utc.astimezone(UTC)
                eastern = utc.astimezone(ET)
                local = dt.datetime.fromisoformat(row["endEastern"])
                if local.replace(tzinfo=None) != eastern.replace(tzinfo=None):
                    raise ValueError("ET/UTC mismatch")
                if int(row["utcOffsetMinutes"]) != int(eastern.utcoffset().total_seconds() / 60):
                    raise ValueError("wrong Eastern UTC offset")
                if int(row["frameMinutes"]) != frame or row["contract"] != contract:
                    raise ValueError("contract/timeframe mismatch")
                if previous is not None and utc <= previous:
                    raise ValueError("duplicated or non-monotonic UTC bar")
                previous = utc
                prices = [float(row[k]) for k in ("open", "high", "low", "close")]
                op, hi, lo, cl = prices
                vol = float(row["volume"])
                if not all(math.isfinite(x) for x in prices + [vol]) or lo <= 0 or vol < 0 or hi < max(op, cl) or lo > min(op, cl):
                    raise ValueError("invalid OHLCV")
                if not all(abs(price * 4 - round(price * 4)) < 1e-6 for price in prices):
                    raise ValueError("non-ES 0.25 tick increment")
                rows[utc] = (op, hi, lo, cl, vol)
            except (ValueError, TypeError, OverflowError) as exc:
                if len(report["errors"]) < 60:
                    report["errors"].append(f"{path.name}:{number}: {exc}")
    return rows


def audit(folder, begin_et, end_et, contract):
    report = {"schema": "ninjex-research-audit-v1", "contract": contract,
              "fromEastern": begin_et.isoformat(), "throughEastern": end_et.isoformat(),
              "frames": {}, "errors": [], "warnings": [], "result": "FAIL"}
    if end_et < begin_et:
        raise ValueError("End before start")
    manpath = folder / "manifest.json"
    try:
        manifest = json.loads(manpath.read_text(encoding="utf-8-sig"))
        if manifest.get("contract") != contract:
            report["errors"].append("Manifest contract does not match")
        if manifest.get("schemaVersion") != "ninjex-ohlcv-v1":
            report["errors"].append("Unsupported historical exporter schema")
    except (OSError, ValueError) as exc:
        report["errors"].append(f"Missing/unreadable manifest: {exc}")
        manifest = {}
    frames = {}
    for frame in FRAMES:
        f = folder / f"bars_{frame}m.csv"
        if not f.exists():
            report["errors"].append(f"Missing {f.name}")
            frames[frame] = {}
            continue
        rows = load_rows(f, frame, contract, report)
        frames[frame] = rows
        expected = set(auditable_slots(begin_et, end_et, frame))
        observed = {t for t in rows if begin_et.astimezone(UTC) <= t <= end_et.astimezone(UTC)}
        missing = sorted(expected - observed)
        extra = sorted(observed - expected)
        report["frames"][f"{frame}m"] = {
            "fileRows": len(rows), "expected": len(expected),
            "observedInWindow": len(observed), "missing": len(missing),
            "offCalendar": len(extra),
            "firstUtc": min(rows).isoformat() if rows else None,
            "lastUtc": max(rows).isoformat() if rows else None,
            "missingSampleUtc": [t.isoformat() for t in missing[:20]],
            "offCalendarSampleUtc": [t.isoformat() for t in extra[:20]]}
        if missing:
            report["errors"].append(f"{frame}m: missing {len(missing)} expected closes")
        if extra:
            report["errors"].append(f"{frame}m: {len(extra)} closes outside normal ETH calendar")
    issue_path = folder / "quality_issues.csv"
    if not issue_path.exists():
        report["errors"].append("Missing quality_issues.csv")
    else:
        with issue_path.open(encoding="utf-8-sig", newline="") as f:
            issues = list(csv.DictReader(f))
        report["qualityIssueRows"] = len(issues)
        if any(x.get("issue") in ("INVALID_OHLCV", "NON_MONOTONIC", "INVALID_ET_TIME") for x in issues):
            report["errors"].append("Historical exporter reported invalid, duplicate or DST-invalid bars")
        report["warnings"].append("Review TIME_GAP / holiday / early-close quality issues manually before ML")
    # 1m -> 5m must reconstruct exactly when five 1m constituents exist.
    one, five = frames[1], frames[5]
    checked = 0
    mismatches = []
    for t, (op, hi, lo, cl, vol) in five.items():
        if not begin_et.astimezone(UTC) <= t <= end_et.astimezone(UTC):
            continue
        members = [one.get(t - dt.timedelta(minutes=x)) for x in (4, 3, 2, 1, 0)]
        if any(x is None for x in members):
            continue
        checked += 1
        reconstructed = (members[0][0], max(x[1] for x in members),
                         min(x[2] for x in members), members[-1][3],
                         sum(x[4] for x in members))
        if any(abs(a - b) > 1e-6 for a, b in zip(reconstructed, (op, hi, lo, cl, vol))):
            if len(mismatches) < 20:
                mismatches.append(t.isoformat())
    report["oneToFive"] = {"comparisons": checked, "mismatchSamples": mismatches}
    if mismatches:
        report["errors"].append(f"1m->5m OHLCV mismatch at {len(mismatches)} sampled timestamps")
    if checked < 10:
        report["errors"].append("Insufficient 1m->5m comparison coverage")
    report["result"] = "PASS_WITH_MANUAL_CALENDAR_REVIEW" if not report["errors"] else "FAIL"
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--folder", required=True, type=Path)
    p.add_argument("--contract", default="ES 12-26")
    p.add_argument("--from-et", required=True, help="ISO Eastern timestamp, e.g. 2026-09-27T18:05")
    p.add_argument("--through-et", required=True, help="ISO Eastern timestamp, e.g. 2026-09-30T23:55")
    p.add_argument("--output", type=Path)
    a = p.parse_args()
    start, end = (dt.datetime.fromisoformat(x).replace(tzinfo=ET)
                  for x in (a.from_et, a.through_et))
    result = audit(a.folder, start, end, a.contract)
    document = json.dumps(result, indent=2)
    if a.output:
        a.output.write_text(document, encoding="utf-8")
    print(document)
    return 0 if result["result"] != "FAIL" else 1


if __name__ == "__main__":
    raise SystemExit(main())
