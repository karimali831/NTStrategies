#!/usr/bin/env python3
"""Ninjex AI research: first offline 30-minute outcome ML benchmark.

Only train/validation data is used; the 2026-06-15+ holdout is not scored,
used for calibration, or used for hyperparameter selection.
This does not place trades, calculate executable PnL or certify an edge.
Requires pandas, numpy, scikit-learn, joblib and the previously derived
Ninjex ES historical research CSV.
"""
import argparse
import datetime as dt
import json
import math
from pathlib import Path

import joblib
import numpy as np
import pandas as pd
from sklearn.dummy import DummyClassifier
from sklearn.ensemble import HistGradientBoostingClassifier
from sklearn.impute import SimpleImputer
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import (
    accuracy_score, balanced_accuracy_score, f1_score, log_loss
)
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler
import sklearn

CLASSES = ("DOWN", "NEUTRAL", "UP")
# Fix directional event definitions before looking at validation / holdout:
# after 30m, movement >= +0.5 ATR14 = UP, <= -0.5 ATR14 = DOWN.
ATR_THRESHOLD = 0.5
RANDOM_SEED = 42
BASE_FEATURES = (
    "sessionAsia", "sessionLondon", "sessionNewYork",
    "sessionLondonNewYorkOverlap", "easternWeekday", "atr14_5m",
    "ema20DistanceAtr", "ema50DistanceAtr", "ret3Atr", "ret12Atr",
    "efficiency12", "volumeVsPrior20", "prior12HighDistanceAtr",
    "prior12LowDistanceAtr", "body15Atr", "body60Atr"
)
INPUT_COLUMNS = set(BASE_FEATURES) | {
    "decisionUtc", "decisionEastern", "researchSplit", "contract",
    "label30mCloseRetAtr", "fine1mComplete", "qualityFlags",
    "easternMinuteOfDay"
}
MODEL_FEATURES = BASE_FEATURES + ("timeSin", "timeCos")


def labels_from_returns(values, threshold=ATR_THRESHOLD):
    """Class order: 0 DOWN, 1 NEUTRAL, 2 UP. NaNs must be removed first."""
    if not np.isfinite(values).all():
        raise ValueError("Undefined labels must be removed before classification")
    return np.where(values <= -threshold, 0,
                    np.where(values >= threshold, 2, 1)).astype(np.int8)


def feature_frame(data):
    """Explicit allowed as-of predictor list. No target, contract ID or price."""
    x = pd.DataFrame(index=data.index)
    for column in BASE_FEATURES:
        x[column] = pd.to_numeric(data[column], errors="coerce").astype(float)
    minute = pd.to_numeric(data["easternMinuteOfDay"], errors="coerce")
    rad = 2.0 * np.pi * minute / 1440.0
    x["timeSin"] = np.sin(rad)
    x["timeCos"] = np.cos(rad)
    return x.loc[:, list(MODEL_FEATURES)]


def session_keys(data):
    """Overlapping sessions are encoded as combinations, not mislabelled exclusive."""
    flags = data[["sessionAsia", "sessionLondon", "sessionNewYork"]].fillna(0).astype(int)
    return (flags["sessionAsia"].astype(str) +
            flags["sessionLondon"].astype(str) +
            flags["sessionNewYork"].astype(str)).to_numpy()


def session_prior(train_keys, y_train, val_keys, strength=500):
    base = (np.bincount(y_train, minlength=3) + 1) / (len(y_train) + 3)
    counts = {}
    for key in np.unique(train_keys):
        counts[key] = np.bincount(y_train[train_keys == key], minlength=3)
    predictions = np.empty((len(val_keys), 3), dtype=float)
    for i, key in enumerate(val_keys):
        c = counts.get(key, np.zeros(3))
        predictions[i] = (c + strength * base) / (c.sum() + strength)
    return predictions


def measure(y_true, probabilities):
    probabilities = np.asarray(probabilities, dtype=float)
    if probabilities.shape != (len(y_true), 3):
        raise ValueError("Expected 3-class probabilities")
    if np.any(~np.isfinite(probabilities)):
        raise ValueError("Nonfinite predictions")
    pred = np.argmax(probabilities, axis=1)
    hot = np.eye(3)[y_true]
    class_brier = np.mean((probabilities - hot) ** 2, axis=0)
    confidence = np.max(probabilities, axis=1)
    correct = (pred == y_true)
    ece = 0.
    calibration = []
    for lower, upper in zip(np.linspace(0, 1, 11)[:-1],
                            np.linspace(0, 1, 11)[1:]):
        mask = (confidence >= lower) & (
            confidence < upper if upper < 1. else confidence <= upper)
        if np.any(mask):
            avg_conf = float(np.mean(confidence[mask]))
            accuracy = float(np.mean(correct[mask]))
            ece += float(np.mean(mask)) * abs(avg_conf - accuracy)
            calibration.append({"count": int(mask.sum()),
                                "confidence": round(avg_conf, 5),
                                "accuracy": round(accuracy, 5)})
    return {
        "n": int(len(y_true)),
        "accuracy": round(float(accuracy_score(y_true, pred)), 6),
        "balancedAccuracy": round(float(balanced_accuracy_score(y_true, pred)), 6),
        "macroF1": round(float(f1_score(y_true, pred, labels=[0, 1, 2],
                                      average="macro", zero_division=0)), 6),
        "logLoss": round(float(log_loss(y_true, probabilities, labels=[0, 1, 2])), 6),
        "multiclassBrier": round(float(np.mean(np.sum((probabilities - hot) ** 2, axis=1))), 6),
        "classBrier": {CLASSES[i]: round(float(class_brier[i]), 6) for i in range(3)},
        "maxConfidenceECE": round(ece, 6),
        "calibrationBins": calibration,
    }


def day_block_ci(day_strings, y_true, probabilities_a, probabilities_b,
                 repeats=400, seed=RANDOM_SEED):
    """Paired daily block bootstrap of mean log-loss difference A minus B."""
    p_a = np.clip(probabilities_a[np.arange(len(y_true)), y_true], 1e-15, 1)
    p_b = np.clip(probabilities_b[np.arange(len(y_true)), y_true], 1e-15, 1)
    diff = -np.log(p_a) + np.log(p_b)
    days, indices = np.unique(np.asarray(day_strings), return_inverse=True)
    sums = np.bincount(indices, weights=diff, minlength=len(days))
    counts = np.bincount(indices, minlength=len(days))
    rng = np.random.default_rng(seed)
    sampled = rng.integers(0, len(days), size=(repeats, len(days)))
    results = sums[sampled].sum(axis=1) / counts[sampled].sum(axis=1)
    return {"comparison": "logLoss(A)-logLoss(B)",
            "lower95": round(float(np.quantile(results, .025)), 6),
            "upper95": round(float(np.quantile(results, .975)), 6),
            "calendarDays": int(len(days)),
            "method": "paired bootstrap of Eastern calendar-day blocks; exploratory, not independent-trade inference"}


def session_review(data, y, probs):
    result = {}
    for name, key in (("Asia", "sessionAsia"),
                      ("London", "sessionLondon"),
                      ("NewYork", "sessionNewYork"),
                      ("LondonNewYorkOverlap", "sessionLondonNewYorkOverlap")):
        selected = pd.to_numeric(data[key], errors="coerce").fillna(0).astype(int).to_numpy() == 1
        if selected.sum() == 0:
            continue
        subset = y[selected]
        result[name] = {
            "observations": int(selected.sum()),
            "observedDownRate": round(float(np.mean(subset == 0)), 5),
            "observedNeutralRate": round(float(np.mean(subset == 1)), 5),
            "observedUpRate": round(float(np.mean(subset == 2)), 5),
            "predictedUpProbabilityMean": round(float(np.mean(probs[selected, 2])), 5),
            "predictedDownProbabilityMean": round(float(np.mean(probs[selected, 0])), 5)
        }
    return result


def read_verified_csv(path, manifest):
    if manifest.get("schema") != "ninjex-ml-features-v1":
        raise ValueError("Not a Ninjex ML feature manifest")
    if manifest.get("sourceContracts") != 13:
        raise ValueError("Expected all 13 dated contracts")
    if manifest.get("researchStatus") != "PROVISIONAL_MASKED_NOT_TRAINED":
        raise ValueError("Unexpected source feature status")
    if manifest.get("definitions", {}).get("splits", "").find("2026-06-15") == -1:
        raise ValueError("Unknown holdout boundary; stop rather than redefine it")
    table = pd.read_csv(path, usecols=lambda c: c in INPUT_COLUMNS,
                        low_memory=False)
    absent = INPUT_COLUMNS - set(table.columns)
    if absent:
        raise ValueError("Missing source columns: " + ", ".join(sorted(absent)))
    if len(table) != manifest["counters"]["outputRows"]:
        raise ValueError("Manifest/source CSV row count differs")
    if not set(table["researchSplit"]).issubset({"train", "validation", "holdout"}):
        raise ValueError("Unknown split labels")
    for split, count in (("train", "split_train"), ("validation", "split_validation"),
                         ("holdout", "split_holdout")):
        if int((table["researchSplit"] == split).sum()) != manifest["counters"][count]:
            raise ValueError(f"{split} split count differs from manifest")
    return table


def execute(csv_path, output_dir):
    manifest_path = csv_path.with_suffix(".manifest.json")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    data = read_verified_csv(csv_path, manifest)
    raw_rows = len(data)
    holdout_count = int((data["researchSplit"] == "holdout").sum())
    # Holdout rows (INCLUDING LABELS) are excluded before ML training/evaluation.
    data = data.loc[data["researchSplit"].isin(("train", "validation"))].copy()
    label_series = pd.to_numeric(data["label30mCloseRetAtr"], errors="coerce")
    excluded_unlabelled = int(label_series.isna().sum())
    data = data.loc[label_series.notna()].copy()
    label_series = pd.to_numeric(data["label30mCloseRetAtr"], errors="raise")
    y = labels_from_returns(label_series.to_numpy(dtype=float))
    x = feature_frame(data)
    if x.isna().mean().max() > 0.05:
        raise ValueError("Too many missing ML predictors; inspect source dataset")
    train_mask = (data["researchSplit"] == "train").to_numpy()
    val_mask = (data["researchSplit"] == "validation").to_numpy()
    if train_mask.sum() < 1000 or val_mask.sum() < 1000:
        raise ValueError("Insufficient train/validation labelled samples")
    x_train, x_val = x.loc[train_mask], x.loc[val_mask]
    y_train, y_val = y[train_mask], y[val_mask]
    if len(np.unique(y_train)) != 3:
        raise ValueError("All 3 outcome classes must occur in training")
    train_days = data.loc[train_mask, "decisionEastern"].str.slice(0, 10)
    val_days = data.loc[val_mask, "decisionEastern"].str.slice(0, 10)
    if train_days.max() >= val_days.min():
        raise ValueError("Training and validation chronological windows overlap")
    if val_days.max() >= "2026-06-15":
        raise ValueError("Validation data reaches the reserved holdout period")

    predictors = {
        "prior": DummyClassifier(strategy="prior"),
        "logistic": make_pipeline(SimpleImputer(strategy="median"),
                                  StandardScaler(),
                                  LogisticRegression(C=0.25, max_iter=600,
                                                     random_state=RANDOM_SEED)),
        "histGradientBoosting": make_pipeline(
            SimpleImputer(strategy="median"),
            HistGradientBoostingClassifier(
                max_iter=100, learning_rate=0.045,
                max_leaf_nodes=15, min_samples_leaf=180,
                l2_regularization=10.0, early_stopping=False,
                random_state=RANDOM_SEED)),
    }
    predictions = {}
    scores = {}
    for name, model in predictors.items():
        model.fit(x_train, y_train)
        if not np.array_equal(model.classes_, [0, 1, 2]) and not (
            hasattr(model, "steps") and np.array_equal(model[-1].classes_, [0, 1, 2])):
            raise ValueError(f"Unexpected class order in {name}")
        proba = model.predict_proba(x_val)
        predictions[name] = proba
        scores[name] = measure(y_val, proba)

    prior_keys = session_keys(data.loc[train_mask])
    val_keys = session_keys(data.loc[val_mask])
    sess = session_prior(prior_keys, y_train, val_keys)
    predictions["sessionPrior"] = sess
    scores["sessionPrior"] = measure(y_val, sess)
    # Select on validation only, not holdout. Keep models explicitly research-only.
    best = min(("logistic", "histGradientBoosting"),
               key=lambda key: scores[key]["logLoss"])
    report = {
        "schema": "ninjex-ml-benchmark-v1",
        "status": "RESEARCH_ONLY_NO_TRADING_EDGE_PROVEN",
        "sourceFeatures": str(csv_path),
        "sourceFeatureManifest": str(manifest_path),
        "sourceStatus": manifest["researchStatus"],
        "scikitLearnVersion": sklearn.__version__,
        "randomSeed": RANDOM_SEED,
        "modelInputFeatures": list(MODEL_FEATURES),
        "contractAsPredictor": False,
        "labelDefinition": "30-minute future CLOSE movement divided by decision-time ATR14; DOWN <= -0.5, NEUTRAL between, UP >= +0.5",
        "thresholdAtr": ATR_THRESHOLD,
        "samples": {
            "totalFeatureRows": raw_rows,
            "heldOutNotInspected": holdout_count,
            "unlabelledTrainValidationDropped": excluded_unlabelled,
            "trainLabelled": int(train_mask.sum()),
            "validationLabelled": int(val_mask.sum()),
        },
        "labelDistribution": {
            split: {CLASSES[i]: int(np.sum(group == i)) for i in range(3)}
            for split, group in (("train", y_train), ("validation", y_val))
        },
        "validationScores": scores,
        "validationChosenResearchModel": best,
        "chosenModelBeatsSessionPriorLogLoss": (
            scores[best]["logLoss"] < scores["sessionPrior"]["logLoss"]),
        "chosenVsSessionPriorDailyCI": day_block_ci(
            val_days.to_numpy(), y_val, predictions[best], sess),
        "sessionReviewValidation": session_review(
            data.loc[val_mask], y_val, predictions[best]),
        "holdoutPolicy": "NO HOLDOUT SCORING/INSPECTION/REFITTING PERFORMED; reserve 2026-06-15 onward for final locked-model test",
        "caveats": [
            "Overlapping 30m labels at 5m intervals are serially correlated.",
            "CME holidays and isolated source gaps are still under provenance review.",
            "30m close movement is not an executable stop/target trade outcome.",
            "No commission, spread, slippage, latency, drawdown, prop rules or PnL tested.",
            "Session associations are descriptive, not causal and not strategy edges.",
        ],
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    report_path = output_dir / "es-ml-benchmark-v1.json"
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    # Local model package is NEVER uploaded/executed on cloud or traded live.
    joblib.dump({"schema": "ninjex-ml-provisional-v1",
                 "status": report["status"],
                 "features": list(MODEL_FEATURES),
                 "labelThresholdAtr": ATR_THRESHOLD,
                 "selectionBy": "validation logLoss only",
                 "models": {k: predictors[k] for k in ("logistic", "histGradientBoosting")},
                 "validationChosenResearchModel": best},
                output_dir / "es-ml-benchmark-v1.joblib")
    md = [
        "# Ninjex AI — first 30-minute market ML experiment",
        "",
        "**RESEARCH ONLY — no executable edge or model calibration proven.**",
        "",
        f"Source: {raw_rows:,} feature rows across 13 contracts;",
        f"Train labelled: {int(train_mask.sum()):,}; validation labelled: {int(val_mask.sum()):,};",
        f"Holdout untouched: {holdout_count:,}.",
        "",
        "Outcome classes: DOWN <= -0.5 ATR; UP >= +0.5 ATR in next 30m close; otherwise NEUTRAL.",
        "",
        "| Baseline / Model | Validation log loss ↓ | Multiclass Brier ↓ | Macro F1 ↑ | Confidence ECE ↓ |",
        "|---|---:|---:|---:|---:|",
    ]
    for key in ("prior", "sessionPrior", "logistic", "histGradientBoosting"):
        score = scores[key]
        md.append(f"| {key} | {score['logLoss']:.5f} | "
                  f"{score['multiclassBrier']:.5f} | "
                  f"{score['macroF1']:.5f} | "
                  f"{score['maxConfidenceECE']:.5f} |")
    md += [
        "", f"Chosen research model on validation: **{best}**.",
        f"Better log loss than session prior? **{report['chosenModelBeatsSessionPriorLogLoss']}**.",
        "", "## Observed session outcomes (validation only)", "",
        "| Session | Rows | Observed DOWN | NEUTRAL | UP | Average predicted UP |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for name, s in report["sessionReviewValidation"].items():
        md.append(f"| {name} | {s['observations']:,} | "
                  f"{s['observedDownRate']:.3f} | {s['observedNeutralRate']:.3f} | "
                  f"{s['observedUpRate']:.3f} | "
                  f"{s['predictedUpProbabilityMean']:.3f} |")
    ci = report["chosenVsSessionPriorDailyCI"]
    md += ["", "Paired day-block bootstrap CI for chosen model minus session-prior log loss:",
           f"**[{ci['lower95']:.5f}, {ci['upper95']:.5f}]** "
           "(negative favours chosen model).",
           "", "This does not establish profitability or an A+ trading setup.",
           "Holdout data has not been evaluated, touched for tuning or used for calibration.",
           "All models are research-only; do not load into a live trading system.",
           ""]
    (output_dir / "es-ml-benchmark-v1.md").write_text("\n".join(md), encoding="utf-8")
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--features", required=True, type=Path)
    p.add_argument("--output-dir", required=True, type=Path)
    a = p.parse_args()
    report = execute(a.features, a.output_dir)
    print("[NinjexAI-ML] Train:", report["samples"]["trainLabelled"])
    print("[NinjexAI-ML] Validation:", report["samples"]["validationLabelled"])
    print("[NinjexAI-ML] Untouched holdout:", report["samples"]["heldOutNotInspected"])
    print("[NinjexAI-ML] Research model:", report["validationChosenResearchModel"])
    print("[NinjexAI-ML] Beats session prior:",
          report["chosenModelBeatsSessionPriorLogLoss"])
    print("[NinjexAI-ML] Report:",
          a.output_dir / "es-ml-benchmark-v1.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
