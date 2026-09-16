# Using the ES market collector in Research Explorer

`NinjexEsMarketResearchCollector` 1.2.0 exports the data needed by the Overnight Edge / `FourModelResearchFiltered` view in Ninjex Research Explorer. Continue using the ES 5-minute primary chart, ETH trading hours, unmerged expiry contracts, the agreed contract allocation and sufficient prior-session warm-up.

For each collector run, upload the matching `_manifest.csv`, `_sessions.csv` and `_observations.csv` together in a ZIP. The collector does not need to be rerun solely for this dashboard integration. Import and query support lives in **NinjexApp**; the Explorer view lives in **NinjexWeb**. Those companion changes and the backend's `005_MarketResearchCollector.sql` migration must be deployed first.

The existing `NinjexPremarketRangeResearch` breakout/candidate engine and its exports continue unchanged. The neutral collector has different semantics: one observation per minute, four-model qualification/priority flags, and bounded forward-bar outcomes. Its rows must not be passed off as completed strategy executions or forced into the legacy breakout lifecycle.

Choose **Overnight Edge · FourModelResearchFiltered** in Research Explorer. Select the quarterly runs and evaluation dates (2025-09-17 through 2026-09-11 for this study). Compare models, daily setup frequency, data-quality flags and features such as ATR, overnight width and price versus fast EMA. Overlapping reruns are rejected; out-of-allocation warm-up sessions are excluded from the cohort but retained in storage.

Resolved gross ticks are an independent-setup research measure. Actual Run 4 performance ($22,612.50 net), daily loss limits, concurrent-position blocking, slippage and commissions require strategy execution results and replay validation. Ambiguous/incomplete paths stay explicit, and no break-even change is inferred or applied.
