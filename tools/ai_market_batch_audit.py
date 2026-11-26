#!/usr/bin/env python3
"""Audit all tagged quarterly ES/NQ historical export folders in one pass."""
import argparse
import datetime as dt
import json
from pathlib import Path

try:
    from tools.ai_market_research_audit import audit, load_calendar, ET
except ImportError:
    from ai_market_research_audit import audit, load_calendar, ET


def roll_monday(year, month):
    first = dt.date(year, month, 1)
    third_friday = first + dt.timedelta(days=(4 - first.weekday()) % 7 + 14)
    return third_friday - dt.timedelta(days=4)


def window(contract, global_start, global_end):
    root, month_year = contract.split(" ")
    month, year = map(int, month_year.split("-"))
    year += 2000
    if root not in ("ES", "NQ") or month not in (3, 6, 9, 12):
        raise ValueError("Not a dated ES/NQ quarterly contract: " + contract)
    previous = (year - 1, 12) if month == 3 else (year, month - 3)
    start = max(roll_monday(*previous) - dt.timedelta(days=1), global_start)
    end = min(roll_monday(year, month) - dt.timedelta(days=3), global_end)
    if end < start:
        return None
    first_time = dt.time(18, 1) if start == roll_monday(*previous) - dt.timedelta(days=1) else dt.time.min
    final_time = dt.time(17) if end == roll_monday(year, month) - dt.timedelta(days=3) else dt.time(23, 59)
    return dt.datetime.combine(start, first_time, tzinfo=ET), dt.datetime.combine(end, final_time, tzinfo=ET)


def expected(prefix, first, last):
    result = []
    for year in range(first.year, last.year + 2):
        for month in (3, 6, 9, 12):
            name = f"{prefix} {month:02d}-{year % 100:02d}"
            bounds = window(name, first, last)
            if bounds:
                result.append((name, bounds))
    return sorted(result, key=lambda x: x[1][0])


def calendars_in(directory):
    if not directory:
        return None
    result = {"closures": [], "partial": {}, "calendarName": "Reviewed bulk calendars", "source": str(directory)}
    for file in sorted(directory.glob("*.json")):
        item = load_calendar(file)
        result["closures"].extend(item["closures"])
        for key, start in item["partial"].items():
            if key in result["partial"] and result["partial"][key] != start:
                raise ValueError("Conflicting calendar partial close: " + str(file))
            result["partial"][key] = start
    return result


def audit_batch(root, tag, prefix, start, end, calendar=None, sparse=False, only=None):
    wanted = expected(prefix, start, end)
    if only:
        wanted = [row for row in wanted if row[0] in only]
        if len(wanted) != len(set(only)):
            raise ValueError("One or more chosen contracts are outside research window")
    indexed = {}
    for manifest_file in root.glob("*/manifest.json"):
        try:
            manifest = json.loads(manifest_file.read_text(encoding="utf-8-sig"))
        except (ValueError, OSError):
            continue
        if manifest.get("datasetTag") == tag and manifest.get("contract"):
            indexed.setdefault(manifest["contract"], []).append(manifest_file.parent)
    result = {"schema": "ninjex-basket-audit-v1", "datasetTag": tag, "requested": len(wanted),
              "contracts": [], "summary": {"pass": 0, "flagged": 0, "fail": 0, "missing": 0, "duplicates": 0},
              "rawFilesModified": False,
              "policy": "CME third-Friday-week Monday customary roll. Holiday and provenance review still required."}
    for name, (from_et, through_et) in wanted:
        folders = indexed.get(name, [])
        item = {"contract": name, "fromEastern": from_et.isoformat(),
                "throughEastern": through_et.isoformat(),
                "folders": [str(x) for x in folders]}
        if not folders:
            item["result"] = "NOT_EXPORTED"
            result["summary"]["missing"] += 1
        elif len(folders) != 1:
            item["result"] = "DUPLICATE_RUNS"
            result["summary"]["duplicates"] += 1
        else:
            details = audit(folders[0], from_et, through_et, name,
                            calendar=calendar, allow_sparse_1m=sparse)
            item["result"] = details["result"]
            item["frames"] = {tf: {"present": v["observedInWindow"],
                                    "missing": v["missing"], "offCalendar": v["offCalendar"]}
                              for tf, v in details["frames"].items()}
            item["crossFrameMismatches"] = sum(x["mismatchCount"]
                for x in details["crossFrame"].values())
            item["missing1m"] = details["missingMinuteForensics"]["missing1mCount"]
            item["missing5mByEasternDate"] = (
                details["frames"]["5m"].get("missingByEasternDate", {}))
            item["errors"] = details["errors"][:25]
            item["warnings"] = details["warnings"][:15]
            if item["result"] == "PASS_WITH_MANUAL_CALENDAR_REVIEW":
                result["summary"]["pass"] += 1
            elif item["result"] == "PASS_WITH_FLAGGED_SPARSE_1M":
                result["summary"]["flagged"] += 1
            else:
                result["summary"]["fail"] += 1
        result["contracts"].append(item)
    result["status"] = ("NEEDS_REVIEW" if any(
        item["result"] in ("FAIL", "NOT_EXPORTED", "DUPLICATE_RUNS")
        for item in result["contracts"]) else "PASS_WITH_QUALIFICATIONS")
    return result


def write_summary(report, path):
    lines = ["# Ninjex AI — ES historical basket audit", "",
             "Result: " + report["status"], "",
             "| Contract | Period ET | 1m gaps | 5m gaps | 15m gaps | 60m gaps | Result |",
             "|---|---|---:|---:|---:|---:|---|"]
    for item in report["contracts"]:
        frames = item.get("frames", {})
        misses = [str(frames.get(str(tf) + "m", {}).get("missing", "-"))
                  for tf in (1, 5, 15, 60)]
        period = item["fromEastern"][:10] + " to " + item["throughEastern"][:10]
        lines.append("| " + " | ".join([item["contract"], period] + misses +
                                   [item["result"]]) + " |")
        if item.get("missing5mByEasternDate"):
            lines.append("")
            lines.append("  - 5m missing-day hotspots: " +
                ", ".join(f"{day}: {count}" for day, count in
                          item["missing5mByEasternDate"].items()))
    lines += ["", "## Anomalies", ""]
    for item in report["contracts"]:
        if item["result"] not in ("PASS_WITH_MANUAL_CALENDAR_REVIEW", "PASS_WITH_FLAGGED_SPARSE_1M"):
            lines.append("### " + item["contract"] + " — " + item["result"])
            lines.extend("- " + msg for msg in item.get("errors", ["Review missing or duplicate export"]))
            lines.append("")
    lines += ["", "Strict source validation still requires calendar/rollover review; no files modified."]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--root", type=Path, required=True)
    p.add_argument("--tag", required=True)
    p.add_argument("--instrument", choices=("ES", "NQ"), default="ES")
    p.add_argument("--from-et", default="2023-09-10")
    p.add_argument("--through-et", default="2026-11-25")
    p.add_argument("--contract", action="append")
    p.add_argument("--calendars", type=Path)
    p.add_argument("--allow-sparse-1m", action="store_true")
    p.add_argument("--output", type=Path, required=True)
    a = p.parse_args()
    report = audit_batch(a.root, a.tag, a.instrument, dt.date.fromisoformat(a.from_et),
                         dt.date.fromisoformat(a.through_et), calendars_in(a.calendars),
                         a.allow_sparse_1m, a.contract)
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    write_summary(report, a.output.with_suffix(".md"))
    print("[NinjexAI-BASKET]", report["status"], report["summary"])
    print("Report:", a.output, "and", a.output.with_suffix(".md"))
    return 0 if report["status"] == "PASS_WITH_QUALIFICATIONS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
