#!/usr/bin/env python3
"""Ninjex AI research v2 — learn hypothetical NET ES trade expectancy directly.

Train two separate ML regressors for hypothetical LONG and SHORT outcomes,
using ALL eligible training 5m decisions and actual future 1m OHLC bars.
Score only previously examined validation; holdout rows (June 15 2026+)
are not scored and source 1m holdout paths are not loaded. No live orders.

FIXED research trade policy inherited from the initial bracket experiment:
ES 12-tick stop, 24-tick target, 30m expiry, next 1m open entry,
1 tick adverse slip on each side, $5 round trip, stop-first if 1m
bar touches both levels. Requires ALL future 30 minute bars; source
gaps are never backfilled. Training target is simulated net USD for
each direction, not 30m close direction.

WARNING: this is exploratory on ALREADY USED validation, not a
previously unseen test and not evidence of a real-money edge.
"""
import argparse
import csv
import datetime as dt
import json
import math
from collections import Counter, defaultdict
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from sklearn.ensemble import HistGradientBoostingRegressor
from sklearn.impute import SimpleImputer
from sklearn.pipeline import make_pipeline

if __package__:
    from tools.ai_market_ml_features import read_bars, split_for, NY
    from tools.ai_market_bracket_research import (
        close_time, minute_window, simulate, summarize,
    )
    from tools.ai_market_train_baseline import (
        MODEL_FEATURES, feature_frame, read_verified_csv,
    )
else:
    from ai_market_ml_features import read_bars, split_for, NY
    from ai_market_bracket_research import (
        close_time, minute_window, simulate, summarize,
    )
    from ai_market_train_baseline import (
        MODEL_FEATURES, feature_frame, read_verified_csv,
    )

MIN_EXPECTED_NET_USD = 20.0
MIN_SPACING_MINUTES = 30
VALID_SPLITS = ("train", "validation")
SEED = 42


def eligible_source(row):
    return (int(row["fine1mComplete"]) == 1 and
            str(row["qualityFlags"]) == "OK")


def make_labels(features, bulk_audit, counters, detailed=None):
    """Only historical train+validation rows may enter this routine.

    Per row calculate BOTH alternate hypothetical LONG and SHORT outcomes
    on the same future price path. Do not select a direction from the
    future outcome. The holdout MUST have been removed beforehand.
    """
    if not set(features["researchSplit"]).issubset(VALID_SPLITS):
        raise ValueError("Holdout or unknown split must not enter label builder")
    folders = {}
    for item in bulk_audit["contracts"]:
        if len(item.get("folders", [])) == 1:
            folders[item["contract"]] = Path(item["folders"][0])
    records = []
    for contract, source in features.groupby("contract", sort=True):
        if contract not in folders:
            raise ValueError("Missing or ambiguous source folder: " + contract)
        folder = folders[contract]
        manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8-sig"))
        if manifest.get("contract") != contract or manifest.get("datasetTag") != bulk_audit["datasetTag"]:
            raise ValueError("Source contract/tag provenance mismatch: " + contract)
        # Read each dated contract only once; no individual export reruns.
        candles = {bar.end: bar for bar in read_bars(folder, 1, contract)}
        for idx, row in source.iterrows():
            if not eligible_source(row):
                counters["sourceQualityMasked"] += 1
                continue
            asof = close_time(row["decisionUtc"])
            expected_split = split_for(asof.astimezone(NY).date())
            if expected_split != row["researchSplit"]:
                raise ValueError("Features split boundary differs from actual Eastern timestamp")
            if split_for((asof + dt.timedelta(minutes=30)).astimezone(NY).date()) != expected_split:
                counters["purgedCrossSplit30m"] += 1
                continue
            future = minute_window(candles, asof)
            if future is None:
                counters["missingFuture1m"] += 1
                continue
            # Both sides share identical entry timestamps, prices and costs.
            long = simulate(future, 1)
            short = simulate(future, -1)
            record = {
                "index": idx,
                "month": str(row["decisionEastern"])[:7],
                "easternDate": str(row["decisionEastern"])[:10],
                "pnlLongUsd": long["netUsd"],
                "pnlShortUsd": short["netUsd"],
                "targetLong": int(long["exitReason"] == "TARGET"),
                "targetShort": int(short["exitReason"] == "TARGET"),
                "exitLong": long["exitReason"],
                "exitShort": short["exitReason"],
                "exitTimeLong": long["exitUtc"],
                "exitTimeShort": short["exitUtc"],
                "ambiguousLong": int(long["bothTouched1m"]),
                "ambiguousShort": int(short["bothTouched1m"]),
            }
            records.append(record)
            counters["labelled_" + row["researchSplit"]] += 1
    if not records:
        raise ValueError("No valid 1m bracket outcomes could be labelled")
    return pd.DataFrame.from_records(records).set_index("index").sort_index()


def make_regressor():
    return make_pipeline(
        SimpleImputer(strategy="median"),
        HistGradientBoostingRegressor(
            max_iter=120, max_leaf_nodes=15, learning_rate=0.04,
            min_samples_leaf=200, l2_regularization=35.0,
            early_stopping=False, random_state=SEED)
    )


def select_disjoint(prepared):
    """Pick earliest positive-expected-net decision per contract every 30m.

    All predictor scores, not any realized future outcome, determine
    entry eligibility and long/short selection.
    """
    chosen = []
    for _, group in prepared.sort_values(
        ["contract", "decisionUtc"]).groupby("contract", sort=True):
        last = None
        for idx, row in group.iterrows():
            if row["predictedBestNetUsd"] <= MIN_EXPECTED_NET_USD:
                continue
            t = close_time(row["decisionUtc"])
            if last is not None and t < last + dt.timedelta(minutes=MIN_SPACING_MINUTES):
                continue
            chosen.append(idx)
            last = t
    return prepared.loc[chosen].copy()


def model_vs_baselines(chosen):
    policies = {"mlExpectancy": [], "alwaysLong": [], "alwaysShort": [],
                "momentum12": []}
    for _, row in chosen.iterrows():
        side = 1 if row["predictedSide"] == "LONG" else -1
        momentum = 1 if float(row["ret12Atr"]) >= 0 else -1
        for name, direction in (
            ("mlExpectancy", side), ("alwaysLong", 1),
            ("alwaysShort", -1), ("momentum12", momentum)):
            prefix = "Long" if direction == 1 else "Short"
            result = {
                "decisionUtc": row["decisionUtc"],
                "contract": row["contract"],
                "month": row["month"],
                "session": row["session"],
                "netUsd": float(row["pnl" + prefix + "Usd"]),
                "exitReason": row["exit" + prefix],
                "bothTouched1m": bool(row["ambiguous" + prefix]),
            }
            policies[name].append(result)
    return policies


def session_name(row):
    if row["sessionLondon"] == 1 and row["sessionNewYork"] == 1:
        return "LONDON_NY_OVERLAP"
    if row["sessionNewYork"] == 1:
        return "NEW_YORK"
    if row["sessionLondon"] == 1:
        return "LONDON"
    if row["sessionAsia"] == 1:
        return "ASIA"
    return "OTHER"


def execute(features_path, audit_path, output_dir):
    manifest = json.loads(features_path.with_suffix(".manifest.json").read_text("utf-8-sig"))
    original = read_verified_csv(features_path, manifest)
    holdout_rows = int((original["researchSplit"] == "holdout").sum())
    # Strip holdout before ANY feature construction, label creation or
    # model inference. Only train/validation 1m source runs are referenced.
    research = original.loc[original["researchSplit"].isin(VALID_SPLITS)].copy()
    del original
    audit = json.loads(audit_path.read_text(encoding="utf-8-sig"))
    if audit.get("schema") != "ninjex-basket-audit-v1":
        raise ValueError("Not a Ninjex original multi-contract audit")
    if audit.get("datasetTag") != manifest.get("datasetTag"):
        raise ValueError("Audit and feature manifest dataset tags disagree")
    if not (research["researchSplit"].isin(VALID_SPLITS).all()):
        raise ValueError("Unexpected research split found")
    counters = defaultdict(int)
    y = make_labels(research, audit, counters)
    work = research.loc[y.index].join(y)
    mask_train = (work["researchSplit"] == "train").to_numpy()
    mask_val = (work["researchSplit"] == "validation").to_numpy()
    if mask_train.sum() < 1000 or mask_val.sum() < 1000:
        raise ValueError("Insufficient 1m labelled train / validation outcomes")
    if work.loc[mask_train, "decisionEastern"].str[:10].max() >= "2025-09-01":
        raise ValueError("Training outcome date reaches validation")
    if work.loc[mask_val, "decisionEastern"].str[:10].max() >= "2026-06-15":
        raise ValueError("Validation outcome date reaches untouched holdout")

    # Strict feature allowlist from the original as-of feature builder:
    # no future close, label, 1m outcome or contract identity predictor.
    x = feature_frame(work)
    if list(x.columns) != list(MODEL_FEATURES):
        raise ValueError("Features no longer match the previous model policy")
    if x.isna().mean().max() > .05:
        raise ValueError("Excessive missing predictors")
    y_long = work["pnlLongUsd"].to_numpy(float)
    y_short = work["pnlShortUsd"].to_numpy(float)
    models = {"LONG": make_regressor(), "SHORT": make_regressor()}
    models["LONG"].fit(x.loc[mask_train], y_long[mask_train])
    models["SHORT"].fit(x.loc[mask_train], y_short[mask_train])

    v = work.loc[mask_val].copy()
    vx = x.loc[mask_val]
    v["predictedLongNetUsd"] = models["LONG"].predict(vx)
    v["predictedShortNetUsd"] = models["SHORT"].predict(vx)
    v["predictedSide"] = np.where(
        v["predictedLongNetUsd"] >= v["predictedShortNetUsd"], "LONG", "SHORT")
    v["predictedBestNetUsd"] = v[[
        "predictedLongNetUsd", "predictedShortNetUsd"]].max(axis=1)
    v["session"] = v.apply(session_name, axis=1)

    picked = select_disjoint(v)
    policies = model_vs_baselines(picked)
    baseline_train = {
        "longAvgNetUsd": round(float(y_long[mask_train].mean()), 4),
        "shortAvgNetUsd": round(float(y_short[mask_train].mean()), 4),
        "longTargetFirstRate": round(float(work.loc[mask_train, "targetLong"].mean()), 5),
        "shortTargetFirstRate": round(float(work.loc[mask_train, "targetShort"].mean()), 5)
    }
    policy_results = {key: summarize(values) for key, values in policies.items()}
    selected = {}
    for groupby in ("contract", "session"):
        grouped = {}
        for key, group in picked.groupby(groupby):
            grouped[str(key)] = {
                "count": len(group),
                "ml": summarize([z for z in policies["mlExpectancy"]
                    if z[groupby] == key]),
                "targetFirstRate": round(float(np.mean([
                    float(row["targetLong"] if row["predictedSide"] == "LONG"
                          else row["targetShort"])
                    for _, row in group.iterrows()])), 5),
            }
        selected[groupby] = grouped
    result = {
        "schema": "ninjex-direct-net-expectancy-v2",
        "status": "EXPLORATORY_VALIDATION_ONLY_NO_PROVEN_EDGE",
        "sourceFeatures": str(features_path),
        "sourceAudit": str(audit_path),
        "sourceStatus": manifest["researchStatus"],
        "holdoutNotScoredRows": holdout_rows,
        "counts": {
            "sourceTrainValidationRows": int(len(research)),
            "trainLabelledBothDirections": int(mask_train.sum()),
            "validationLabelledBothDirections": int(mask_val.sum()),
            "validationEligiblePredictedNetAbove20": int(
                (v["predictedBestNetUsd"] > MIN_EXPECTED_NET_USD).sum()),
            "nonOverlappingValidationEntries": len(picked),
            **dict(sorted(counters.items())),
        },
        "featureColumns": list(MODEL_FEATURES),
        "fixedTradePolicy": {
            "entry": "next completed 1m bar OPEN after 5m decision (one tick adverse entry slippage)",
            "stopTicks": 12, "targetTicks": 24, "maxHoldingMinutes": 30,
            "slippageTicksPerSide": 1, "commissionRoundTripUsd": 5,
            "bothHitSame1m": "STOP_FIRST",
            "labelMissingAnyFutureMinute": "EXCLUDE",
            "signalSelection": "predicted max(longNet,shortNet)>$20 and >=30m spacing per contract",
        },
        "trainUnconditional": baseline_train,
        "validationPredictedNetUsd": {
            "longP10P50P90": np.quantile(v["predictedLongNetUsd"], [.1, .5, .9]).round(3).tolist(),
            "shortP10P50P90": np.quantile(v["predictedShortNetUsd"], [.1, .5, .9]).round(3).tolist(),
            "maxPredictedNetUsd": round(float(v["predictedBestNetUsd"].max()), 3)
        },
        "validationPolicyComparisons": policy_results,
        "validationByGroup": selected,
        "warnings": [
            "Research validation was already used for model and confidence profiling, so not independent out-of-sample.",
            "Labels and hypothetical fills use one-minute OHLC, not tick-sequenced trades or real bid/ask.",
            "Regressors predict EXPECTED hypothetical net USD directly, not a calibrated target-first probability.",
            "Large overlapping 30m labels exist in training; validation trade entries are 30m spaced.",
            "One ES contract, constant 12/24 tick bracket, 1tick/side adverse slip +$5 fee are assumptions.",
            "Historical data quality remains provisional; no source candles are imputed.",
            "Max closed-equity drawdown is NOT prop-firm intraday unrealized trailing drawdown.",
            "No execution, no OpenAI API usage, holdout June 2026 onward remains untouched.",
        ],
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir/"es-direct-expectancy-v2.json").write_text(
        json.dumps(result, indent=2), encoding="utf-8")
    with (output_dir/"es-direct-expectancy-v2-decisions.csv").open(
        "w", newline="", encoding="utf-8") as stream:
        cols = ["contract","decisionUtc","researchSplit","predictedSide",
                "predictedLongNetUsd","predictedShortNetUsd",
                "pnlLongUsd","pnlShortUsd","targetLong","targetShort","session"]
        writer = csv.DictWriter(stream, fieldnames=cols)
        writer.writeheader()
        for _, row in picked.iterrows():
            writer.writerow({col: row[col] for col in cols})
    # Local trusted research artifact; not suitable for live NinjaScript.
    joblib.dump({"schema": "ninjex-direct-expectancy-v2",
                 "status": result["status"], "models": models,
                 "features": list(MODEL_FEATURES),
                 "thresholdPredictedNetUsd": MIN_EXPECTED_NET_USD},
                output_dir/"es-direct-expectancy-v2.joblib")
    lines = [
        "# Ninjex AI — train directly on hypothetical ES trade outcomes",
        "", "**Exploratory previously-used validation only; no live edge proven.**",
        "", f"Training 5m timestamps labelled for both LONG and SHORT: {int(mask_train.sum()):,}.",
        f"Validation labelled: {int(mask_val.sum()):,}; reserved holdout NOT scored: {holdout_rows:,}.",
        f"ML signals with predicted net >$20: {result['counts']['validationEligiblePredictedNetAbove20']:,}; "
        f"nonoverlapping hypothetical entries: {len(picked):,}.",
        "", "| Policy | Trades | Net USD | Mean/trade | Win % | PF | Max closed DD |",
        "|---|---:|---:|---:|---:|---:|---:|",
    ]
    for name, summary in policy_results.items():
        pf = summary["profitFactor"]
        lines.append(
            f"| {name} | {summary['trades']} | {summary['netUsd']:,.2f} | "
            f"{summary['meanPerTradeUsd']:,.2f} | "
            f"{(summary['winRate'] or 0)*100:.1f}% | "
            f"{pf if pf is not None else '—'} | "
            f"{summary['maxClosedEquityDrawdownUsd']:,.2f} |")
    lines += [
        "", f"Train unconditional long net expectation: {baseline_train['longAvgNetUsd']:.2f} USD;",
        f"short: {baseline_train['shortAvgNetUsd']:.2f} USD.",
        "", "The training labels use both directions at every clean 5m timestamp.",
        "The validation selection is made ONLY using the model's predictions.",
        "No tuning is performed based on validation PnL in this script.",
        "", "These results reuse validation and cannot establish an independent edge.",
        "No futures brokerage fills, prop firm drawdown or intraday risk proof.",
        "Holdout remains reserved for a final locked protocol.", ""
    ]
    (output_dir/"es-direct-expectancy-v2.md").write_text(
        "\n".join(lines), encoding="utf-8")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", type=Path, required=True)
    parser.add_argument("--audit", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    a = parser.parse_args()
    report = execute(a.features, a.audit, a.output_dir)
    print("[NinjexAI-EXPECTANCY] Labelled training:",
          report["counts"]["trainLabelledBothDirections"])
    print("[NinjexAI-EXPECTANCY] Labelled validation:",
          report["counts"]["validationLabelledBothDirections"])
    print("[NinjexAI-EXPECTANCY] Holdout not scored:", report["holdoutNotScoredRows"])
    print("[NinjexAI-EXPECTANCY] Selected entries:",
          report["counts"]["nonOverlappingValidationEntries"])
    for key, row in report["validationPolicyComparisons"].items():
        print("[NinjexAI-EXPECTANCY]", key, "net:", row["netUsd"],
              "max closed DD:", row["maxClosedEquityDrawdownUsd"])
    print("[NinjexAI-EXPECTANCY] Report:", a.output_dir/"es-direct-expectancy-v2.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
