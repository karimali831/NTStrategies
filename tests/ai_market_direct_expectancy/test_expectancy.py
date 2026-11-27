import csv
import datetime as dt
import json
import tempfile
import unittest
from collections import defaultdict
from pathlib import Path

import numpy as np
import pandas as pd

from tools.ai_market_direct_expectancy import (
    MIN_EXPECTED_NET_USD, make_labels, select_disjoint, execute,
)
from tools.ai_market_train_baseline import INPUT_COLUMNS
from tools.ai_market_ml_features import Bar

UTC = dt.timezone.utc


def write_fixture(root, train_count=1050, val_count=1050, holdout_count=30):
    raw = root / "ES_03-26_raw"
    raw.mkdir()
    contract = "ES 03-26"
    tag = "isolated-expectancy-test"
    (raw / "manifest.json").write_text(json.dumps(
        {"contract": contract, "datasetTag": tag}))
    raw_path = raw / "bars_1m.csv"
    # Flat data gives deterministic time exits after entry/exit slippage:
    # -$30 net for both sides, so a faithful expectancy model should
    # decline to trade rather than hunt for a lucky configuration.
    starts = [
        dt.datetime(2025, 8, 10, 13, 0, tzinfo=UTC),
        dt.datetime(2026, 3, 1, 13, 0, tzinfo=UTC)
    ]
    with raw_path.open("w", newline="") as stream:
        w = csv.writer(stream)
        w.writerow("schemaVersion,contract,frameMinutes,endUtc,endEastern,utcOffsetMinutes,open,high,low,close,volume,flags".split(","))
        for start, count in zip(starts, (train_count, val_count)):
            for i in range(count * 5 + 50):
                bar_end = start + dt.timedelta(minutes=i)
                w.writerow(["ninjex-ohlcv-v1", contract, 1,
                            bar_end.isoformat(), bar_end.isoformat(), 0,
                            6000, 6000.25, 5999.75, 6000, 2, "OK"])
    audit = root / "audit.json"
    audit.write_text(json.dumps({
        "schema": "ninjex-basket-audit-v1", "status": "NEEDS_REVIEW",
        "datasetTag": tag,
        "contracts": [{"contract":contract, "folders":[str(raw)]}]
    }))
    rows = []
    for split, start, count in (
        ("train", starts[0], train_count),
        ("validation", starts[1], val_count),
        ("holdout", dt.datetime(2026, 7, 1, 13, 0, tzinfo=UTC), holdout_count)):
        for i in range(count):
            at = start + dt.timedelta(minutes=5*i)
            r = {col: 1 for col in INPUT_COLUMNS}
            r.update({
                "contract": contract, "decisionUtc": at.isoformat(),
                "decisionEastern": at.isoformat(),
                "researchSplit": split, "fine1mComplete": 1,
                "qualityFlags": "OK",
                "sessionAsia": 0, "sessionLondon": 1,
                "sessionNewYork": 0, "sessionLondonNewYorkOverlap": 0,
                "easternWeekday": at.weekday(),
                "easternMinuteOfDay": 13*60 + (i*5) % 500,
                "atr14_5m": 5, "close": 6000,
                "ret12Atr": 0.3, "ret3Atr":0.1,
                "label30mCloseRetAtr": 0,
            })
            rows.append(r)
    csv_path = root / "features.csv"
    pd.DataFrame(rows).to_csv(csv_path, index=False)
    csv_path.with_suffix(".manifest.json").write_text(json.dumps({
        "schema": "ninjex-ml-features-v1", "datasetTag": tag,
        "sourceContracts": 13,
        "researchStatus": "PROVISIONAL_MASKED_NOT_TRAINED",
        "counters": {
            "outputRows": len(rows), "split_train":train_count,
            "split_validation":val_count, "split_holdout":holdout_count
        },
        "definitions": {
            "splits": "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"
        }
    }))
    return csv_path, audit


class DirectExpectancyTests(unittest.TestCase):
    def test_label_builder_refuses_holdout(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path, audit_path = write_fixture(root, train_count=3, val_count=3)
            audit = json.loads(audit_path.read_text())
            frame = pd.read_csv(path)
            holdout = frame.loc[frame["researchSplit"] == "holdout"].copy()
            with self.assertRaisesRegex(ValueError, "Holdout"):
                make_labels(holdout, audit, defaultdict(int))

    def test_both_directions_same_candle_path_and_net_friction(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path, audit_path = write_fixture(root, train_count=3, val_count=3)
            audit = json.loads(audit_path.read_text())
            df = pd.read_csv(path)
            research = df.loc[df["researchSplit"] != "holdout"].copy()
            result = make_labels(research, audit, defaultdict(int))
            self.assertEqual(len(result), 6)
            self.assertTrue((result["pnlLongUsd"] == -30).all())
            self.assertTrue((result["pnlShortUsd"] == -30).all())
            self.assertTrue((result["targetLong"] == 0).all())
            self.assertTrue((result["targetShort"] == 0).all())

    def test_disjoint_signal_policy_never_peeks_at_future_pnl(self):
        base = dt.datetime(2026, 3, 5, 13, 0, tzinfo=UTC)
        frame = pd.DataFrame([{
            "contract": "ES 03-26",
            "decisionUtc": (base+dt.timedelta(minutes=i*5)).isoformat(),
            "predictedBestNetUsd": 50 if i in (0,1,2,6,7) else -10,
            "pnlLongUsd": 10000 if i%2 else -10000,
        } for i in range(12)])
        picked = select_disjoint(frame)
        self.assertEqual(len(picked), 2)
        self.assertEqual(picked["decisionUtc"].tolist(),
                         [frame.iloc[0]["decisionUtc"], frame.iloc[6]["decisionUtc"]])
        self.assertEqual(MIN_EXPECTED_NET_USD, 20)

    def test_full_workflow_declines_when_only_negative_expected_returns(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            path, audit = write_fixture(root)
            result = execute(path, audit, root / "out")
            self.assertEqual(result["holdoutNotScoredRows"], 30)
            self.assertGreaterEqual(result["counts"]["trainLabelledBothDirections"], 1000)
            self.assertGreaterEqual(result["counts"]["validationLabelledBothDirections"], 1000)
            self.assertEqual(result["counts"]["nonOverlappingValidationEntries"], 0)
            self.assertLess(result["validationPredictedNetUsd"]["maxPredictedNetUsd"], 0)
            self.assertEqual(result["validationPolicyComparisons"]["mlExpectancy"]["trades"], 0)
            self.assertTrue((root / "out" / "es-direct-expectancy-v2.json").exists())
            self.assertTrue((root / "out" / "es-direct-expectancy-v2.md").exists())
            self.assertTrue((root / "out" / "es-direct-expectancy-v2.joblib").exists())


if __name__ == "__main__":
    unittest.main()
