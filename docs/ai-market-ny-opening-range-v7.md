# Ninjex AI v7: New York cash-session opening-range research (TRAIN only)

## Why v4–v6 are now retired

The uploaded v6 frozen stress report contains 43 simulated trades across
September 2025–June 2026, with:

- model direction: -$3,327.50, profit factor 0.40;
- reversed model: **+$22.50**, profit factor 1.005, closed DD $1,620;
- matched always-short: -$102.50, closed DD $845;
- reversed minus always-short mean +$2.91/trade,
  paired 95% daily interval **[-$49.43, +$52.79]**.

This does not establish a tradable or sizeable edge, and a 43-trade
sample is nowhere near the desired >=$10k/year within $2k drawdown.
Do NOT keep reversing directions, lowering thresholds, or retuning this
model on the same previously inspected data.

Instead the v7 diagnostic asks a *different*, session-anchored question:
Does the first break or failed sweep of the 09:30–10:00 Eastern opening
range generate a more informative directional event than arbitrary
rolling one-hour highs and lows?

## Predeclared hypothesis / signal rules

- Cash RTH for this study means **09:30 America/New_York local**.
  Handles US DST automatically via zoneinfo and uses bar-close stamps.
- Use the six completed five-minute candles closing 09:35, 09:40,
  09:45, 09:50, 09:55 and 10:00 ET to form opening-range HIGH/LOW.
  Every bar must exist and be clean; do not estimate missing prices.
- Inspect **only 10:05–11:30 ET closed five-minute bars**. There must
  be no missing source five-minute candle from the end of the opening
  range to the selected trigger.
- BREAK_LONG: bar closes >= opening range HIGH + 1 ES tick and
  closes above its own open. BREAK_SHORT mirrors below the OR LOW.
- REJECT_SHORT: bar high reaches >= OR HIGH + 1 tick, then closes
  <= OR HIGH - 1 tick below its own open. REJECT_LONG mirrors a
  sweep beneath the OR LOW and close back inside.
- If the first possible signal is ambiguous, skip that day.
- **First qualifying event per ET day**, across all four categories;
  no later entry if the first event has incomplete source price data.
- Days with duplicate dated contracts are excluded rather than
  cherry-picking a better-performing contract.

For all qualifying days, simulate four directional policies at the
**same timestamp**: signal direction, opposite direction, always LONG,
always SHORT. This distinguishes market timing from signal-direction
value. Fixed same v4–v6 trade economics: one ES, 12-tick stop, 24-tick
target, maximum 30-minute hold, entry at next observed 1m OHLC open,
1 adverse tick on entry and exit, $5 round-trip fees; same 1m bar
hitting both levels is conservatively stopped out.

## Exact local PowerShell command

Pull latest NTStrategies/master, use the same Python environment as
your successful v6 experiment, open C:\Projects\NTStrategies:

    python .\tools\ai_market_ny_opening_range_v7.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\NyOpeningRangeV7"

Generated outputs:

- es-ny-opening-range-v7.json — full results, counts, exceptions, by
  four event types, contracts and early vs late training;
- es-ny-opening-range-v7.md — concise readout;
- es-ny-opening-range-v7-events.csv — timestamped hypothetical events
  for source discrepancy investigations. No need to upload CSV unless
  we want to inspect specific timestamps.

Send the first two summary files. Original CSVs, NinjaTrader imports
and your original purchased data are untouched.

## Research safeguards

- **TRAIN split only**: ES five-minute as-of decisions strictly before
  September 1, 2025 Eastern; source one-/five-minute reader stops
  before this date. No validation/holdout OHLC is loaded to compute
  trades; only split counts are read from feature CSV.
- The prior-used September 2025–June 14 2026 validation and the final
  June 15 2026+ holdout remain unscored.
- Trading-day RTH open is anchored to US time, NOT arbitrary UTC/UK
  time or the CME ETH maintenance reset. No holiday bar is invented.
- No ML fit, model selection, optimizing stop/target ratios, threshold
  scans, or selecting a best subgroup after checking profits.
- Every hypothetical stop/target/order uses 1-minute OHLC; this does
  NOT demonstrate actual tick-by-tick fill feasibility, live spread,
  latency, news restrictions or prop-firm intraday trailing drawdown.
- Date uniqueness and source integrity are verified; incomplete
  morning bars and 30-minute future paths are excluded without fills.

**Decision policy:** Compare overall and early-vs-late fixed event
results, and require significant directional improvement relative to
same-time opposite/always-long/always-short in both periods before
considering an additional event-aware ML research stage. Any apparently
strong subset remains an exploratory hypothesis until a genuinely new
chronological test is available. Avoid consuming the reserved holdout
on a hypothesis that fails basic training evidence.

Three years of historical bars are useful, but they cannot guarantee
that an executable edge exists. The source is a derived 1m/5m/15m/60m
OHLCV dataset, not full tick-order/replay fills.
