#!/usr/bin/env python3
"""Summarise cleaned Overnight Edge baseline research runs.

Reads actual strategy TradeExit rows plus the persistent portfolio-risk audit.
Use --run-id once per verified contract to avoid mixing overlapping replays.
"""
import argparse
import csv
from collections import defaultdict
from pathlib import Path


def read_rows(path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def number(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def closed_drawdown(rows):
    equity = 0.0
    peak = 0.0
    max_dd = 0.0
    for row in sorted(rows, key=lambda r: r["EventTime"]):
        pnl = number(row["TradeGross"])
        if pnl is None:
            continue
        equity += pnl
        peak = max(peak, equity)
        max_dd = max(max_dd, peak - equity)
    return max_dd


def summarise(rows):
    rows = [r for r in rows if number(r.get("TradeGross")) is not None]
    gross = sum(number(r["TradeGross"]) for r in rows)
    wins = [r for r in rows if number(r["TradeGross"]) > 0]
    losses = [r for r in rows if number(r["TradeGross"]) < 0]
    gain = sum(number(r["TradeGross"]) for r in wins)
    pain = -sum(number(r["TradeGross"]) for r in losses)
    return {
        "Trades": len(rows),
        "Wins": len(wins),
        "Losses": len(losses),
        "WinRate": round(len(wins) / len(rows), 4) if rows else "",
        "GrossPnl": round(gross, 2),
        "ProfitFactor": round(gain / pain, 3) if pain else "",
        "ClosedTradeDrawdown": round(closed_drawdown(rows), 2),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", type=Path, required=True)
    ap.add_argument("--output", type=Path, required=True)
    ap.add_argument("--run-id", action="append", default=[])
    args = ap.parse_args()

    research = []
    for path in args.input.glob("overnight_edge_research_*.csv"):
        research.extend(read_rows(path))

    risk = []
    for path in args.input.glob("overnight_edge_portfolio_risk_*.csv"):
        risk.extend(read_rows(path))

    if args.run_id:
        selected = set(args.run_id)
        research = [r for r in research if r.get("RunId") in selected]
        risk = [r for r in risk if r.get("RunId") in selected]

    research = [
        r for r in research
        if r.get("PortfolioMode") == "PmhRegimeBaseline"
    ]

    run_by_instrument = defaultdict(set)
    for row in research:
        run_by_instrument[row.get("Instrument", "")].add(row.get("RunId", ""))

    ambiguous = {
        instrument: sorted(run_ids)
        for instrument, run_ids in run_by_instrument.items()
        if len(run_ids) > 1
    }
    if ambiguous:
        ap.error(
            "Multiple RunIds for an instrument. Select exact verified --run-id "
            "values: " + str(ambiguous)
        )

    exits = [r for r in research if r.get("EventType") == "TradeExit"]
    if not exits:
        ap.error("No PmhRegimeBaseline TradeExit rows found.")

    risk_by_key = defaultdict(list)
    for row in risk:
        risk_by_key[(row.get("Instrument", ""), row.get("RunId", ""))].append(row)

    output = []
    groups = defaultdict(list)
    for row in exits:
        groups[(row["Instrument"], row["RunId"])].append(row)

    for (instrument, run_id), trades in sorted(groups.items()):
        audit = risk_by_key[(instrument, run_id)]
        max_intratrade = max(
            [number(r.get("MaxIntratradeDrawdown")) or 0.0 for r in audit]
            or [0.0]
        )
        blocks = sum(
            1 for r in audit if r.get("EventType") == "ProspectiveRiskBlock"
        )

        summary = {
            "Scope": "Contract",
            "Instrument": instrument,
            "RunId": run_id,
            "Signal": "ALL",
        }
        summary.update(summarise(trades))
        summary["MaxIntratradeDrawdown"] = round(max_intratrade, 2)
        summary["ProspectiveRiskBlocks"] = blocks
        output.append(summary)

        by_signal = defaultdict(list)
        for trade in trades:
            by_signal[trade["Signal"]].append(trade)

        for signal, signal_trades in sorted(by_signal.items()):
            row = {
                "Scope": "ContractModel",
                "Instrument": instrument,
                "RunId": run_id,
                "Signal": signal,
            }
            row.update(summarise(signal_trades))
            row["MaxIntratradeDrawdown"] = ""
            row["ProspectiveRiskBlocks"] = ""
            output.append(row)

    total = {
        "Scope": "SelectedRuns",
        "Instrument": "ALL",
        "RunId": "SELECTED",
        "Signal": "ALL",
    }
    total.update(summarise(exits))
    total["MaxIntratradeDrawdown"] = round(
        max(
            [number(r.get("MaxIntratradeDrawdown")) or 0.0 for r in risk]
            or [0.0]
        ),
        2,
    )
    total["ProspectiveRiskBlocks"] = sum(
        1 for r in risk if r.get("EventType") == "ProspectiveRiskBlock"
    )
    output.append(total)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(output[0].keys()))
        writer.writeheader()
        writer.writerows(output)

    for row in output:
        if row["Scope"] == "Contract":
            print(
                f'{row["Instrument"]:<10} trades={row["Trades"]:<3} '
                f'gross={row["GrossPnl"]:+9.2f} '
                f'closedDD={row["ClosedTradeDrawdown"]:>8.2f} '
                f'maxDD={row["MaxIntratradeDrawdown"]:>8.2f} '
                f'blocks={row["ProspectiveRiskBlocks"]}'
            )
    print("Saved", args.output)


if __name__ == "__main__":
    main()
