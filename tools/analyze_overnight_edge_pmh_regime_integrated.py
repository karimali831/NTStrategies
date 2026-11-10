#!/usr/bin/env python3
"""Summarise the actual integrated PMH-regime Playback portfolio.

This script reads real strategy TradeExit rows (not V1/V2 shadow outcomes) and
the integrated tick-updated equity audit. It refuses to mix multiple RunIds for
one instrument unless --run-id is supplied explicitly.
"""
import argparse
import csv
from collections import defaultdict
from pathlib import Path


def rows(path):
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def num(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def closed_drawdown(trades):
    equity = peak = maximum = 0.0
    for trade in sorted(trades, key=lambda r: r["EventTime"]):
        pnl = num(trade["TradeGross"])
        if pnl is None:
            continue
        equity += pnl
        peak = max(peak, equity)
        maximum = max(maximum, peak - equity)
    return maximum


def summarise(trades):
    completed = [r for r in trades if num(r["TradeGross"]) is not None]
    gross = sum(num(r["TradeGross"]) for r in completed)
    wins = [r for r in completed if num(r["TradeGross"]) > 0]
    losses = [r for r in completed if num(r["TradeGross"]) < 0]
    gains = sum(num(r["TradeGross"]) for r in wins)
    pain = -sum(num(r["TradeGross"]) for r in losses)
    return {
        "Trades": len(completed),
        "Wins": len(wins),
        "Losses": len(losses),
        "WinRate": round(len(wins) / len(completed), 4) if completed else "",
        "GrossPnl": round(gross, 2),
        "ProfitFactor": round(gains / pain, 3) if pain else "",
        "ClosedTradeDrawdown": round(closed_drawdown(completed), 2),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--input", required=True, type=Path)
    ap.add_argument("--output", required=True, type=Path)
    ap.add_argument("--run-id", action="append", default=[])
    args = ap.parse_args()

    research = []
    for path in args.input.glob("overnight_edge_research_*.csv"):
        research.extend(rows(path))

    equity = []
    for path in args.input.glob("overnight_edge_pmh_regime_integrated_equity_*.csv"):
        equity.extend(rows(path))

    if args.run_id:
        selected = set(args.run_id)
        research = [r for r in research if r.get("RunId") in selected]
        equity = [r for r in equity if r.get("RunId") in selected]

    research = [
        r for r in research
        if r.get("PortfolioMode") == "PmhRegimeIntegratedEvaluation"
    ]

    run_by_instrument = defaultdict(set)
    for r in research:
        run_by_instrument[r.get("Instrument", "")].add(r.get("RunId", ""))
    ambiguous = {k: sorted(v) for k, v in run_by_instrument.items() if len(v) > 1}
    if ambiguous:
        ap.error("Multiple integrated RunIds per instrument; pass verified "
                 "--run-id values explicitly: " + str(ambiguous))

    exits = [r for r in research if r.get("EventType") == "TradeExit"]
    if not exits:
        ap.error("No integrated TradeExit rows found.")

    max_dd_by_key = defaultdict(float)
    for r in equity:
        key = (r.get("Instrument", ""), r.get("RunId", ""))
        value = num(r.get("MaxIntratradeDrawdown"))
        if value is not None:
            max_dd_by_key[key] = max(max_dd_by_key[key], value)

    output = []
    by_contract = defaultdict(list)
    by_model = defaultdict(list)
    for r in exits:
        by_contract[(r["Instrument"], r["RunId"])].append(r)
        by_model[r["Signal"]].append(r)

    for (instrument, run_id), group in sorted(by_contract.items()):
        summary = {
            "Scope": "Contract",
            "Instrument": instrument,
            "RunId": run_id,
            "Signal": "ALL",
        }
        summary.update(summarise(group))
        summary["MaxIntratradeDrawdown"] = round(
            max_dd_by_key[(instrument, run_id)], 2)
        output.append(summary)

        signals = defaultdict(list)
        for r in group:
            signals[r["Signal"]].append(r)
        for signal, model_group in sorted(signals.items()):
            row = {
                "Scope": "ContractModel",
                "Instrument": instrument,
                "RunId": run_id,
                "Signal": signal,
            }
            row.update(summarise(model_group))
            row["MaxIntratradeDrawdown"] = ""
            output.append(row)

    total = {
        "Scope": "AllContracts",
        "Instrument": "ALL",
        "RunId": "SELECTED",
        "Signal": "ALL",
    }
    total.update(summarise(exits))
    # Contract replays are separate equity paths, so the meaningful worst value
    # is the maximum contract drawdown, not a stitched synthetic equity curve.
    total["MaxIntratradeDrawdown"] = round(
        max(max_dd_by_key.values()) if max_dd_by_key else 0.0, 2)
    output.append(total)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(output[0].keys()))
        writer.writeheader()
        writer.writerows(output)

    print("Integrated PMH regime Playback:")
    for row in output:
        if row["Scope"] == "Contract":
            print(
                f'  {row["Instrument"]:<10} trades={row["Trades"]:<3} '
                f'gross={row["GrossPnl"]:+9.2f} '
                f'closedDD={row["ClosedTradeDrawdown"]:>8.2f} '
                f'intratradeDD={row["MaxIntratradeDrawdown"]:>8.2f}'
            )
    print("Saved", args.output)
    print("Use commissions/slippage separately; GrossPnl is strategy gross.")


if __name__ == "__main__":
    main()
