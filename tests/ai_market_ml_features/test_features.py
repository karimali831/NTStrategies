import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

from tools.ai_market_ml_features import (
    Bar, NY, UTC, FIVE, contract_rows, label_return, read_bars, run,
    session_flags, split_for, validate_higher
)


def create_series(root, contract="ES 12-26", drop_index=None):
    root.mkdir(parents=True, exist_ok=True)
    start = dt.datetime(2026, 9, 28, 13, 0, tzinfo=UTC)
    primary = []
    for i in range(132):
        if i == drop_index:
            continue
        end = start + FIVE * i
        px = 5500 + (i * 0.25)
        primary.append(Bar(end, px, px + 0.25, px - 0.25, px, 5, "OK"))
    m5 = {bar.end: bar for bar in primary}
    one = []
    for bar in primary:
        for j in range(4, -1, -1):
            when = bar.end - dt.timedelta(minutes=j)
            one.append(Bar(when, bar.open, bar.high, bar.low,
                           bar.close, 1, "OK"))
    # Generate fully reconstructed higher bars from continuous sources.
    fifteen = []
    sixty = []
    for higher, target in ((15, fifteen), (60, sixty)):
        n = higher // 5
        for bar in primary:
            if (bar.end.hour * 60 + bar.end.minute) % higher != 0:
                continue
            parts = [m5.get(bar.end - FIVE * j)
                     for j in range(n - 1, -1, -1)]
            if any(x is None for x in parts):
                continue
            target.append(Bar(bar.end, parts[0].open,
                              max(x.high for x in parts),
                              min(x.low for x in parts), parts[-1].close,
                              sum(x.volume for x in parts), "OK"))
    for frame, bars in ((1, sorted(one, key=lambda b: b.end)),
                        (5, primary), (15, fifteen), (60, sixty)):
        path = root / f"bars_{frame}m.csv"
        with path.open("w", newline="") as f:
            w = csv.writer(f)
            w.writerow("schemaVersion,contract,frameMinutes,endUtc,endEastern,utcOffsetMinutes,open,high,low,close,volume,flags".split(","))
            for bar in bars:
                et = bar.end.astimezone(NY)
                w.writerow(["ninjex-ohlcv-v1", contract, frame, bar.end.isoformat(),
                            et.strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3],
                            int(et.utcoffset().total_seconds()/60),
                            bar.open, bar.high, bar.low,
                            bar.close, bar.volume, bar.flags])
    (root / "manifest.json").write_text(json.dumps({
        "contract": contract, "datasetTag": "test-basket"
    }))
    return primary


def make_audit(root, folder):
    path = root / "basket.json"
    path.write_text(json.dumps({
        "schema": "ninjex-basket-audit-v1",
        "datasetTag": "test-basket",
        "status": "NEEDS_REVIEW",
        "contracts": [{"contract": "ES 12-26", "folders": [str(folder)],
                       "result": "FAIL"}]
    }))
    return path


class MLFeaturesTests(unittest.TestCase):
    def test_session_local_dst_and_overlap(self):
        # September 2026 London 14:30 BST, New York 09:30 EDT.
        when = dt.datetime(2026, 9, 28, 13, 30, tzinfo=UTC)
        et, asia, london, us, overlap, minutes = session_flags(when)
        self.assertEqual((asia, london, us, overlap), (0, 1, 1, 1))
        self.assertEqual(minutes, 570)
        self.assertEqual(split_for(et.date()), "holdout")
        # DST offset mismatch between US/UK transition dates should
        # be resolved using respective actual city local time zones.
        when = dt.datetime(2026, 3, 20, 13, 30, tzinfo=UTC)
        et, asia, london, us, overlap, minutes = session_flags(when)
        self.assertEqual((london, us), (1, 1))
        self.assertEqual(minutes, 570)

    def test_training_row_asof_and_future_labels(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            folder = root / "raw"
            create_series(folder)
            report = run(make_audit(root, folder), root / "features.csv")
            self.assertGreater(report["counters"]["outputRows"], 40)
            rows = list(csv.DictReader((root / "features.csv").open()))
            self.assertTrue(rows)
            for row in rows:
                decision = dt.datetime.fromisoformat(row["decisionUtc"])
                prior15 = dt.datetime.fromisoformat(row["lastClosed15Utc"])
                prior60 = dt.datetime.fromisoformat(row["lastClosed60Utc"])
                self.assertLessEqual(prior15, decision, "Future 15m leak")
                self.assertLessEqual(prior60, decision, "Future 60m leak")
            self.assertTrue(any(row["label30mCloseRetAtr"] for row in rows))
            self.assertTrue(any(row["label60mCloseRetAtr"] for row in rows))
            self.assertEqual(report["counters"]["rejectedUnreconciled60m"], 0)

    def test_future_labels_mask_missing_five_minute(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            folder = root / "raw"
            original = create_series(folder, drop_index=120)
            report = run(make_audit(root, folder), root / "features.csv")
            rows = list(csv.DictReader((root / "features.csv").open()))
            at_115 = original[115].end.isoformat()
            r = next(row for row in rows if row["decisionUtc"] == at_115)
            self.assertEqual(r["label30mCloseRetAtr"], "")
            self.assertEqual(r["label60mCloseRetAtr"], "")
            self.assertGreater(report["counters"]["masked30mOutcome"], 0)

    def test_last_bar_cannot_label_future(self):
        now = dt.datetime(2026, 9, 28, 20, 0, tzinfo=UTC)
        bars = [Bar(now + FIVE * i, 5000, 5001, 4999, 5000 + i, 10, "")
                for i in range(4)]
        self.assertIsNone(label_return(bars, 2, 6, 2.0))
        self.assertGreater(label_return(bars, 0, 3, 2.0)[0], 0)

    def test_partial_hourly_bar_is_not_used_as_full_hour(self):
        time = dt.datetime(2025, 11, 28, 18, 15, tzinfo=UTC)
        fake = Bar(time, 5000, 5001, 4999, 5000, 10, "OK")
        val, rejected = validate_higher([fake], {}, 5, 60)
        self.assertEqual(len(val), 0)
        self.assertEqual(rejected[0][1], "OFF_ALIGNED_PARTIAL")


if __name__ == "__main__":
    unittest.main()
