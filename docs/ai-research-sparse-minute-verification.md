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

## Opt-in research acceptance without inventing minute candles

A small number of absent 1-minute candle closes should not block
higher-timeframe feature research when **all** missing minutes reconcile
exactly to source 5m OHLC and volume and no other audit errors remain.

Run the audit with `--allow-sparse-1m` to enable this narrow exception.
The default mode remains strict `FAIL`.

The result `PASS_WITH_FLAGGED_SPARSE_1M` means:
- Every missing 1m close is covered by an otherwise complete 5m bar;
- Remaining 1m bars explain its entire open/high/low/close/volume;
- Every 5m, 15m and 60m bar expected by the dated session calendar
  is present, valid and cross-frame-consistent;
- Source absence is *candidate no-trade*, **not verified** against ticks;
- The raw dataset is not modified or filled;
- Model features requiring a 1m history crossing missing minute slots
  must be *masked* or excluded, not forward-filled as observed bars;
- 5m/15m/60m research can proceed, with an explicit quality caveat;
- Tick-level order-execution research still requires real source ticks.

Any other issue (incomplete final close, parent missing, mismatched
volume, incorrect OHLC, bad offsets, holidays, duplicated bars) still
returns `FAIL` and is not waived by this option. Use only with
a known, reviewed dataset and preserve the audit JSON and provenance.

Example:

    python tools/ai_market_research_audit.py --contract "ES 12-23" --folder "YOUR_EXPORT_FOLDER" --from-et "2023-09-10T18:01:00" --through-et "2023-12-08T17:00:00" --calendar tools/ai_market_calendars/es_2023_thanksgiving.json --allow-sparse-1m --output ES_12-23_research_quality.json

Note that this still FAILS until the genuinely existing 17:00 ET
terminal source bars are captured or explicitly validated.
