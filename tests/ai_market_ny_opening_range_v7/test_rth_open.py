import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd

from tools.ai_market_ml_features import Bar, NY
from tools.ai_market_train_baseline import INPUT_COLUMNS
from tools.ai_market_ny_opening_range_v7 import (
    EVENTS, RAW_END_UTC, RANGE_CLOCKS, SIGNAL_CLOCKS,
    first_event, paired_day_ci, run, trigger, utc_close,
)
from tools.ai_market_frozen_reverse_v6 import read_raw_before

UTC = dt.timezone.utc


def bar(t, op=6000, high=6001, low=5999, close=6000):
    return Bar(t,op,high,low,close,10,"OK")


def fixture(root):
    contract, tag = "ES 03-25", "synthetic-ny-or-v7"
    raw = root/"ES_03-25_original"
    raw.mkdir()
    (raw/"manifest.json").write_text(json.dumps({
        "contract":contract,"datasetTag":tag
    }))
    days = (dt.date(2024,6,11), dt.date(2025,3,12))
    one, five, rows = [],[],[]
    for index,day in enumerate(days):
        for k in RANGE_CLOCKS:
            five.append(bar(utc_close(day,k)))
        event_utc=utc_close(day,605)
        if index==0:
            actual = bar(event_utc,6000,6002.5,5999.5,6002)
        else:
            actual = bar(event_utc,6001,6002.5,5999.75,6000.5)
        five.append(actual)
        for j in range(1,31):
            one.append(bar(event_utc+dt.timedelta(minutes=j),
                           6000,6000.25,5999.75,6000))
        feature = {c:1 for c in INPUT_COLUMNS}
        feature.update({
            "contract":contract, "decisionUtc":event_utc.isoformat(),
            "decisionEastern":event_utc.astimezone(NY).isoformat(),
            "researchSplit":"train", "close":actual.close,
            "fine1mComplete":1, "qualityFlags":"OK"
        })
        rows.append(feature)
    # Ensure even a poisoned future price value is NOT parsed by raw loader.
    poison = bar(RAW_END_UTC,6000,6001,5999,6000)
    one.append(poison)
    five.append(poison)
    for frame, bars in ((1,one),(5,five)):
        with (raw/("bars_"+str(frame)+"m.csv")).open("w",newline="") as stream:
            w=csv.writer(stream)
            w.writerow(["schemaVersion","contract","frameMinutes","endUtc",
                        "endEastern","utcOffsetMinutes","open","high","low",
                        "close","volume","flags"])
            for b in sorted(bars,key=lambda x:x.end):
                w.writerow(["ninjex-ohlcv-v1",contract,frame,b.end.isoformat(),
                            b.end.astimezone(NY).isoformat(),-240,
                            b.open,b.high,b.low,b.close,b.volume,b.flags])
    for split,t in (
        ("validation",dt.datetime(2025,10,6,14,5,tzinfo=UTC)),
        ("holdout",dt.datetime(2026,8,6,14,5,tzinfo=UTC))
    ):
        f={c:1 for c in INPUT_COLUMNS}
        f.update({
            "contract":contract,"researchSplit":split,
            "decisionUtc":t.isoformat(),
            "decisionEastern":t.astimezone(NY).isoformat(),
            "close":6666, "fine1mComplete":1,"qualityFlags":"OK"
        })
        rows.append(f)
    features=root/"features.csv"
    pd.DataFrame(rows).to_csv(features,index=False)
    features.with_suffix(".manifest.json").write_text(json.dumps({
        "schema":"ninjex-ml-features-v1",
        "datasetTag":tag,"sourceContracts":13,
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
        "contracts":[{"contract":contract,"folders":[str(raw)]}]
    }))
    return features,audit,raw,days


class NewYorkOpeningRangeResearchTests(unittest.TestCase):
    def test_complete_opening_range_uses_six_closed_bars_and_dst(self):
        summer=utc_close(dt.date(2024,6,11),605)
        winter=utc_close(dt.date(2025,3,3),605)
        self.assertEqual(summer.hour,14)
        self.assertEqual(winter.hour,15)
        self.assertEqual(len(RANGE_CLOCKS),6)
        self.assertEqual(RANGE_CLOCKS[0],575)
        self.assertEqual(RANGE_CLOCKS[-1],600)
        self.assertEqual(SIGNAL_CLOCKS[0],605)

    def test_four_events_and_ambiguous_gap(self):
        now=dt.datetime(2025,3,12,14,5,tzinfo=UTC)
        cases=[
            (bar(now,6000,6002,5999.5,6002), "BREAK_LONG"),
            (bar(now,6000,6000.5,5998,5998.5),"BREAK_SHORT"),
            (bar(now,5999,6000,5998,5999.5),"REJECT_LONG"),
            (bar(now,6001,6002,5999.5,6000.5),"REJECT_SHORT"),
            (bar(now,6000,6002,5998,6001.5),"AMBIGUOUS")
        ]
        for current,expected in cases:
            with self.subTest(expected=expected):
                self.assertEqual(trigger(current,6001,5999),expected)

    def test_missing_five_minute_in_range_or_pre_signal_is_rejected(self):
        day=dt.date(2024,6,11)
        samples={utc_close(day,k):bar(utc_close(day,k)) for k in RANGE_CLOCKS}
        samples[utc_close(day,605)]=bar(utc_close(day,605),
                                        6000,6002,5999,6002)
        self.assertEqual(first_event(day,samples)[0][0],"BREAK_LONG")
        del samples[utc_close(day,590)]
        self.assertEqual(first_event(day,samples)[1],"missingOpeningRange5m")
        samples[utc_close(day,590)]=bar(utc_close(day,590))
        samples[utc_close(day,605)]=bar(utc_close(day,605))
        samples[utc_close(day,615)]=bar(utc_close(day,615),
                                       6000,6002,5999,6002)
        self.assertEqual(first_event(day,samples)[1],"gapBeforeFirstEvent")

    def test_end_to_end_train_only_and_matched_ohlcv_policy(self):
        with tempfile.TemporaryDirectory() as tmp:
            path,audit,raw,days=fixture(Path(tmp))
            result=run(path,audit,Path(tmp)/"out")
            self.assertEqual(result["trainFeatureRows"],2)
            self.assertEqual(result["validationFeatureRowsNotScored"],1)
            self.assertEqual(result["holdoutFeatureRowsNotScored"],1)
            self.assertEqual(result["oneEventPerDayCount"],2)
            self.assertEqual(result["eventCounts"],
                             {"BREAK_LONG":1,"REJECT_SHORT":1})
            for policy in ("signal","opposite","alwaysLong","alwaysShort"):
                self.assertEqual(result["overall"][policy]["trades"],2)
                self.assertEqual(result["overall"][policy]["netUsd"],-60)
            self.assertTrue((Path(tmp)/"out"/"es-ny-opening-range-v7.json").exists())
            self.assertTrue((Path(tmp)/"out"/"es-ny-opening-range-v7.md").exists())
            self.assertTrue((Path(tmp)/"out"/"es-ny-opening-range-v7-events.csv").exists())
            self.assertEqual(len(pd.read_csv(
                Path(tmp)/"out"/"es-ny-opening-range-v7-events.csv")),2)

    def test_data_mismatch_does_not_silently_trade(self):
        with tempfile.TemporaryDirectory() as tmp:
            path,audit,raw,days=fixture(Path(tmp))
            rows=pd.read_csv(path)
            rows.loc[0,"close"]=98765.
            rows.to_csv(path,index=False)
            with self.assertRaisesRegex(ValueError,"conflicts with source"):
                run(path,audit,Path(tmp)/"out")

    def test_source_reader_stops_before_future_price(self):
        with tempfile.TemporaryDirectory() as tmp:
            path,audit,raw,days=fixture(Path(tmp))
            # A price parse on this cutoff row would throw ValueError.
            p=raw/"bars_1m.csv"
            lines=p.read_text().splitlines()
            cells=lines[-1].split(",")
            cells[6]="POISONED_VALIDATION_PRICE"
            lines[-1]=",".join(cells)
            p.write_text("\n".join(lines)+"\n")
            rows=read_raw_before(raw,1,"ES 03-25",RAW_END_UTC)
            self.assertEqual(len(rows),60)
            run(path,audit,Path(tmp)/"out")


if __name__=="__main__":
    unittest.main()
