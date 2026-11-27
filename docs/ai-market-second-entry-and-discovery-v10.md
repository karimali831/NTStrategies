# Ninjex AI v10 — second-entry pullback and genuine AI ranking

## Honest project assessment

The user expected **AI-driven, full three-year ES tick-data strategy
discovery**, not a succession of manually proposed fixed strategies.
That expectation has NOT been met yet. Our actual completed products
are:

1. Historical ES 1m/5m/15m/60m OHLCV exports, chronology, audit and
   provisional research features, with a reserved final holdout.
2. Generic directional ML, conditional expectancy ML, and deliberately
   conservative rules-based entry studies, all without a verified edge.
3. A v9 unconditional ES tradeability/cost audit, which measured
   many possible opportunities but contains NO predictive strategy.
4. With this PR: a mechanical EMA second-entry event baseline AND
   a real supervised regression that learns conditional after-cost
   expectancy for those detected events.

**Still missing**: time-and-sales tick / bid-ask quote ingestion,
event/mining discovery that proposes patterns without manual
enumeration, cross-family walk-forward feature/strategy search with
multiple-test penalties, and tick-accurate execution/prop-firm intraday
trailing-drawdown audit. Do not advertise these as built.

### V9 uploaded report, interpreted correctly

V9: 5,973 clean clock-based ES study intervals on 508 TRAIN days.
Always LONG -$174,565, -$29.23 mean; always SHORT -$210,752.50,
-$35.28 mean. At least one of the TWO directions was profitable
after costs in 69.1% of intervals. A hindsight "best side" oracle
was +$671,372.50 but can ONLY choose the side AFTER observing future
prices and is completely UNTRADEABLE. Its +$112.40/trade is not
evidence that a predictive model can select those winners.
Thirty-minute mean raw range 14.25 ES points is also not a
trade-executable per-position opportunity.
V9's $30 assumed round trip comprises 1 adverse ES tick per side
and $5 commission. Our first 8 studies did not identify a verified
tradeable edge; adding more handpicked signals must not silently
become the full AI strategy discovery project.

## What v10 implements now

This v10 study provides a **specific mechanically auditable
interpretation**, not an authoritative definition of every discretionary
H2/L2 trading technique or 21 EMA on a 2000-tick chart.

On continuous historical ES ETH **completed five-minute** candles:

- EMA21 starts from the earliest uninterrupted history. ATR14 uses
  recent true ranges. Reset state and EMA warmup after source gaps.
- Trend: directional EMA21 slope over five 5m bars >= 0.05 ATR14;
  a completed impulse closes at least 0.5 ATR beyond EMA.
- Pullback: later completed bar low/high revisits EMA within 0.25
  ATR and closes against the impulse; invalidate if close more than
  0.5 ATR through EMA. Mirror every rule for shorts.
- H1/L1: first strong directional bar breaking the preceding bar's
  high/low by >= one ES tick, body >=50% of entire candle and
  close in directional top/bottom 30%.
- First attempt FAILS in price action only after a SUBSEQUENT 5m
  candle breaches the H1/L1 adverse extreme by one tick, without
  simultaneously breaking its original continuation extreme.
  This does NOT prove that an H1 stop order was filled and stopped.
- H2/L2: a later second strong higher-high/lower-low signal bar
  following the failed attempt, all within 12 bars after pullback.
  The detector is **stateful**; never uses later bars to retroactively
  define an earlier signal.
- RTH signal closes 09:35–15:00 America/New_York. EMA warmup uses
  earlier contiguous ETH candles, not a 9:30 restart.
- On each completed signal candle, place a *simulated* resting
  stop order 1 ES tick beyond its high/low for the following five
  complete 1m bars, and protective stop 1 tick beyond its opposing
  extreme. Hypothetical 2R profit target is calculated from actual
  entry fill to original protective stop, not a fixed 24 ticks.
  Maximum 30 minute hold AFTER fill.
- One adverse tick on entry and exit, $5 commission round-trip.
  Conservative STOP-FIRST if one 1m candle can hit both.
  An entry gap past the trigger can increase actual risk;
  an oversized fill is marked UNSAFE, **not asserted preventable**
  with a resting market order in live conditions.
- Reject if nominal risk including adverse entry/exit slip and fee
  > $250 on one ES contract; the report counts missed setups.
- H1 and H2 are SEPARATE benchmarks for measuring "second entry
  versus first entry", at most two signals per ET day and minimum
  35min ex-ante signal spacing PER COHORT. Do not add their PnLs
  together as though both systems had simultaneously traded.

All raw bars stop strictly before Sep 1 2025 ET. Prior-used
Sep2025–Jun2026 validation and reserved June2026+ final holdout
are never scored. The earlier 2023–Aug2024 and later Sep2024–Aug2025
TRAIN descriptions alone are NOT independent replication.

### Genuine AI stage (implemented, deliberately modest)

A separate script consumes only filled **H2/L2** event snapshots,
containing EMA slope/position, ATR, trend/pullback shape, failed-first
depth, signal-bar body/close location, 5m volume, 12-bar efficiency,
nominal stop width, local clock and side. It DOES NOT use labels,
exit reasons, raw price, full contract identity, or future outcomes
as predictors.

It fits a fixed regularized Ridge regression to actual
after-assumed-cost net PnL from EARLY_TRAIN events only and
predicts LATE_TRAIN. The frozen decision selects only forecasts
>= maximum($20, 80th percentile of early-train fit predictions),
at most one H2/L2 trade per NY day. Reports model mean-absolute
error vs an unconditional earlier-year mean, selected PnL, missed
trades and the all-H2 benchmark. No grid search on late labels.

**Automatic abstention** if fewer than 50 early or 30 later H2/L2
filled trades. Lack of adequate frequency is a critical result, not
an excuse to lower risk or overfit.

This is **real supervised learning but NOT autonomous discovery**.
No verified edge can be inferred from positive TRAIN results.

## Run both stages without new NinjaTrader exports

Pull master into C:\Projects\NTStrategies. Use your existing
Python environment with tools/requirements-ai-market-ml.txt.

In PowerShell, from the repository root:

    python .\tools\ai_market_second_entry_v10.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\SecondEntryV10"

Then:

    python .\tools\ai_market_second_entry_ml_v10.py --events "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\SecondEntryV10\es-second-entry-v10-events.csv" --benchmark "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\SecondEntryV10\es-second-entry-v10.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\SecondEntryV10"

Send:
- es-second-entry-v10.json and es-second-entry-v10.md
- es-second-entry-v10-ai.json and es-second-entry-v10-ai.md

If we need timestamp audits, the detailed local events CSV and ML
prediction CSV (if model fitted) can be uploaded separately.

## What comes next (NOT implemented by this PR)

The AI-first workstream needs new **raw time-and-sales ticks and
bid/ask** with instrument, contract, exchange timestamps, trade size,
aggressor-side quality, continuous rollover, and provenance checks.
Use bounded, chunked exports/research ingestion; large files should
remain local in Parquet/SQL rather than sent to a chat model.
Construct bars per **actual trade count** for real tick-chart tests;
the present 5m EMA21 cannot reproduce a 2,000-tick EMA21.

Then build a **self-directed discovery service** that constructs
causally available features, proposes/combines candidate event families
(trend, pullbacks, order-flow imbalance, opening drive, volatility
transition, liquidity absorption), runs nested chronological
walk-forward with embargo and costs, penalizes multiple testing,
and logs every experiment—including negative ones. ML should
rank ex-ante conditional trade expectancy and abstain, not find an
optimal hindsight direction by looking at future prices.

Only sufficiently promising genuinely unseen policies proceed
to bid/ask tick-sequenced execution, open-equity/trailing drawdown,
news compliance, broker/funding rule checks, then NinjaTrader sim
and real-time shadow mode. Reserve the June15 2026+ final holdout
until one pre-registered candidate is ready for a *single*
independent confirmation. Achieving $10k/year <=$2k DD is an
aspiration, **not** a guaranteed output of three years of data.
