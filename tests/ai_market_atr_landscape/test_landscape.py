import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd

from tools.ai_market_atr_bracket_landscape import (
    POLICIES, bracket, future_bars, simulate, train_hourly, perform,
)
from tools.ai_market_ml_features import Bar, NY
from tools.ai_market_train_baseline import INPUT_COLUMNS

UTC = dt.timezone.utc


def fixture(root):
    folder = root / "ES_06-25_20250801T000000000_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    folder.mkdir()
    contract = "ES 06-25"
    tag = "atr-training-fixture"
    (folder / "manifest.json").write_text(json.dumps(
        {"contract": contract, "datasetTag": tag}))
    first = dt.datetime(2024, 4, 1, 12, 0, tzinfo=UTC)
    second = dt.datetime(2025, 5, 5, 12, 0, tzinfo=UTC)
    test_validation = dt.datetime(2025, 10, 2, 12, 0, tzinfo=UTC)
    test_holdout = dt.datetime(2026, 8, 2, 12, 0, tzinfo=UTC)
    with (folder/"bars_1m.csv").open("w", newline="") as stream:
        w = csv.writer(stream)
        w.writerow("schemaVersion,contract,frameMinutes,endUtc,endEastern,utcOffsetMinutes,open,high,low,close,volume,flags".split(","))
        for start in (first, second):
            for i in range(150*60+65):
                time = start + dt.timedelta(minutes=i)
                w.writerow(["ninjex-ohlcv-v1", contract, 1,
                            time.isoformat(), time.astimezone(NY).isoformat(),
                            -240, 6000,6000.25,5999.75,6000, 1, "OK"])
    features = root/"test-features.csv"
    rows = []
    for split,start,count in (
        ("train",first,150),("train",second,150),
        ("validation",test_validation,10),
        ("holdout",test_holdout,10)):
        for i in range(count):
            t = start + dt.timedelta(hours=i)
            row = {k:1 for k in INPUT_COLUMNS}
            row.update({
                "contract":contract, "researchSplit":split,
                "decisionUtc":t.isoformat(),
                "decisionEastern":t.astimezone(NY).isoformat(),
                "fine1mComplete":1, "qualityFlags":"OK",
                "close":6000, "atr14_5m":2,
                "sessionLondon":1,"sessionNewYork":0,"sessionAsia":0,
                "sessionLondonNewYorkOverlap":0,
                "easternMinuteOfDay":480, "easternWeekday":1,
                "ret12Atr":0.1, "ret3Atr":0.1
            })
            rows.append(row)
    pd.DataFrame(rows).to_csv(features,index=False)
    features.with_suffix(".manifest.json").write_text(json.dumps({
        "schema":"ninjex-ml-features-v1",
        "sourceContracts":13, "datasetTag":tag,
        "researchStatus":"PROVISIONAL_MASKED_NOT_TRAINED",
        "counters":{"outputRows":320,"split_train":300,
                    "split_validation":10,"split_holdout":10},
        "definitions":{"splits":
        "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"}
    }))
    audit = root/"audit.json"
    audit.write_text(json.dumps({
        "schema":"ninjex-basket-audit-v1", "datasetTag":tag,
        "status":"NEEDS_REVIEW",
        "contracts":[{"contract":contract,"folders":[str(folder)]}]
    }))
    return features,audit


class ATRLandscapeTests(unittest.TestCase):
    def test_stop_target_rounding_and_risk_cap(self):
        self.assertEqual(bracket(POLICIES[0], 8), (12,24,30))
        self.assertEqual(bracket(POLICIES[1], 2), (8,12,30))
        self.assertEqual(bracket(POLICIES[2], 3), (12,24,60))
        self.assertIsNone(bracket(POLICIES[2], 5))
        self.assertIsNone(bracket(POLICIES[1], 6))

    def test_same_bar_touch_is_always_stop_first(self):
        now = dt.datetime(2025, 5, 6, 13, 0, tzinfo=UTC)
        bars = [Bar(now+dt.timedelta(minutes=i),
                    6000,6000.25,5999.75,6000,1,"OK")
                for i in range(1,61)]
        bars[0] = Bar(bars[0].end,6000,6007,5996,6000,10,"OK")
        result = simulate(bars,1,12,24,30)
        self.assertEqual(result["exitReason"],"AMBIGUOUS_STOP_FIRST")
        self.assertEqual(result["netUsd"],-180)
        self.assertTrue(result["bothTouched1m"])
        self.assertEqual(simulate(bars,-1,12,24,30)["exitReason"],
                         "AMBIGUOUS_STOP_FIRST")

    def test_winner_and_flat_time_exit(self):
        now = dt.datetime(2025, 5, 6, 13, 0, tzinfo=UTC)
        bars = [Bar(now+dt.timedelta(minutes=i),
                    6000,6000.25,5999.75,6000,1,"OK")
                for i in range(1,61)]
        self.assertEqual(simulate(bars,1,12,24,30)["netUsd"],-30)
        self.assertEqual(simulate(bars,-1,12,24,60)["netUsd"],-30)
        bars[2] = Bar(bars[2].end,6000,6007,5999.75,6006,1,"OK")
        self.assertEqual(simulate(bars,1,12,24,30)["netUsd"],270)

    def test_missing_future_not_imputed(self):
        now = dt.datetime(2025,5,6,13,tzinfo=UTC)
        bars={now+dt.timedelta(minutes=i):
              Bar(now+dt.timedelta(minutes=i),6000,6001,5999,6000,1,"OK")
              for i in range(1,61)}
        self.assertEqual(len(future_bars(bars,now)),60)
        del bars[now+dt.timedelta(minutes=19)]
        self.assertIsNone(future_bars(bars,now))

    def test_only_train_hourly_rows(self):
        times=["2025-08-01T13:00:00+00:00","2025-10-01T13:00:00+00:00",
               "2026-08-01T13:00:00+00:00","2025-08-01T13:05:00+00:00"]
        rows=[{"researchSplit":split,"decisionUtc":time,"fine1mComplete":1,
               "qualityFlags":"OK"} for split,time in zip(
                 ("train","validation","holdout","train"),times)]
        result=train_hourly(pd.DataFrame(rows))
        self.assertEqual(result["decisionUtc"].tolist(), [times[0]])

    def test_full_pipeline_never_scores_validation_or_holdout(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            features,audit=fixture(root)
            result=perform(features,audit,root/"out")
            self.assertEqual(result["validationRowsNotScored"],10)
            self.assertEqual(result["holdoutRowsNotScored"],10)
            self.assertEqual(result["matchedDecisionTimestamps"],300)
            self.assertEqual(len(result["results"]),6)
            for name,entry in result["results"].items():
                self.assertEqual(entry["all"]["trades"],300)
                self.assertEqual(entry["all"]["meanPerTradeUsd"],-30)
                self.assertEqual(entry["all"]["netUsd"],-9000)
            self.assertTrue((root/"out"/"es-atr-landscape-v3.json").exists())
            self.assertTrue((root/"out"/"es-atr-landscape-v3.md").exists())


if __name__ == "__main__":
    unittest.main()
