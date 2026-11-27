#!/usr/bin/env python3
"""V8: freeze v7 first REJECT_SHORT opening-range event => LONG.

The v7 candidate was chosen post hoc from 4 categories x 2 directions.
Score only previously-inspected Sep2025-Jun14 2026 validation; final
Jun15 2026+ holdout prices are never read. Fixed 12/24 ticks,
30 minutes, pessimistic same-minute stops, 1 adverse tick each side,
$5 round-trip fee. No ML fit, optimisation or NinjaTrader orders.

Report same-event SHORT and descriptive same-contract/same-Eastern-
clock LONG outcomes on other non-event days; no clock controls influence
event selection. This is research, NOT independent confirmation.
"""
import argparse
import datetime as dt
import json
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_ml_features import NY
    from tools.ai_market_train_baseline import read_verified_csv
    from tools.ai_market_bracket_research import minute_window, simulate, summarize
    from tools.ai_market_frozen_reverse_v6 import read_raw_before, HOLDOUT_CUTOFF_UTC
    from tools.ai_market_ny_opening_range_v7 import (
        RANGE_CLOCKS, SIGNAL_CLOCKS, first_event, utc_close,
    )
else:
    from ai_market_ml_features import NY
    from ai_market_train_baseline import read_verified_csv
    from ai_market_bracket_research import minute_window, simulate, summarize
    from ai_market_frozen_reverse_v6 import read_raw_before, HOLDOUT_CUTOFF_UTC
    from ai_market_ny_opening_range_v7 import (
        RANGE_CLOCKS, SIGNAL_CLOCKS, first_event, utc_close,
    )

VALIDATION_START = dt.date(2025, 9, 1)
HOLDOUT_START = dt.date(2026, 6, 15)
FROZEN_EVENT = "REJECT_SHORT"
STOP_TICKS, TARGET_TICKS, HOLD_MINUTES = 12, 24, 30
MIN_OTHER_DAYS = 10


def good_asof(stamp, data, five):
    subset = data.loc[data["utcClose"] == pd.Timestamp(stamp)]
    if len(subset) != 1:
        return False
    item = subset.iloc[0]
    current = five.get(stamp)
    if current is None or "INVALID" in current.flags:
        return False
    if pd.to_numeric(item["fine1mComplete"], errors="coerce") != 1:
        return False
    if str(item["qualityFlags"]) != "OK":
        return False
    if abs(float(item["close"]) - current.close) > 1e-7:
        raise ValueError("Feature 5m closing price differs from original export")
    return True


def non_event_control(day, clock, data, five, minute):
    # The entire RTH range and preceding post-range 5m path are present.
    for at in list(RANGE_CLOCKS) + [v for v in SIGNAL_CLOCKS if v <= clock]:
        t = utc_close(day, at)
        b = five.get(t)
        if b is None or "INVALID" in b.flags:
            return None
    t = utc_close(day, clock)
    if t + dt.timedelta(minutes=HOLD_MINUTES) >= HOLDOUT_CUTOFF_UTC:
        return None
    if not good_asof(t, data, five):
        return None
    horizon = minute_window(minute, t)
    return (simulate(horizon, 1, STOP_TICKS, TARGET_TICKS)["netUsd"]
            if horizon is not None else None)


def collect_events(validation, basket, tag):
    if set(validation["researchSplit"]) != {"validation"}:
        raise ValueError("Only strictly validation source features allowed")
    data = validation.copy()
    data["utcClose"] = pd.to_datetime(data["decisionUtc"], utc=True)
    data["etDate"] = data["utcClose"].dt.tz_convert(NY).dt.date
    if any(day < VALIDATION_START or day >= HOLDOUT_START
           for day in data["etDate"]):
        raise ValueError("Validation dates cross final holdout or previous training")
    multi = data.groupby("etDate")["contract"].nunique()
    ambiguous = set(multi.loc[multi > 1].index)
    paths = {entry["contract"]: Path(entry["folders"][0])
             for entry in basket["contracts"] if len(entry.get("folders", [])) == 1}
    selected, controls, excluded = [], defaultdict(list), Counter()
    for contract, group in data.groupby("contract", sort=True):
        if contract not in paths:
            raise ValueError("Missing unique dated ES contract export " + contract)
        folder = paths[contract]
        raw_meta = json.loads((folder/"manifest.json").read_text("utf-8-sig"))
        if raw_meta.get("contract") != contract or raw_meta.get("datasetTag") != tag:
            raise ValueError("Dated ES raw source provenance mismatch")
        # The raw loader STOPS before June 15 2026 ET. Do not decode
        # future holdout OHLC, even on the cross-quarter ES 06-26.
        five = read_raw_before(folder, 5, contract, HOLDOUT_CUTOFF_UTC)
        one = read_raw_before(folder, 1, contract, HOLDOUT_CUTOFF_UTC)
        for day, day_rows in group.groupby("etDate", sort=True):
            if day in ambiguous:
                excluded["multipleDatedContracts"] += 1
                continue
            if day.weekday() >= 5:
                continue
            event, why = first_event(day, five)
            if event is not None and event[0] == FROZEN_EVENT:
                _, stamp, high, low = event
                if stamp + dt.timedelta(minutes=HOLD_MINUTES) >= HOLDOUT_CUTOFF_UTC:
                    excluded["futureTouchesHoldout"] += 1
                    continue
                if not good_asof(stamp, day_rows, five):
                    excluded["badAsOfFeatures"] += 1
                    continue
                horizon = minute_window(one, stamp)
                if horizon is None:
                    excluded["incompleteFutureOneMinute"] += 1
                    continue
                long = simulate(horizon, 1, STOP_TICKS, TARGET_TICKS)
                short = simulate(horizon, -1, STOP_TICKS, TARGET_TICKS)
                clock = stamp.astimezone(NY)
                selected.append({
                    "contract": contract,
                    "easternDate": day.isoformat(),
                    "decisionUtc": stamp.isoformat(),
                    "month": day.strftime("%Y-%m"),
                    "clockMinute": clock.hour*60 + clock.minute,
                    "orWidthTicks": round((high-low)/.25, 2),
                    "longUsd": long["netUsd"], "shortUsd": short["netUsd"],
                    "longReason": long["exitReason"],
                    "shortReason": short["exitReason"],
                    "longAmbiguous": long["bothTouched1m"],
                    "shortAmbiguous": short["bothTouched1m"]
                })
                # Do not use ANY selected event day as a clock baseline.
                continue
            excluded[why if event is None else "differentFirstEvent"] += 1
            for clock in SIGNAL_CLOCKS:
                value = non_event_control(day, clock, day_rows, five, one)
                if value is not None:
                    controls[(contract, clock)].append(float(value))
    selected.sort(key=lambda r: r["decisionUtc"])
    if len({r["easternDate"] for r in selected}) != len(selected):
        raise ValueError("More than one event on the same New York market day")
    return selected, controls, dict(sorted(excluded.items()))


def side_stats(records, side):
    return summarize([
        {"decisionUtc": r["decisionUtc"], "month": r["month"],
         "netUsd": r[side+"Usd"], "exitReason": r[side+"Reason"],
         "bothTouched1m": r[side+"Ambiguous"]}
        for r in records])


def bootstrap(numbers):
    if not numbers:
        return {"observations": 0, "meanUsd": None, "ci95": None}
    array = np.array(numbers, dtype=float)
    rng = np.random.default_rng(42)
    choices = rng.integers(0, len(array), (1500, len(array)))
    samples = array[choices].mean(axis=1)
    return {"observations": len(array),
            "meanUsd": round(float(array.mean()), 3),
            "ci95": [round(float(x), 3)
                     for x in np.quantile(samples, [.025,.975])]}


def research(features, audit, out):
    meta = json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    basket = json.loads(audit.read_text("utf-8-sig"))
    if basket.get("schema") != "ninjex-basket-audit-v1" or (
        basket.get("datasetTag") != meta.get("datasetTag")
    ):
        raise ValueError("Source basket/feature tags differ")
    frame = read_verified_csv(features, meta)
    counts = {name: int((frame["researchSplit"] == name).sum())
              for name in ("train", "validation", "holdout")}
    validation = frame.loc[frame["researchSplit"] == "validation"].copy()
    del frame
    events, controls, exclusions = collect_events(
        validation, basket, meta["datasetTag"])
    for r in events:
        values = controls.get((r["contract"], r["clockMinute"]), [])
        r["nonEventPeerDays"] = len(values)
        r["nonEventClockLongMeanUsd"] = (
            round(float(np.mean(values)), 5)
            if len(values) >= MIN_OTHER_DAYS else None)
        r["longMinusClockMeanUsd"] = (
            round(r["longUsd"] - r["nonEventClockLongMeanUsd"], 5)
            if r["nonEventClockLongMeanUsd"] is not None else None)
    matched = [r["longMinusClockMeanUsd"] for r in events
               if r["longMinusClockMeanUsd"] is not None]
    quarters = {}
    for start, end, name in (
        ("2025-09", "2025-09", "2025_Q3_partial"),
        ("2025-10", "2025-12", "2025_Q4"),
        ("2026-01", "2026-03", "2026_Q1"),
        ("2026-04", "2026-06", "2026_Q2_partial"),
    ):
        subset = [r for r in events if start <= r["month"] <= end]
        quarters[name] = {"long": side_stats(subset, "long"),
                          "short": side_stats(subset, "short")}
    result = {
        "schema": "ninjex-ny-upper-sweep-v8",
        "status": "POSTHOC_SUBGROUP_REUSED_VALIDATION_NOT_PROVEN_EDGE",
        "sourceAuditStatus": basket.get("status"),
        "frozenTrainingObservation": {
            "event": "V7 FIRST REJECT_SHORT then LONG",
            "netUsd": 3027.5, "events": 82,
            "earlyTrainNetUsd": 1562.5, "lateTrainNetUsd": 1465.,
            "closedDrawdownUsd": 1190.
        },
        "frozenRules": {
            "openingRange": "09:30-10:00 NY completed 5m opening range",
            "event": "FIRST REJECT_SHORT closes after sweeping range high, 10:05-11:30 ET",
            "direction": "LONG only",
            "oneTradePerDay": True,
            "entry": "next observed 1m candle open",
            "stopTicks": STOP_TICKS, "targetTicks": TARGET_TICKS,
            "maximumMinutes": HOLD_MINUTES,
            "adverseSlippageTicksPerSide": 1, "commissionRoundTripUsd": 5.,
            "sameOneMinuteStopAndTarget": "STOP FIRST",
            "comparator": "all eligible OTHER validation days, same dated ES contract and same NY clock; 10+ peers. No control outcome influences signal selection"
        },
        "sourceSplitRows": {
            "trainingNotScored": counts["train"],
            "reusedValidationUsed": counts["validation"],
            "finalHoldoutNotScored": counts["holdout"],
            "finalHoldoutRawNotLoaded": True
        },
        "exclusions": exclusions,
        "selectedEvents": len(events),
        "longOnEvents": side_stats(events, "long"),
        "shortSameTimestamp": side_stats(events, "short"),
        "longMinusShortBootstrap": bootstrap([
            r["longUsd"] - r["shortUsd"] for r in events]),
        "longMinusMatchedContractClockBaseline": bootstrap(matched),
        "eventsWithEligibleClockBaseline": len(matched),
        "eventsWithoutEligibleClockBaseline": len(events) - len(matched),
        "clockControlCells": len(controls),
        "quarterlyDescriptive": quarters,
        "warnings": [
            "REJECT_SHORT -> LONG was selected post hoc from v7 four event families and both sides.",
            "The Sep2025-Jun2026 validation period was ALREADY inspected in older studies. NOT independent replication.",
            "The final June15 2026+ holdout is completely unscored and no holdout raw OHLC is parsed.",
            "Other-day clock baseline controls may be reused across events; bootstrap treats control means as fixed and understates uncertainty.",
            "No model tuning or parameter changes. Only fixed signal outcomes in 1m OHLC are examined.",
            "The 12/24-tick 30m trade and $30 roundtrip assumed costs are not broker/tick execution verified.",
            "Closed-equity drawdown is NOT intraday unrealized trailing prop-firm drawdown.",
            "Roughly 41 trades/year at a $270 maximum nominal target payout cannot reliably generate $10k/year without materially greater exposure."
        ]
    }
    out.mkdir(parents=True, exist_ok=True)
    (out/"es-ny-upper-sweep-v8.json").write_text(
        json.dumps(result, indent=2), encoding="utf-8")
    if events:
        pd.DataFrame(events).to_csv(
            out/"es-ny-upper-sweep-v8-events.csv", index=False)
    summary = [
        "# Ninjex AI v8 — frozen NY upper opening-range sweep LONG",
        "",
        "**Post-hoc v7 hypothesis, reused forward validation; NOT independent confirmation.**",
        "",
        "V7 training: 82 entries, +$3,027.50 net; early +$1,562.50, late +$1,465.",
        "Frozen forward entries: {}; reserved holdout unscored: {}.".format(
            len(events), counts["holdout"]),
        "",
        "| Same-timestamp direction | Trades | Net USD | Mean USD | PF | Closed DD |",
        "|---|---:|---:|---:|---:|---:|"
    ]
    for label, stats in (("LONG", result["longOnEvents"]),
                         ("SHORT", result["shortSameTimestamp"])):
        summary.append("| {} | {} | {} | {} | {} | {} |".format(
            label, stats["trades"], stats["netUsd"],
            stats["meanPerTradeUsd"], stats["profitFactor"],
            stats["maxClosedEquityDrawdownUsd"]))
    summary += [
        "",
        "## Non-event same-contract and same-clock LONG baseline",
        "",
        "Event observations with 10+ eligible other days: {}.".format(len(matched)),
        "Mean LONG event advantage over descriptive clock baseline: {} USD".format(
            result["longMinusMatchedContractClockBaseline"]["meanUsd"]),
        "Bootstrap interval (underestimates comparator uncertainty): {}".format(
            result["longMinusMatchedContractClockBaseline"]["ci95"]),
        "",
        "## Quarter-by-quarter (descriptive; no seasonal optimisation)"
    ]
    for period, item in quarters.items():
        x = item["long"]
        summary.append("- {}: {} entries, net USD {}, PF {}".format(
            period, x["trades"], x["netUsd"], x["profitFactor"]))
    summary += [
        "", "This is NOT a live trading recommendation.",
        "No unseen June2026+ final holdout, no full tick fills, no prop trailing DD.",""
    ]
    (out/"es-ny-upper-sweep-v8.md").write_text(
        "\n".join(summary), encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", type=Path, required=True)
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    result = research(args.features, args.audit, args.output_dir)
    print("[NinjexAI-V8] Frozen upper-sweep events:", result["selectedEvents"])
    print("[NinjexAI-V8] Clock-matched controls:",
          result["eventsWithEligibleClockBaseline"])
    print("[NinjexAI-V8] Final holdout NOT SCORED:",
          result["sourceSplitRows"]["finalHoldoutNotScored"])
    print("[NinjexAI-V8] Output:", args.output_dir/"es-ny-upper-sweep-v8.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
