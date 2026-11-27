import datetime as dt
import tempfile
import unittest
from pathlib import Path

import numpy as np
import pandas as pd

from tools.ai_market_event_meta import (
    MIN_NET_FORECAST, choose_trades, direction_labels, predictors,
    prepare, test_early_late,
)
from tools.ai_market_train_baseline import BASE_FEATURES


def fixture(n=550):
    source = []
    for period, start in (
        ("EARLY_TRAIN", dt.datetime(2024, 4, 1, 12, tzinfo=dt.timezone.utc)),
        ("LATE_TRAIN", dt.datetime(2024, 10, 1, 12, tzinfo=dt.timezone.utc))
    ):
        for i in range(n):
            t = start + dt.timedelta(hours=6*i)
            bullish = (i % 4 == 0 or i % 4 == 1)
            trigger = ("BREAKOUT_LONG", "BREAKOUT_SHORT",
                       "REJECTION_LONG", "REJECTION_SHORT")[i % 4]
            long_pnl = 95.0 if bullish else -90.
            short_pnl = -90. if bullish else 95.0
            chosen = long_pnl if trigger.endswith("_LONG") else short_pnl
            opposite = short_pnl if trigger.endswith("_LONG") else long_pnl
            fields = {"feature_"+f: 0.2 for f in BASE_FEATURES}
            fields["feature_volumeVsPrior20"] = 2. if bullish else 0.5
            fields["feature_easternMinuteOfDay"] = 560. + (i%3)*60
            fields.update({
                "contract": "ES 06-24" if period=="EARLY_TRAIN" else "ES 03-25",
                "decisionUtc": t.isoformat(),
                "easternDate": t.date().isoformat(),
                "month": t.strftime("%Y-%m"),
                "period": period,
                "trigger": trigger,
                "netUsd": chosen,
                "oppositeUsd": opposite,
                "leakageFutureReturn": 100000. if period=="LATE_TRAIN" else -100000.
            })
            source.append(fields)
    df = pd.DataFrame(source)
    report = {
        "nonOverlappingEvents": len(df),
        "validationRowsNotScored": 52224,
        "holdoutRowsNotScored": 30853,
        "dataStatus": "PROVISIONAL_MASKED_NOT_TRAINED"
    }
    return df, report


class EventMetaTests(unittest.TestCase):
    def test_predictors_cannot_use_labels_or_dates_or_lookahead(self):
        e,_ = fixture()
        x = predictors(e)
        self.assertNotIn("netUsd", x)
        self.assertNotIn("oppositeUsd", x)
        self.assertNotIn("leakageFutureReturn", x)
        self.assertNotIn("contract", x)
        self.assertNotIn("researchSplit", x)
        self.assertIn("volumeVsPrior20", x)
        self.assertIn("event_BREAKOUT_LONG", x)
        self.assertTrue(np.isfinite(x.to_numpy(float)).all())

    def test_mirrored_labels_agree(self):
        e,_=fixture()
        long,short=direction_labels(e)
        self.assertEqual(long[0],95.0)
        self.assertEqual(short[0],-90.0)
        self.assertEqual(long[2],-90.0)
        self.assertEqual(short[2],95.0)

    def test_fixed_threshold_and_daily_limit_and_abstention(self):
        t=dt.datetime(2025, 2, 3, 13, tzinfo=dt.timezone.utc)
        rows=[]
        for i in range(6):
            rows.append({
                "decisionUtc":(t+dt.timedelta(minutes=30*i)).isoformat(),
                "easternDate":"2025-02-03",
                "predictedBest":MIN_NET_FORECAST+10 if i<5 else -40.,
                "predictedGap":11. if i<5 else 5.
            })
        chosen=choose_trades(pd.DataFrame(rows))
        self.assertEqual(len(chosen),2)
        self.assertEqual(len(choose_trades(pd.DataFrame(rows).assign(
            predictedBest=-40.))),0)

    def test_validation_and_holdout_are_rejected_even_if_disguised(self):
        e,report=fixture()
        prepare(e,report)
        e.loc[0,"easternDate"]="2026-08-01"
        with self.assertRaisesRegex(ValueError,"Validation/holdout"):
            prepare(e,report)

    def test_one_entry_point_trains_only_early_and_scores_only_late(self):
        e,report=fixture()
        with tempfile.TemporaryDirectory() as tmp:
            out=test_early_late(e,report,Path(tmp))
            self.assertEqual(out["earlyFitEvents"],550)
            self.assertEqual(out["laterChallengeEvents"],550)
            self.assertEqual(out["validationRowsNotScored"],52224)
            self.assertEqual(out["holdoutRowsNotScored"],30853)
            self.assertIn("longModelMaeUsd",out["forecastErrors"])
            self.assertEqual(set(out["fixedPolicy"]),{
                "trades","modelChosen","sameTimeAlwaysLong",
                "sameTimeAlwaysShort","sameTimeOpposite"})
            self.assertTrue((Path(tmp)/"es-meta-v5.json").exists())
            self.assertTrue((Path(tmp)/"es-meta-v5.md").exists())
            self.assertTrue((Path(tmp)/"es-meta-v5-late-predictions.csv").exists())
            self.assertEqual(len(pd.read_csv(Path(tmp)/"es-meta-v5-late-predictions.csv")),550)


if __name__=="__main__":
    unittest.main()
