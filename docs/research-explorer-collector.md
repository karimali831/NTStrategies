# Using the ES market collector in Research Explorer

`NinjexEsMarketResearchCollector` 1.2.0 exports the data needed by the Overnight Edge / `FourModelResearchFiltered` view in Ninjex Research Explorer. Continue using the ES 5-minute primary chart, ETH trading hours, unmerged expiry contracts, the agreed contract allocation and sufficient prior-session warm-up.

For each collector run, upload the matching `_manifest.csv`, `_sessions.csv` and `_observations.csv` together in a ZIP. The collector does not need to be rerun solely for this dashboard integration. Import and query support lives in **NinjexApp**; the Explorer view lives in **NinjexWeb**. Those companion changes and the backend's `005_MarketResearchCollector.sql` migration must be deployed first.

The existing `NinjexPremarketRangeResearch` breakout/candidate engine and its exports continue unchanged. The neutral collector has different semantics: one observation per minute, four-model qualification/priority flags, and bounded forward-bar outcomes. Its rows must not be passed off as completed strategy executions or forced into the legacy breakout lifecycle.

Choose **Overnight Edge · FourModelResearchFiltered** in Research Explorer. Select the quarterly runs and evaluation dates (2025-09-17 through 2026-09-11 for this study). Compare models, daily setup frequency, data-quality flags and features such as ATR, overnight width and price versus fast EMA. Overlapping reruns are rejected; out-of-allocation warm-up sessions are excluded from the cohort but retained in storage.

Resolved gross ticks are an independent-setup research measure. Actual Run 4 performance ($22,612.50 reported before costs), daily loss limits, concurrent-position blocking, slippage and commissions require strategy execution results and replay validation. Ambiguous/incomplete paths stay explicit, and no break-even change is inferred or applied.

## Five-minute context correction (context-asof-1)

The CSV schema remains 1.2.0 so existing Research Explorer imports continue to
recognize it. The manifest now records CollectorImplementationRevision=context-asof-1
and FiveMinuteContextPolicy. Do not combine old and new implementation manifests
in one Explorer analysis; its parameter-consistency check intentionally rejects
that mixture.

Completed five-minute OHLC, ATR, ADX, EMAs and slopes are stored together in a
bounded timestamped history. Each observation selects the latest captured
snapshot whose timestamp does not exceed its completed one-minute timestamp.
The four-model qualification flags use that same selected snapshot.
If no snapshot is available, the collector logs
OBSERVATION SKIP Reason=FiveMinuteContextUnavailable and emits no observation.
That missing minute remains visible in the session observation count and Explorer
coverage audit. It is not filled with future data or relabelled.
Older snapshots retain their true timestamp and produce a CONTEXT REVIEW diagnostic;
this patch does not add a new stale-context entry filter or change trading rules.

Validate first on ES 06-26, 9–10 June 2026, and ES 09-26, 25 June 2026,
loading sufficient earlier history and comparing normal and maximum replay speed.
Specifically check 9 June 10:57 and 25 June 09:59 ET. Every exported
Context5mTime must be no later than Time; review any skips or stale-context logs.
This correction does not establish that maximum replay speed caused the original
gaps and does not repair the original ZIP or missing provider/replay data.
Separate incomplete overnight counts on 23 December, 16 March and 15 June still
need data/warm-up review. The live NinjexOvernightEdgePortfolio strategy is unchanged.

The regression suite compiles the actual snapshot history and existing forward/tick
methods in a platform-free .NET fixture. A full NinjaTrader build and replay remain
necessary to verify platform event ordering. Keep the Explorer future-context
validation enabled.
