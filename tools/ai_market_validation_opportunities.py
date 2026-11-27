#!/usr/bin/env python3
"""Research-only diagnostics for Ninjex AI's first trained ES classifier.

Scores VALIDATION data only with the user's locally generated research
joblib. Does not inspect holdout outcomes, fit models, retune parameters,
simulate fills or produce trade signals.

Directional research thresholds are exploratory and must NOT be used to
claim validated setups after being selected using this same validation data.
"""
import argparse
import datetime as dt
import json
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

from tools.ai_market_train_baseline import (
    ATR_THRESHOLD, MODEL_FEATURES, feature_frame, labels_from_returns,
    read_verified_csv,
)

# Predeclared descriptive thresholds. Not optimized using validation data.
THRESHOLDS = (0.45, 0.50, 0.55, 0.60, 0.65)
MIN_DIRECTIONAL_MARGIN = 0.10
FUTURE_HORIZON_MINUTES = 30
MIN_SUBGROUP_SIZE = 50


def validate_bundle(bundle):
    if not isinstance(bundle, dict):
        raise ValueError("Unexpected local model format")
    if bundle.get("schema") != "ninjex-ml-provisional-v1":
        raise ValueError("Not a Ninjex v1 local research model")
    if bundle.get("status") != "RESEARCH_ONLY_NO_TRADING_EDGE_PROVEN":
        raise ValueError("Not an approved provisional model artifact")
    if bundle.get("features") != list(MODEL_FEATURES):
        raise ValueError("Input features changed after model training")
    if bundle.get("labelThresholdAtr") != ATR_THRESHOLD:
        raise ValueError("Outcome threshold differs from training")
    if bundle.get("validationChosenResearchModel") != "histGradientBoosting":
        raise ValueError("Unexpected model version; use original benchmark")
    return bundle["models"]["histGradientBoosting"]


def prepare_validation(data):
    """Filter ALL holdout rows before constructing labels or ML features."""
    heldout = int((data["researchSplit"] == "holdout").sum())
    train = data.loc[data["researchSplit"] == "train"].copy()
    val = data.loc[data["researchSplit"] == "validation"].copy()
    train_label = pd.to_numeric(train["label30mCloseRetAtr"], errors="coerce")
    val_label = pd.to_numeric(val["label30mCloseRetAtr"], errors="coerce")
    train = train.loc[train_label.notna()].copy()
    val = val.loc[val_label.notna()].copy()
    if not len(train) or not len(val):
        raise ValueError("No labelled train/validation rows")
    if val["decisionEastern"].str[:10].max() >= "2026-06-15":
        raise ValueError("Validation touches reserved holdout")
    if train["decisionEastern"].str[:10].max() >= val["decisionEastern"].str[:10].min():
        raise ValueError("Train/validation chronology overlaps")
    return train, val, heldout


def quantile_cutoffs(train):
    """Train-only reference thresholds, no validation-derived bin tuning."""
    atr = pd.to_numeric(train["atr14_5m"], errors="coerce")
    px = pd.to_numeric(train["close"], errors="coerce")
    volatility = (atr / px).to_numpy(dtype=float)
    efficiency = pd.to_numeric(train["efficiency12"], errors="coerce").to_numpy(dtype=float)
    if np.isfinite(volatility).sum() < 100 or np.isfinite(efficiency).sum() < 100:
        raise ValueError("Insufficient clean training reference features")
    return {
        "relativeVolatility": np.nanquantile(volatility, [1/3, 2/3]).tolist(),
        "efficiency": np.nanquantile(efficiency, [1/3, 2/3]).tolist(),
    }


def group_three(series, cutoffs):
    x = pd.to_numeric(series, errors="coerce")
    return np.where(x <= cutoffs[0], "LOW",
                    np.where(x <= cutoffs[1], "MEDIUM", "HIGH"))


def describe_sessions(val):
    """One exclusive session label avoids counting overlap twice in profiles."""
    flags = val[["sessionAsia", "sessionLondon", "sessionNewYork"]].astype(int)
    asia = flags["sessionAsia"].to_numpy() == 1
    london = flags["sessionLondon"].to_numpy() == 1
    ny = flags["sessionNewYork"].to_numpy() == 1
    return np.select(
        (london & ny, ny, london, asia),
        ("LONDON_NY_OVERLAP", "NEW_YORK", "LONDON", "ASIA"),
        default="OTHER"
    )


def add_validation_profiles(val, model, boundaries):
    """Return a new validation-only frame without changing raw source rows."""
    if not len(val):
        raise ValueError("Empty validation dataset")
    x = feature_frame(val)
    probabilities = model.predict_proba(x)
    if probabilities.shape != (len(val), 3) or not np.isfinite(probabilities).all():
        raise ValueError("Expected finite DOWN/NEUTRAL/UP probabilities")
    if not np.allclose(probabilities.sum(axis=1), 1.0, atol=1e-6):
        raise ValueError("Malformed model probability vectors")
    v = val.reset_index(drop=True).copy()
    v["label"] = labels_from_returns(
        pd.to_numeric(v["label30mCloseRetAtr"], errors="raise").to_numpy(float))
    v["pDown"] = probabilities[:, 0]
    v["pNeutral"] = probabilities[:, 1]
    v["pUp"] = probabilities[:, 2]
    v["direction"] = np.where(probabilities[:, 2] >= probabilities[:, 0], "UP", "DOWN")
    v["pDirection"] = np.maximum(probabilities[:, 0], probabilities[:, 2])
    v["pOpposite"] = np.minimum(probabilities[:, 0], probabilities[:, 2])
    v["directionMargin"] = v["pDirection"] - v["pOpposite"]
    v["directionCorrect"] = np.where(v["direction"] == "UP",
                                     v["label"] == 2, v["label"] == 0)
    v["signed30mReturnAtr"] = (pd.to_numeric(v["label30mCloseRetAtr"])
                               * np.where(v["direction"] == "UP", 1., -1.))
    v["sessionGroup"] = describe_sessions(v)
    relvol = (pd.to_numeric(v["atr14_5m"], errors="coerce") /
              pd.to_numeric(v["close"], errors="coerce"))
    v["relativeVolatility"] = group_three(relvol, boundaries["relativeVolatility"])
    v["trendEfficiency"] = group_three(v["efficiency12"], boundaries["efficiency"])
    v["trendDirection"] = np.where(
        (v["ema20DistanceAtr"] > 0) & (v["ema50DistanceAtr"] > 0),
        "UP", np.where(
            (v["ema20DistanceAtr"] < 0) & (v["ema50DistanceAtr"] < 0),
            "DOWN", "MIXED"))
    v["month"] = v["decisionEastern"].str.slice(0, 7)
    v["decisionUtcParsed"] = pd.to_datetime(v["decisionUtc"], utc=True)
    return v


def select_candidates(v, threshold):
    clean = ((pd.to_numeric(v["fine1mComplete"], errors="coerce") == 1)
             & (v["qualityFlags"].fillna("") == "OK"))
    return v.loc[
        clean & (v["pDirection"] >= threshold)
        & (v["pDirection"] >= v["pNeutral"])
        & (v["directionMargin"] >= MIN_DIRECTIONAL_MARGIN)].copy()


def select_nonoverlap(rows):
    """Deterministic max-one overlapping 30m label per dated contract."""
    if not len(rows):
        return rows
    result = []
    for _, partition in rows.sort_values(
        ["contract", "decisionUtcParsed"]).groupby("contract", sort=True):
        last_close = None
        for index, row in partition.iterrows():
            time = row["decisionUtcParsed"]
            if last_close is None or time >= last_close + dt.timedelta(
                    minutes=FUTURE_HORIZON_MINUTES):
                result.append(index)
                last_close = time
    return rows.loc[result].copy()


def group_summary(rows):
    if not len(rows):
        return {
            "observations": 0, "nonOverlappingObservations": 0,
            "observedDirectionalEventRate": None,
            "meanDirectionalProbability": None,
            "nonOverlappingObservedEventRate": None,
            "meanSigned30mCloseMoveAtr": None
        }
    disjoint = select_nonoverlap(rows)
    return {
        "observations": int(len(rows)),
        "nonOverlappingObservations": int(len(disjoint)),
        "observedDirectionalEventRate": round(float(rows["directionCorrect"].mean()), 5),
        "meanDirectionalProbability": round(float(rows["pDirection"].mean()), 5),
        "nonOverlappingObservedEventRate": (
            round(float(disjoint["directionCorrect"].mean()), 5)
            if len(disjoint) else None),
        "meanSigned30mCloseMoveAtr": round(float(rows["signed30mReturnAtr"].mean()), 5),
        "monthlyCounts": {str(k): int(x) for k, x in rows.groupby("month").size().items()},
    }


def grouped_summary(rows, by):
    result = {}
    for name, group in rows.groupby(by, sort=True):
        key = str(name)
        result[key] = group_summary(group)
        result[key]["smallSample"] = len(group) < MIN_SUBGROUP_SIZE
    return result


def run(features_csv, model_file, output_dir):
    feature_manifest = json.loads(features_csv.with_suffix(
        ".manifest.json").read_text(encoding="utf-8-sig"))
    data = read_verified_csv(features_csv, feature_manifest)
    train, val, holdout_count = prepare_validation(data)
    # A joblib model is pickle-compatible executable content.
    # Load only the locally generated file from the user's own prior run.
    bundle = joblib.load(model_file)
    model = validate_bundle(bundle)
    boundaries = quantile_cutoffs(train)
    v = add_validation_profiles(val, model, boundaries)
    profiles = []
    for threshold in THRESHOLDS:
        selected = select_candidates(v, threshold)
        profile = {
            "thresholdPredictedDirectionalProbability": threshold,
            "minimumDirectionVsOppositeMargin": MIN_DIRECTIONAL_MARGIN,
            "all": group_summary(selected),
            "byDirection": grouped_summary(selected, "direction"),
            "bySession": grouped_summary(selected, "sessionGroup"),
            "byRelativeVolatility": grouped_summary(selected, "relativeVolatility"),
            "byTrendEfficiency": grouped_summary(selected, "trendEfficiency"),
            "byTrendAlignment": grouped_summary(selected, "trendDirection"),
            "byContract": grouped_summary(selected, "contract"),
        }
        profiles.append(profile)
    report = {
        "schema": "ninjex-ml-validation-opportunity-profile-v1",
        "status": "EXPLORATORY_VALIDATION_ONLY_NO_TRADING_EDGE_PROVEN",
        "features": str(features_csv),
        "localResearchModel": str(model_file),
        "modelName": "histGradientBoosting",
        "sourceResearchStatus": feature_manifest["researchStatus"],
        "split": "validation 2025-09-01 through 2026-06-14",
        "trainUsedOnlyToComputeReferenceQuantiles": True,
        "trainReferenceCutoffs": boundaries,
        "sampleCounts": {
            "sourceRows": int(len(data)),
            "trainLabelledForReferenceOnly": int(len(train)),
            "validationLabelledScored": int(len(v)),
            "holdoutRowsNotScored": holdout_count,
        },
        "validationUnconditional": {
            "observedUpEventRate": round(float((v["label"] == 2).mean()), 5),
            "observedDownEventRate": round(float((v["label"] == 0).mean()), 5),
            "observedNeutralEventRate": round(float((v["label"] == 1).mean()), 5),
        },
        "candidateProfiles": profiles,
        "method": {
            "thresholds": list(THRESHOLDS),
            "directionMargin": MIN_DIRECTIONAL_MARGIN,
            "candidate": "Clean source 1m parent + max(P(UP),P(DOWN))>=threshold, P(direction)>=P(NEUTRAL), P(direction)-P(opposite)>=0.10",
            "observedEvent": "Future 30m CLOSE >= +0.5 ATR for UP or <= -0.5 ATR for DOWN, not a trade fill",
            "disjoint": "Within each threshold & group, greedily retain earliest observation per contract and then at least 30 minutes apart",
            "sessions": "Exclusive ASIA, LONDON, NEW_YORK, LONDON_NY_OVERLAP, OTHER for diagnostic counts only",
            "regimeBins": "Relative ATR/close and efficiency terciles fixed using training split only",
            "groupEvaluation": "All candidate thresholds & subgroups are exploratory diagnostics on validation reused from model selection; no significance claim, no model retuning.",
        },
        "warnings": [
            "These are diagnostic conditional outcomes, NOT A+ setups or verified high-probability trades.",
            "Validation reuse after model selection creates selection bias; any threshold/group hypothesis needs separate forward validation before final holdout.",
            "Overlapping 30m labels cause serial dependence. Nonoverlap subsets reduce but cannot eliminate it.",
            "Missing/holiday calendar quality exceptions remain under review.",
            "No stop/target order, intrabar ticks, slippage, commissions, prop rules, drawdown or PnL is tested.",
            "Holdout rows and labels are never scored or accessed by the model.",
        ],
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / "es-ml-opportunity-profile-v1.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    md = [
        "# Ninjex AI — validation directional opportunity diagnostics",
        "",
        "**EXPLORATORY — no A+ setup or profitable trade verified.**",
        "",
        f"Validation scored: {len(v):,}. Holdout not scored: {holdout_count:,}.",
        "Direction = UP or DOWN (not NEUTRAL); threshold on model's own class probability.",
        "Candidate requires complete parent 1m observations and 0.10 probability margin.",
        "",
        "| Threshold | Candidates | Nonoverlap | Event rate | Mean model probability | Nonoverlap event rate | Mean signed 30m return (ATR) |",
        "|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for p in profiles:
        a = p["all"]
        def f(key):
            value = a[key]
            return f"{value:.3f}" if value is not None else "—"
        md.append(
            f"| {p['thresholdPredictedDirectionalProbability']:.2f} | "
            f"{a['observations']:,} | {a['nonOverlappingObservations']:,} | "
            f"{f('observedDirectionalEventRate')} | "
            f"{f('meanDirectionalProbability')} | "
            f"{f('nonOverlappingObservedEventRate')} | "
            f"{f('meanSigned30mCloseMoveAtr')} |")
    md += ["", "## Conditional breakdown at threshold 0.55 (descriptive only)", "",
           "| Group | Obs | Nonoverlap | Observed event | Mean predicted event | Signed close move (ATR) |",
           "|---|---:|---:|---:|---:|---:|"]
    chosen = next(x for x in profiles
                  if x["thresholdPredictedDirectionalProbability"] == 0.55)
    for dim in ("byDirection", "bySession", "byRelativeVolatility",
                "byTrendEfficiency", "byTrendAlignment", "byContract"):
        md += ["", "**" + dim + "**", ""]
        for name, summary in chosen[dim].items():
            def pct(x):
                return f"{x:.3f}" if x is not None else "—"
            md.append(
                f"| {name} | {summary['observations']} | "
                f"{summary['nonOverlappingObservations']} | "
                f"{pct(summary['observedDirectionalEventRate'])} | "
                f"{pct(summary['meanDirectionalProbability'])} | "
                f"{pct(summary['meanSigned30mCloseMoveAtr'])} |")
    md += [
        "", "Session/regime cohorts reuse validation data and have not been"
        " independently tested. Treat high event rates only as hypotheses.",
        "The reserved 2026-06-15+ holdout remains untouched.",
        "No financial results or live orders are produced.",
        ""]
    (output_dir / "es-ml-opportunity-profile-v1.md").write_text(
        "\n".join(md), encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", required=True, type=Path)
    parser.add_argument("--model", required=True, type=Path,
                        help="Your OWN locally generated v1 research joblib")
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    result = run(args.features, args.model, args.output_dir)
    print("[NinjexAI-OPPORTUNITY] Status:", result["status"])
    print("[NinjexAI-OPPORTUNITY] Validation scored:",
          result["sampleCounts"]["validationLabelledScored"])
    print("[NinjexAI-OPPORTUNITY] Holdout untouched:",
          result["sampleCounts"]["holdoutRowsNotScored"])
    for row in result["candidateProfiles"]:
        summary = row["all"]
        print("[NinjexAI-OPPORTUNITY] P>=",
              row["thresholdPredictedDirectionalProbability"],
              "candidates=", summary["observations"],
              "nonoverlap=", summary["nonOverlappingObservations"],
              "observed30mEvent=", summary["observedDirectionalEventRate"])
    print("[NinjexAI-OPPORTUNITY] Report:",
          args.output_dir / "es-ml-opportunity-profile-v1.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
