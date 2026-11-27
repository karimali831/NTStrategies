# Ninjex AI v8 — freeze first NY upper opening-range sweep => LONG

## What the v7 reports actually showed

On 491 one-event-per-day 2023–2025 training observations, the v7
overall direction policy lost $16,930; reverse all events lost $12,505,
always-long lost $6,942.50, and always-short lost $22,492.50.

The sole after-cost positive subgroup, identified AFTER reading the
report, was LONG at v7 event REJECT_SHORT: **82 events, +$3,027.50,
mean +$36.92, PF 1.4421 and max CLOSED drawdown $1,190**.
Early train: +$1,562.50 across 50 entries; later train: +$1,465 across
32 entries. That direction is identical to always-LONG at those
same 82 timestamps, so merely comparing the opposite short side
does not establish an event-timing edge.

With ~41 entries/year and a maximum net bracket winner of $270
on a single ES, the theoretical ceiling is ~$11k/year even if
every single entry were a full winner. Actual ~+$1,514/year is
far short of >=$10k/year, and multiplying lots would also
multiply intraday drawdown exposure.

## One-shot protocol, not an optimisation

- Freeze v7 definition: 09:30–10:00 NY opening-range high/low;
  FIRST qualifying completed 5m event 10:05–11:30 ET must be
  REJECT_SHORT (sweeps above the OR high by >= 1 tick, then
  closes at least one tick back inside, with bearish candle).
- Hypothetical direction: LONG at NEXT 1m open, 12-tick SL,
  24-tick TP, at most 30 minutes, one adverse tick each side,
  $5 roundtrip commission, stop first if both touched in 1m.
- Score only already-inspected **2025-09 through 2026-06-14
  validation**. Final June15 2026+ holdout untouched, raw read
  stops before its first bar. No re-training or threshold adjustment.
- At identical event timestamps show LONG and SHORT outcomes.
- Separately compute a descriptive baseline: all OTHER eligible
  day-clock observations in the SAME dated ES contract, at the
  EXACT same NY 5-minute closing clock as the event; require 10+
  eligible peers, continuous opening range through that minute,
  quality-checked source and a 30m 1m outcome. Disallow any peer
  clock at or AFTER a first REJECT_SHORT event on that date; a
  LATER event must not remove an EARLIER peer observation based
  on future knowledge. Controls do not affect entry selection.
  Match the per-event clock, not a conveniently chosen time after
  seeing the PnL.
- Show event LONG mean minus same-clock LONG baseline mean and
  descriptive bootstrap interval. Controls are reused across
  events; interval treats them as fixed and understates uncertainty.
- Show chronological quarter and monthly breakdowns. Never
  select the best quarter/event parameter based on this readout.

This is **NOT independent replication**: the event family and
direction came from looking at training results, and our validation
period was also inspected in prior v1/v2 research. It can falsify
the idea or strengthen a future research case, not prove a live edge.

## PowerShell command from C:\Projects\NTStrategies

Pull master (after this PR merges), then:

    python .\tools\ai_market_ny_upper_sweep_v8.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\NyUpperSweepV8"

Send ONLY:
- es-ny-upper-sweep-v8.json
- es-ny-upper-sweep-v8.md

A supplementary es-ny-upper-sweep-v8-events.csv is created if there
are eligible trades, for later timestamp-level audit. No new
NinjaTrader export, dependency or actual trade is necessary.

## Strict interpretation

If overall LONG is not robustly after-cost positive and meaningfully
above same-time non-event LONG, retire this specific upper-sweep
hypothesis. If positive, it remains too low-frequency and too weak
economically for the current $10k/year target. Investigate a
different higher-frequency phenomenon, richer multi-timeframe
regime detection, or truly granular execution, but do NOT use the
final holdout to repeatedly tune discarded ideas.

The remaining final holdout is 30,853 feature rows dated
June15–Nov25 2026. It remains unused by v8. 1m OHLC stop-first
is NOT bid/ask tick-simulation, and CLOSED equity max drawdown
does not measure prop-firm intraday open-equity trailing drawdown.
