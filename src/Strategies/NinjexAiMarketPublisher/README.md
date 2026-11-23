# Ninjex AI Market Publisher

Observe-only NinjaScript strategy. It cannot submit orders.

## Requirements
- Run on a **5-minute ES chart** (NQ supported for future research).
- Chart/session uses CME US Index Futures ETH; set NinjaTrader
  Tools > Options > General > Time zone to **Eastern Time (ET)**.
- Historical 15m and 60m bars available (at least 100 completed bars each).
- Backend migration and publisher credentials configured as described in
  NinjexApp's \`docs/ai-market-observer.md\`.
- Define Windows environment variable \`NINJEX_AI_PUBLISHER_KEY\` to the same
  random publisher secret as the backend. Set it for the account launching NT,
  restart NinjaTrader afterward; never store the secret in a strategy template.

## Usage
Add **Ninjex AI Market Publisher (observe only)** to a 5m chart.
Keep "Enable publishing" false until ready, then set Backend URL to
the desired HTTPS Ninjex backend and enable publishing. It will publish
up to one completed 5m observation per bar when State == Realtime.
The strategy adds 15m and 60m series internally.

The first 100 bars of each timeframe are warmup. Unavailable/stale
higher-timeframe data can be rejected by the backend and remains
visible in NinjaTrader Output as HTTP 400. Signals do not control
any order, account or existing Ninjex strategy.

The publisher assumes all \`Times[series]\` are in ET. If the user has a
different NinjaTrader timezone, DO NOT enable until the timestamp
conversion is adapted. During ambiguous DST times, observations are
skipped. Simulated/live bar data are supported but historical
backfill and Market Replay acceleration are deliberately not.
