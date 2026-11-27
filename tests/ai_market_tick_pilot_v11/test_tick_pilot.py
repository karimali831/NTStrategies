import datetime as dt
import json
import tempfile
import unittest
from pathlib import Path

import pandas as pd

from tools.ai_market_tick_pilot_v11 import (
    CUTOFF_UTC, parse_tick, ingest, TickBarBuilder, TICK,
)


def tick_line(t, price, volume=1, fields=3):
    stamp=t.strftime("%Y%m%d %H%M%S")+" 0000000"
    text=f"{stamp};{price};{volume}"
    if fields==5:
        text+=";0;0"
    return text+"\n"


class NativeLastTickTests(unittest.TestCase):
    def test_7_decimal_timestamp_and_same_stamp_are_allowed(self):
        t, fraction, p, size, fields = parse_tick(
            "20240611 140501 1234567;5400.25;8", 1)
        self.assertEqual(t.microsecond,123456)
        self.assertEqual(fraction,7)
        self.assertEqual(str(p),"5400.25")
        self.assertEqual(size,8)
        self.assertEqual(fields,3)

    def test_price_size_bad_columns_and_negative_extra_fail(self):
        examples=[
            "20240611 140501;5400.10;1",
            "20240611 140501;5400.25;0",
            "20240611 140501;5400.25;1.25",
            "20240611 140501;5400.25;1;0",
            "20240611 140501;5400.25;1;0;-1",
            "20240611 140501;nan;1",
        ]
        for raw in examples:
            with self.subTest(raw=raw):
                with self.assertRaises(ValueError):
                    parse_tick(raw,3)

    def test_tick_count_not_volume_and_ema_is_prior_plus_current(self):
        b=TickBarBuilder(2,"ES 06-24","pilot.txt")
        base=dt.datetime(2024,6,11,14,0,tzinfo=dt.timezone.utc)
        self.assertIsNone(b.append(base,5400,100))
        row=b.append(base+dt.timedelta(seconds=1),5400.25,200)
        self.assertEqual(row["tickCount"],2)
        self.assertEqual(row["volume"],300)
        self.assertEqual(row["ema21"],5400.25)
        self.assertEqual(row["rangeTicks"],1)
        self.assertEqual(b.completed,1)

    def test_native_stream_two_bars_and_future_row_not_parsed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            t=dt.datetime(2024,6,11,14,0,tzinfo=dt.timezone.utc)
            entries=[
                tick_line(t,5400,1),
                tick_line(t+dt.timedelta(seconds=1),5400.25,2,5),
                tick_line(t+dt.timedelta(seconds=2),5400.50,3),
                tick_line(t+dt.timedelta(seconds=3),5400.25,4),
                # Poison future prices; date cutoff must be tested BEFORE
                # parsing the future amount.
                tick_line(CUTOFF_UTC,"NOT_A_PRICE",1),
            ]
            source=root/"Last-export.txt"
            source.write_text("".join(entries))
            output=ingest(source,"ES 06-24",root/"out",2)
            self.assertEqual(output["ticksRead"],4)
            self.assertEqual(output["threeFieldRecords"],3)
            self.assertEqual(output["fiveFieldRecordsUnclassifiedExtras"],1)
            self.assertEqual(output["completeTickBars"],2)
            self.assertTrue(output["stoppedBeforeValidationOrHoldout"])
            bars=pd.read_csv(output["tickBarsCsv"])
            self.assertEqual(len(bars),2)
            self.assertEqual(list(bars["tickCount"]),[2,2])
            self.assertEqual(list(bars["volume"]),[3,7])
            self.assertEqual(output["discardedPartialBarTicks"],0)
            self.assertTrue(Path(root/"out"/"ES_06-24-2tick-audit-v11.json").exists())

    def test_gap_discards_partial_and_resets_ema(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            t=dt.datetime(2024,6,11,14,0,tzinfo=dt.timezone.utc)
            future=t+dt.timedelta(minutes=21)
            source=root/"trades.txt"
            source.write_text(
                tick_line(t,5400)+
                tick_line(future,5400.25)+
                tick_line(future+dt.timedelta(seconds=1),5400.50))
            r=ingest(source,"ES 06-24",root/"out",2)
            self.assertEqual(r["resetsForSourceGaps"],1)
            self.assertEqual(r["discardedPartialBarTicks"],1)
            self.assertEqual(r["completeTickBars"],1)

    def test_reversed_timestamp_rejected_not_repaired(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            t=dt.datetime(2024,6,11,14,0,tzinfo=dt.timezone.utc)
            source=root/"trades.txt"
            source.write_text(tick_line(t+dt.timedelta(seconds=2),5400)+
                              tick_line(t,5400.25))
            with self.assertRaisesRegex(ValueError,"reversed tick timestamp"):
                ingest(source,"ES 06-24",root/"out",2)
            self.assertFalse((root/"out"/"ES_06-24-2tick-train-v11.csv").exists())
            self.assertFalse((root/"out"/"ES_06-24-2tick-audit-v11.json").exists())

    def test_explicit_pilot_limit_is_recorded(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            t=dt.datetime(2024,6,11,14,0,tzinfo=dt.timezone.utc)
            source=root/"trades.txt"
            source.write_text("".join(tick_line(
                t+dt.timedelta(seconds=i),5400+i*.25
            ) for i in range(10)))
            r=ingest(source,"ES 06-24",root/"out",2,limit_rows=3)
            self.assertEqual(r["ticksRead"],3)
            self.assertTrue(r["pilotRowLimitApplied"])
            self.assertEqual(r["completeTickBars"],1)
            self.assertEqual(r["discardedPartialBarTicks"],1)


if __name__ == "__main__":
    unittest.main()
