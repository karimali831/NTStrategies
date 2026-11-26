# Sparse one-minute candles — ES research data diagnostics

On ES 12-23 (2023-09-10–2023-12-08), the data quality gate found
59 missing 1-minute candle timestamps outside the regular New York cash
session, plus the Friday 2023-12-08 17:00 ET session-ending candle.
The 59 gaps occur within 51 existing five-minute bars. Reconstructing
each of those 51 bars from the available 1-minute candles gives exact
open/high/low/close and **zero unexplained volume**. This is consistent
with zero-trade minutes, not proof that the source tick dataset is complete.

The research audit now reports:
- `missingMinuteForensics.missing1mCount`
- `volumeReconciledCandidate1mCount`
- `volumeReconciledParent5mCount`
- `unreconciled1mCount`
- `candidateSamples` (UTC bar timestamps and 5m closing times)
- `unreconciledSamples` (failed source volume/OHLC comparisons)

**Strict FAIL status remains unchanged** if any expected candle is absent,
even when other minutes and volume reconcile. The gate does not create
synthetic OHLCV, fill missing 1m bars, infer ticks or certify an ML
dataset automatically.

Verify representative missing minutes against NinjaTrader
Tools > Historical Data > Edit > ES 12-23 > Last > Tick and Minute.
Record the exact absence of Last trades during the missing minute,
remembering historical minute timestamps are the bar close. Compare
the 5m chart's OHLCV. Verify the final Friday 17:00 ET candle
separately, ideally with the Analyzer end date after Friday (Monday
Dec 11 for the Dec 8 session).

Once source no-trade minutes are independently confirmed, the ML data
loader can retain them as explicit **no-transactions** event gaps, or
fill a separate regularly-spaced feature grid with zero-volume flags
while keeping raw trade-time candles untouched. Never treat a
fabricated zero-volume bar as tick evidence or use it for stop/target
first-touch execution labelling.
