#!/usr/bin/env python3
"""Summarise independent Failed-PMH V2 shadow results (not an executable portfolio).

Run from the repository root:
    python tools/analyze_overnight_edge_failed_pmh_v2.py --input "C:\Research" \
        --run-id "2026..._..." --output "C:\Research\failed_pmh_v2_summary.csv"

Supply --run-id once per verified contract. If you omit it, the script refuses
to silently mix multiple RunIds for a single instrument.
"""
import argparse
import csv
from collections import defaultdict
from pathlib import Path


def read_csv(path):
    with path.open(newline="", encoding="utf-8-sig") as file:
        return list(csv.DictReader(file))


def number(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def max_closed_drawdown(records):
    equity = 0.0
    peak = 0.0
    maximum = 0.0
    for item in sorted(records, key=lambda r: (r["EntryTimeET"], r["ExitTimeET"])):
        pnl = number(item["GrossPnl"])
        if pnl is None:
            continue
        equity += pnl
        peak = max(peak, equity)
        maximum = max(maximum, peak - equity)
    return maximum


def summarise(group):
    completed = [r for r in group if number(r["GrossPnl"]) is not None]
    gross = sum(number(r["GrossPnl"]) for r in completed)
    wins = [r for r in completed if number(r["GrossPnl"]) > 0]
    losses = [r for r in completed if number(r["GrossPnl"]) < 0]
    positive = sum(number(r["GrossPnl"]) for r in wins)
    negative = -sum(number(r["GrossPnl"]) for r in losses)
    possible = [
        r for r in completed
        if r["PortfolioPositionOpen"] == "0"
        and r["PortfolioCapsReached"] == "0"
        and r["PortfolioOrderPending"] == "0"
    ]
    possible_pnl = sum(number(r["GrossPnl"]) for r in possible)
    return {
        "Completed": len(completed),
        "Wins": len(wins),
        "Losses": len(losses),
        "WinRate": round(len(wins) / len(completed), 4) if completed else "",
        "GrossPnl": round(gross, 2),
        "ProfitFactor": round(positive / negative, 3) if negative else "",
        "AveragePnl": round(gross / len(completed), 2) if completed else "",
        "MaxClosedDrawdown": round(max_closed_drawdown(completed), 2),
        "NoBaselinePositionOrCapsCount": len(possible),
        "NoBaselinePositionOrCapsGross": round(possible_pnl, 2),
        "Unscored": len(group) - len(completed),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--run-id", action="append", default=[],
                        help="Repeat for each verified contract's exact RunId")
    args = parser.parse_args()

    rows = []
    candidates = []
    for path in args.input.glob("overnight_edge_failed_pmh_v2_trades_*.csv"):
        rows.extend(read_csv(path))
    for path in args.input.glob("overnight_edge_failed_pmh_v2_candidates_*.csv"):
        candidates.extend(read_csv(path))

    if args.run_id:
        selected = set(args.run_id)
        rows = [row for row in rows if row["RunId"] in selected]
        candidates = [row for row in candidates if row["RunId"] in selected]

    run_by_instrument = defaultdict(set)
    for row in rows + candidates:
        run_by_instrument[row["Instrument"]].add(row["RunId"])
    ambiguous = {k: v for k, v in run_by_instrument.items() if len(v) > 1}
    if ambiguous:
        parser.error("Multiple RunIds for the same contract. Select verified "
                     "--run-id values explicitly: " + str(ambiguous))
    if not rows:
        parser.error("No V2 trade rows found for the selected research runs.")

    groups = defaultdict(list)
    for row in rows:
        if row["ExitReason"] in ("IncompleteAtTermination",
                                  "MissingSameDayExitTick", "NoCausalEntry",
                                  "StructureStopTooWide", "MissingConfirmationATR"):
            # Preserve them as unscored; summarise() handles blank gross.
            pass
        groups[(row["Instrument"], row["RunId"], row["Scenario"])].append(row)

    summaries = []
    for (instrument, run_id, scenario), group in sorted(groups.items()):
        summary = {"Instrument": instrument, "RunId": run_id,
                   "Scenario": scenario}
        summary.update(summarise(group))
        summaries.append(summary)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="", encoding="utf-8") as out:
        writer = csv.DictWriter(out, fieldnames=list(summaries[0]))
        writer.writeheader()
        writer.writerows(summaries)

    dispositions = defaultdict(int)
    for row in candidates:
        dispositions[(row["Instrument"], row["Status"])] += 1

    print("Failed PMH V2 candidate statuses:")
    for (instrument, status), count in sorted(dispositions.items()):
        print("  {:<10} {:<29} {}".format(instrument, status, count))
    print("\nTrade scenarios (gross hypothetical P&L, excluding costs):")
    for row in summaries:
        print("  {Instrument:<10} {Scenario:<16} trades={Completed:<4} "
              "gross={GrossPnl:+9.2f} DD={MaxClosedDrawdown:>8.2f} "
              "baseline-flat={NoBaselinePositionOrCapsCount}".format(**row))
    print("\nSaved", args.output)
    print("WARNING: Independent shadow outcomes may overlap each other and "
          "Run 8. Baseline-flat counts are NOT a portfolio backtest; "
          "closed-trade DD is NOT intratrade/trailing account drawdown.")


if __name__ == "__main__":
    main()
