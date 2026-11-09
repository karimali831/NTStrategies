# Failed PMH Continuation Long — V2 retest-and-hold research

**Research only.** V2 adds no NinjaTrader orders and does not change the four
Run 8 models, their priority, daily limits, or the earlier V1 research model.
This is a *new hypothesis* prompted by V1's weak performance in the
September–November 2026 out-of-sample-looking period. V2 parameters are selected
after seeing historical examples; **the available history is not independent
out-of-sample proof**.

## Why V2

V1 entered a hypothetical long after two closes back above PMH. Its independent
shadow trade list returned a positive gross P&L across earlier contracts but
a weak result in ES 12-26, with overlapping entries and portfolio conflicts.
V2 tests a different mechanism: **PMH becomes support after a failed short
rejection**, followed by actual bullish momentum.

## Deterministic, causal signal definition

1. Start from any **qualified** original \`PMH-REJECT-S\` candidate. The candidate
   can be blocked by original Run 8 position/daily caps.
2. Parent context must have completed five-minute EMA(9) > EMA(21), with
   nonnegative completed-fast-EMA slope, an EMA spread at least the configured
   number of ticks, and the parent's close at or above EMA(9).
3. Within **10** completed one-minute bars after the parent, require **two
   consecutive closes at least two ticks above PMH**.
4. Within **10** minutes of that reclaim, a *subsequent* completed candle must
   touch the PMH level within six ticks, never wick more than six ticks below
   PMH, and **close at or above PMH**. This is the retest-and-hold.
5. Within **seven** minutes of the retest, require a *separate*, bullish
   completed one-minute candle whose close:
   - reaches or exceeds the retest candle high (default follow-through buffer 0);
   - remains >= PMH + 2 ticks;
   - remains >= the cached completed EMA(9), with EMA(9) > EMA(21).
   A deeper breach below PMH invalidates the setup.
6. Simulate a **long** at the first causal Last tick after confirmation. There
   is no entry if the original entry window has ended. Duplicate confirmations
   at the same timestamp count only once.

The signal logic does **not** inspect future candles or whether the original PMH
short eventually won or lost. Parent, reclaim, retest, confirmation, first-tick
entry and hypothetical exit timestamps are exported for independent verification.

A full historical walk-through of the source one-minute bars suggested
approximately **21 unique confirmations across the recorded five-contract
sample** using these default tolerances. This is an approximate offline
signal-count sanity check, not NinjaTrader execution verification or an estimate
of future profitability. The number of completed hypothetical trades may differ.

## Three independent shadow exit scenarios

| Scenario | Stop | Target |
| --- | --- | --- |
| \`Fixed20_40\` | 20 ticks | 40 ticks |
| \`ATR_Stop_2R\` | ceil(0.60 × completed five-minute ATR ticks at confirmation), bounded to 12–30 ticks | 2 × actual stop ticks |
| \`Retest_Stop_2R\` | ceil(entry-to-retest-low distance in ticks + 2-tick buffer), minimum 8 | 2 × actual stop ticks |

If the structural stop requires more than **30 ticks**, record the attempt as
\`StructureStopTooWide\` (blank gross P&L). Never silently replace it with an
invalid, artificially tight structural stop. All scenarios use one contract
unless \`OrderQuantity\` differs from 1, and exit after 60 minutes at most, or
at the original 16:00 ET flatten.

Hypothetical stop/target fills are at their intended price when a Last tick
crosses them; they are not exchange executions. No commissions, bid/ask spread,
slippage, position scheduling or account trailing drawdown are simulated.

## NT8 properties

- **ES 5 Minute primary series**, CME US Index Futures ETH, timestamps ET.
- \`Enable Research Telemetry = True\` (required).
- \`Enable Failed PMH V2 Retest Research = True\` (new; defaults false).
- \`Enable Failed PMH Continuation Research\` (V1) may remain off. If both on,
  each writes separate results under its own prefix.
- Research Session Export and Research Perturbation are optional.
- Historical-tick **Playback** does not require a separate Tick Replay switch.

New controls appear in **7. Failed PMH V2 Research**. Defaults:
reclaim 10m / 2 ticks / 2 closes; retest 10m / 6-tick touch tolerance /
6-tick invalidation; follow-through 7m / 0-tick break buffer; minimum EMA
spread 0 ticks, minimum fast-EMA slope 0 ticks; ATR multiplier 0.60;
structural buffer 2 ticks; max stop 30 ticks; hold 60m.

Do **not** enable the new model for live execution: it has no order entry.

## Files per instance

Under \`Documents\NinjaTrader 8\NinjexResearch\OvernightEdgePortfolio\`:

- \`overnight_edge_failed_pmh_v2_candidates_ES_..._{RunId}.csv\`: each parent
  signal's terminal status, saved EMA/ATR context and separate reclaim, retest
  and confirmation timestamps.
- \`overnight_edge_failed_pmh_v2_trades_ES_..._{RunId}.csv\`: three independent
  hypothetical paths per unique confirmation, with stop/target, prices,
  exit reason, MFE/MAE, gross P&L and **Run 8 position/cap/order-pending
  snapshots at the hypothetical entry tick**.

The snapshots indicate collisions with the existing strategy but **are not a
full portfolio simulation**. They also do not prevent simultaneous V2 shadow
positions. An actual portfolio evaluation must recompute entry scheduling,
daily counters, positions, costs and worst **intratrade equity** drawdown
against the $1,800 target.

The exporter writes \`RunId\` in every file. Do not combine overlapping runs.
Choose the intended run per contract for analysis.

## Smoke-test first

1. Rebuild in NinjaTrader/Rider.
2. Run the original Run 8 strategy on **ES 12-26 5 Minute** in Playback from
   **2–4 November 2026**, with Research Telemetry enabled and V2 enabled.
3. Confirm that all original Run 8 entries, exit prices, trade counts and
   gross P&L **match** your established baseline with V2 off.
4. Confirm V2 candidate CSV is created; inspect November 3 reclaim/retest
   sequence and exact chronology from the one-minute bars.
5. Confirm a confirmed V2 candidate, if any, yields exactly three shadow
   scenario rows sharing the same entry tick, and **no additional NT orders**.
   Zero confirmed rows on a date is valid and not a failure.
6. Look for \`FAILED PMH V2 DISABLED\` errors, and ensure session/candidate
   CSVs are flushed when strategy terminates.
7. Send the two V2 CSVs and the Run 8 executions from the same short test.
   We should only then proceed with the full five-contract Playback.

## Per-contract evaluation

After collecting verified contract replays, run from repository root:

\`\`\`powershell
python tools/analyze_overnight_edge_failed_pmh_v2.py \`
  --input "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexResearch\OvernightEdgePortfolio" \`
  --output "$env:USERPROFILE\Desktop\FailedPMH_V2_Summary.csv" \`
  --run-id "PASTE_ES_12_25_RUN_ID" \`
  --run-id "PASTE_ES_03_26_RUN_ID" \`
  --run-id "PASTE_ES_06_26_RUN_ID" \`
  --run-id "PASTE_ES_09_26_RUN_ID" \`
  --run-id "PASTE_ES_12_26_RUN_ID"
\`\`\`

Summary includes scenario, instrument, completed trades, win rate, hypothetical
gross P&L, profit factor, max **closed-trade** drawdown and baseline collision
counts. These cannot be treated as a deployable portfolio backtest.

### Acceptance criteria before any live consideration

- NT8 compilation and short Playback parity against Run 8.
- No duplicate or noncausal confirmation entries; expiry and all three stop
  scenarios match independently inspected tick paths.
- Sufficient trade sample in each meaningful chronological/regime segment.
- Compare to V1 in identical signal windows and with realistic fees/slippage.
- A separate **integrated portfolio** replay satisfying trade priority, caps,
  $1,800 worst-case **intratrade** drawdown, and plausible live fills.
- Forward/held-out validation after model selection.

**Never infer an edge from the historical shadow gross P&L alone.**
