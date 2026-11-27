import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path
from zoneinfo import ZoneInfo

import numpy as np
import pandas as pd

from tools.ai_market_train_baseline import (
    ATR_THRESHOLD, MODEL_FEATURES, CLASSES,
    day_block_ci, execute, feature_frame, labels_from_returns,
    measure, session_prior
)

ET = ZoneInfo("America/New_York")
BASE = {
    "sessionAsia": 0, "sessionLondon": 0, "sessionNewYork": 1,
    "sessionLondonNewYorkOverlap": 0, "easternWeekday": 2,
    "easternMinuteOfDay": 600, "atr14_5m": 10, "close": 6000,
    "ema20DistanceAtr": 0.2, "ema50DistanceAtr": 0.4, "ret3Atr": 0.1,
    "ret12Atr": 0.5, "efficiency12": 0.3, "volumeVsPrior20": 1.2,
    "prior12HighDistanceAtr": -0.3, "prior12LowDistanceAtr": 0.7,
    "body15Atr": 0.5, "body60Atr": 0.8
}


def fixture(root, corrupt=False):
    feature_file = root / "es-test-features.csv"
    counters = {"outputRows": 2600, "split_train": 1200,
                "split_validation": 1200, "split_holdout": 200}
    columns = list(BASE) + [
        "contract", "decisionUtc", "decisionEastern",
        "researchSplit", "label30mCloseRetAtr",
        "fine1mComplete", "qualityFlags"
    ]
    with feature_file.open("w", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=columns)
        writer.writeheader()
        for split, start, count in (
            ("train", dt.datetime(2025, 8, 10, tzinfo=ET), 1200),
            ("validation", dt.datetime(2026, 2, 2, tzinfo=ET), 1200),
            ("holdout", dt.datetime(2026, 7, 1, tzinfo=ET), 200)):
            for i in range(count):
                date = start + dt.timedelta(minutes=5 * i)
                # Clearly non-fitted deterministic synthetic labels. The
                # holdout labels are unobserved by the benchmark.
                y = 0.8 * np.sin(i * 0.23)
                r = dict(BASE)
                r.update({"contract": "ES 12-26",
                          "decisionUtc": date.astimezone(dt.timezone.utc).isoformat(),
                          "decisionEastern": date.isoformat(),
                          "researchSplit": split,
                          "label30mCloseRetAtr": float(y),
                          "fine1mComplete": 1,
                          "qualityFlags": "OK",
                          "ret3Atr": float(np.cos(i * 0.23)),
                          "easternMinuteOfDay": date.hour * 60 + date.minute,
                          "easternWeekday": date.weekday()})
                writer.writerow(r)
    manifest = {
        "schema": "ninjex-ml-features-v1", "sourceContracts": 13,
        "researchStatus": "PROVISIONAL_MASKED_NOT_TRAINED",
        "counters": counters,
        "definitions": {"splits":
            "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"}
    }
    if corrupt:
        manifest["counters"]["outputRows"] += 1
    feature_file.with_suffix(".manifest.json").write_text(
        json.dumps(manifest))
    return feature_file


class FirstMLBenchmarkChecks(unittest.TestCase):
    def test_fixed_three_class_outcomes(self):
        y = labels_from_returns(np.array([-1.0, -0.5, -0.499, 0.0,
                                          0.499, 0.5, 1.0]))
        self.assertEqual(y.tolist(), [0, 0, 1, 1, 1, 2, 2])
        self.assertEqual(ATR_THRESHOLD, 0.5)

    def test_future_labels_never_become_features(self):
        frame = pd.DataFrame([dict(BASE, label30mCloseRetAtr=-100.0,
                                   label60mCloseRetAtr=200.0)])
        x = feature_frame(frame)
        self.assertEqual(tuple(x.columns), MODEL_FEATURES)
        self.assertFalse(any("label" in f.lower() for f in x.columns))
        self.assertFalse(any("contract" in f.lower() for f in x.columns))
        self.assertFalse(any("close60" in f.lower() for f in x.columns))
        self.assertTrue(np.isfinite(x.to_numpy()).all())

    def test_probabilities_prior_and_daily_block(self):
        x = np.array(["010", "010", "001", "000", "111"])
        y = np.array([0, 1, 2, 1, 0], dtype=np.int8)
        prob = session_prior(x, y, x, strength=50)
        self.assertEqual(prob.shape, (5, 3))
        self.assertTrue(np.allclose(prob.sum(axis=1), 1.0))
        report = measure(y, prob)
        self.assertTrue(report["logLoss"] > 0)
        ci = day_block_ci(["2026-02-02"] * 2 +
                          ["2026-02-03"] * 3, y, prob, prob)
        self.assertEqual(ci["lower95"], 0.0)
        self.assertEqual(ci["upper95"], 0.0)
        self.assertEqual(ci["calendarDays"], 2)

    def test_end_to_end_does_not_evaluate_holdout(self):
        with tempfile.TemporaryDirectory() as folder:
            path = fixture(Path(folder))
            report = execute(path, Path(folder) / "result")
            self.assertEqual(report["samples"]["totalFeatureRows"], 2600)
            self.assertEqual(report["samples"]["heldOutNotInspected"], 200)
            self.assertEqual(report["samples"]["trainLabelled"], 1200)
            self.assertEqual(report["samples"]["validationLabelled"], 1200)
            self.assertEqual(set(report["validationScores"]),
                             {"prior", "sessionPrior", "logistic", "histGradientBoosting"})
            self.assertEqual(len(report["sessionReviewValidation"]), 1)
            self.assertTrue((Path(folder) / "result" /
                             "es-ml-benchmark-v1.json").exists())
            self.assertTrue((Path(folder) / "result" /
                             "es-ml-benchmark-v1.md").exists())
            self.assertTrue((Path(folder) / "result" /
                             "es-ml-benchmark-v1.joblib").exists())

    def test_manifest_mismatch_is_fatal(self):
        with tempfile.TemporaryDirectory() as folder:
            path = fixture(Path(folder), corrupt=True)
            with self.assertRaisesRegex(ValueError, "Manifest/source CSV"):
                execute(path, Path(folder) / "result")


if __name__ == "__main__":
    unittest.main()
