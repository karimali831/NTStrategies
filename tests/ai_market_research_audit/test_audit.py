import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path
from tools.ai_market_research_audit import audit, auditable_slots, ET, load_calendar

class AuditTests(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.TemporaryDirectory()
        self.folder = Path(self.t.name)
        self.start = dt.datetime(2026, 9, 28, 19, 5, tzinfo=ET)
        self.end = dt.datetime(2026, 9, 28, 20, 0, tzinfo=ET)
        (self.folder / "manifest.json").write_text(json.dumps({
            "contract": "ES 12-26", "schemaVersion": "ninjex-ohlcv-v1"}))
        (self.folder / "quality_issues.csv").write_text("frameMinutes,endEastern,issue,details\n")
        for frame in (1, 5, 15, 60):
            with (self.folder / f"bars_{frame}m.csv").open("w", newline="") as f:
                w = csv.writer(f)
                w.writerow("schemaVersion,contract,frameMinutes,endUtc,endEastern,utcOffsetMinutes,open,high,low,close,volume,flags".split(","))
                for utc in auditable_slots(self.start, self.end, frame):
                    eastern = utc.astimezone(ET)
                    w.writerow(["ninjex-ohlcv-v1", "ES 12-26", frame, utc.isoformat(),
                        eastern.strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3], -240,
                        5000, 5000, 5000, 5000, frame, "OK"])
    def tearDown(self):
        self.t.cleanup()
    def test_good(self):
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "PASS_WITH_MANUAL_CALENDAR_REVIEW", r["errors"])
        self.assertEqual(r["oneToFive"]["comparisons"], 11)
        self.assertEqual(r["frames"]["15m"]["expected"], 4)
        self.assertEqual(r["frames"]["60m"]["expected"], 1)
        self.assertEqual(r["crossFrame"]["1mTo5m"]["mismatchCount"], 0)
        self.assertEqual(r["crossFrame"]["5mTo15m"]["mismatchCount"], 0)
    def test_missing(self):
        f = self.folder / "bars_5m.csv"
        lines = f.read_text().splitlines()
        f.write_text("\n".join(lines[:-1]) + "\n")
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL")
        self.assertEqual(r["frames"]["5m"]["missing"], 1)
        self.assertEqual(r["frames"]["5m"]["missingByEasternDate"],
                         {"2026-09-28": 1})
    def test_price_mismatch(self):
        f = self.folder / "bars_5m.csv"
        f.write_text(f.read_text().replace(",5000,5000,5000,5000,5,OK",
            ",5000,5000.25,5000,5000,5,OK", 1))
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        # Cross-frame 5m->15m detects the first mismatch even where the
        # first 5m bar has insufficient preceding 1m history in this fixture.
        self.assertEqual(r["result"], "FAIL")
        self.assertGreater(r["crossFrame"]["5mTo15m"]["mismatchCount"], 0)
        f.write_text(f.read_text().replace(",5000,5000,5000,5000,5,OK",
            ",5000,5000.25,5000,5000,5,OK", 1))
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL")
        self.assertGreater(r["crossFrame"]["1mTo5m"]["mismatchCount"], 0)
    def test_fifteen_minute_aggregation_mismatch(self):
        f = self.folder / "bars_15m.csv"
        content = f.read_text()
        original = ",5000,5000,5000,5000,15,OK"
        self.assertIn(original, content)
        f.write_text(content.replace(original,
            ",5000,5000.25,5000,5000,15,OK", 1))
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL")
        self.assertTrue(r["crossFrame"]["1mTo15m"]["mismatchCount"] > 0 or
                        r["crossFrame"]["5mTo15m"]["mismatchCount"] > 0)
    def test_manifest_count_mismatch(self):
        p = self.folder / "manifest.json"
        manifest = json.loads(p.read_text())
        manifest["frameCounts"] = {"5m": 9999}
        p.write_text(json.dumps(manifest))
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL")
        self.assertTrue(any("manifest count" in e for e in r["errors"]))
    def test_source_verified_holiday_calendar(self):
        calendar = load_calendar(Path("tools/ai_market_calendars/es_2023_thanksgiving.json"))
        # Thanksgiving: the 13:00 close is valid, 13:01-18:00 is halted.
        start = dt.datetime(2023, 11, 23, 12, 55, tzinfo=ET)
        end = dt.datetime(2023, 11, 23, 18, 5, tzinfo=ET)
        minute = {x.astimezone(ET).strftime("%H:%M") for x in
                  auditable_slots(start, end, 1, calendar)}
        self.assertIn("13:00", minute)
        self.assertNotIn("13:01", minute)
        self.assertNotIn("18:00", minute)
        self.assertIn("18:01", minute)
        # Black Friday: 13:15 close is included, 13:20/14:00 are closed.
        start = dt.datetime(2023, 11, 24, 12, 55, tzinfo=ET)
        end = dt.datetime(2023, 11, 24, 17, 0, tzinfo=ET)
        hourly = [x.astimezone(ET).strftime("%H:%M")
                  for x in auditable_slots(start, end, 60, calendar)]
        self.assertEqual(hourly, ["13:00", "13:15"])
        self.assertNotIn("14:00", hourly)

    def test_winter_2023_2024_cme_equity_closures(self):
        calendar = load_calendar(Path(
            "tools/ai_market_calendars/es_winter_2023_2024.json"))
        for date in ((2023, 12, 25), (2024, 1, 1)):
            start = dt.datetime(*date, 0, 0, tzinfo=ET)
            end = dt.datetime(*date, 18, 5, tzinfo=ET)
            times = [x.astimezone(ET).strftime("%H:%M")
                     for x in auditable_slots(start, end, 5, calendar)]
            self.assertEqual(times, ["18:05"], str(date))
        for date in ((2024, 1, 15), (2024, 2, 19)):
            start = dt.datetime(*date, 12, 55, tzinfo=ET)
            end = dt.datetime(*date, 18, 5, tzinfo=ET)
            times = [x.astimezone(ET).strftime("%H:%M")
                     for x in auditable_slots(start, end, 5, calendar)]
            self.assertEqual(times, ["12:55", "13:00", "18:05"], str(date))

    def test_missing_one_minute_can_reconcile_with_five_minute(self):
        # Simulate a no-trade minute: absent 1m row, but 5m OHLCV is
        # exactly accounted for by the other four 1m bars.
        file1 = self.folder / "bars_1m.csv"
        source = file1.read_text().splitlines()
        file1.write_text("\n".join(
            line for line in source if "2026-09-28T19:11:00.000" not in line) + "\n")
        file5 = self.folder / "bars_5m.csv"
        lines = file5.read_text().splitlines()
        adjusted = []
        for line in lines:
            if "2026-09-28T19:15:00.000" in line:
                cells = line.split(",")
                cells[-2] = "4"
                line = ",".join(cells)
            adjusted.append(line)
        file5.write_text("\n".join(adjusted) + "\n")
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL", "No automatic gap acceptance")
        self.assertEqual(r["missingMinuteForensics"]["missing1mCount"], 1)
        self.assertEqual(r["missingMinuteForensics"]["volumeReconciledCandidate1mCount"], 1)
        self.assertEqual(r["missingMinuteForensics"]["unreconciled1mCount"], 0)

    def test_missing_one_minute_with_volume_residual_is_unreconciled(self):
        file1 = self.folder / "bars_1m.csv"
        file1.write_text("\n".join(
            line for line in file1.read_text().splitlines()
            if "2026-09-28T19:11:00.000" not in line) + "\n")
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["missingMinuteForensics"]["volumeReconciledCandidate1mCount"], 0)
        self.assertEqual(r["missingMinuteForensics"]["unreconciled1mCount"], 1)

    def test_explicit_sparse_policy_allows_only_reconciled_one_minute(self):
        # Missing one 1m row with zero unexplained OHLC or volume:
        # adjust each containing higher timeframe by the same unit.
        one = self.folder / "bars_1m.csv"
        one.write_text("\n".join(x for x in one.read_text().splitlines()
            if "2026-09-28T19:11:00.000" not in x) + "\n")
        for frame, timestamp, new_volume in [
            (5, "2026-09-28T19:15:00.000", "4"),
            (15, "2026-09-28T19:15:00.000", "14"),
            (60, "2026-09-28T20:00:00.000", "59")]:
            path = self.folder / f"bars_{frame}m.csv"
            updated = []
            seen = False
            for line in path.read_text().splitlines():
                if timestamp in line:
                    cells = line.split(",")
                    cells[-2] = new_volume
                    line = ",".join(cells)
                    seen = True
                updated.append(line)
            self.assertTrue(seen, timestamp)
            path.write_text("\n".join(updated) + "\n")
        strict = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(strict["result"], "FAIL")
        permitted = audit(self.folder, self.start, self.end, "ES 12-26",
                          allow_sparse_1m=True)
        self.assertEqual(permitted["result"], "PASS_WITH_FLAGGED_SPARSE_1M",
                         permitted["errors"])
        self.assertEqual(permitted["missingMinuteForensics"]["missing1mCount"], 1)
        self.assertEqual(permitted["missingMinuteForensics"]["unreconciled1mCount"], 0)
        self.assertEqual(permitted["crossFrame"]["5mTo15m"]["mismatchCount"], 0)
        self.assertTrue(permitted["sparseMinutePolicy"]["acceptedWithCaution"])
        self.assertFalse(permitted["sparseMinutePolicy"]["sourceVerifiedNoTrade"])

    def test_sparse_policy_does_not_hide_bad_volume_or_missing_5m(self):
        one = self.folder / "bars_1m.csv"
        one.write_text("\n".join(x for x in one.read_text().splitlines()
            if "2026-09-28T19:11:00.000" not in x) + "\n")
        report = audit(self.folder, self.start, self.end, "ES 12-26",
                       allow_sparse_1m=True)
        self.assertEqual(report["result"], "FAIL")
        self.assertFalse(report["sparseMinutePolicy"]["acceptedWithCaution"])
        self.assertEqual(report["missingMinuteForensics"]["unreconciled1mCount"], 1)

    def test_session(self):
        self.assertFalse(list(auditable_slots(dt.datetime(2026,9,27,17,tzinfo=ET),
            dt.datetime(2026,9,27,18,tzinfo=ET), 5)))
        self.assertEqual(71, len(list(auditable_slots(
            dt.datetime(2026,9,27,18,5,tzinfo=ET),
            dt.datetime(2026,9,27,23,55,tzinfo=ET),5))))

if __name__ == "__main__":
    unittest.main()
