#!/usr/bin/env python3
"""Ninjex AI v6 — frozen v5 reversal hypothesis, forward stress (NOT unseen).

Refit the EXACT v5 early-TRAIN model, with unchanged predictors, settings
and predicted-net gates; do NOT fit it to later TRAIN or validation.
Evaluate frozen model, reverse direction, always-long and always-short
at precisely the same qualifying structural-event decision timestamps
in Sep2025–Jun14 2026 ET (previously INSPECTED validation period).
June15 2026+ final holdout is never scored and its raw bars are not read.

NO retraining or threshold sweep using observed challenge outcomes.
Research ONLY: 1m OHLC stop-first simulated 12/24 ticks/30min; 1 tick
adverse slip at entry + exit, $5 round-trip; neither tick execution nor
true intraday prop-firm drawdown are validated.
"""
import argparse
import csv
import datetime as dt
import json
import math
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_ml_features import Bar, NY
    from tools.ai_market_train_baseline import BASE_FEATURES, read_verified_csv
    from tools.ai_market_structure_events import (
        analyze as rebuild_v4_training, detect_trigger, prior_12_contiguous,
        choose_chronological_events, clean_row, session_name,
        STOP_TICKS, TARGET_TICKS, HOLD_MINUTES,
    )
    from tools.ai_market_bracket_research import close_time, minute_window, simulate, summarize
    from tools.ai_market_event_meta import (
        model, predictors, direction_labels, choose_trades,
        MIN_NET_FORECAST, MIN_DIRECTION_GAP, MAX_TRADES_PER_DAY,
    )
else:
    from ai_market_ml_features import Bar, NY
    from ai_market_train_baseline import BASE_FEATURES, read_verified_csv
    from ai_market_structure_events import (
        analyze as rebuild_v4_training, detect_trigger, prior_12_contiguous,
        choose_chronological_events, clean_row, session_name,
        STOP_TICKS, TARGET_TICKS, HOLD_MINUTES,
    )
    from ai_market_bracket_research import close_time, minute_window, simulate, summarize
    from ai_market_event_meta import (
        model, predictors, direction_labels, choose_trades,
        MIN_NET_FORECAST, MIN_DIRECTION_GAP, MAX_TRADES_PER_DAY,
    )

UTC = dt.timezone.utc
VALIDATION_START = dt.date(2025, 9, 1)
HOLDOUT_START = dt.date(2026, 6, 15)
# The actual start instant of the final New York-local calendar date.
HOLDOUT_CUTOFF_UTC = dt.datetime(2026, 6, 15, tzinfo=NY).astimezone(UTC)
POLICIES = ("modelDirection", "reverseModelDirection", "alwaysLong", "alwaysShort")
RESEARCH_ONLY_STATUS = "REUSED_VALIDATION_STRESS_ONLY_NOT_A_PROVEN_EDGE"


def read_raw_before(folder, frame, contract, cutoff_utc):
    """Only touch historical rows BEFORE final holdout start.

    The first holdout record's timestamp is inspected to stop the CSV
    stream but its price/flags/volume are never loaded or interpreted.
    This permits a June-2026 rollover file spanning both splits.
    """
    result = {}
    filename = folder / ("bars_" + str(frame) + "m.csv")
    previous = None
    with filename.open(newline="", encoding="utf-8-sig") as stream:
        for n, raw in enumerate(csv.DictReader(stream), 2):
            instant = dt.datetime.fromisoformat(raw["endUtc"].replace("Z", "+00:00"))
            if instant.tzinfo is None:
                raise ValueError(f"{filename}:{n} has naive timestamps")
            instant = instant.astimezone(UTC)
            if instant >= cutoff_utc:
                break
            if previous is not None and instant <= previous:
                raise ValueError(f"{filename}:{n} source timestamps reverse")
            if raw["contract"] != contract or int(raw["frameMinutes"]) != frame:
                raise ValueError(f"{filename}:{n} source contract/frame mismatch")
            values = [float(raw[key]) for key in ("open","high","low","close","volume")]
            if not all(math.isfinite(v) for v in values):
                raise ValueError(f"{filename}:{n} invalid finite source OHLCV")
            result[instant] = Bar(instant, *values, raw.get("flags", ""))
            previous = instant
    return result


def extract_validation_events(validation, basket, tag):
    """Same v4 structural event selection but validation-only bounded prices.

    All decisions are formed from PRIOR+current completed candles;
    their later outcomes are attached for AFTER the frozen predictors
    determine the research policy. No holdout OHLCV is accessed.
    """
    if not set(validation["researchSplit"]).issubset({"validation"}):
        raise ValueError("Only validation feature decisions may be extracted")
    sources = {}
    for item in basket["contracts"]:
        paths = item.get("folders", [])
        if len(paths) == 1:
            sources[item["contract"]] = Path(paths[0])
    candidates = []
    dropped = Counter()
    pre_spacing = Counter()
    for contract, group in validation.groupby("contract", sort=True):
        if contract not in sources:
            raise ValueError("Missing or ambiguous original contract export " + contract)
        folder = sources[contract]
        raw_manifest = json.loads((folder / "manifest.json").read_text("utf-8-sig"))
        if raw_manifest.get("contract") != contract or raw_manifest.get("datasetTag") != tag:
            raise ValueError("Historical source provenance mismatch: " + contract)
        one = read_raw_before(folder, 1, contract, HOLDOUT_CUTOFF_UTC)
        five = read_raw_before(folder, 5, contract, HOLDOUT_CUTOFF_UTC)
        for _, row in group.iterrows():
            decision = close_time(row["decisionUtc"])
            et_date = decision.astimezone(NY).date()
            if not VALIDATION_START <= et_date < HOLDOUT_START:
                raise ValueError("Unexpected or future validation Eastern date")
            if not clean_row(row):
                dropped["badDecisionSource"] += 1
                continue
            if (decision + dt.timedelta(minutes=HOLD_MINUTES)) >= HOLDOUT_CUTOFF_UTC:
                dropped["futureCrossesFinalHoldout"] += 1
                continue
            current = five.get(decision)
            if current is None or "INVALID" in current.flags:
                dropped["missingDecision5m"] += 1
                continue
            if abs(current.close - float(row["close"])) > 1e-7:
                raise ValueError("Raw source and as-of feature 5m close disagree")
            prior = prior_12_contiguous(five, decision)
            if prior is None:
                dropped["incompletePrior5mHistory"] += 1
                continue
            event = detect_trigger(current, prior)
            if event is None:
                dropped["noUniqueStructureTrigger"] += 1
                continue
            pre_spacing[event] += 1
            horizon = minute_window(one, decision)
            if horizon is None:
                dropped["incompleteFuture1m"] += 1
                continue
            long = simulate(horizon, 1, STOP_TICKS, TARGET_TICKS)
            short = simulate(horizon, -1, STOP_TICKS, TARGET_TICKS)
            asof = {}
            for name in BASE_FEATURES + ("easternMinuteOfDay",):
                number = pd.to_numeric(row[name], errors="coerce")
                asof["feature_" + name] = float(number) if pd.notna(number) else float("nan")
            candidates.append({
                **asof,
                "contract": contract,
                "decisionUtc": decision.isoformat(),
                "easternDate": et_date.isoformat(),
                "month": et_date.strftime("%Y-%m"),
                "session": session_name(row),
                "trigger": event,
                "actualLong": float(long["netUsd"]),
                "actualShort": float(short["netUsd"]),
                "longReason": long["exitReason"],
                "shortReason": short["exitReason"],
                "longAmbiguous": long["bothTouched1m"],
                "shortAmbiguous": short["bothTouched1m"],
                "longStopGap": long["exitReason"] == "STOP_GAP",
                "shortStopGap": short["exitReason"] == "STOP_GAP",
            })
    disjoint = choose_chronological_events(candidates)
    return pd.DataFrame(disjoint), {
        "eligibleEventsBeforeSpacing": len(candidates),
        "nonOverlappingEvents": len(disjoint),
        "overlapSuppressed": len(candidates) - len(disjoint),
        "eventCountBeforeOutcomeAndSpacing": dict(pre_spacing),
        "excluded": dict(sorted(dropped.items())),
    }


def fit_early_v5(events):
    if "period" not in events:
        raise ValueError("Missing frozen early/late TRAIN provenance")
    early = events.loc[events["period"] == "EARLY_TRAIN"].copy()
    late = events.loc[events["period"] == "LATE_TRAIN"].copy()
    if len(early) < 500 or len(late) < 500:
        raise ValueError("Expected v5 original early and late training slices")
    if (pd.to_datetime(early["easternDate"]).dt.date >= dt.date(2024, 9, 1)).any():
        raise ValueError("Early model would see later-training dates")
    if (pd.to_datetime(late["easternDate"]).dt.date >= VALIDATION_START).any():
        raise ValueError("Late training reaches previously-used validation")
    x = predictors(early)
    long_labels, short_labels = direction_labels(early)
    ml_long = model()
    ml_short = model()
    ml_long.fit(x, long_labels)
    ml_short.fit(x, short_labels)
    return ml_long, ml_short, {"earlyFitEvents":len(early),
                               "lateTrainingNotRefit":len(late)}


def select_frozen_policies(raw_events, models):
    """Generate eligible timestamps ONLY from frozen v5 forecast gates.

    Selection is independent of realized LONG/SHORT outcome. PnLs are
    joined only after the exact v5 event and day policy has selected.
    """
    if raw_events.empty:
        return raw_events.copy(), pd.DataFrame()
    x = predictors(raw_events)
    long_pred = models[0].predict(x)
    short_pred = models[1].predict(x)
    if not np.isfinite(long_pred).all() or not np.isfinite(short_pred).all():
        raise ValueError("Cannot score a nonfinite validation forecast")
    scored = raw_events.copy()
    scored["predictedLong"] = long_pred
    scored["predictedShort"] = short_pred
    scored["predictedBest"] = np.maximum(long_pred, short_pred)
    scored["predictedGap"] = np.abs(long_pred - short_pred)
    scored["modelSide"] = np.where(long_pred >= short_pred, "LONG", "SHORT")
    # Reuse IDENTICAL v5 chooser, $20/$10 gate and maximum 2 Eastern day.
    selected = choose_trades(scored)
    return scored, selected


def side_for_policy(row, name):
    if name == "alwaysLong":
        return "LONG"
    if name == "alwaysShort":
        return "SHORT"
    if name == "modelDirection":
        return str(row["modelSide"])
    if name == "reverseModelDirection":
        return "SHORT" if row["modelSide"] == "LONG" else "LONG"
    raise ValueError("Unknown policy")


def as_trades(selected, policy):
    trades = []
    for _, row in selected.iterrows():
        side = side_for_policy(row, policy)
        lower = side.lower()
        trades.append({
            "decisionUtc": str(row["decisionUtc"]), "month": str(row["month"]),
            "easternDate": str(row["easternDate"]),
            "contract": str(row["contract"]), "session": str(row["session"]),
            "side": side,
            "netUsd": float(row["actual" + side.title()]),
            "exitReason": str(row[lower + "Reason"]),
            "bothTouched1m": bool(row[lower + "Ambiguous"]),
        })
    return trades


def summary(trades):
    data = summarize(trades)
    by = {}
    for field in ("contract", "session"):
        by[field] = {}
        for value in sorted({item[field] for item in trades}):
            by[field][value] = summarize([r for r in trades if r[field] == value])
    data["by"] = by
    return data


def daily_ci(trades, baseline=None, repeats=2000):
    """Descriptive day-block confidence intervals for per-entry mean.

    For paired comparisons dates and entries MUST match exactly;
    sample both trade policies by common calendar date and order.
    """
    if not trades:
        return {"days":0, "meanUsd":None, "ci95":None}
    a = sorted(trades, key=lambda x: x["decisionUtc"])
    if baseline is not None:
        b = sorted(baseline, key=lambda x: x["decisionUtc"])
        if [(x["decisionUtc"], x["contract"]) for x in a] != [
                (x["decisionUtc"], x["contract"]) for x in b]:
            raise ValueError("Paired comparison requires IDENTICAL timestamps")
    day = defaultdict(lambda: [0.0, 0])
    for idx, r in enumerate(a):
        delta = float(r["netUsd"]) - (float(b[idx]["netUsd"]) if baseline else 0.)
        day[r["easternDate"]][0] += delta
        day[r["easternDate"]][1] += 1
    values = np.asarray(list(day.values()), dtype=float)
    rng = np.random.default_rng(42)
    indexes = rng.integers(0, len(values), (repeats, len(values)))
    draws = values[indexes]
    means = draws[:,:,0].sum(axis=1) / draws[:,:,1].sum(axis=1)
    return {
        "days":len(day),
        "meanUsd":round(float(values[:,0].sum()/values[:,1].sum()), 3),
        "ci95":[round(float(x),3) for x in np.quantile(means, [.025,.975])]
    }


def test_frozen_forward(features, audit, output_dir):
    output_dir.mkdir(parents=True, exist_ok=True)
    # Regenerate ONLY historical train v4 events with as-of predictor CSV.
    train_folder = output_dir/"train-rebuild"
    original = rebuild_v4_training(features, audit, train_folder)
    train_events = pd.read_csv(original["trainEventRowsPath"], low_memory=False)
    model_long, model_short, fit = fit_early_v5(train_events)
    manifest = json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    basket = json.loads(audit.read_text("utf-8-sig"))
    if basket.get("schema") != "ninjex-basket-audit-v1" or (
            basket.get("datasetTag") != manifest.get("datasetTag")):
        raise ValueError("Mismatched source audit/feature dataset tags")
    table = read_verified_csv(features, manifest)
    split_counts = {s:int((table["researchSplit"] == s).sum())
                    for s in ("train","validation","holdout")}
    validation = table.loc[table["researchSplit"] == "validation"].copy()
    del table
    events, extraction = extract_validation_events(validation, basket, manifest["datasetTag"])
    scored, selected = select_frozen_policies(events, (model_long, model_short))
    performances = {k: as_trades(selected, k) for k in POLICIES}
    stats = {k:summary(v) for k,v in performances.items()}
    selected_count = len(selected)
    reverse = performances["reverseModelDirection"]
    forward = performances["modelDirection"]
    short = performances["alwaysShort"]
    result = {
        "schema": "ninjex-ai-frozen-reverse-v6",
        "status": RESEARCH_ONLY_STATUS,
        "sourceDatasetStatus":manifest["researchStatus"],
        "auditStatus":basket.get("status"),
        "modelFit":fit,
        "originalV5FrozenPolicy": {
            "netForecastMinimumUsd":MIN_NET_FORECAST,
            "forecastDirectionGapMinimumUsd":MIN_DIRECTION_GAP,
            "maxPerEasternDay":MAX_TRADES_PER_DAY,
            "model": "unchanged v5 small two-HGBRegressor settings; trained solely on v4 EARLY_TRAIN",
            "eventSignal": "same v4 12-prior-5m high/low breakout or rejection",
            "entry": "next 1m bar open",
            "stopTicks":STOP_TICKS, "targetTicks":TARGET_TICKS,
            "holdingMinutes":HOLD_MINUTES,
            "adverseSlippageTicksEachSide":1, "roundTripCommissionUsd":5,
        },
        "sourceSplitCounts":{
            "trainFeaturesUsedToRebuildEarly":split_counts["train"],
            "previouslyUsedValidationSourceRows":split_counts["validation"],
            "finalHoldoutFeaturesNotScored":split_counts["holdout"],
            "finalHoldoutRawBarsNotLoaded":True,
        },
        "validationEventExtraction":extraction,
        "scoredValidationEvents":len(scored),
        "qualifiedSelectedEntries":selected_count,
        "modelChosenDirections":selected["modelSide"].value_counts().to_dict()
            if selected_count else {},
        "fixedPolicyComparisons":stats,
        "dailyBlockBootstrap":{
            "reverseMeanNetUsd":daily_ci(reverse),
            "reverseMinusModelPerTradeUsd":daily_ci(reverse,forward),
            "reverseMinusAlwaysShortPerTradeUsd":daily_ci(reverse,short)
        },
        "researchDecision":{
            "reversePositive":bool(stats["reverseModelDirection"]["netUsd"] > 0),
            "reverseProfitFactorAboveOne":bool(
                stats["reverseModelDirection"]["profitFactor"] is not None and
                stats["reverseModelDirection"]["profitFactor"] > 1),
            "reverseOutperformsSameTimeAlwaysShort":bool(
                stats["reverseModelDirection"]["netUsd"] >
                stats["alwaysShort"]["netUsd"]),
            "meetsTenThousandAnnualizedReturn":bool(
                stats["reverseModelDirection"]["netUsd"] > 10000 * 10/12),
            "closedDdBelowTwoThousand":bool(
                stats["reverseModelDirection"]["maxClosedEquityDrawdownUsd"] <= 2000),
            "riskConclusion":"CLOSED equity drawdown only; NEVER certifies intraday/trailing drawdown"
        },
        "warnings":[
            "This validation period (Sep2025–June2026) was previously inspected in v1-v2; NOT an untouched independent test.",
            "Reversing v5 predictions was chosen AFTER inspecting v5 late-train profit; this is exploratory falsification, not unbiased discovery.",
            "Thresholds and architecture are frozen from v5, not adjusted using validation prices or outcomes.",
            "Final June15 2026+ holdout data are not read/scored; source CSV reading stops BEFORE its first bar.",
            "One-minute OHLC bars approximate fills and pessimistically assume stop-first when ambiguous.",
            "Actual contracts, broker fees, tick-sequenced execution, open-equity/intraday DD and news filters require later verification.",
            "If reverse does not show robust improvement over SAME-TIME always-short, the apparent v5 inversion should be discarded.",
            "A result below approximately $10k/year cannot fulfill the user's requested annual profitability even if genuine."
        ]
    }
    (output_dir/"es-frozen-reverse-v6.json").write_text(
        json.dumps(result, indent=2), encoding="utf-8")
    if selected_count:
        selected.to_csv(output_dir/"es-frozen-reverse-v6-selected.csv",
                        index=False, float_format="%.10g")
    lines = [
        "# Ninjex AI v6 — frozen inversion of v5 on forward reused validation",
        "",
        "**NOT independent; validation was previously examined in earlier ML research.**",
        "",
        f"Original model fit EARLY_TRAIN: {fit['earlyFitEvents']:,} events; "
        f"LATE_TRAIN excluded from fit: {fit['lateTrainingNotRefit']:,}.",
        f"Previously-used validation observations: {split_counts['validation']:,}; "
        f"final untouched holdout: {split_counts['holdout']:,}.",
        f"Validation structural events: {len(scored):,}; "
        f"qualifying frozen-policy entries: {selected_count:,}.",
        "",
        "| Direction policy (IDENTICAL entry times) | Trades | Net USD | Mean/entry | PF | Closed DD |",
        "|---|---:|---:|---:|---:|---:|"
    ]
    for name, v in stats.items():
        lines.append(
            "| {} | {:,} | {:,.2f} | {:,.2f} | {} | {:,.2f} |".format(
                name, v["trades"], v["netUsd"], v["meanPerTradeUsd"],
                v["profitFactor"], v["maxClosedEquityDrawdownUsd"]))
    lines += [
        "", "## Resampling uncertainty (descriptive only)", "",
        "Reverse mean net/trade 95% day-block interval: {}".format(
            result["dailyBlockBootstrap"]["reverseMeanNetUsd"]["ci95"]),
        "Reverse minus always-short 95% paired interval: {}".format(
            result["dailyBlockBootstrap"]["reverseMinusAlwaysShortPerTradeUsd"]["ci95"]),
        "",
        "**No stop, threshold, time, feature or model adjustment used any future-period results.**",
        "The v5 reverse hypothesis was selected after seeing v5; this test cannot certify an independently replicated edge.",
        "Max CLOSED equity drawdown is NOT a prop-firm intraday/trailing drawdown.",
        "No NinjaTrader orders are placed; actual tick-by-tick fills remain unverified.",""
    ]
    (output_dir/"es-frozen-reverse-v6.md").write_text("\n".join(lines), "utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", required=True, type=Path)
    parser.add_argument("--audit", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    result = test_frozen_forward(args.features, args.audit, args.output_dir)
    print("[NinjexAI-REVERSE] Internal fit:",
          result["modelFit"]["earlyFitEvents"])
    print("[NinjexAI-REVERSE] Reused validation event count:",
          result["scoredValidationEvents"])
    print("[NinjexAI-REVERSE] Frozen selected:",
          result["qualifiedSelectedEntries"])
    print("[NinjexAI-REVERSE] Untouched holdout:",
          result["sourceSplitCounts"]["finalHoldoutFeaturesNotScored"])
    print("[NinjexAI-REVERSE] Report:",
          args.output_dir / "es-frozen-reverse-v6.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
