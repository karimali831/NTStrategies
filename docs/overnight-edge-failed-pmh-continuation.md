# Failed PMH continuation long — research-only v1

This experiment is deliberately **shadow-only**. It does not call `EnterLong`,
`EnterShort`, `SetStopLoss`, `SetProfitTarget`, or otherwise modify strategy
orders, signals, risk caps, or the Run 8 four-model portfolio.

## Signal definition (no future data)

1. A **qualified** `PMH-REJECT-S` original rejection is observed in the
   regular research candidate CSV. Portfolio blocks do not suppress research.
2. If **Require Bullish EMA Context** is on (default), that parent rejection
   must have completed 5-minute EMA(9) > EMA(21) and a one-minute rejection
   close at or above EMA(9). This filter uses only the *parent* bar's cached
   indicator values. It does not depend on whether the real PMH short won.
3. During the next **10 completed one-minute bars** (configurable), price must
   print **two consecutive completed closes** at least **2 ES ticks** above
   the premarket high (both configurable).
4. On the **first causal Last tick** after the second confirming bar, create
   two independently simulated, **long** shadow outcomes:
   - `Run8_20_40`: 20-tick stop, 40-tick target.
   - `Research_15_50`: 15-tick stop, 50-tick target.

Both scenarios use **60 minutes** maximum holding time from their own entry
tick (configurable), or the original 16:00 ET flatten, whichever is first.
They are not bracket orders and no commissions, bid/ask spread, slippage,
queue effects, or portfolio scheduling are simulated.

Two rejections can produce the same confirming candle. To avoid writing
duplicate longs at the same instant, only the first confirmed research signal
for that timestamp is scored. Each candidate's disposition is still logged.

A failed rejection does **not** mean every PMH short is inverted. A rejection
that never reclaims PMH, or lacks the required bullish context, generates no long.

## NinjaTrader configuration

Use **ES 5 Minute** primary series and CME US Index Futures ETH, ET timestamps.
Tick Replay is not required for historical-tick Playback; the existing
`OnMarketData(Last)` research engine evaluates tick-by-tick hypothetical exits.

- Enable Research Telemetry = **true** (required)
- Enable Failed PMH Continuation Research = **true** (new; default false)
- Enable Research Session Export = true for chart generation (optional)
- Failed PMH Confirmation Minutes = **10**
- Failed PMH Consecutive Closes = **2**
- Failed PMH Reclaim Ticks = **2**
- Failed PMH Require Bullish EMA Context = **true**
- Failed PMH Max Shadow Hold Minutes = **60**

Leave production risk parameters unchanged.

## Output files

Under `Documents\NinjaTrader 8\NinjexResearch\OvernightEdgePortfolio`:

- `overnight_edge_failed_pmh_candidates_{instrument}_{RunId}.csv`:
  one disposition per qualified original PMH candidate: filtered by context,
  confirmation expired, duplicate confirmation, confirmed, or interrupted.
  Includes parent ATR, EMA, sweep and timestamp.
- `overnight_edge_failed_pmh_trades_{instrument}_{RunId}.csv`:
  two hypothetical exit records per confirmed long (20/40 and 15/50)
  including confirmation/entry/exit times, prices, reasons, gross P&L and
  MFE/MAE. Unfinished trades have blank gross P&L, not a fabricated result.

Join with the original research and chart exports by `RunId`, instrument,
date and `ParentCandidateId` / parent timestamp. Do not mix files from
different runs or count independent overlapping shadows as achievable trades.

## Short playback verification

1. Compile in NinjaTrader 8 / Rider.
2. Replay the **2–4 November 2026** market sessions from historical tick data.
3. Check that the *original* `PMH-REJECT-S` entries, exit prices and
   portfolio P&L remain identical with the new switch on versus off.
4. Check 3 November: the previous data showed qualifying PMH rejections in a
   strong bullish opening and later closes above PMH. At least one long
   confirmation should appear if the 5-minute context and bar sequence are
   consistent. No guaranteed winning outcome is implied.
5. Confirm that the two hypothetical scenarios share the same entry price and
   timestamp, differ only in stop/target, and have no actual NinjaTrader
   Orders/Executions.
6. Check incomplete/zero-trade days: the candidate log may exist with no
   confirmed long, and the trades CSV may contain only its header.
7. In longer runs, inspect any `FAILED PMH RESEARCH DISABLED` diagnostics.

The model is **not** ready for live trading because prior exploratory
PMH-reversal results were parameter-selected in sample. Validate across
ES 12-25, 03-26, 06-26, 09-26, and 12-26, including post-September 2026,
then evaluate a genuinely held-out period, portfolio caps, realistic
execution costs and an $1,800 intratrade drawdown constraint.

## Limitations

This experiment observes qualified original PMH candidates only. It does not
invent entries from raw rejected PMH signals. It can produce overlapping
hypothetical trades when distinct parent signals confirm at different times.
Use the logged outcomes for **research**, not a summed portfolio equity curve.
