#!/usr/bin/env python3
"""Exploratory ES 1m OHLC bracket-path research; NEVER actual fills or orders.

Scores only the existing validation split with an already trained, locally
trusted ML model. Holdout is not scored. Fixed research policy:
- Model candidate p >= .45, direction margin .10, source flags OK;
- 30-minute spaced decisions per dated contract, with no stacked trades;
- next one-minute bar open as idealized entry (then adverse slippage);
- ES 12-tick stop, 24-tick target, 30-minute maximum holding period;
- pessimistic STOP-first if both levels touched within a 1m bar;
- skip any trade whose 30 future 1m closes are incomplete or nonmonotonic;
- $5 commission per round trip and one-tick adverse slippage per side;
- compare ML direction against always-long and 12-bar momentum at SAME times.

This is NOT calibrated, independent validation: the model was selected
using the same validation period, and p=.45 came from its opportunity
diagnostic. Any PnL here is exploratory, not a confirmed ES strategy.
"""
import argparse
import csv
import datetime as dt
import json
import math
from collections import defaultdict
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_train_baseline import (
        feature_frame, read_verified_csv,
    )
    from tools.ai_market_validation_opportunities import (
        prepare_validation, quantile_cutoffs, add_validation_profiles,
        select_candidates, select_nonoverlap, validate_bundle,
    )
    from tools.ai_market_ml_features import read_bars
else:
    from ai_market_train_baseline import feature_frame, read_verified_csv
    from ai_market_validation_opportunities import (
        prepare_validation, quantile_cutoffs, add_validation_profiles,
        select_candidates, select_nonoverlap, validate_bundle,
    )
    from ai_market_ml_features import read_bars

DIRECTIONAL_THRESHOLD = .45
STOP_TICKS = 12
TARGET_TICKS = 24
HOLD_MINUTES = 30
ES_TICK_SIZE = .25
ES_POINT_VALUE = 50.
COMMISSION_ROUND_TURN = 5.
SLIPPAGE_TICKS_PER_SIDE = 1
ONE_MINUTE = dt.timedelta(minutes=1)
STRATEGIES = ("ml", "alwaysLong", "momentum12")


def close_time(value):
    timestamp = dt.datetime.fromisoformat(str(value).replace("Z", "+00:00"))
    if timestamp.tzinfo is None:
        raise ValueError("Naive timestamps cannot be used in trade simulation")
    return timestamp.astimezone(dt.timezone.utc)


def minute_window(source_by_close, decision_utc):
    """Never guess through a 1m source gap; keep simulation gap-free."""
    times = [decision_utc + ONE_MINUTE * minute
             for minute in range(1, HOLD_MINUTES + 1)]
    candles = [source_by_close.get(moment) for moment in times]
    if any(bar is None for bar in candles):
        return None
    if any("INVALID" in bar.flags for bar in candles):
        return None
    return candles


def simulate(candles, side, ticks_stop=STOP_TICKS, ticks_target=TARGET_TICKS):
    """Long=+1, short=-1. Both touched => STOP. All exits slip adversely."""
    if side not in (-1, 1):
        raise ValueError("Trade direction must be +1 or -1")
    if len(candles) != HOLD_MINUTES:
        raise ValueError("Require complete contiguous horizon, never fill holes")
    # Bracket distances anchor to the next bar's unadjusted OPEN.
    # Entry/exit then both receive adverse slippage independently;
    # otherwise stop/target risk would silently absorb entry slippage.
    reference = candles[0].open
    entry = reference + side * SLIPPAGE_TICKS_PER_SIDE * ES_TICK_SIZE
    stop = reference - side * ticks_stop * ES_TICK_SIZE
    target = reference + side * ticks_target * ES_TICK_SIZE
    exit_mid = candles[-1].close
    exit_reason = "TIME"
    ambiguous = False
    exit_utc = candles[-1].end
    for bar in candles:
        if side == 1:
            stop_open = bar.open <= stop
            target_open = bar.open >= target
            hit_stop = bar.low <= stop
            hit_target = bar.high >= target
        else:
            stop_open = bar.open >= stop
            target_open = bar.open <= target
            hit_stop = bar.high >= stop
            hit_target = bar.low <= target
        if stop_open:
            # Opening through stop is worse than stop fill.
            exit_mid = bar.open
            exit_reason = "STOP_GAP"
        elif target_open:
            # Conservative limit fill; no favourable price improvement.
            exit_mid = target
            exit_reason = "TARGET"
        elif hit_stop and hit_target:
            exit_mid = stop
            ambiguous = True
            exit_reason = "AMBIGUOUS_STOP_FIRST"
        elif hit_stop:
            exit_mid = stop
            exit_reason = "STOP"
        elif hit_target:
            exit_mid = target
            exit_reason = "TARGET"
        else:
            continue
        exit_utc = bar.end
        break
    exit_fill = exit_mid - side * SLIPPAGE_TICKS_PER_SIDE * ES_TICK_SIZE
    net = side * (exit_fill - entry) * ES_POINT_VALUE - COMMISSION_ROUND_TURN
    return {
        "entry": round(entry, 2), "exit": round(exit_fill, 2),
        "stop": round(stop, 2), "target": round(target, 2),
        "netUsd": round(net, 2), "exitReason": exit_reason,
        "exitUtc": exit_utc.isoformat(), "bothTouched1m": ambiguous
    }


def summarize(trades):
    if not trades:
        return {"trades": 0, "netUsd": 0, "meanPerTradeUsd": 0,
                "maxClosedEquityDrawdownUsd": 0, "winRate": None,
                "profitFactor": None, "months": {},
                "ambiguousStops": 0, "stopGapExits": 0}
    ordered = sorted(trades, key=lambda row: row["decisionUtc"])
    pnl = np.asarray([item["netUsd"] for item in ordered], dtype=float)
    equity = np.cumsum(pnl)
    peaks = np.maximum.accumulate(np.insert(equity, 0, 0.))
    drawdown = float(np.max(peaks[1:] - equity))
    gains = pnl[pnl > 0].sum()
    losses = -pnl[pnl < 0].sum()
    return {
        "trades": len(ordered),
        "netUsd": round(float(pnl.sum()), 2),
        "meanPerTradeUsd": round(float(pnl.mean()), 2),
        "winRate": round(float(np.mean(pnl > 0)), 5),
        "profitFactor": round(float(gains / losses), 4) if losses else None,
        "maxClosedEquityDrawdownUsd": round(drawdown, 2),
        "months": {month: {"trades": len(rows),
                          "netUsd": round(sum(x["netUsd"] for x in rows), 2)}
                   for month, rows in sorted(_group(ordered, "month").items())},
        "ambiguousStops": sum(x["bothTouched1m"] for x in ordered),
        "stopGapExits": sum(x["exitReason"] == "STOP_GAP" for x in ordered),
    }


def _group(rows, field):
    grouped = defaultdict(list)
    for record in rows:
        grouped[record[field]].append(record)
    return grouped


def execute(features, model_path, audit_path, output_dir):
    manifest = json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    table = read_verified_csv(features, manifest)
    train, val, holdout_count = prepare_validation(table)
    model = validate_bundle(joblib.load(model_path))
    boundaries = quantile_cutoffs(train)
    scored = add_validation_profiles(val, model, boundaries)
    chosen = select_nonoverlap(select_candidates(scored, DIRECTIONAL_THRESHOLD))
    # Always reserve the 2026-06-15+ dates: no raw 1m loads, labels,
    # predictions, or outcomes from that period.
    audit = json.loads(audit_path.read_text("utf-8-sig"))
    if audit.get("schema") != "ninjex-basket-audit-v1":
        raise ValueError("Expected original Ninjex bulk research audit")
    if audit.get("datasetTag") != manifest.get("datasetTag"):
        raise ValueError("Feature/audit dataset tags disagree")
    folders = {}
    for item in audit["contracts"]:
        if len(item.get("folders", [])) == 1:
            folders[item["contract"]] = Path(item["folders"][0])
    if not set(chosen["contract"]).issubset(folders):
        raise ValueError("Missing or duplicated source contract from audit")
    excluded = defaultdict(int)
    trades = {name: [] for name in STRATEGIES}
    for contract, group in chosen.groupby("contract"):
        folder = folders[contract]
        raw_manifest = json.loads((folder / "manifest.json").read_text("utf-8-sig"))
        if raw_manifest["contract"] != contract or raw_manifest["datasetTag"] != manifest["datasetTag"]:
            raise ValueError("Original source folder provenance mismatch")
        bars = read_bars(folder, 1, contract)
        valid = {bar.end: bar for bar in bars}
        for _, row in group.iterrows():
            time = close_time(row["decisionUtc"])
            if row["researchSplit"] != "validation" or row["decisionEastern"][:10] >= "2026-06-15":
                raise ValueError("Reserved holdout cannot be used for trade research")
            candles = minute_window(valid, time)
            if candles is None:
                excluded["missingFuture1m"] += 1
                continue
            if candles[-1].end.astimezone(dt.timezone(dt.timedelta(0))) < time:
                raise ValueError("Future horizon date reversal")
            signal = 1 if row["direction"] == "UP" else -1
            momentum = 1 if float(row["ret12Atr"]) >= 0 else -1
            directions = {"ml": signal, "alwaysLong": 1, "momentum12": momentum}
            for name, side in directions.items():
                pnl = simulate(candles, side)
                trades[name].append({
                    "strategy": name, "contract": contract,
                    "decisionUtc": time.isoformat(),
                    "decisionEastern": str(row["decisionEastern"]),
                    "month": str(row["month"]),
                    "session": str(row["sessionGroup"]),
                    "modelDirection": str(row["direction"]),
                    "simDirection": "LONG" if side == 1 else "SHORT",
                    "pDirection": round(float(row["pDirection"]), 5),
                    "atr14": round(float(row["atr14_5m"]), 5),
                    **pnl
                })

    results = {
        "schema": "ninjex-ml-1m-bracket-simulation-v1",
        "status": "EXPLORATORY_SAME_VALIDATION_NO_LIVE_TRADING_EDGE",
        "inputResearchStatus": manifest["researchStatus"],
        "dataQualityStatus": audit.get("status"),
        "sourceFeatures": str(features),
        "sourceAudit": str(audit_path),
        "model": str(model_path),
        "validationRowsScored": len(scored),
        "holdoutRowsNotScored": holdout_count,
        "signalsAt45pct": int(len(select_candidates(scored, DIRECTIONAL_THRESHOLD))),
        "nonOverlappingDecisions": int(len(chosen)),
        "excluded": dict(excluded),
        "policy": {
            "entry": "first NEXT 1m bar open after completed 5m decision +1 tick adverse slippage",
            "decisionProbabilityThreshold": DIRECTIONAL_THRESHOLD,
            "timeBetweenSignalsMinutes": HOLD_MINUTES,
            "stopTicks": STOP_TICKS,
            "targetTicks": TARGET_TICKS,
            "holdMinutes": HOLD_MINUTES,
            "slippageTicksPerSide": SLIPPAGE_TICKS_PER_SIDE,
            "commissionRoundTripUsd": COMMISSION_ROUND_TURN,
            "pointValueUsd": ES_POINT_VALUE,
            "tickSize": ES_TICK_SIZE,
            "ambiguousBar": "STOP FIRST (pessimistic); source ticks needed before execution claims",
            "missingFuture1m": "EXCLUDE, not forward-fill",
            "modelsCompared": list(STRATEGIES),
            "comparisonFairness": "same candidate timestamps and same 1m price paths for all 3 directions",
        },
        "results": {name: summarize(values) for name, values in trades.items()},
        "bySession": {name: {key: summarize(values)
                            for key, values in _group(trades[name], "session").items()}
                      for name in STRATEGIES},
        "byContract": {name: {key: summarize(values)
                             for key, values in _group(trades[name], "contract").items()}
                       for name in STRATEGIES},
        "warnings": [
            "This is an OHLC-based research simulation, NOT a NinjaTrader Strategy Analyzer or broker execution backtest.",
            "Model and 0.45 threshold were examined on this same validation split; selection is optimistically biased.",
            "1m candles do not reveal exact stop-vs-target hit order; both-touched cases pessimistically stop first.",
            "Opening trades on the next 1m open assumes negligible model and network latency.",
            "Transaction costs are configured assumptions, not verified broker/prop account fees.",
            "Drawdown is closed-equity peak-to-trough, NOT intraday unrealized/proprietary trailing drawdown.",
            "No bid/ask, depth, market impact, tier-one news, slippage distribution or rollover outages tested.",
            "Holdout June 15 2026 onward remains untouched."
        ]
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    json_path = output_dir / "es-ml-bracket-research-v1.json"
    json_path.write_text(json.dumps(results, indent=2), encoding="utf-8")
    with (output_dir / "es-ml-bracket-research-v1-trades.csv").open("w", newline="",
                                                                      encoding="utf-8") as stream:
        keys = ["strategy", "contract", "decisionUtc", "decisionEastern", "month",
                "session", "modelDirection", "simDirection", "pDirection",
                "atr14", "entry", "exit", "stop", "target", "netUsd",
                "exitReason", "exitUtc", "bothTouched1m"]
        writer = csv.DictWriter(stream, fieldnames=keys)
        writer.writeheader()
        for name in STRATEGIES:
            writer.writerows(trades[name])
    md = ["# Ninjex AI — ES exploratory 1-minute bracket-path comparison",
          "", "**NOT A VERIFIED STRATEGY OR LIVE FILL BACKTEST.**", "",
          f"Validation scored: {len(scored):,}; reserved holdout untouched: {holdout_count:,}.",
          f"Eligible 45% signals: {results['signalsAt45pct']:,}; disjoint decisions: {len(chosen):,}.",
          f"Excluded incomplete minute paths: {excluded['missingFuture1m']:,}.",
          "", "Fixed hypothetical ES trade: 12 tick stop / 24 tick target /"
          " 30m exit; entry next-minute open; one-tick slippage on both sides;"
          " $5 round-turn commissions.", "",
          "| Direction policy | Trades | Net USD | Mean USD/trade | Win rate | Profit factor | Closed max DD USD | Ambiguous 1m stops |",
          "|---|---:|---:|---:|---:|---:|---:|---:|"]
    for name in STRATEGIES:
        a = results["results"][name]
        pf = f"{a['profitFactor']:.2f}" if a["profitFactor"] is not None else "—"
        win = f"{100*a['winRate']:.1f}%" if a["winRate"] is not None else "—"
        md.append(f"| {name} | {a['trades']} | {a['netUsd']:,.2f} | "
                  f"{a['meanPerTradeUsd']:,.2f} | {win} | {pf} | "
                  f"{a['maxClosedEquityDrawdownUsd']:,.2f} | {a['ambiguousStops']} |")
    md += ["", "## ML direction by source contract", "",
           "| Contract | Trades | Net USD | Max closed DD USD |",
           "|---|---:|---:|---:|"]
    for contract, a in sorted(results["byContract"]["ml"].items()):
        md.append(f"| {contract} | {a['trades']} | {a['netUsd']:,.2f} | "
                  f"{a['maxClosedEquityDrawdownUsd']:,.2f} |")
    md += ["", "These results use previously inspected validation data and fixed",
           "simplified OHLC assumptions. They must not be used to claim an",
           "out-of-sample edge. Negative or positive numbers are both research",
           "diagnostics, not broker-executable PnL.", ""]
    (output_dir / "es-ml-bracket-research-v1.md").write_text("\n".join(md), "utf-8")
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True,
                        help="ONLY user's locally generated, trusted research joblib")
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    a = parser.parse_args()
    result = execute(a.features, a.model, a.audit, a.output_dir)
    print("[NinjexAI-TRADE] Status:", result["status"])
    print("[NinjexAI-TRADE] Validation rows:", result["validationRowsScored"])
    print("[NinjexAI-TRADE] Holdout untouched:", result["holdoutRowsNotScored"])
    print("[NinjexAI-TRADE] Simulated decisions:",
          result["results"]["ml"]["trades"])
    for name, r in result["results"].items():
        print("[NinjexAI-TRADE]", name, "netUSD=", r["netUsd"],
              "maxClosedDD=", r["maxClosedEquityDrawdownUsd"],
              "winRate=", r["winRate"])
    print("[NinjexAI-TRADE] Output:", a.output_dir)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
