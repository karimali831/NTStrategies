import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import joblib
import numpy as np
import pandas as pd

from tools.ai_market_train_baseline import INPUT_COLUMNS, MODEL_FEATURES
from tools.ai_market_bracket_research import (
    ES_POINT_VALUE, ES_TICK_SIZE, HOLD_MINUTES, STOP_TICKS, TARGET_TICKS,
    execute, minute_window, simulate, summarize, close_time,
)
from tools.ai_market_ml_features import Bar


class FixedSyntheticModel:
    classes_ = np.array([0, 1, 2])
    def predict_proba(self, x):
        return np.tile(np.array([.23, .24, .53]), (len(x), 1))


def make_source(root):
    source = root / "ES_03-26_run"
    source.mkdir()
    (source / "manifest.json").write_text(json.dumps({
        "contract": "ES 03-26", "datasetTag": "synthetic-13-contract-research"
    }))
    start = dt.datetime(2026, 2, 10, 9, 30, tzinfo=dt.timezone.utc)
    # Build complete minute history for each validation candidate, so all
    # 30m brackets can be evaluated; prices held constant to make friction
    # effect explicit instead of designing a fake winning model.
    with (source / "bars_1m.csv").open("w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["schemaVersion","contract","frameMinutes","endUtc",
                         "endEastern","utcOffsetMinutes","open","high","low",
                         "close","volume","flags"])
        for i in range(730):
            t = start + dt.timedelta(minutes=i)
            writer.writerow(["ninjex-ohlcv-v1","ES 03-26",1,t.isoformat(),
                             t.isoformat(),0,6000,6000.25,5999.75,
                             6000,10,"OK"])
    audit = root / "basket.json"
    audit.write_text(json.dumps({
        "schema": "ninjex-basket-audit-v1",
        "datasetTag": "synthetic-13-contract-research",
        "status": "NEEDS_REVIEW",
        "contracts": [{"contract": "ES 03-26", "folders": [str(source)]}]
    }))
    features = root / "features.csv"
    rows = []
    for name, origin, n in [
        ("train", dt.datetime(2025, 8, 10, 9, 30, tzinfo=dt.timezone.utc),300),
        ("validation",start,120),
        ("holdout",dt.datetime(2026, 7, 1, 9, 30, tzinfo=dt.timezone.utc),80)
    ]:
        for i in range(n):
            t = origin + dt.timedelta(minutes=5*i)
            row = {key:1 for key in INPUT_COLUMNS}
            row.update({
                "contract":"ES 03-26", "decisionUtc":t.isoformat(),
                "decisionEastern":t.isoformat(),
                "researchSplit":name, "qualityFlags":"OK",
                "fine1mComplete":1,"close":6000.0,"atr14_5m":10.0,
                "efficiency12":.2+i*.001,
                "sessionAsia":0,"sessionLondon":1, "sessionNewYork":0,
                "sessionLondonNewYorkOverlap":0, "easternMinuteOfDay":600+i,
                "label30mCloseRetAtr":.6, "ret3Atr":.1, "ret12Atr":.2
            })
            rows.append(row)
    pd.DataFrame(rows).to_csv(features, index=False)
    features.with_suffix(".manifest.json").write_text(json.dumps({
        "schema":"ninjex-ml-features-v1",
        "sourceContracts":13,
        "researchStatus":"PROVISIONAL_MASKED_NOT_TRAINED",
        "datasetTag":"synthetic-13-contract-research",
        "counters":{"outputRows":500,
                    "split_train":300,"split_validation":120,"split_holdout":80},
        "definitions":{"splits":
            "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"}
    }))
    model = root / "ml.joblib"
    joblib.dump({
        "schema":"ninjex-ml-provisional-v1",
        "status":"RESEARCH_ONLY_NO_TRADING_EDGE_PROVEN",
        "features":list(MODEL_FEATURES),
        "labelThresholdAtr":.5,
        "validationChosenResearchModel":"histGradientBoosting",
        "models":{"histGradientBoosting":FixedSyntheticModel()}
    },model)
    return features, model, audit


class BracketResearchTests(unittest.TestCase):
    def test_long_reaches_target_before_stop(self):
        now = dt.datetime(2026, 3, 10, 14, 0, tzinfo=dt.timezone.utc)
        candles = [Bar(now+dt.timedelta(minutes=i), 6000, 6000.25,5999.75,6000,10,"OK")
                   for i in range(1, HOLD_MINUTES+1)]
        candles[2] = Bar(candles[2].end,6000,6007,5999,6006,40,"OK")
        result = simulate(candles, 1)
        self.assertEqual(result["exitReason"],"TARGET")
        self.assertEqual(result["entry"],6000.25)
        # 24 ticks = six points, minus two adverse slippage ticks and fees.
        self.assertEqual(result["netUsd"],270.)

    def test_stop_target_same_minute_assumes_stop(self):
        now = dt.datetime(2026, 3, 10, 14, 0, tzinfo=dt.timezone.utc)
        candles = [Bar(now+dt.timedelta(minutes=i),6000,6000.5,5999.5,6000,10,"OK")
                   for i in range(1,HOLD_MINUTES+1)]
        candles[0] = Bar(candles[0].end,6000,6007.5,5997,6001,10,"OK")
        result = simulate(candles,1)
        self.assertEqual(result["exitReason"],"AMBIGUOUS_STOP_FIRST")
        self.assertTrue(result["bothTouched1m"])
        self.assertEqual(result["netUsd"],-180.)

    def test_short_stop_and_trading_fee(self):
        now = dt.datetime(2026, 3, 10, 14, 0, tzinfo=dt.timezone.utc)
        candles = [Bar(now+dt.timedelta(minutes=i),6000,6000,6000,6000,10,"OK")
                   for i in range(1,HOLD_MINUTES+1)]
        candles[1] = Bar(candles[1].end,6000,6004,5999.75,6003,10,"OK")
        result = simulate(candles,-1)
        self.assertEqual(result["exitReason"],"STOP")
        self.assertEqual(result["netUsd"],-180.)

    def test_missing_minute_never_synthetic_fill(self):
        now = dt.datetime(2026, 3, 10, 14, 0, tzinfo=dt.timezone.utc)
        future = {now+dt.timedelta(minutes=i):
                  Bar(now+dt.timedelta(minutes=i),6000,6001,5999,6000,10,"OK")
                  for i in range(1,HOLD_MINUTES+1)}
        self.assertEqual(len(minute_window(future,now)),HOLD_MINUTES)
        del future[now+dt.timedelta(minutes=3)]
        self.assertIsNone(minute_window(future,now))

    def test_zero_trades_and_utc_rejection(self):
        self.assertEqual(summarize([])["ambiguousStops"],0)
        with self.assertRaisesRegex(ValueError,"Naive"):
            close_time("2026-03-10T14:00:00")

    def test_end_to_end_validation_only_equal_time_comparison(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            features,model,audit = make_source(root)
            report = execute(features,model,audit,root/"out")
            self.assertEqual(report["holdoutRowsNotScored"],80)
            self.assertEqual(report["validationRowsScored"],120)
            self.assertEqual(report["signalsAt45pct"],120)
            self.assertGreater(report["nonOverlappingDecisions"],0)
            self.assertEqual(report["excluded"],{})
            for name in ("ml","alwaysLong","momentum12"):
                self.assertEqual(report["results"][name]["trades"],
                                 report["nonOverlappingDecisions"])
                self.assertLess(report["results"][name]["netUsd"],0)
            self.assertEqual(report["results"]["ml"]["netUsd"],
                             report["results"]["alwaysLong"]["netUsd"])
            self.assertTrue((root/"out"/"es-ml-bracket-research-v1.json").exists())
            self.assertTrue((root/"out"/"es-ml-bracket-research-v1.md").exists())
            self.assertTrue((root/"out"/"es-ml-bracket-research-v1-trades.csv").exists())


if __name__ == "__main__":
    unittest.main()
