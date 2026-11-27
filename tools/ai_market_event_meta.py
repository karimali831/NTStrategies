#!/usr/bin/env python3
"""Ninjex AI v5: frozen event-conditioned LONG/SHORT expectancy.
Fit only on EARLY TRAIN events (Sep 2023–Aug 2024), score LATE TRAIN
(Sep 2024–Aug 2025) without fitting or tuning. Already examined as
summary in v4: NOT pristine out-of-sample. Previously-used validation
and reserved final holdout are not scored or used.
"""
import argparse
import json
from collections import Counter
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingRegressor
from sklearn.impute import SimpleImputer
from sklearn.metrics import mean_absolute_error
from sklearn.pipeline import make_pipeline

if __package__:
    from tools.ai_market_structure_events import analyze
    from tools.ai_market_train_baseline import BASE_FEATURES
    from tools.ai_market_bracket_research import summarize
else:
    from ai_market_structure_events import analyze
    from ai_market_train_baseline import BASE_FEATURES
    from ai_market_bracket_research import summarize

TRIGGERS = ("BREAKOUT_LONG", "BREAKOUT_SHORT",
            "REJECTION_LONG", "REJECTION_SHORT")
MIN_NET_FORECAST = 20.0
MIN_DIRECTION_GAP = 10.0
MAX_TRADES_PER_DAY = 2


def predictors(events):
    x = pd.DataFrame(index=events.index)
    for name in BASE_FEATURES + ("easternMinuteOfDay",):
        col = "feature_" + name
        if col not in events:
            raise ValueError("Missing as-of feature: " + col)
        x[name] = pd.to_numeric(events[col], errors="coerce")
    phase = 2.0 * np.pi * x.pop("easternMinuteOfDay") / 1440.0
    x["timeSin"], x["timeCos"] = np.sin(phase), np.cos(phase)
    if not set(events["trigger"]).issubset(TRIGGERS):
        raise ValueError("Unknown structure trigger")
    for trigger in TRIGGERS:
        x["event_" + trigger] = (events["trigger"] == trigger).astype(float)
    if x.isna().mean().max() > .10:
        raise ValueError("As-of predictor source missing too often")
    return x


def direction_labels(events):
    sign = events["trigger"].str.endswith("_LONG").to_numpy()
    chosen = events["netUsd"].to_numpy(float)
    opposite = events["oppositeUsd"].to_numpy(float)
    if not (np.isfinite(chosen).all() and np.isfinite(opposite).all()):
        raise ValueError("Nonfinite counterfactual labels")
    return np.where(sign, chosen, opposite), np.where(sign, opposite, chosen)


def model():
    # Frozen before examining internal-challenge model performance.
    return make_pipeline(
        SimpleImputer(strategy="median"),
        HistGradientBoostingRegressor(
            max_iter=90, max_leaf_nodes=7, min_samples_leaf=90,
            learning_rate=.045, l2_regularization=20,
            early_stopping=False, random_state=42))


def summarize_column(data, column):
    trades = [{"decisionUtc": str(r["decisionUtc"]), "month": str(r["month"]),
               "netUsd": float(r[column]), "exitReason": "SIMULATED",
               "bothTouched1m": False}
              for _, r in data.iterrows()]
    return summarize(trades)


def choose_trades(scored):
    eligible = scored.loc[
        (scored["predictedBest"] >= MIN_NET_FORECAST) &
        (scored["predictedGap"] >= MIN_DIRECTION_GAP)
    ].sort_values("decisionUtc")
    by_day = Counter()
    chosen = []
    for index, row in eligible.iterrows():
        day = str(row["easternDate"])
        if by_day[day] >= MAX_TRADES_PER_DAY:
            continue
        by_day[day] += 1
        chosen.append(index)
    return scored.loc[chosen].sort_values("decisionUtc")


def prepare(events, v4):
    if len(events) != v4["nonOverlappingEvents"]:
        raise ValueError("Event-row count does not match rebuilt v4")
    required = {"contract", "decisionUtc", "easternDate", "month", "period",
                "trigger", "netUsd", "oppositeUsd"}
    if not required.issubset(events):
        raise ValueError("Incomplete v4 event dataset")
    if events.duplicated(["contract", "decisionUtc"]).any():
        raise ValueError("Duplicate historical event timestamp")
    if set(events["period"]) != {"EARLY_TRAIN", "LATE_TRAIN"}:
        raise ValueError("Internal chronological training periods missing")
    days = pd.to_datetime(events["easternDate"])
    if (days >= pd.Timestamp("2025-09-01")).any():
        raise ValueError("Validation/holdout dates must never be modelled")
    expected = np.where(days < pd.Timestamp("2024-09-01"),
                        "EARLY_TRAIN", "LATE_TRAIN")
    if not np.array_equal(expected, events["period"].to_numpy()):
        raise ValueError("Corrupt internal period split")


def test_early_late(events, original_v4, output_dir):
    prepare(events, original_v4)
    train = (events["period"] == "EARLY_TRAIN").to_numpy()
    challenge = ~train
    if train.sum() < 500 or challenge.sum() < 500:
        raise ValueError("Insufficient independent chronological internal training rows")
    x = predictors(events)
    y_long, y_short = direction_labels(events)
    long_model, short_model = model(), model()
    long_model.fit(x.loc[train], y_long[train])
    short_model.fit(x.loc[train], y_short[train])
    long_pred = long_model.predict(x.loc[challenge])
    short_pred = short_model.predict(x.loc[challenge])
    if not (np.isfinite(long_pred).all() and np.isfinite(short_pred).all()):
        raise ValueError("Nonfinite after-cost forecasts")
    scored = events.loc[challenge, ["decisionUtc", "easternDate", "month",
                                    "contract", "trigger"]].copy()
    scored["predictedLong"], scored["predictedShort"] = long_pred, short_pred
    scored["actualLong"], scored["actualShort"] = y_long[challenge], y_short[challenge]
    scored["predictedBest"] = np.maximum(long_pred, short_pred)
    scored["predictedGap"] = np.abs(long_pred-short_pred)
    scored["direction"] = np.where(long_pred >= short_pred, "LONG", "SHORT")
    scored["actualSelected"] = np.where(
        long_pred >= short_pred, y_long[challenge], y_short[challenge])
    scored["actualOpposite"] = np.where(
        long_pred >= short_pred, y_short[challenge], y_long[challenge])
    scored["actualV4Direction"] = np.where(
        scored["trigger"].str.endswith("_LONG"),
        y_long[challenge], y_short[challenge])
    selected = choose_trades(scored)
    # Forecast-band boundaries fixed solely from *earlier* fitted
    # training predictions (not challenge forecasts).
    train_preds = np.maximum(
        long_model.predict(x.loc[train]), short_model.predict(x.loc[train]))
    cutoffs = np.quantile(train_preds, [.5, .8, .95])
    scored["priorTrainForecastBand"] = pd.cut(
        scored["predictedBest"],
        [-np.inf, *cutoffs, np.inf],
        labels=["LOW_50", "MID_50_80", "HIGH_80_95", "TOP_5"]).astype(str)
    groups = {}
    for band, subset in scored.groupby("priorTrainForecastBand"):
        groups[str(band)] = {
            "count": len(subset),
            "predictedMeanUsd": round(float(subset["predictedBest"].mean()), 2),
            "actualMeanUsd": round(float(subset["actualSelected"].mean()), 2),
            "actualOppositeMeanUsd": round(float(subset["actualOpposite"].mean()), 2)
        }
    policy = {
        "trades": len(selected),
        "modelChosen": summarize_column(selected, "actualSelected"),
        "sameTimeAlwaysLong": summarize_column(selected, "actualLong"),
        "sameTimeAlwaysShort": summarize_column(selected, "actualShort"),
        "sameTimeOpposite": summarize_column(selected, "actualOpposite")
    }
    result = {
        "schema": "ninjex-es-meta-v5",
        "status": "INTERNAL_TRAIN_CHALLENGE_NOT_PROVEN",
        "earlyFitEvents": int(train.sum()),
        "laterChallengeEvents": int(challenge.sum()),
        "validationRowsNotScored": original_v4["validationRowsNotScored"],
        "holdoutRowsNotScored": original_v4["holdoutRowsNotScored"],
        "frozenModel": "2 HistGradientBoostingRegressor(max_iter=90, leaf_nodes=7, min_leaf=90)",
        "predictors": x.columns.tolist(),
        "frozenPolicy": {
            "netForecastMinimumUsd": MIN_NET_FORECAST,
            "forecastDirectionGapMinimumUsd": MIN_DIRECTION_GAP,
            "maxPerEasternDay": MAX_TRADES_PER_DAY,
            "bracket": "12tick stop, 24tick target, 30minute time exit",
            "costs": "1tick adverse each side + $5 roundtrip"
        },
        "forecastErrors": {
            "longModelMaeUsd": round(float(mean_absolute_error(y_long[challenge], long_pred)), 3),
            "longConstantTrainMeanMaeUsd": round(float(mean_absolute_error(
                y_long[challenge], np.full(int(challenge.sum()), y_long[train].mean()))), 3),
            "shortModelMaeUsd": round(float(mean_absolute_error(y_short[challenge], short_pred)), 3),
            "shortConstantTrainMeanMaeUsd": round(float(mean_absolute_error(
                y_short[challenge], np.full(int(challenge.sum()), y_short[train].mean()))), 3)
        },
        "trainDerivedBandBoundaries": [float(v) for v in cutoffs],
        "lateTrainingForecastBands": groups,
        "allLateEventsModelChosen": summarize_column(scored, "actualSelected"),
        "allLateEventsOriginalSignal": summarize_column(scored, "actualV4Direction"),
        "fixedPolicy": policy,
        "warnings": [
            "Both early and late training history were already summarized in v4; late is NOT a pristine independent test.",
            "No validation (Sep2025–Jun2026) or reserved holdout (2026-06-15 onward) outcomes were evaluated.",
            "The fixed positive-expectancy gate was NOT chosen by scanning late-training thresholds.",
            "These labels use one-minute OHLC stop-first and approximate slippage/commission, not real tick-level order fills.",
            "Trade selection is only simulated; no NinjaTrader order is ever sent.",
            "Even a positive result needs independently unseen chronological verification, tick execution, and unrealized prop drawdown analysis."
        ]
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir/"es-meta-v5.json").write_text(json.dumps(result, indent=2), "utf-8")
    scored.to_csv(output_dir/"es-meta-v5-late-predictions.csv", index=False)
    m = [
        "# Ninjex AI v5 — event-conditional after-cost expectancy",
        "",
        "**Research only: internal train challenge, NOT independent unseen validation.**",
        "",
        "Model fit on {:,} early-training events; scored {:,} late-training events.".format(
            int(train.sum()), int(challenge.sum())),
        "Unscored prior validation: {:,}; unscored final holdout: {:,}.".format(
            result["validationRowsNotScored"], result["holdoutRowsNotScored"]),
        "",
        "## Frozen selective policy",
        "Predict best direction >={} USD after cost, >={} USD ahead of opposite, max {} entries/day.".format(
            MIN_NET_FORECAST, MIN_DIRECTION_GAP, MAX_TRADES_PER_DAY),
        "",
        "| Result | Value |", "|---|---:|",
        "| Qualified trades | {} |".format(policy["trades"]),
        "| Net P&L | {} USD |".format(policy["modelChosen"]["netUsd"]),
        "| Mean P&L | {} USD |".format(policy["modelChosen"]["meanPerTradeUsd"]),
        "| Profit factor | {} |".format(policy["modelChosen"]["profitFactor"]),
        "| Max closed drawdown | {} USD |".format(
            policy["modelChosen"]["maxClosedEquityDrawdownUsd"]),
        "| Same-time always long | {} USD |".format(
            policy["sameTimeAlwaysLong"]["netUsd"]),
        "| Same-time always short | {} USD |".format(
            policy["sameTimeAlwaysShort"]["netUsd"]),
        "",
        "## Frozen early-training forecast score groups",
        "| Band | Count | Predicted USD | Actual after-cost USD |",
        "|---|---:|---:|---:|"
    ]
    for name, item in sorted(groups.items()):
        m.append("| {} | {} | {} | {} |".format(
            name, item["count"], item["predictedMeanUsd"], item["actualMeanUsd"]))
    m += ["", "The score bands are descriptive, not optimized for trading.",
          "A zero-trade result is valid evidence that a selective model correctly abstained.",
          "No full tick-by-tick historical path or exchange execution is assessed.", ""]
    (output_dir/"es-meta-v5.md").write_text("\n".join(m), "utf-8")
    return result


def execute(features, audit, output_dir):
    output_dir.mkdir(parents=True, exist_ok=True)
    v4 = analyze(features, audit, output_dir)
    events = pd.read_csv(v4["trainEventRowsPath"], low_memory=False)
    return test_early_late(events, v4, output_dir)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", required=True, type=Path)
    parser.add_argument("--audit", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    result = execute(args.features, args.audit, args.output_dir)
    print("[NinjexAI-META] Fit early:", result["earlyFitEvents"])
    print("[NinjexAI-META] Challenge late TRAIN:", result["laterChallengeEvents"])
    print("[NinjexAI-META] Validation NOT scored:", result["validationRowsNotScored"])
    print("[NinjexAI-META] Holdout NOT scored:", result["holdoutRowsNotScored"])
    print("[NinjexAI-META] Qualified:", result["fixedPolicy"]["trades"])
    print("[NinjexAI-META] Output:", args.output_dir / "es-meta-v5.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
