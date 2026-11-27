import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

from tools.ai_market_train_baseline import INPUT_COLUMNS
from tools.ai_market_validation_opportunities import (
    add_validation_profiles, describe_sessions, group_summary,
    group_three, prepare_validation, quantile_cutoffs, run,
    select_candidates, select_nonoverlap, validate_bundle,
)


class FakeResearchModel:
    """Synthetic, nonfit probability fixture; no real market data needed."""
    classes_ = np.array([0, 1, 2])

    def predict_proba(self, x):
        n = len(x)
        up = np.where(x["ret3Atr"].to_numpy() > 0, 0.70, 0.24)
        down = np.where(x["ret3Atr"].to_numpy() < 0, 0.66, 0.20)
        neutral = 1.0 - up - down
        return np.stack([down, neutral, up], axis=1)


def fake_dataset(root):
    csv_path = root / "features.csv"
    records = []
    split_settings = [
        ("train", dt.datetime(2025, 8, 10, 9, 30, tzinfo=dt.timezone.utc), 300),
        ("validation", dt.datetime(2026, 2, 10, 9, 30, tzinfo=dt.timezone.utc), 200),
        ("holdout", dt.datetime(2026, 7, 15, 9, 30, tzinfo=dt.timezone.utc), 100)
    ]
    for split, start, count in split_settings:
        for i in range(count):
            time = start + dt.timedelta(minutes=5*i)
            sign = 1 if i % 2 == 0 else -1
            row = {k: 1 for k in INPUT_COLUMNS}
            row.update({
                "contract": "ES 12-26",
                "decisionUtc": time.isoformat(),
                "decisionEastern": time.isoformat(),
                "researchSplit": split,
                "sessionAsia": 0, "sessionLondon": 1,
                "sessionNewYork": 0, "sessionLondonNewYorkOverlap": 0,
                "easternMinuteOfDay": 540 + (i*5) % 500,
                "easternWeekday": time.weekday(),
                "atr14_5m": 12. + i/100,
                "close": 6000.0 + i/100,
                "ret3Atr": float(sign * 0.3),
                "efficiency12": 0.4 + i/5000.,
                "label30mCloseRetAtr": float(sign * 0.7),
                "fine1mComplete": 0 if split == "validation" and i == 0 else 1,
                "qualityFlags": ("SPARSE_1M_IN_PARENT" if split=="validation" and i==0 else "OK")
            })
            records.append(row)
    pd.DataFrame(records).to_csv(csv_path, index=False)
    manifest = {
        "schema": "ninjex-ml-features-v1",
        "sourceContracts": 13,
        "researchStatus": "PROVISIONAL_MASKED_NOT_TRAINED",
        "counters": {"outputRows": 600, "split_train": 300,
                     "split_validation": 200, "split_holdout": 100},
        "definitions": {"splits":
            "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"}
    }
    csv_path.with_suffix(".manifest.json").write_text(json.dumps(manifest))
    model_path = root / "locally_generated.joblib"
    from tools.ai_market_train_baseline import MODEL_FEATURES
    joblib.dump({
        "schema": "ninjex-ml-provisional-v1",
        "status": "RESEARCH_ONLY_NO_TRADING_EDGE_PROVEN",
        "features": list(MODEL_FEATURES),
        "labelThresholdAtr": .5,
        "models": {"histGradientBoosting": FakeResearchModel()},
        "validationChosenResearchModel": "histGradientBoosting"
    }, model_path)
    return csv_path, model_path


class ValidationOpportunityTests(unittest.TestCase):
    def test_holdout_rows_removed_before_features_or_outcomes(self):
        df = pd.DataFrame([
            {"researchSplit": "train", "label30mCloseRetAtr": .7,
             "decisionEastern": "2025-07-01"},
            {"researchSplit": "validation", "label30mCloseRetAtr": -.7,
             "decisionEastern": "2026-01-01"},
            {"researchSplit": "holdout", "label30mCloseRetAtr": 1000000,
             "decisionEastern": "2026-08-01"},
        ])
        train, validation, heldout = prepare_validation(df)
        self.assertEqual((len(train), len(validation), heldout), (1, 1, 1))
        self.assertFalse((validation["researchSplit"] == "holdout").any())

    def test_disjoint_window_reduces_overlapping_event_counts(self):
        base = dt.datetime(2026, 3, 5, 13, 30, tzinfo=dt.timezone.utc)
        rows = pd.DataFrame([
            {"contract": "ES 03-26", "decisionUtcParsed": base+dt.timedelta(minutes=k*5),
             "directionCorrect": k%2 == 0, "pDirection": .7, "month": "2026-03",
             "signed30mReturnAtr": .6} for k in range(12)])
        sampled = select_nonoverlap(rows)
        self.assertEqual(len(sampled), 2)
        self.assertEqual(len(set(sampled.index)), 2)
        summary = group_summary(rows)
        self.assertEqual(summary["nonOverlappingObservations"], 2)
        self.assertEqual(summary["observations"], 12)

    def test_missing_minutes_not_considered_candidate(self):
        now = pd.Timestamp("2026-05-10T13:35:00Z")
        v = pd.DataFrame([
            {"fine1mComplete": 0, "qualityFlags": "SPARSE_1M_IN_PARENT",
             "pDirection": .78, "pNeutral": .1, "directionMargin": .5,
             "direction": "UP", "directionCorrect": True, "contract": "ES 03-26",
             "decisionUtcParsed": now, "month": "2026-05", "signed30mReturnAtr": .9},
            {"fine1mComplete": 1, "qualityFlags": "OK",
             "pDirection": .7, "pNeutral": .1, "directionMargin": .5,
             "direction": "UP", "directionCorrect": True, "contract": "ES 03-26",
             "decisionUtcParsed": now+dt.timedelta(minutes=5), "month": "2026-05", "signed30mReturnAtr": .9}
        ])
        self.assertEqual(len(select_candidates(v, .6)), 1)

    def test_end_to_end_exploratory_report_does_not_score_holdout(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp)
            feat, model = fake_dataset(folder)
            report = run(feat, model, folder / "out")
            self.assertEqual(report["sampleCounts"]["sourceRows"], 600)
            self.assertEqual(report["sampleCounts"]["validationLabelledScored"], 200)
            self.assertEqual(report["sampleCounts"]["holdoutRowsNotScored"], 100)
            self.assertEqual(len(report["candidateProfiles"]), 5)
            self.assertTrue((folder / "out" / "es-ml-opportunity-profile-v1.json").exists())
            self.assertTrue((folder / "out" / "es-ml-opportunity-profile-v1.md").exists())
            self.assertIn("bySession", report["candidateProfiles"][0])
            self.assertGreater(report["candidateProfiles"][0]["all"]["observations"], 0)
            self.assertEqual(report["status"], "EXPLORATORY_VALIDATION_ONLY_NO_TRADING_EDGE_PROVEN")


if __name__ == "__main__":
    unittest.main()
