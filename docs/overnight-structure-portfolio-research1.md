# Ninjex Overnight Structure Portfolio — Research 1

This is a **new, independent NinjaTrader strategy**. Do not replace or delete
\`NinjexOvernightEdgePortfolio\`: it remains the historical control/baseline.

**Status:** unvalidated research hypothesis, *not approved for live trading*.
No result from the exploratory three-year one-minute simulation is asserted
to be a NinjaTrader trading result.

## Thesis and models

All signals are formed from **completed one-minute bars** with cached,
**completed five-minute context**. Entries, when enabled, are sent on the
first causal 1-tick-series event *after* the signal. No time-zone conversion:
chart and feed must use ET.

| Model | Signal | Context/filter | Exit |
|---|---|---|---|
| ONL short | \`ONL-COMP-BREAK-S\` | First RTH close below overnight low; premarket width at/below 35th percentile of **previous 20 valid premarket sessions**; optional bearish RTH-opening momentum | 30-tick stop / 75-tick target |
| ONH long | \`ONH-DISP-BREAK-L\` | First RTH close above overnight high, bullish candle body >=10 ticks and optionally close above RTH open | 15-tick stop / 40-tick target |
| PDC long | \`PDC-TREND-RECLAIM-L\` | Cross above the previous valid RTH close; completed EMA(21) 5m slope >=0 ticks; 1m candle range <=30 ticks | 25-tick stop / 40-tick target |

**These are starting hypotheses, not previously validated fixed rules.**
Their definitions are deliberately explicit and may differ from the exploratory
minute simulator (especially fill mechanics, first-attempt semantics,
qualifying session coverage and portfolio scheduling).

ONL and ONH only consider **the first actual crossing** per trading session,
even when that first crossing is rejected by the model filters. This prevents
selecting a later, cleaner crossover retrospectively.

PDC is evaluated at every actual upward cross, but only one position can exist
at a time. New signals have the fixed priority **ONL > ONH > PDC** when their
one-minute confirmation bars coincide.

### Compression — no current-session leakage

When the current day's premarket range finalizes at 09:30 ET, the model:

1. Calculates the nearest-rank 35th percentile of the **prior** 20 complete,
   valid premarket widths in ticks.
2. Compares the current day's width with that threshold.
3. Only **then** adds the current width to the rolling history.

The first 20 valid sessions **within each strategy instance/contract** are a
warmup: ONL compression cannot qualify yet. This is a deliberate limitation of
Research 1, not a use of future data. For fair cross-contract comparisons,
include adequate historical warmup bars and explicitly audit the
\`InsufficientPremarketHistory\` candidates. Contract transitions should not
silently use future or duplicated sessions.

The overnight and premarket reference levels use the same existing
\`NinjexPremarketRangeEngine\` implementation that was used in Overnight Edge,
requiring at least 180/186 completed overnight 5m bars and 75/78 premarket 5m
bars by default.

## Execution and account safety

- **ES only**, primary **5-minute** chart, CME US Index Futures ETH, ET.
- Secondary BIP 1 = one-minute completed-bar signals.
- Secondary BIP 2 = one-tick causal executions and intratrade equity.
- NinjaScript \`Calculate.OnEachTick\` is intentional: signals use only the
  previous, completed candle on the first tick of the next candle.
- \`Enable Playback Orders = True\` by default, but the order gate requires
  **Realtime state and a detected Playback account/connection**. Normal
  broker/live accounts cannot receive new entries.
- \`Enable Historical Backtest Orders = False\` by default. Set True only for
  an explicit historical tick-series Strategy Analyzer simulation. Ensure
  suitable historical tick data exists; a bar-only run is not a substitute
  for the Playback fill audit.
- Standard fill-relative managed stops/targets are configured before entry.
- One strategy position at a time, maximum **3 trades / 2 winners / 2 losers**
  per RTH day, maximum hold 60 minutes, flatten at 16:00 ET.
- No automatic ES→MES scaling, break-even or prop-account drawdown lockout.
- **Never run this simultaneously with Overnight Edge or LevelCandidate on the
  same Playback account/instrument** when establishing performance.

The research model does not implement restart recovery for live deployment,
and it must not be promoted to live on the basis of a short positive run.

## Research exports

With \`Enable Research Export=True\` (default), files go to:

\`Documents\NinjaTrader 8\NinjexResearch\OvernightStructurePortfolio\1.0.0-research1\<RunId>\`

Six CSVs per instance:

- \`overnight_structure_*_bars.csv\` — completed one-minute OHLCV
  (03:00–16:00), levels, EMA5, ATR, compression state.
- \`overnight_structure_*_context.csv\` — completed five-minute context,
  including the overnight/late-ETH bars.
- \`overnight_structure_*_sessions.csv\` — premarket snapshots and RTH
  summaries including zero-trade dates and overnight/premarket coverage.
- \`overnight_structure_*_candidates.csv\` — every first ONL/ONH crossing
  plus each PDC upward crossing, **including raw rejected signals**,
  reasons, selected model and portfolio block reason.
- \`overnight_structure_*_trades.csv\` — actual managed executions, outcome,
  fill price, stop/target, MFE/MAE and gross P&L.
- \`overnight_structure_*_equity.csv\` — new observed equity peaks and
  intratrade drawdown maxima, plus trade exits, recorded from the tick series.

Daily log files, including strategy version and date:

\`Documents\NinjaTrader 8\NinjexLogs\OvernightStructurePortfolio\1.0.0-research1\`

No strategy order decisions should depend on whether research export succeeds.

## Playback permission diagnosis (Research 1.0.1)

The first ES 12-26 test exported 106 candidate crossings, including
38 qualified setups, but no actual trade. The recorded disposition for
all 38 was the generic `ResearchOnlyNoOrders`. Realtime callbacks were
present, so the prior export did **not** establish which individual safety
condition rejected orders.

Research 1.0.1 keeps the Playback-only safety policy but logs the precise
cause on every RTH date in the daily file:

`EXECUTION_GATE State=... Primary=... Master=... Account=... Connection=... PlaybackOrders=... HistoricalOrders=... Result=...`

Candidate dispositions now identify specific failures such as
`PlaybackOrdersDisabled`, `AccountNotPlayback`,
`PrimarySeriesMustBeES5Minute` or `HistoricalOrdersDisabled`.
This is diagnostics only; it is not permission to place live orders.

**Do not enable Historical Backtest Orders just because the Playback
controller is set to Historical.** Playback historical-tick data can still
arrive through Realtime callbacks; verify `State` and
`Enable Playback Orders` from the log. Keep a normal live broker
account disallowed.

For an efficient verification, replay the 20 Nov 2026 RTH session with
sufficient historical warmup. The prior CSV recorded a qualified
`ONH-DISP-BREAK-L` at 09:58 ET on that date. If settings and
account are valid, it should no longer be rejected by the execution gate.

## Required first smoke test (before 13-contract replay)

1. Import into NinjaTrader and compile the C# project.
2. Apply **only this strategy**, on a Playback ES 5-minute chart.
3. Replay 20+ valid premarket sessions for compression warmup, then a small
   3–5-session interval with representative ONL/ONH/PDC events.
4. Verify the RTH/PDC levels and the previously completed 5m EMA/ATR.
5. Verify candidate chronology and first-break consumption.
6. Verify an accepted candidate creates exactly one entry and the correct
   fill-relative stop/target orders.
7. Verify max-hold and 16:00 exits and exact trading-day 3/2/2 counters.
8. Validate tick drawdown against the trade list/Control Center.
9. Confirm no unwanted entries if the account is not Playback.
10. ZIP the full research folder **plus daily logs** for audit.

Short run trade results must be reconciled before any long uninterrupted
13-contract run. Do not run both strategies simultaneously.

## Three-year objective / evaluation

The requirement is **>= $10,000 per full year** with **<= $2,000 stitched
intratrade drawdown** after realistic trading costs. These three components
have **not** yet passed that test. A positive in-sample simulation
(+~$29.3k over the discovery sample, with >$2k closed drawdown) was the reason
to research these patterns, *not* evidence the threshold has been achieved.

Select one verified, non-overlapping RunId per ES contract. For historical
reports, run:

\`\`\`powershell
python tools\analyze_overnight_structure.py \`
  --root "$env:USERPROFILE\Documents\NinjaTrader 8\NinjexResearch\OvernightStructurePortfolio" \`
  --output "$env:USERPROFILE\Desktop\OvernightStructure_R1_Summary.csv"
\`\`\`

The script warns if selected runs overlap and will not claim a stitched DD in
that case. It reports per-contract completed results and records the precise
RunIds. It does **not** adjust gross P&L for commissions, spread, slippage, or
real contract rollover.

Post-run research must separately score:
- 2024, 2025, 2026 annual performance;
- combined and per-model *stitched intratrade DD*;
- drawdown clustering and concentration;
- per-contract and rolling 3/6/12-month consistency;
- candidate rejection reasons and lost portfolio opportunities;
- realistic slippage/commission sensitivity;
- robustness to small variations in thresholds **without tuning on a holdout**;
- new future, unseen data after a rules freeze.

If it fails, retire these hypotheses and research a different architecture.
