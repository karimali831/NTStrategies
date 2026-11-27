import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd
from tools.ai_market_ml_features import Bar
from tools.ai_market_second_entry_v10 import (
    SecondEntryState, detect, simulate_pending, fixed_selection,
    future_minutes, FUTURE_MINUTES,
)
from tools.ai_market_second_entry_ml_v10 import (
    FEATURES, evaluate, prepare, select, asof_features,
)

UTC=dt.timezone.utc

def bar(i,o,h,l,c):
    return Bar(dt.datetime(2024,5,6,14,tzinfo=UTC)+dt.timedelta(minutes=5*i),
               o,h,l,c,100,"OK")

def sequence(side=1):
    xs=[bar(0,101,103,100.5,102),bar(1,101.8,102,100,100.6),
        bar(2,100.8,103,100.25,102.8),
        bar(3,102.5,102.75,99.75,100.25),
        bar(4,100.5,103.5,99.75,103.2)]
    if side==-1:
        xs=[Bar(x.end,-x.open,-x.low,-x.high,-x.close,100,"OK") for x in xs]
    return xs


class EntryPatternTests(unittest.TestCase):
    def test_long_and_short_h1_fail_then_h2(self):
        for side in (1,-1):
            xs=sequence(side)
            state=SecondEntryState(side)
            found=[state.on_bar(x,xs[i-1] if i else x,
                                side*100,side*99.75,2,i)
                   for i,x in enumerate(xs)]
            self.assertEqual([r["type"] if r else None for r in found],
                             [None,None,"H1",None,"H2"])
            self.assertEqual(found[4]["side"],"LONG" if side==1 else "SHORT")

    def test_ambiguous_failure_cannot_create_h2(self):
        xs=sequence()
        xs[3]=bar(3,102.5,103.75,99.75,100.25)
        state=SecondEntryState(1)
        found=[state.on_bar(x,xs[i-1] if i else x,100,99.75,2,i)
               for i,x in enumerate(xs)]
        self.assertEqual(found[2]["type"],"H1")
        self.assertIsNone(found[4])

    def test_gap_resets_detector(self):
        xs=[bar(i,100,101,99,100) for i in range(90)]
        x=xs[30]
        xs[30]=Bar(x.end+dt.timedelta(minutes=2),100,101,99,100,100,"OK")
        found,resets=detect(xs)
        self.assertEqual(found,[])
        self.assertGreaterEqual(resets,2)


class SimTests(unittest.TestCase):
    def candidate(self):
        return {"type":"H2","side":"LONG",
                "decisionUtc":"2024-05-06T14:00:00+00:00",
                "easternDate":"2024-05-06",
                "entryTrigger":6001.,"stopPrice":5998.,
                "nominalStopTicks":12}

    def candles(self):
        t=dt.datetime(2024,5,6,14,tzinfo=UTC)
        return [Bar(t+dt.timedelta(minutes=i),6000,6000.25,5999.75,
                    6000,10,"OK") for i in range(1,FUTURE_MINUTES+1)]

    def test_pending_stop_order_unfilled_and_missing_path(self):
        r=simulate_pending(self.candidate(),self.candles())
        self.assertFalse(r["filled"])
        candles=self.candles()
        t=dt.datetime(2024,5,6,14,tzinfo=UTC)
        source={x.end:x for x in candles}
        self.assertEqual(len(future_minutes(source,t)),FUTURE_MINUTES)
        del source[t+dt.timedelta(minutes=9)]
        self.assertIsNone(future_minutes(source,t))

    def test_2r_fill_and_pessimistic_same_minute_stop(self):
        xs=self.candles()
        x=xs[0]
        xs[0]=Bar(x.end,6000,6001.25,5999.75,6001,10,"OK")
        x=xs[1]
        xs[1]=Bar(x.end,6001,6008,6000,6007,10,"OK")
        r=simulate_pending(self.candidate(),xs)
        self.assertEqual(r["entryFill"],6001.25)
        self.assertEqual(r["targetPrice"],6007.75)
        self.assertEqual(r["netUsd"],307.5)
        xs=self.candles()
        x=xs[0]
        xs[0]=Bar(x.end,6000,6008,5997,6001,10,"OK")
        r=simulate_pending(self.candidate(),xs)
        self.assertTrue(r["bothTouched1m"])
        self.assertEqual(r["exitReason"],"AMBIGUOUS_STOP_FIRST")

    def test_gap_risk_rejected_and_nominal_cap(self):
        xs=self.candles()
        x=xs[0]
        xs[0]=Bar(x.end,6005,6006,6004,6005,10,"OK")
        self.assertEqual(simulate_pending(self.candidate(),xs)["reason"],
                         "unsafeGapOrRisk")
        c=self.candidate()
        c["nominalStopTicks"]=20
        keep,rejections=fixed_selection([c])
        self.assertFalse(keep)
        self.assertEqual(rejections["nominalRiskAbove250Usd"],1)


def write_ml_fixture(csv,n):
    rows=[]
    for phase,month in (("EARLY_TRAIN",3),("LATE_TRAIN",10)):
        for i in range(n):
            t=dt.datetime(2024,month,1,tzinfo=UTC)+dt.timedelta(hours=i*6)
            row={name:.5+(i%5)/10 for name in FEATURES}
            row.update({"type":"H2","side":"LONG" if i%2 else "SHORT",
                        "decisionUtc":t.isoformat(),"easternDate":t.date().isoformat(),
                        "month":t.strftime("%Y-%m"),"period":phase,
                        "clockEtMinute":615+i%5*5,
                        "netUsd":100 if i%3==0 else -80,
                        "exitReason":"TARGET" if i%3==0 else "STOP",
                        "bothTouched1m":False})
            rows.append(row)
    pd.DataFrame(rows).to_csv(csv,index=False)


def write_benchmark(path):
    record={"schema":"ninjex-es-second-entry-21ema-v10",
            "status":"TRAIN_ONLY_MECHANICAL_BENCHMARK_NOT_AI_DISCOVERY",
            "validationRowsNotScored":52224,
            "finalHoldoutRowsNotScored":30853}
    path.write_text(json.dumps(record))
    return record


class AiTests(unittest.TestCase):
    def test_train_only_ml_and_no_outcome_in_predictor_matrix(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            csv,manifest=root/"events.csv",root/"benchmark.json"
            write_ml_fixture(csv,52)
            write_benchmark(manifest)
            r=evaluate(csv,manifest,root/"out")
            self.assertTrue(r["trained"])
            self.assertEqual(r["earlyFitCount"],52)
            self.assertEqual(r["lateChallengeCount"],52)
            self.assertEqual(r["selectedLateH2"]["trades"],r["selectedCount"])
            x=asof_features(pd.read_csv(csv))
            self.assertNotIn("netUsd",x)
            self.assertNotIn("exitReason",x)

    def test_zero_or_insufficient_signals_abstains(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            csv,manifest=root/"events.csv",root/"benchmark.json"
            csv.write_text("")
            write_benchmark(manifest)
            self.assertFalse(evaluate(csv,manifest,root/"out")["trained"])
            write_ml_fixture(csv,5)
            self.assertFalse(evaluate(csv,manifest,root/"out")["trained"])

    def test_future_dates_rejected_and_fixed_one_daily_gate(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            csv=root/"events.csv"
            write_ml_fixture(csv,52)
            source=write_benchmark(root/"benchmark.json")
            f=pd.read_csv(csv)
            f.loc[0,"easternDate"]="2026-08-01"
            with self.assertRaisesRegex(ValueError,"Future validation"):
                prepare(f,source)
            x=pd.DataFrame({"decisionUtc":["2024-10-01T13:00:00+00:00",
                                            "2024-10-01T13:05:00+00:00"],
                            "easternDate":["2024-10-01"]*2,
                            "predictedNetUsd":[30.,50.]})
            self.assertEqual(len(select(x,20)),1)


if __name__=="__main__":
    unittest.main()
