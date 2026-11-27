import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd

from tools.ai_market_ml_features import Bar, NY
from tools.ai_market_train_baseline import INPUT_COLUMNS
from tools.ai_market_tradeability_v9 import (
    CLOCKS, RAW_END, atr_bucket, describe, priced_cohort, run,
)

UTC=dt.timezone.utc


def fixture(root):
    tag="tradeability-v9-synthetic"
    contract="ES 12-24"
    folder=root/"ES_12-24_data"
    folder.mkdir()
    (folder/"manifest.json").write_text(json.dumps({
        "datasetTag":tag,"contract":contract
    }))
    intervals=[
        dt.datetime(2024,4,3,14,0,tzinfo=UTC),
        dt.datetime(2025,4,3,14,0,tzinfo=UTC)
    ]
    source1,source5,features=[],[],[]
    for stamp in intervals:
        source5.append(Bar(stamp,6000,6001,5999,6000,10,"OK"))
        for i in range(1,31):
            t=stamp+dt.timedelta(minutes=i)
            source1.append(Bar(t,6000,6000.25,5999.75,6000,5,"OK"))
        r={col:1 for col in INPUT_COLUMNS}
        r.update({
            "contract":contract, "decisionUtc":stamp.isoformat(),
            "decisionEastern":stamp.astimezone(NY).isoformat(),
            "researchSplit":"train", "close":6000.,
            "fine1mComplete":1, "qualityFlags":"OK",
            "atr14_5m":3., "volumeVsPrior20":1.5,
            "efficiency12":.5
        })
        features.append(r)
    for split,time in (
        ("validation",dt.datetime(2025,10,3,14,0,tzinfo=UTC)),
        ("holdout",dt.datetime(2026,8,3,14,0,tzinfo=UTC))
    ):
        r={col:1 for col in INPUT_COLUMNS}
        r.update({
            "contract":contract, "decisionUtc":time.isoformat(),
            "decisionEastern":time.astimezone(NY).isoformat(),
            "researchSplit":split,"close":987654321,
            "fine1mComplete":1,"qualityFlags":"OK"
        })
        features.append(r)
    for frame,bars in ((1,source1),(5,source5)):
        with (folder/("bars_"+str(frame)+"m.csv")).open("w",newline="") as stream:
            w=csv.writer(stream)
            w.writerow(["schemaVersion","contract","frameMinutes",
                        "endUtc","endEastern","utcOffsetMinutes","open",
                        "high","low","close","volume","flags"])
            for bar in sorted(bars,key=lambda b:b.end):
                w.writerow(["ninjex-ohlcv-v1",contract,frame,
                            bar.end.isoformat(),bar.end.astimezone(NY).isoformat(),
                            -240,bar.open,bar.high,bar.low,bar.close,
                            bar.volume,bar.flags])
    path=root/"features.csv"
    pd.DataFrame(features).to_csv(path,index=False)
    path.with_suffix(".manifest.json").write_text(json.dumps({
        "schema":"ninjex-ml-features-v1",
        "datasetTag":tag,
        "sourceContracts":13,
        "researchStatus":"PROVISIONAL_MASKED_NOT_TRAINED",
        "counters":{"outputRows":4,"split_train":2,
                    "split_validation":1,"split_holdout":1},
        "definitions":{"splits":
            "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"}
    }))
    audit=root/"audit.json"
    audit.write_text(json.dumps({
        "schema":"ninjex-basket-audit-v1","datasetTag":tag,
        "status":"NEEDS_REVIEW",
        "contracts":[{"contract":contract,"folders":[str(folder)]}]
    }))
    return path,audit,folder


class TradeabilityResearchTests(unittest.TestCase):
    def test_fixed_clock_not_price_trigger_and_realistic_costs(self):
        self.assertEqual(sorted(CLOCKS)[0],600)
        self.assertEqual(sorted(CLOCKS)[-1],930)
        self.assertEqual(len(CLOCKS),12)
        self.assertEqual(atr_bucket(3),"ATR_2_TO_4")
        with tempfile.TemporaryDirectory() as tmp:
            f,a,folder=fixture(Path(tmp))
            r=run(f,a,Path(tmp)/"output")
            self.assertEqual(r["trainingRowsAvailable"],2)
            self.assertEqual(r["validationRowsNotScored"],1)
            self.assertEqual(r["finalHoldoutRowsNotScored"],1)
            self.assertTrue(r["rawAfterSep1_2025ETNeverLoaded"])
            self.assertEqual(r["sourceCompleteDecisions"],2)
            self.assertEqual(r["allTraining"]["days"],2)
            # No price movement: $30 net loss for BOTH directions.
            self.assertEqual(r["allTraining"]["long"]["netUsd"],-60)
            self.assertEqual(r["allTraining"]["short"]["netUsd"],-60)
            self.assertEqual(r["allTraining"]["oracleBestNOTExecutable"]["netUsd"],-60)
            self.assertEqual(r["allTraining"]["atLeastOneProfitableSideRate"],0)
            self.assertEqual(r["allTraining"]["meanLongBeforeAssumedCostsUsd"],0)
            self.assertTrue((Path(tmp)/"output"/"es-tradeability-v9.json").exists())
            self.assertTrue((Path(tmp)/"output"/"es-tradeability-v9.md").exists())
            self.assertTrue((Path(tmp)/"output"/"es-tradeability-v9-decisions.csv").exists())

    def test_validation_must_not_be_used_as_train(self):
        with tempfile.TemporaryDirectory() as tmp:
            f,a,folder=fixture(Path(tmp))
            frame=pd.read_csv(f)
            bad=frame.loc[frame["researchSplit"]=="validation"].copy()
            bad["researchSplit"]="train"
            with self.assertRaisesRegex(ValueError,"TRAIN data includes"):
                priced_cohort(bad)

    def test_price_reconciliation_fails_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            f,a,folder=fixture(Path(tmp))
            df=pd.read_csv(f)
            df.loc[0,"close"]=6500.
            df.to_csv(f,index=False)
            with self.assertRaisesRegex(ValueError,"Raw 5m close"):
                run(f,a,Path(tmp)/"output")

    def test_empty_description_is_no_trades_not_a_winning_strategy(self):
        self.assertEqual(describe([]),{"count":0})
        self.assertEqual(atr_bucket(float("nan")),"UNKNOWN")


if __name__=="__main__":
    unittest.main()
