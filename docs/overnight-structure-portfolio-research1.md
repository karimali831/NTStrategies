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

**Research 1.0.2** stores completed premarket widths in a shared, date-keyed
memory ledger. All ES contract instances in the same NinjaTrader process can
carry the most recent 20 **distinct** valid sessions across rollovers without
restarting a 20-session warmup at every contract change. Only dates strictly
before the current PM date can contribute to its threshold. The current PM
width is published **after** its threshold has been evaluated.

The ledger also:
- excludes duplicate calendar dates;
- quarantines dates with conflicting widths from different import/contract
  streams, rather than silently selecting one;
- rejects lookback widths older than 50 calendar days;
- drops later cached dates only when a **single strategy instance**
  genuinely moves its own Realtime Playback clock backwards by multiple days.
  A different contract instance processing earlier historical/preload dates
  does not count as a global Playback rewind.

**Compression history group** must match for all contracts belonging to the
same continuous replay. Set a **new group name for every independent replay**,
such as \`R102-2023-2026-A\`, to prevent studies from sharing stale in-memory
history. All 13 contract strategy instances should use that identical group.
The ledger does not persist across NinjaTrader restarts. A full research replay
is intended to run chronologically in one NT session; if interrupted after an
NT restart, either restart with adequate preceding sessions or treat the
initial post-restart ONL warmup as incomplete.

The first 20 valid sessions of an entirely new history group still require
warmup; this is expected. Check \`CrossContract\`, \`Available\`,
\`Conflicts\` and \`RewindReset\` in the daily PM_FINAL diagnostics to confirm
rollover continuity and absence of future-data leakage.

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

\`Documents\NinjaTrader 8\NinjexResearch\OvernightStructurePortfolio\1.0.3-research1\<RunId>\`

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

\`Documents\NinjaTrader 8\NinjexLogs\OvernightStructurePortfolio\1.0.3-research1\`

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

## Research 1.0.3 — instance-aware Playback rewind correction

The 1.0.2 rollover test used a valid ES 09-26 front-month dataset ending
11 September 2026 and ES 12-26 active from the Sunday session on
13 September at 18:00 ET. The December contract's earlier Historical
or partial Realtime preload on 31 August/1 September does **not** mean
its live trading began there. Nor does the ES 09-26 trade of 2 September
belong to ES 12-26.

However, the shared 1.0.2 `LatestRealtimeDate` incorrectly treated the
December instance's earlier preload callback as a user Playback rewind and
discarded the outgoing ES 09-26 history. The shared session ledger now uses
`LatestRealtimeByInstance[RunId]`, where `RunId` is generated uniquely
for each strategy's DataLoaded lifecycle. Only a backwards Realtime move
of **that same instance** can trigger cache pruning.

The key regression test is
`RunMixedContractInitializationTest`. It reproduces ES 09-26 accumulating
ten valid PM sessions through 11 September, followed by ES 12-26
initialising with partial 1 September data. Expected research output:

| ES 12-26 PM date | Available previous valid sessions | Cross-contract sessions |
|---|---:|---:|
| 14 September 2026 | 10 | 0 (lookback not complete) |
| 28 September 2026 | 20 | 10 |

`CompressionRewindReset` must remain `0` for the cross-instance
initialisation sequence, and `CompressionPriorConflicts` should remain 0
when the inactive December pre-rollover PM ranges are invalid.
The first full 20-date ONL percentile therefore becomes available on
28 September, rather than 9 October.

The regression suite still tests **genuine** same-instance Playback rewinds
as well as strict-before-today percentiles, duplicate-date quarantine,
group isolation, and cross-contract provenance.

**Run the Windows regression test and a short real NT8 Playback rollover
smoke test before beginning the 13-contract history.** If the two
strategy instances emit PM_FINAL callbacks out of chronological sequence
or the September 14 count is not ten, send the 1.0.3 `sessions.csv` and
daily logs; this study cannot assume that merely sharing a static ledger
guarantees every source instance has already published its prior dates.

## Research 1.0.2 — end-of-day file finalisation

The current RTH session is now finalised when its last completed one-minute
RTH candle has been observed and trading is flat, **or** at the first
subsequent completed post-RTH minute on the same date. An in-flight 16:00
flatten exit is included before the daily totals are committed.

The output files are flushed at session finalisation, not just when the next
morning arrives or the strategy is disabled. A normal completed RTH day writes
exactly one \`RthSummary\` row. A severely incomplete session writes
\`RthPartial\` instead. The per-run CSVs include the history-group name,
available session count, number of cross-contract lookback sessions and any
conflicted days.

**Still disable the strategy before creating the final ZIP**. Reading an open
CSV while its writer is actively appending cannot guarantee that an
intermediate line is complete. When all instances are disabled, the writers
are disposed and the final files are safe to archive.

The ledger's date-ordering, nearest-rank percentile, duplicate-date
conflict quarantine, cross-contract provenance and Playback rewind handling
have standalone C# regression cases under
\`tests/OvernightStructureCompressionHistory.Tests.cs\`. On a Windows/.NET
Framework machine with the repository checked out, run:

\`\`\`powershell
.\tools\test_overnight_structure_compression_history.ps1
\`\`\`

These tests compile only the pure history coordinator; they do **not**
replace an actual NT8 compile or Playback execution test.

Minimal rollover smoke test: run a few valid sessions of the outgoing ES
contract, followed by the first few sessions of the next contract, with both
instances using the same \`Compression history group\`. Verify that the
incoming contract's first \`PM_FINAL\` shows the inherited 20-session history
and a nonzero \`CrossContract\` count. Verify there are no two
\`RthSummary\` rows for one (Instrument, RunId, Date).

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
