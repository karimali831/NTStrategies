import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd

from tools.ai_market_ml_features import Bar, NY
from tools.ai_market_train_baseline import INPUT_COLUMNS
from tools.ai_market_structure_events import (
    PRIOR_BARS, analyze, choose_chronological_events,
    detect_trigger, prior_12_contiguous,
)

UTC = dt.timezone.utc


def bar(t, op=6000, hi=6001, lo=5999, close=6000):
    return Bar(t, op, hi, lo, close, 100, "OK")


def fixture(root):
    tag = "ninjex-structure-synthetic"
    contract = "ES 12-24"
    folder = root / "ES_12-24_synthetic"
    folder.mkdir()
    (folder / "manifest.json").write_text(json.dumps({
        "contract": contract, "datasetTag": tag
    }))
    starts = [
        dt.datetime(2024, 8, 2, 12, 0, tzinfo=UTC),
        dt.datetime(2025, 5, 2, 12, 0, tzinfo=UTC)
    ]
    feature_rows = []
    with (folder / "bars_5m.csv").open("w", newline="") as five, (
            folder / "bars_1m.csv").open("w", newline="") as one:
        fieldnames = ["schemaVersion", "contract", "frameMinutes", "endUtc",
                      "endEastern", "utcOffsetMinutes", "open", "high",
                      "low", "close", "volume", "flags"]
        fw = csv.DictWriter(five, fieldnames=fieldnames)
        ow = csv.DictWriter(one, fieldnames=fieldnames)
        fw.writeheader()
        ow.writeheader()

        def record(writer, frame, t, op, high, low, close):
            writer.writerow({
                "schemaVersion": "ninjex-ohlcv-v1", "contract": contract,
                "frameMinutes": frame, "endUtc": t.isoformat(),
                "endEastern": t.astimezone(NY).isoformat(),
                "utcOffsetMinutes": -240, "open": op,
                "high": high, "low": low, "close": close,
                "volume": 10, "flags": "OK"
            })

        for n, start in enumerate(starts):
            for i in range(1, PRIOR_BARS+1):
                record(fw, 5, start+dt.timedelta(minutes=i*5),
                       6000, 6001, 5999, 6000)
            decision = start + dt.timedelta(minutes=65)
            if n == 0:
                # Breakout LONG against completed historical high.
                current = (6000, 6002, 5999.75, 6001.5)
                expected = "BREAKOUT_LONG"
            else:
                # Upper sweep, then back under historical high.
                current = (6001, 6001.75, 5999.75, 6000.5)
                expected = "REJECTION_SHORT"
            record(fw, 5, decision, *current)
            for i in range(1, 31):
                t = decision + dt.timedelta(minutes=i)
                record(ow, 1, t, 6000, 6000.25, 5999.75, 6000)
            r = {column: 1 for column in INPUT_COLUMNS}
            r.update({
                "contract": contract, "decisionUtc": decision.isoformat(),
                "decisionEastern": decision.astimezone(NY).isoformat(),
                "researchSplit": "train", "close": current[3],
                "qualityFlags": "OK", "fine1mComplete": 1,
                "easternWeekday": decision.weekday(),
                "easternMinuteOfDay": 540,
                "sessionLondon": 1, "sessionNewYork": 0,
                "sessionAsia": 0, "sessionLondonNewYorkOverlap": 0,
                "expectedTrigger": expected,
            })
            feature_rows.append(r)
    for split, when in (
        ("validation", dt.datetime(2025, 10, 2, 13, 5, tzinfo=UTC)),
        ("holdout", dt.datetime(2026, 8, 2, 13, 5, tzinfo=UTC))
    ):
        row = {column: 1 for column in INPUT_COLUMNS}
        row.update({
            "contract": contract, "decisionUtc": when.isoformat(),
            "decisionEastern": when.astimezone(NY).isoformat(),
            "researchSplit": split, "close": 1,
            "fine1mComplete": 1, "qualityFlags": "OK",
        })
        feature_rows.append(row)
    features = root / "features.csv"
    # Extra synthetic-only reference column is ignored by the verified reader.
    pd.DataFrame(feature_rows).to_csv(features, index=False)
    features.with_suffix(".manifest.json").write_text(json.dumps({
        "schema": "ninjex-ml-features-v1", "sourceContracts": 13,
        "datasetTag": tag, "researchStatus": "PROVISIONAL_MASKED_NOT_TRAINED",
        "counters": {
            "outputRows": len(feature_rows),
            "split_train": 2, "split_validation": 1, "split_holdout": 1
        },
        "definitions": {
            "splits": "train <2025-09-01 ET; validation through 2026-06-14; untouched holdout from 2026-06-15"
        }
    }))
    audit = root / "basket.json"
    audit.write_text(json.dumps({
        "schema": "ninjex-basket-audit-v1", "datasetTag": tag,
        "contracts": [{"contract": contract, "folders": [str(folder)]}]
    }))
    return features, audit


class StructuralEventTests(unittest.TestCase):
    def test_four_mirrored_events_only_use_closed_history(self):
        now = dt.datetime(2024, 8, 2, 12, 0, tzinfo=UTC)
        preceding = [bar(now+dt.timedelta(minutes=i*5)) for i in range(12)]
        cases = [
            (bar(now, 6000, 6002, 5999.5, 6001.5), "BREAKOUT_LONG"),
            (bar(now, 6000, 6000.5, 5998, 5998.5), "BREAKOUT_SHORT"),
            (bar(now, 5999.25, 6000, 5998, 5999.5), "REJECTION_LONG"),
            (bar(now, 6001, 6002, 5999.5, 6000.5), "REJECTION_SHORT"),
            (bar(now, 6000, 6002, 5998, 6001.5), None),
        ]
        for current, expected in cases:
            with self.subTest(expected=expected):
                self.assertEqual(detect_trigger(current, preceding), expected)

    def test_missing_prior_bar_rejects_event_without_imputation(self):
        now = dt.datetime(2024, 8, 2, 12, 0, tzinfo=UTC)
        past = {now-dt.timedelta(minutes=5*i):
                bar(now-dt.timedelta(minutes=5*i))
                for i in range(1, 13)}
        self.assertEqual(len(prior_12_contiguous(past, now)), 12)
        del past[now-dt.timedelta(minutes=25)]
        self.assertIsNone(prior_12_contiguous(past, now))

    def test_spacing_is_global_across_both_directions_and_events(self):
        base = dt.datetime(2024, 8, 2, 12, 0, tzinfo=UTC)
        events = [
            {"contract": "ES 12-24",
             "decisionUtc": (base+dt.timedelta(minutes=i)).isoformat(),
             "trigger": "BREAKOUT_LONG" if i%2 else "REJECTION_SHORT"}
            for i in (0, 5, 25, 30, 55, 60)]
        selected = choose_chronological_events(events)
        self.assertEqual(len(selected), 3)
        self.assertEqual([x["decisionUtc"] for x in selected],
                         [events[i]["decisionUtc"] for i in (0,3,5)])

    def test_complete_pipeline_never_reads_later_features_for_signals(self):
        with tempfile.TemporaryDirectory() as location:
            root = Path(location)
            features, audit = fixture(root)
            report = analyze(features, audit, root/"output")
            self.assertEqual(report["trainingRowsAvailable"], 2)
            self.assertEqual(report["validationRowsNotScored"], 1)
            self.assertEqual(report["holdoutRowsNotScored"], 1)
            self.assertEqual(report["nonOverlappingEvents"], 2)
            self.assertEqual(report["eventCountsPreSpacing"],
                             {"BREAKOUT_LONG": 1, "REJECTION_SHORT": 1})
            self.assertEqual(report["overall"]["chosen"]["netUsd"], -60)
            self.assertEqual(report["overall"]["opposite"]["netUsd"], -60)
            self.assertTrue((root/"output"/"es-structure-v4.json").exists())
            self.assertTrue((root/"output"/"es-structure-v4.md").exists())

    def test_raw_close_discrepancy_fails_closed(self):
        with tempfile.TemporaryDirectory() as location:
            root = Path(location)
            features, audit = fixture(root)
            rows = pd.read_csv(features)
            rows.loc[0, "close"] = 6200
            rows.to_csv(features, index=False)
            with self.assertRaisesRegex(ValueError, "does not match"):
                analyze(features, audit, root/"output")


if __name__ == "__main__":
    unittest.main()
