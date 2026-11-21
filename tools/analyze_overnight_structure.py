#!/usr/bin/env python3
"""Summarise distinct Ninjex Overnight Structure research replays.

Usage:
 python tools/analyze_overnight_structure.py --root "C:\...\NinjexResearch\OvernightStructurePortfolio" --output "C:\StructureSummary.csv"

This does not optimise parameters and never merges overlapping contract windows.
Exact intratrade DD is per run; stitched DD is calculated only if the input
contains complete chronological, strictly non-overlapping run intervals.
"""
import argparse
import csv
import pathlib
from collections import defaultdict


def read_csv(path):
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def numeric(x, default=None):
    try:
        return float(x)
    except (TypeError, ValueError):
        return default


def summary(trades):
    gross = sum(numeric(t.get("GrossPnl"), 0.0) for t in trades)
    wins = [t for t in trades if numeric(t.get("GrossPnl"), 0.0) > 0]
    losses = [t for t in trades if numeric(t.get("GrossPnl"), 0.0) < 0]
    gain = sum(numeric(t["GrossPnl"], 0.0) for t in wins)
    pain = -sum(numeric(t["GrossPnl"], 0.0) for t in losses)
    equity = peak = dd = 0.0
    for t in sorted(trades, key=lambda t: t["ExitTimeET"]):
        equity += numeric(t.get("GrossPnl"), 0.0)
        peak = max(peak, equity)
        dd = max(dd, peak - equity)
    return {
        "Trades": len(trades),
        "Wins": len(wins),
        "Gross": round(gross, 2),
        "ProfitFactor": round(gain / pain, 3) if pain else "",
        "ClosedDD": round(dd, 2),
    }


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--root", required=True, type=pathlib.Path)
    p.add_argument("--output", required=True, type=pathlib.Path)
    p.add_argument("--run-id", action="append", default=[])
    a = p.parse_args()
    by_run = defaultdict(lambda: {"trades": [], "sessions": [], "equity": [], "candidates": []})
    for suffix, key in [
        ("_trades.csv", "trades"),
        ("_sessions.csv", "sessions"),
        ("_equity.csv", "equity"),
        ("_candidates.csv", "candidates"),
    ]:
        for file in a.root.rglob("overnight_structure_*" + suffix):
            for row in read_csv(file):
                run_id = row.get("RunId")
                if a.run_id and run_id not in a.run_id:
                    continue
                name = row.get("Instrument")
                if not run_id or not name:
                    continue
                by_run[(name, run_id)][key].append(row)
    if not by_run:
        p.error("No research outputs. Confirm --root and selected --run-id.")

    rows = []
    intervals = []
    for (instrument, run_id), d in by_run.items():
        dates = sorted({s["Date"] for s in d["sessions"]
                        if s.get("Date") and s.get("Event") == "RthSummary"})
        if not dates:
            print("Warning:", instrument, run_id, "has no completed RTH summaries")
            continue
        sessions = len(dates)
        trades = d["trades"]
        e = d["equity"]
        intratrade_dd = max(
            [numeric(x.get("MaxIntratradeDD"), 0.0) for x in e] or [0.0]
        )
        metrics = summary(trades)
        metrics.update({
            "Scope": "Contract", "Instrument": instrument, "RunId": run_id,
            "From": dates[0], "To": dates[-1], "Sessions": sessions,
            "Signal": "ALL", "MaxIntratradeDD": round(intratrade_dd, 2),
        })
        rows.append(metrics)
        intervals.append((dates[0], dates[-1], instrument, run_id, d))

        for sig in ["ONL-COMP-BREAK-S", "ONH-DISP-BREAK-L", "PDC-TREND-RECLAIM-L"]:
            mt = [t for t in trades if t.get("Signal") == sig]
            mm = summary(mt)
            mm.update({
                "Scope": "ContractModel", "Instrument": instrument,
                "RunId": run_id, "From": dates[0], "To": dates[-1],
                "Sessions": sessions, "Signal": sig,
                "MaxIntratradeDD": "",
            })
            rows.append(mm)

        reason_counts = defaultdict(int)
        for c in d["candidates"]:
            reason_counts[(c.get("Signal"), c.get("Disposition"))] += 1
        for (sig, reason), count in sorted(reason_counts.items()):
            print(instrument, sig, reason, count)

    intervals.sort(key=lambda x: x[0])
    overlaps = [
        (left[2], right[2], left[1], right[0])
        for left, right in zip(intervals, intervals[1:])
        if left[1] >= right[0]
    ]
    if overlaps:
        print("\nWARNING: overlapping run dates detected. No stitched DD claimed:")
        for item in overlaps:
            print("  ", item)
    else:
        # The equity audit is a sparse series of new local peaks and maximum
        # local drawdowns plus trade exits. This yields the maximum *recorded*
        # stitched excursion, not necessarily all tick-by-tick path details.
        offset = peak = stitched_dd = 0.0
        for start, end, instrument, run_id, d in intervals:
            for e in sorted(d["equity"], key=lambda x: x["EventTimeET"]):
                local = numeric(e.get("Equity"))
                if local is None:
                    continue
                equity = offset + local
                peak = max(peak, equity)
                stitched_dd = max(stitched_dd, peak - equity)
            offset += sum(numeric(t.get("GrossPnl"), 0.0)
                          for t in d["trades"])
            peak = max(peak, offset)
        print("\nRecorded stitched intratrade drawdown:", round(stitched_dd, 2))
        print("Important: benchmark again against exact tick replay after selection.")

    pth = a.output
    pth.parent.mkdir(parents=True, exist_ok=True)
    if rows:
        names = ["Scope", "Instrument", "RunId", "From", "To", "Sessions",
                 "Signal", "Trades", "Wins", "Gross", "ProfitFactor", "ClosedDD",
                 "MaxIntratradeDD"]
        with pth.open("w", encoding="utf-8", newline="") as f:
            out = csv.DictWriter(f, fieldnames=names)
            out.writeheader()
            out.writerows(rows)
        print("\nSaved:", pth)
        for x in rows:
            if x["Scope"] == "Contract":
                print("{Instrument:<10} {Trades:>3} trades  gross {Gross:>10.2f}"
                      "  closed DD {ClosedDD:>8.2f}  intratrade DD {MaxIntratradeDD:>8.2f}"
                      .format(**x))


if __name__ == "__main__":
    main()
