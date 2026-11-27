import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd

from tools.ai_market_ml_features import Bar, NY
from tools.ai_market_train_baseline import INPUT_COLUMNS
from tools.ai_market_ny_opening_range_v7 import RANGE_CLOCKS, utc_close
from tools.ai_market_ny_upper_sweep_v8 import (
    MIN_OTHER_DAYS, bootstrap, collect_events, non_event_control, research,
)
from tools.ai_market_frozen_reverse_v6 import HOLDOUT_CUTOFF_UTC


def synthetic_source(root):
    contract, tag = "ES 12-25", "upper-sweep-v8-test"
    raw = root/"ES_12-25_test"
    raw.mkdir()
    (raw/"manifest.json").write_text(json.dumps({
        "contract": contract,"datasetTag": tag
    }))
    first=dt.date(2025,10,1)
    controls=[stamp.date() for stamp in pd.bdate_range(
        "2025-10-02",periods=11)]
    all_days=[first]+controls
    five,one,features=[],[],[]
    for day in all_days:
        for k in RANGE_CLOCKS:
            t=utc_close(day,k)
            five.append(Bar(t,6000,6001,5999,6000,10,"OK"))
        event_time=utc_close(day,605)
        if day==first:
            # Test the exact v7 first-event REJECT_SHORT, traded LONG.
            bar=Bar(event_time,6001,6002,5999.5,6000.5,10,"OK")
        else:
            bar=Bar(event_time,6000,6001,5999,6000,10,"OK")
        five.append(bar)
        for i in range(1,31):
            t=event_time+dt.timedelta(minutes=i)
            one.append(Bar(t,6000,6000.25,5999.75,6000,1,"OK"))
        entry={f:1 for f in INPUT_COLUMNS}
        entry.update({
            "contract":contract,
            "decisionUtc":event_time.isoformat(),
            "decisionEastern":event_time.astimezone(NY).isoformat(),
            "researchSplit":"validation",
            "fine1mComplete":1,"qualityFlags":"OK","close":bar.close
        })
        features.append(entry)
    for split,t in (
        ("train",dt.datetime(2025,3,3,14,5,tzinfo=dt.timezone.utc)),
        ("holdout",dt.datetime(2026,8,3,14,5,tzinfo=dt.timezone.utc))
    ):
        entry={f:1 for f in INPUT_COLUMNS}
        entry.update({
            "contract":contract,"decisionUtc":t.isoformat(),
            "decisionEastern":t.astimezone(NY).isoformat(),
            "researchSplit":split,"close":12345,
            "fine1mComplete":1,"qualityFlags":"OK"
        })
        features.append(entry)
    for timeframe,rows in ((1,one),(5,five)):
        with (raw/("bars_"+str(timeframe)+"m.csv")).open("w",newline="") as f:
            writer=csv.writer(f)
            writer.writerow(["schemaVersion","contract","frameMinutes",
                "endUtc","endEastern","utcOffsetMinutes","open","high",
                "low","close","volume","flags"])
            for b in sorted(rows,key=lambda x:x.end):
                writer.writerow(["ninjex-ohlcv-v1",contract,timeframe,
                    b.end.isoformat(),b.end.astimezone(NY).isoformat(),
                    -240,b.open,b.high,b.low,b.close,b.volume,b.flags])
    features_path=root/"features.csv"
    pd.DataFrame(features).to_csv(features_path,index=False)
    features_path.with_suffix(".manifest.json").write_text(json.dumps({
        "schema":"ninjex-ml-features-v1",
        "datasetTag":tag,"sourceContracts":13,
        "researchStatus":"PROVISIONAL_MASKED_NOT_TRAINED",
        "counters":{"outputRows":len(features),
                    "split_train":1,"split_validation":len(all_days),
                    "split_holdout":1},
        "definitions":{"splits":"train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"}
    }))
    audit_path=root/"audit.json"
    audit_path.write_text(json.dumps({
        "schema":"ninjex-basket-audit-v1",
        "datasetTag":tag,"status":"NEEDS_REVIEW",
        "contracts":[{"contract":contract,"folders":[str(raw)]}]
    }))
    return features_path,audit_path,raw,first


class FrozenUpperSweepTests(unittest.TestCase):
    def test_rth_first_event_and_clock_baseline(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            features,audit,raw,day=synthetic_source(root)
            report=research(features,audit,root/"out")
            self.assertEqual(report["selectedEvents"],1)
            self.assertEqual(report["sourceSplitRows"]["reusedValidationUsed"],12)
            self.assertEqual(report["sourceSplitRows"]["finalHoldoutNotScored"],1)
            self.assertTrue(report["sourceSplitRows"]["finalHoldoutRawNotLoaded"])
            self.assertEqual(report["longOnEvents"]["netUsd"],-30)
            self.assertEqual(report["shortSameTimestamp"]["netUsd"],-30)
            self.assertEqual(report["eventsWithEligibleClockBaseline"],1)
            self.assertEqual(report["longMinusMatchedContractClockBaseline"]["meanUsd"],0)
            for name in ("es-ny-upper-sweep-v8.json",
                         "es-ny-upper-sweep-v8.md",
                         "es-ny-upper-sweep-v8-events.csv"):
                self.assertTrue((root/"out"/name).exists())

    def test_frozen_source_prices_must_reconcile(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            features,audit,raw,day=synthetic_source(root)
            df=pd.read_csv(features)
            df.loc[0,"close"]=6500.
            df.to_csv(features,index=False)
            with self.assertRaisesRegex(ValueError,"closing price differs"):
                research(features,audit,root/"out")

    def test_no_holdout_data_can_enter_source_feature_split(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            features,audit,raw,day=synthetic_source(root)
            items=pd.read_csv(features)
            val=items.loc[items["researchSplit"]=="validation"].copy()
            val.loc[val.index[0],"decisionUtc"]=(
                HOLDOUT_CUTOFF_UTC+dt.timedelta(minutes=5)).isoformat()
            with self.assertRaisesRegex(ValueError,"cross final holdout"):
                collect_events(val,json.loads(audit.read_text()),"upper-sweep-v8-test")

    def test_no_signal_when_all_source_gap(self):
        self.assertEqual(bootstrap([])["observations"],0)
        self.assertEqual(bootstrap([100,100,100])["meanUsd"],100)


if __name__=="__main__":
    unittest.main()
