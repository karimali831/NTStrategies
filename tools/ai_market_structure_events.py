#!/usr/bin/env python3
"""Ninjex AI v4 — predefined ES price-structure event study, TRAIN ONLY.

On completed 5-minute bars, investigate a 12-bar prior-high/low
BREAKOUT_CONTINUATION and a 12-bar high/low SWEEP_REJECTION, with
mirror-image long and short definitions. NO model training or optimisation.

Require all 12 preceding bars to be 5-minute-contiguous. Trigger using
only the just-CLOSED five-minute candle and its earlier candles.
Hypothetical entry next one-minute open, fixed 12/24 ES ticks, 30m
maximum, 1 tick adverse slippage per side and $5 round-trip commission.
Same-minute TP+SL means STOP first. Missing future 1m => drop.

Training rows before Sep 1 2025 ET only. Prior-used validation and
reserved June 2026+ holdout remain unscored. This is exploratory
hypothesis research, not trading advice or a live execution backtest.
"""
import argparse
import datetime as dt
import json
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_ml_features import NY, read_bars
    from tools.ai_market_train_baseline import read_verified_csv
    from tools.ai_market_bracket_research import (
        close_time, minute_window, simulate, summarize,
    )
else:
    from ai_market_ml_features import NY, read_bars
    from ai_market_train_baseline import read_verified_csv
    from ai_market_bracket_research import close_time, minute_window, simulate, summarize

TICK = 0.25
PRIOR_BARS = 12
STOP_TICKS = 12
TARGET_TICKS = 24
HOLD_MINUTES = 30
NO_OVERLAP_MINUTES = 30
TRAIN_END_DATE = dt.date(2025, 9, 1)
EARLY_END_DATE = dt.date(2024, 9, 1)
MIN_GROUP_SIZE = 50
TRIGGERS = (
    "BREAKOUT_LONG", "BREAKOUT_SHORT", "REJECTION_LONG", "REJECTION_SHORT"
)


def detect_trigger(current, preceding):
    """Use ONLY completed current bar and 12 PRIOR consecutive 5m bars.

    Return None if multiple conflicting definitions match, to avoid
    selecting a direction based on later results or code order.
    """
    if len(preceding) != PRIOR_BARS:
        raise ValueError("Must supply exactly 12 earlier closed bars")
    highest = max(x.high for x in preceding)
    lowest = min(x.low for x in preceding)
    long_break = current.close >= highest + TICK and current.close > current.open
    short_break = current.close <= lowest - TICK and current.close < current.open
    long_reject = (current.low <= lowest - TICK
                   and current.close >= lowest + TICK
                   and current.close > current.open)
    short_reject = (current.high >= highest + TICK
                    and current.close <= highest - TICK
                    and current.close < current.open)
    found = [name for yes, name in (
        (long_break, "BREAKOUT_LONG"),
        (short_break, "BREAKOUT_SHORT"),
        (long_reject, "REJECTION_LONG"),
        (short_reject, "REJECTION_SHORT"),
    ) if yes]
    return found[0] if len(found) == 1 else None


def session_name(row):
    if int(row["sessionLondon"]) and int(row["sessionNewYork"]):
        return "LONDON_NY_OVERLAP"
    if int(row["sessionNewYork"]):
        return "NEW_YORK"
    if int(row["sessionLondon"]):
        return "LONDON"
    if int(row["sessionAsia"]):
        return "ASIA"
    return "OTHER"


def clean_row(row):
    return (int(row["fine1mComplete"]) == 1
            and str(row["qualityFlags"]) == "OK")


def prior_12_contiguous(bars_by_close, time):
    """Return chronological prior bars ending at t-5, ... t-60 minutes."""
    timestamps = [time - dt.timedelta(minutes=5 * i) for i in range(PRIOR_BARS, 0, -1)]
    rows = [bars_by_close.get(t) for t in timestamps]
    if any(x is None or "INVALID" in x.flags for x in rows):
        return None
    return rows


def choose_chronological_events(events):
    """Deterministically keep earliest eligible trigger, with 30m separation.

    Collisions across long, short, breakout and rejection count as one
    mutually exclusive candidate pool, not four simultaneous systems.
    """
    keep = []
    for contract in sorted({x["contract"] for x in events}):
        last = None
        contract_rows = sorted(
            (row for row in events if row["contract"] == contract),
            key=lambda row: row["decisionUtc"])
        for row in contract_rows:
            time = close_time(row["decisionUtc"])
            if last is not None and time < last + dt.timedelta(minutes=NO_OVERLAP_MINUTES):
                continue
            keep.append(row)
            last = time
    return keep


def pnl_summary(rows, use_opposite=False):
    trades = []
    for r in rows:
        pnl = r["oppositeUsd"] if use_opposite else r["netUsd"]
        trades.append({
            "decisionUtc": r["decisionUtc"],
            "month": r["month"],
            "netUsd": pnl,
            "exitReason": r["oppositeReason"] if use_opposite else r["exitReason"],
            "bothTouched1m": r["oppositeAmbiguous"] if use_opposite else r["bothTouched1m"],
        })
    stats = summarize(trades)
    stats["meanVsOppositeUsd"] = (
        round(float(np.mean([r["netUsd"]-r["oppositeUsd"] for r in rows])), 3)
        if rows else None)
    return stats


def paired_day_bootstrap(rows):
    """Day-block resampling for chosen-side minus opposite-side net USD.

    This interval is DESCRIPTIVE on training history, not a p-value
    for a new, out-of-sample or multiple-comparison-corrected edge.
    """
    if not rows:
        return {"days": 0, "meanUsd": None, "ci95": None}
    daily = defaultdict(list)
    for row in rows:
        daily[row["easternDate"]].append(row["netUsd"] - row["oppositeUsd"])
    daymeans = np.array([np.mean(x) for x in daily.values()])
    counts = np.array([len(x) for x in daily.values()])
    values = np.array([sum(x) for x in daily.values()])
    rng = np.random.default_rng(42)
    draws = rng.integers(0, len(daymeans), (1000, len(daymeans)))
    bootstrap = values[draws].sum(axis=1) / counts[draws].sum(axis=1)
    return {
        "days": len(daily),
        "meanUsd": round(float(values.sum()/counts.sum()), 4),
        "ci95": [round(float(v), 4)
                 for v in np.quantile(bootstrap, [0.025, 0.975])]
    }


def summarize_dimension(rows, dimension):
    result = {}
    values = sorted({r[dimension] for r in rows})
    for v in values:
        subset = [r for r in rows if r[dimension] == v]
        result[str(v)] = {
            "chosen": pnl_summary(subset),
            "opposite": pnl_summary(subset, use_opposite=True),
            "smallSample": len(subset) < MIN_GROUP_SIZE,
            "pairedDifferenceDailyBootstrap": paired_day_bootstrap(subset),
        }
    return result


def analyze(features, audit, output_dir):
    manifest = json.loads(features.with_suffix(
        ".manifest.json").read_text(encoding="utf-8-sig"))
    basket = json.loads(audit.read_text(encoding="utf-8-sig"))
    if basket.get("schema") != "ninjex-basket-audit-v1" or (
            basket.get("datasetTag") != manifest.get("datasetTag")):
        raise ValueError("Feature/basket provenance mismatch")
    data = read_verified_csv(features, manifest)
    split_counts = {split: int((data["researchSplit"] == split).sum())
                    for split in ("train", "validation", "holdout")}
    # Drop both later splits before building an event or scoring its path.
    training = data.loc[data["researchSplit"] == "train"].copy()
    del data
    if training.empty:
        raise ValueError("No TRAIN source decisions")

    source_paths = {
        x["contract"]: Path(x["folders"][0])
        for x in basket["contracts"] if len(x.get("folders", [])) == 1
    }
    candidates = []
    excluded = Counter()
    seen_trigger = Counter()
    for contract, group in training.groupby("contract", sort=True):
        if contract not in source_paths:
            raise ValueError("No unique raw contract folder: " + contract)
        root = source_paths[contract]
        local_manifest = json.loads(
            (root / "manifest.json").read_text(encoding="utf-8-sig"))
        if local_manifest.get("contract") != contract or (
                local_manifest.get("datasetTag") != basket["datasetTag"]):
            raise ValueError("Raw source contract/tag mismatch")
        source5 = {b.end: b for b in read_bars(root, 5, contract)}
        source1 = {b.end: b for b in read_bars(root, 1, contract)}
        for _, row in group.iterrows():
            decision = close_time(row["decisionUtc"])
            et_date = decision.astimezone(NY).date()
            if et_date >= TRAIN_END_DATE:
                raise ValueError("TRAIN source date includes validation")
            if not clean_row(row):
                excluded["badDecisionSource"] += 1
                continue
            current = source5.get(decision)
            if current is None or "INVALID" in current.flags:
                excluded["missingDecision5m"] += 1
                continue
            if abs(current.close - float(row["close"])) > 1e-7:
                raise ValueError("Feature 5m close does not match raw source at " + decision.isoformat())
            preceding = prior_12_contiguous(source5, decision)
            if preceding is None:
                excluded["incompletePrior12x5m"] += 1
                continue
            event = detect_trigger(current, preceding)
            if event is None:
                excluded["notUniqueStructureTrigger"] += 1
                continue
            seen_trigger[event] += 1
            # Skip any future price path crossing the train/validation border.
            if (decision + dt.timedelta(minutes=HOLD_MINUTES)).astimezone(
                    NY).date() >= TRAIN_END_DATE:
                excluded["crossesSplitBoundary"] += 1
                continue
            future = minute_window(source1, decision)
            if future is None:
                excluded["incompleteFuture1m"] += 1
                continue
            side = 1 if event.endswith("_LONG") else -1
            result = simulate(future, side, STOP_TICKS, TARGET_TICKS)
            opposite = simulate(future, -side, STOP_TICKS, TARGET_TICKS)
            candidates.append({
                "decisionUtc": decision.isoformat(),
                "easternDate": et_date.isoformat(), "month": et_date.strftime("%Y-%m"),
                "period": "EARLY_TRAIN" if et_date < EARLY_END_DATE else "LATE_TRAIN",
                "contract": contract, "session": session_name(row), "trigger": event,
                "kind": event.split("_")[0],
                "direction": "LONG" if side == 1 else "SHORT",
                "netUsd": result["netUsd"],
                "exitReason": result["exitReason"],
                "bothTouched1m": result["bothTouched1m"],
                "oppositeUsd": opposite["netUsd"],
                "oppositeReason": opposite["exitReason"],
                "oppositeAmbiguous": opposite["bothTouched1m"]
            })
    selected = choose_chronological_events(candidates)
    result = {
        "schema": "ninjex-es-structural-events-v4",
        "status": "TRAIN_ONLY_EXPLORATORY_NOT_A_PROVEN_EDGE",
        "sourceFeatures": str(features),
        "sourceAudit": str(audit),
        "dataStatus": manifest.get("researchStatus"),
        "trainingRowsAvailable": split_counts["train"],
        "validationRowsNotScored": split_counts["validation"],
        "holdoutRowsNotScored": split_counts["holdout"],
        "eventCountsPreSpacing": dict(sorted(seen_trigger.items())),
        "fullyLabelledEventsPreSpacing": len(candidates),
        "nonOverlappingEvents": len(selected),
        "overlapsSuppressed": len(candidates) - len(selected),
        "excluded": dict(sorted(excluded.items())),
        "frozenRules": {
            "lookbackPrior5mBars": PRIOR_BARS,
            "breakout": "current close >= prior highest high +1tick and close>open (inverse for SHORT)",
            "rejection": "current low <= prior lowest low -1tick and close >= prior low +1tick and close>open; inverse for SHORT",
            "ambiguousMultipleTriggers": "discard",
            "hourlySampling": False,
            "minSeparationMinutesAcrossAllSignals": NO_OVERLAP_MINUTES,
            "entry": "next 1m OHLC open after completed triggering 5m candle",
            "stopTicks": STOP_TICKS, "targetTicks": TARGET_TICKS,
            "maxHoldingMinutes": HOLD_MINUTES,
            "slippageTicksPerSide": 1,
            "commissionRoundTripUsd": 5,
            "same1mStopAndTarget": "STOP_FIRST",
            "comparisons": "chosen structural direction vs exact same-time opposite direction",
            "time": "TRAIN decisions only before Sep 1 2025 ET",
        },
        "overall": {
            "chosen": pnl_summary(selected),
            "opposite": pnl_summary(selected, use_opposite=True),
            "pairedDifferenceDailyBootstrap": paired_day_bootstrap(selected)
        },
        "byTrigger": summarize_dimension(selected, "trigger"),
        "byKind": summarize_dimension(selected, "kind"),
        "byPeriod": summarize_dimension(selected, "period"),
        "bySession": summarize_dimension(selected, "session"),
        "byContract": summarize_dimension(selected, "contract"),
        "byTriggerPeriod": {
            event: summarize_dimension(
                [r for r in selected if r["trigger"] == event], "period")
            for event in TRIGGERS
        },
        "warnings": [
            "Signals and labels use only TRAIN dates; prior-used validation and final holdout untouched.",
            "No ML fitted, no threshold optimization, no search for a best post-hoc cell.",
            "Raw one-minute OHLC does NOT prove true intrabar tick order or executable broker fills.",
            "Historical source quality remains provisional; gaps are rejected, not interpolated.",
            "Day-block bootstrap is descriptive on training history; multiple testing and structural hypothesis selection need separate evaluation.",
            "Closed equity drawdown is not prop intraday unrealized trailing drawdown.",
            "The hourly random-entry benchmark v3 is NOT a matched timestamp or event-frequency baseline.",
            "If a period/regime looks favourable, it still requires a NEW independent forward/walk-forward validation."
        ]
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / "es-structure-v4.json").write_text(
        json.dumps(result, indent=2), encoding="utf-8")
    md = [
        "# Ninjex AI v4 — ES price-structure triggers, training only",
        "",
        "**Exploratory; no ML trained, no validation/holdout data scored.**",
        "",
        "Raw events before minimum spacing: {}, selected nonoverlapping: {}.".format(
            len(candidates), len(selected)),
        "Validation untouched: {:,}; holdout untouched: {:,}.".format(
            split_counts["validation"], split_counts["holdout"]),
        "",
        "| Trigger | Selected | Chosen net USD | Chosen avg/trade | PF | Opposite net USD | Avg advantage vs opposite |",
        "|---|---:|---:|---:|---:|---:|---:|"
    ]
    for name in TRIGGERS:
        row = result["byTrigger"].get(name)
        if row is None:
            md.append("| {} | 0 | — | — | — | — | — |".format(name))
            continue
        a = row["chosen"]
        b = row["opposite"]
        md.append("| {} | {} | {} | {} | {} | {} | {} |".format(
            name, a["trades"], a["netUsd"], a["meanPerTradeUsd"],
            a["profitFactor"], b["netUsd"], a["meanVsOppositeUsd"]))
    md.extend(["", "## Early-vs-late training comparison", ""])
    for name in TRIGGERS:
        md.append("### "+name)
        for period, item in result["byTriggerPeriod"][name].items():
            stats = item["chosen"]
            md.append("- {}: {} observations, mean {} USD, PF {}, "
                      "vs opposite mean advantage {} USD".format(
                          period, stats["trades"], stats["meanPerTradeUsd"],
                          stats["profitFactor"], stats["meanVsOppositeUsd"]))
    md.extend(["", "No parameters were optimized; this is a test of"
               " predeclared structure-event hypotheses, not proof of"
               " a tradeable edge.", "Any promising result needs"
               " independent chronological confirmation and later"
               " tick/real-execution risk testing.", ""])
    (output_dir / "es-structure-v4.md").write_text("\n".join(md), encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", type=Path, required=True)
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    report = analyze(args.features, args.audit, args.output_dir)
    print("[NinjexAI-STRUCTURE] Candidates:",
          report["fullyLabelledEventsPreSpacing"])
    print("[NinjexAI-STRUCTURE] Nonoverlapping:",
          report["nonOverlappingEvents"])
    print("[NinjexAI-STRUCTURE] Validation untouched:",
          report["validationRowsNotScored"])
    print("[NinjexAI-STRUCTURE] Holdout untouched:",
          report["holdoutRowsNotScored"])
    print("[NinjexAI-STRUCTURE] Report:", args.output_dir / "es-structure-v4.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
