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


def auditable_slots(start, end, frame, calendar=None):
    # Iterating in UTC avoids fall-back duplicates in ET local time.
    utc = start.astimezone(UTC)
    end_utc = end.astimezone(UTC)
    # Align every timeframe independently. A 18:05 ET start is on a 5m
    # boundary but NOT a 15m/60m boundary; otherwise those timeframes
    # produce zero expected slots and falsely fail valid exports.
    remainder = (utc.hour * 60 + utc.minute) % frame
    if remainder:
        utc += dt.timedelta(minutes=frame - remainder)
    while utc <= end_utc:
        if exchange_close(utc.astimezone(ET), frame) and not (
            calendar and any(first <= utc <= last
                             for first, last in calendar["closures"])):
            yield utc
        utc += dt.timedelta(minutes=frame)
    if calendar:
        for (special_frame, special_close), _ in calendar["partial"].items():
            if special_frame == frame and start.astimezone(UTC) <= special_close <= end_utc:
                yield special_close


def load_calendar(path):
    """Explicit, sourced historical special hours. No inferred holiday closures."""
    source = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    closures = []
    partial = {}
    def utc_time(value):
        wall = dt.datetime.fromisoformat(value)
        if wall.tzinfo is not None:
            return wall.astimezone(UTC)
        return wall.replace(tzinfo=ET).astimezone(UTC)
    for item in source.get("closedWindowsEastern", []):
        first, last = utc_time(item["from"]), utc_time(item["through"])
        if last < first:
            raise ValueError("Inverted holiday closure range")
        closures.append((first, last))
    for item in source.get("partialClosesEastern", []):
        frame = int(item["frameMinutes"])
        close = utc_time(item["close"])
        previous = utc_time(item["startsAfter"])
        if frame not in FRAMES or close <= previous:
            raise ValueError("Invalid partial candle specification")
        partial[(frame, close)] = previous
    return {"closures": closures, "partial": partial,
            "calendarName": source.get("name", Path(path).name),
            "source": source.get("source", "Unspecified")}


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
                    raise ValueError("non-ES/NQ 0.25 tick increment")
                rows[utc] = (op, hi, lo, cl, vol)
            except (ValueError, TypeError, OverflowError) as exc:
                if len(report["errors"]) < 60:
                    report["errors"].append(f"{path.name}:{number}: {exc}")
    return rows


def audit(folder, begin_et, end_et, contract, calendar=None, allow_sparse_1m=False):
    report = {"schema": "ninjex-research-audit-v1", "contract": contract,
              "fromEastern": begin_et.isoformat(), "throughEastern": end_et.isoformat(),
              "frames": {}, "errors": [], "warnings": [], "result": "FAIL"}
    if end_et < begin_et:
        raise ValueError("End before start")
    if calendar:
        report["calendar"] = {"name": calendar["calendarName"], "source": calendar["source"],
                              "closures": len(calendar["closures"]),
                              "partialCloses": len(calendar["partial"])}
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
        expected_count = manifest.get("frameCounts", {}).get(f"{frame}m")
        if expected_count is not None and int(expected_count) != len(rows):
            report["errors"].append(
                f"{frame}m manifest count {expected_count} differs from CSV {len(rows)}")
        expected = set(auditable_slots(begin_et, end_et, frame, calendar))
        observed = {t for t in rows if begin_et.astimezone(UTC) <= t <= end_et.astimezone(UTC)}
        missing = sorted(expected - observed)
        extra = sorted(observed - expected)
        missing_by_eastern_date = {}
        for t in missing:
            day = t.astimezone(ET).date().isoformat()
            missing_by_eastern_date[day] = missing_by_eastern_date.get(day, 0) + 1
        report["frames"][f"{frame}m"] = {
            "fileRows": len(rows), "expected": len(expected),
            "observedInWindow": len(observed), "missing": len(missing),
            "offCalendar": len(extra),
            "firstUtc": min(rows).isoformat() if rows else None,
            "lastUtc": max(rows).isoformat() if rows else None,
            "missingByEasternDate": dict(sorted(missing_by_eastern_date.items())),
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
    # Reconcile every hierarchy, rather than only 1m->5m. As-of
    # reconstruction is exact when all lower constituent bars exist.
    report["crossFrame"] = {}
    for lower, higher in ((1, 5), (1, 15), (1, 60), (5, 15), (15, 60)):
        source, target = frames[lower], frames[higher]
        tested = 0
        mismatch_total = 0
        samples = []
        constituents = higher // lower
        for t, (op, hi, lo, cl, vol) in target.items():
            if not begin_et.astimezone(UTC) <= t <= end_et.astimezone(UTC):
                continue
            if calendar and (higher, t) in calendar["partial"]:
                special_start = calendar["partial"][(higher, t)]
                # The candle ends early at a documented exchange close;
                # compare ONLY lower bars after its true start.
                duration = int((t - special_start).total_seconds() / 60)
                if duration % lower:
                    report["errors"].append(
                        f"Invalid partial close {t.isoformat()} for {lower}m components")
                    continue
                members_count = duration // lower
            else:
                members_count = constituents
            members = [source.get(t - dt.timedelta(minutes=lower * j))
                       for j in range(members_count - 1, -1, -1)]
            if any(x is None for x in members):
                continue
            tested += 1
            reconstructed = (
                members[0][0], max(x[1] for x in members),
                min(x[2] for x in members), members[-1][3],
                sum(x[4] for x in members))
            if any(abs(x - y) > 1e-6
                   for x, y in zip(reconstructed, (op, hi, lo, cl, vol))):
                mismatch_total += 1
                if len(samples) < 20:
                    samples.append(t.isoformat())
        key = f"{lower}mTo{higher}m"
        report["crossFrame"][key] = {
            "comparisons": tested, "mismatchCount": mismatch_total,
            "mismatchSamplesUtc": samples}
        if mismatch_total:
            report["errors"].append(
                f"{lower}m->{higher}m: {mismatch_total} OHLCV mismatches")
        if (lower, higher) == (1, 5) and tested < 10:
            report["errors"].append(
                "Insufficient 1m->5m comparison coverage (fewer than 10 bars)")
    # Preserve older clients' 1m->5m summary shape.
    report["oneToFive"] = {
        "comparisons": report["crossFrame"]["1mTo5m"]["comparisons"],
        "mismatchSamples": report["crossFrame"]["1mTo5m"]["mismatchSamplesUtc"]}
    if manifest.get("valid") is not None:
        total = sum(len(x) for x in frames.values())
        if int(manifest["valid"]) != total:
            report["errors"].append(
                f"Manifest total valid={manifest['valid']} differs from CSV total={total}")
    # Missing 1m candle does not necessarily imply lost tick trades: thin
    # ES overnight markets can have minutes without any Last transactions.
    # Reconstruct affected 5m bars using ONLY existing 1m constituents.
    # A zero-residual match is a CANDIDATE no-trade minute, NOT source proof.
    observed_one = frames.get(1, {})
    observed_five = frames.get(5, {})
    gaps_one = sorted(set(auditable_slots(begin_et, end_et, 1, calendar)) -
                      set(observed_one))
    grouped = {}
    for minute in gaps_one:
        offset = (-minute.minute) % 5
        parent = minute + dt.timedelta(minutes=offset)
        grouped.setdefault(parent, []).append(minute)
    reconciled, unreconciled = [], []
    for parent, missing_minutes in sorted(grouped.items()):
        original = observed_five.get(parent)
        offsets = [parent - dt.timedelta(minutes=j) for j in (4, 3, 2, 1, 0)]
        existing = [observed_one.get(x) for x in offsets]
        found = [x for x in existing if x is not None]
        if original is None or not found:
            mismatch = "Parent 5m or all 1m candles missing"
            residual = None
        else:
            reconstructed = (found[0][0], max(x[1] for x in found),
                             min(x[2] for x in found), found[-1][3],
                             sum(x[4] for x in found))
            residual = round(original[4] - reconstructed[4], 6)
            mismatch = None if all(abs(x - y) <= 1e-6 for x, y
                in zip(original, reconstructed)) else (
                "Remaining 1m OHLCV does not reconstruct 5m")
        record = {
            "parent5mUtc": parent.isoformat(),
            "missing1mUtc": [x.isoformat() for x in missing_minutes],
            "missingCount": len(missing_minutes),
            "volumeResidual": residual,
            "reason": mismatch or "Exact 5m OHLCV; possible no-trade minutes"}
        if mismatch:
            unreconciled.append(record)
        else:
            reconciled.append(record)
    report["missingMinuteForensics"] = {
        "missing1mCount": len(gaps_one),
        "volumeReconciledCandidate1mCount": sum(x["missingCount"] for x in reconciled),
        "volumeReconciledParent5mCount": len(reconciled),
        "unreconciled1mCount": sum(x["missingCount"] for x in unreconciled),
        "unreconciledParent5mCount": len(unreconciled),
        "candidateSamples": reconciled[:30],
        "unreconciledSamples": unreconciled[:30],
        "warning": "Volume-reconciled absent 1m candles are not certified as no-trade minutes until source Last tick data is checked. Missing timestamps still FAIL strict gate."}
    if reconciled:
        report["warnings"].append(
            f"{sum(x['missingCount'] for x in reconciled)} missing 1m slots across "
            f"{len(reconciled)} 5m bars exactly reconstruct OHLCV: check underlying Last ticks")
    report["sparseMinutePolicy"] = {
        "optedIn": allow_sparse_1m,
        "sourceVerifiedNoTrade": False,
        "fineGrainedFeatureUse": "MASK_WINDOW_IF_MISSING_MINUTE",
        "rawDataModified": False,
    }
    # Optional explicit exception ONLY when:
    #  - every missing 1m close perfectly reconstructs its parent 5m;
    #  - all higher-timeframe candles exist and their OHLCV reconciles;
    #  - the sole audit error is missing 1m close timestamps.
    # This does not prove no Last trades; ML 1m windows crossing these
    # dates must be masked and the source CSV stays unchanged.
    expected_sparse_error = f"1m: missing {len(gaps_one)} expected closes"
    sparse_only = (
        allow_sparse_1m and len(gaps_one) > 0 and
        report["missingMinuteForensics"]["unreconciled1mCount"] == 0 and
        report["missingMinuteForensics"]["volumeReconciledCandidate1mCount"] == len(gaps_one) and
        report["errors"] == [expected_sparse_error]
    )
    if sparse_only:
        report["result"] = "PASS_WITH_FLAGGED_SPARSE_1M"
        report["sparseMinutePolicy"]["acceptedWithCaution"] = True
        report["warnings"].append(
            "Sparse 1m minute timestamps accepted for ES/NQ higher-timeframe research ONLY. "
            "Not verified as no-trade, not usable for exact tick execution; "
            "mask 1m feature/outcome windows crossing any absent minute.")
    else:
        report["result"] = "PASS_WITH_MANUAL_CALENDAR_REVIEW" if not report["errors"] else "FAIL"
        report["sparseMinutePolicy"]["acceptedWithCaution"] = False
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--folder", required=True, type=Path)
    p.add_argument("--contract", required=True, help="Explicit dated contract, e.g. ES 12-23")
    p.add_argument("--from-et", required=True, help="ISO Eastern timestamp, e.g. 2026-09-27T18:05")
    p.add_argument("--through-et", required=True, help="ISO Eastern timestamp, e.g. 2026-09-30T23:55")
    p.add_argument("--output", type=Path)
    p.add_argument("--calendar", type=Path,
                   help="Explicit sourced exchange holiday schedule/partial bars in JSON")
    p.add_argument("--allow-sparse-1m", action="store_true",
                   help="Opt-in conditional PASS only if missing 1m candles have exact 5m OHLCV residual zero and no other errors; mask affected 1m windows")
    a = p.parse_args()
    start, end = (dt.datetime.fromisoformat(x).replace(tzinfo=ET)
                  for x in (a.from_et, a.through_et))
    result = audit(a.folder, start, end, a.contract,
                   load_calendar(a.calendar) if a.calendar else None,
                   allow_sparse_1m=a.allow_sparse_1m)
    document = json.dumps(result, indent=2)
    if a.output:
        a.output.write_text(document, encoding="utf-8")
    print(document)
    return 0 if result["result"] != "FAIL" else 1


if __name__ == "__main__":
    raise SystemExit(main())
