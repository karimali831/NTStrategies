import csv
import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path
from tools.ai_market_research_audit import audit, auditable_slots, ET

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
    def test_missing(self):
        f = self.folder / "bars_5m.csv"
        lines = f.read_text().splitlines()
        f.write_text("\n".join(lines[:-1]) + "\n")
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL")
        self.assertEqual(r["frames"]["5m"]["missing"], 1)
    def test_price_mismatch(self):
        f = self.folder / "bars_5m.csv"
        f.write_text(f.read_text().replace(",5000,5000,5000,5000,5,OK",
            ",5000,5000.25,5000,5000,5,OK", 1))
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "PASS_WITH_MANUAL_CALENDAR_REVIEW")
        f.write_text(f.read_text().replace(",5000,5000,5000,5000,5,OK",
            ",5000,5000.25,5000,5000,5,OK", 1))
        r = audit(self.folder, self.start, self.end, "ES 12-26")
        self.assertEqual(r["result"], "FAIL")
        self.assertTrue(r["oneToFive"]["mismatchSamples"])
    def test_session(self):
        self.assertFalse(list(auditable_slots(dt.datetime(2026,9,27,17,tzinfo=ET),
            dt.datetime(2026,9,27,18,tzinfo=ET), 5)))
        self.assertEqual(71, len(list(auditable_slots(
            dt.datetime(2026,9,27,18,5,tzinfo=ET),
            dt.datetime(2026,9,27,23,55,tzinfo=ET),5))))

if __name__ == "__main__":
    unittest.main()
