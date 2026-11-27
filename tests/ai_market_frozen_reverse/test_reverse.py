import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np
import pandas as pd

from tools.ai_market_frozen_reverse_v6 import (
    HOLDOUT_CUTOFF_UTC, extract_validation_events, fit_early_v5,
    read_raw_before, select_frozen_policies, as_trades, daily_ci,
)
from tools.ai_market_train_baseline import BASE_FEATURES

UTC = dt.timezone.utc


def csv_bars(folder, frame, records, contract="ES 09-25"):
    path = folder / ("bars_" + str(frame) + "m.csv")
    with path.open("w",newline="") as stream:
        writer=csv.writer(stream)
        writer.writerow([
            "schemaVersion","contract","frameMinutes","endUtc",
            "endEastern","utcOffsetMinutes","open","high","low",
            "close","volume","flags"
        ])
        for stamp,op,high,low,close in records:
            writer.writerow([
                "ninjex-ohlcv-v1",contract,frame,stamp.isoformat(),
                stamp.isoformat(),0,op,high,low,close,3,"OK"
            ])
    return path


def synthetic_validation_source(root):
    directory=root/"dated_09_25"
    directory.mkdir()
    contract="ES 09-25"
    tag="v6-fixed-synthetic"
    (directory/"manifest.json").write_text(json.dumps({
        "contract":contract,"datasetTag":tag
    }))
    t=dt.datetime(2025,10,2,14,5,tzinfo=UTC)
    earlier=[]
    for i in range(12,0,-1):
        earlier.append((t-dt.timedelta(minutes=5*i),6000,6001,5999,6000))
    now=(t,6000,6002.5,5999,6002.0)
    csv_bars(directory,5,earlier+[now],contract)
    future=[(t+dt.timedelta(minutes=i),6002,6002.25,6001.75,6002)
            for i in range(1,31)]
    csv_bars(directory,1,future,contract)
    data={name:0.1 for name in BASE_FEATURES}
    data.update({
        "contract":contract,
        "researchSplit":"validation",
        "decisionUtc":t.isoformat(),
        "decisionEastern":t.isoformat(),
        "easternMinuteOfDay":605,
        "fine1mComplete":1,
        "qualityFlags":"OK",
        "close":6002.0,
        "sessionAsia":0,
        "sessionLondon":1,
        "sessionNewYork":0,
        "sessionLondonNewYorkOverlap":0,
    })
    audit={"schema":"ninjex-basket-audit-v1","datasetTag":tag,
           "contracts":[{"contract":contract,"folders":[str(directory)]}]}
    return directory, pd.DataFrame([data]), audit, t


def training_frame(count=520):
    rows=[]
    for period, year in (("EARLY_TRAIN",2024),("LATE_TRAIN",2025)):
        for i in range(count):
            # All timestamps before sep in both years.
            day = dt.datetime(year,3,1,tzinfo=UTC) + dt.timedelta(hours=3*i)
            feature = {"feature_"+col: float(i%7)/3 for col in BASE_FEATURES}
            feature["feature_easternMinuteOfDay"] = 300+i%400
            feature.update({
                "contract":"ES 06-24",
                "decisionUtc":day.isoformat(),
                "easternDate":day.date().isoformat(),
                "period":period,
                "trigger":("BREAKOUT_LONG","BREAKOUT_SHORT",
                           "REJECTION_LONG","REJECTION_SHORT")[i%4],
                "netUsd": 60. if i%3==0 else -45.,
                "oppositeUsd": -45. if i%3==0 else 60.,
            })
            rows.append(feature)
    return pd.DataFrame(rows)


class FrozenReverseTests(unittest.TestCase):
    def test_unopened_holdout_bar_not_decoded_from_original_csv(self):
        with tempfile.TemporaryDirectory() as tmp:
            f=Path(tmp)
            prior=HOLDOUT_CUTOFF_UTC-dt.timedelta(minutes=1)
            nextday=HOLDOUT_CUTOFF_UTC
            path=csv_bars(f,1,[
                (prior,6000,6001,5999,6000),
                (nextday,"POISONED_HOLDOUT_PRICE",6001,5999,6000),
            ])
            data=read_raw_before(f,1,"ES 09-25",HOLDOUT_CUTOFF_UTC)
            self.assertEqual(list(data),[prior])
            self.assertEqual(data[prior].close,6000)

    def test_exact_v4_structure_and_both_side_labels_from_validation_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder,features,basket,decision=synthetic_validation_source(Path(tmp))
            events,stats=extract_validation_events(features,basket,basket["datasetTag"])
            self.assertEqual(stats["nonOverlappingEvents"],1)
            self.assertEqual(len(events),1)
            self.assertEqual(events.iloc[0]["trigger"],"BREAKOUT_LONG")
            self.assertEqual(events.iloc[0]["actualLong"],-30.0)
            self.assertEqual(events.iloc[0]["actualShort"],-30.0)
            self.assertNotIn("feature_label30mCloseRetAtr",events.columns)

    def test_validation_source_price_mismatch_raises(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder,features,basket,decision=synthetic_validation_source(Path(tmp))
            features.loc[0,"close"]=6666
            with self.assertRaisesRegex(ValueError,"close disagree"):
                extract_validation_events(features,basket,basket["datasetTag"])

    def test_reject_any_holdout_timestamp_or_split(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder,features,basket,decision=synthetic_validation_source(Path(tmp))
            features.loc[0,"researchSplit"]="holdout"
            with self.assertRaisesRegex(ValueError,"Only validation"):
                extract_validation_events(features,basket,basket["datasetTag"])
            features.loc[0,"researchSplit"]="validation"
            features.loc[0,"decisionUtc"]=(HOLDOUT_CUTOFF_UTC+
                dt.timedelta(minutes=5)).isoformat()
            with self.assertRaisesRegex(ValueError,"future validation"):
                extract_validation_events(features,basket,basket["datasetTag"])

    def test_frozen_refit_leaves_late_training_out(self):
        e=training_frame()
        a,b,details=fit_early_v5(e)
        self.assertEqual(details["earlyFitEvents"],520)
        self.assertEqual(details["lateTrainingNotRefit"],520)
        e.loc[0,"easternDate"]="2025-10-01"
        with self.assertRaisesRegex(ValueError,"later-training dates"):
            fit_early_v5(e)

    def test_identical_timestamps_for_model_reverse_and_benchmarks(self):
        base=dt.datetime(2026,2,10,14,tzinfo=UTC)
        rows=[]
        for i in range(3):
            t=base+dt.timedelta(minutes=30*i)
            rows.append({
                "decisionUtc":t.isoformat(),
                "easternDate":"2026-02-10",
                "month":"2026-02",
                "contract":"ES 03-26",
                "session":"NEW_YORK",
                "modelSide":"LONG" if i%2==0 else "SHORT",
                "actualLong":270.,
                "actualShort":-180.,
                "longReason":"TARGET","shortReason":"STOP",
                "longAmbiguous":False,"shortAmbiguous":False,
                "predictedBest":50.,"predictedGap":11.
            })
        from tools.ai_market_event_meta import choose_trades
        chosen=choose_trades(pd.DataFrame(rows))
        self.assertEqual(len(chosen),2)
        model=as_trades(chosen,"modelDirection")
        inverse=as_trades(chosen,"reverseModelDirection")
        long=as_trades(chosen,"alwaysLong")
        short=as_trades(chosen,"alwaysShort")
        self.assertEqual([r["decisionUtc"] for r in model],
                         [r["decisionUtc"] for r in inverse])
        self.assertEqual([r["netUsd"] for r in inverse],[-180.,270.])
        self.assertEqual([r["netUsd"] for r in long],[270.,270.])
        self.assertEqual([r["netUsd"] for r in short],[-180.,-180.])
        result=daily_ci(inverse,short,repeats=40)
        self.assertAlmostEqual(result["meanUsd"],225.)

    def test_model_abstention_is_valid(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder,features,basket,decision=synthetic_validation_source(Path(tmp))
            selected, result=select_frozen_policies(pd.DataFrame(),(object(),object()))
            self.assertTrue(selected.empty)
            self.assertTrue(result.empty)


if __name__=="__main__":
    unittest.main()
