import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from tools.ai_market_batch_audit import (
    audit_batch, expected, roll_monday, window, write_summary,
)

START = dt.date(2023, 9, 10)
END = dt.date(2026, 11, 25)


class BulkAuditTests(unittest.TestCase):
    def test_cme_standard_roll_calendar(self):
        self.assertEqual(dt.date(2023, 12, 11), roll_monday(2023, 12))
        self.assertEqual(dt.date(2024, 3, 11), roll_monday(2024, 3))
        self.assertEqual(dt.date(2026, 6, 15), roll_monday(2026, 6))

        self.assertEqual(
            ("2023-09-10T18:01:00-04:00", "2023-12-08T17:00:00-05:00"),
            tuple(x.isoformat() for x in window("ES 12-23", START, END)))
        self.assertEqual(
            ("2023-12-10T18:01:00-05:00", "2024-03-08T17:00:00-05:00"),
            tuple(x.isoformat() for x in window("ES 03-24", START, END)))
        self.assertEqual(
            ("2026-09-13T18:01:00-04:00", "2026-11-25T23:59:00-05:00"),
            tuple(x.isoformat() for x in window("ES 12-26", START, END)))
        rows = expected("ES", START, END)
        self.assertEqual(len(rows), 13)
        self.assertEqual(rows[0][0], "ES 12-23")
        self.assertEqual(rows[-1][0], "ES 12-26")

    def test_unexported_basket_gets_actionable_report(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            report = audit_batch(root, "batch-pilot", "ES", START, END,
                                 only=["ES 12-23", "ES 03-24"])
            self.assertEqual(report["summary"]["missing"], 2)
            self.assertEqual(report["status"], "NEEDS_REVIEW")
            md = root / "summary.md"
            write_summary(report, md)
            content = md.read_text()
            self.assertIn("ES 12-23", content)
            self.assertIn("ES 03-24", content)
            self.assertIn("NOT_EXPORTED", content)

    def test_duplicate_tag_is_never_silently_chosen(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ("old", "new"):
                run = root / name
                run.mkdir()
                (run / "manifest.json").write_text(json.dumps(
                    {"datasetTag": "basket-pilot", "contract": "ES 12-23"}))
            report = audit_batch(root, "basket-pilot", "ES", START, END,
                                 only=["ES 12-23"])
            self.assertEqual(report["summary"]["duplicates"], 1)
            self.assertEqual(report["contracts"][0]["result"], "DUPLICATE_RUNS")

    def test_single_export_invokes_existing_strict_auditor(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            run = root / "ES-run"
            run.mkdir()
            (run / "manifest.json").write_text(json.dumps(
                {"datasetTag": "pilot", "contract": "ES 12-23"}))
            mock_details = {
                "result": "PASS_WITH_FLAGGED_SPARSE_1M",
                "frames": {str(n) + "m": {"observedInWindow": 100,
                          "missing": 0, "offCalendar": 0} for n in (1, 5, 15, 60)},
                "crossFrame": {"1mTo5m": {"mismatchCount": 0}},
                "missingMinuteForensics": {"missing1mCount": 2},
                "errors": [], "warnings": ["Sparse minutes retained"]
            }
            with patch("tools.ai_market_batch_audit.audit", return_value=mock_details) as mocked:
                report = audit_batch(root, "pilot", "ES", START, END,
                                     sparse=True, only=["ES 12-23"])
            self.assertEqual(report["status"], "PASS_WITH_QUALIFICATIONS")
            self.assertEqual(report["summary"]["flagged"], 1)
            self.assertEqual(report["contracts"][0]["missing1m"], 2)
            self.assertEqual(mocked.call_args.kwargs["allow_sparse_1m"], True)


if __name__ == "__main__":
    unittest.main()
