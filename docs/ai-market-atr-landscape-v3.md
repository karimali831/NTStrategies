# Ninjex AI — v3 ATR bracket geometry study (train only)

The first net-expectancy ML experiment selected 10 simulated LONG
trades across its previously used validation dates: +450 USD, exactly
equal to always LONG at those timestamps. Ten trades over ten months
and no directional lift are NOT evidence of a repeatable A+ edge.

Across the earlier training data, the fixed 12-tick stop / 24-tick
target / 30-minute exit averaged about -30 USD per unconditional
long or short trade. We should test whether the rigid stop/target
distance itself is poorly matched to changing volatility BEFORE
building a more complex ML model.

## One local command (no NinjaTrader export, API or OpenAI subscription)

Pull this PR, then open PowerShell in C:\Projects\NTStrategies:

    python .\tools\ai_market_atr_bracket_landscape.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\AtrLandscapeV3"

The tool produces:
- es-atr-landscape-v3.json
- es-atr-landscape-v3.md

Send only these reports. Keep original CSVs and NinjaTrader data untouched.

## Strict design decisions

- All simulated timestamps come from TRAIN rows ONLY (prior to
  September 1, 2025). No validation/holdout scoring or model training.
- Select as-of completed 5-minute bars at UTC HH:00, at most one per hour
  per dated ES contract, independent of future results.
- Require all future 60 one-minute OHLC candles even for 30-minute exits.
  Reject incomplete, suspicious or session-boundary observations.
- Compare three PREDEFINED, *non-optimized* brackets on identical
  timestamps for both LONG and SHORT:
    - Fixed 12 ticks stop / 24 ticks target / 30 minutes maximum.
    - ATR14 x0.75 stop / ATR14 x1.50 target / 30 minutes maximum.
    - ATR14 x1.00 stop / ATR14 x2.00 target / 60 minutes maximum.
- ATR stops round UP to ES tick increments, minimum 8 ticks and
  maximum 16 ticks. If either ATR policy exceeds 16 ticks, exclude
  the time FROM ALL policies to ensure matched comparisons.
  A 16-tick nominal stop means around 230 USD including assumed
  one-tick slippage on both sides and five dollars commission;
  gap-through losses remain uncapped.
- Same pessimistic simulator as v2: entry at NEXT 1m OHLC open,
  one tick slippage per side, 5 USD round-trip fee; both stop and
  target reached during the same minute means STOP FIRST.
- The 2023-2024 early training period is described separately from
  2024-2025 late training, with volatility terciles fixed using
  EARLY TRAIN only. This is descriptive, not an independent holdout.
- Outputs all overall and per-dated-contract/session/volatility
  breakdowns, including hypothetical PnL, trade count, profit factor,
  target-first rate, and max CLOSED-equity drawdown.

## What this is and isn't

This study can falsify the simplistic belief that a fixed 3-point ES
stop and 6-point target are always appropriate across volatility
regimes. It does NOT identify a profitable A+ strategy by itself, and
does not optimize thousands of parameter combinations until an edge
appears. The observed sample is clock-scheduled, not a complete
trade-entry signal and not proof any particular timestamp is attractive.

The user's purchased historical tick data is **NOT** yet directly
used here; the exported one-minute OHLC bars are used. Stops
reached within the same minute remain ambiguous and are treated
pessimistically. Actual bid/ask, slippage, news windows, trailing
intraday prop-firm drawdown and broker fill queue must be evaluated
separately.

If adaptive brackets produce an interesting pattern in BOTH early
and late training, the next experiment will pre-register one or
two specific structure/regime hypotheses and train a target-first/
after-cost expectancy model with nested chronological walk-forward,
without tuning on the reused validation or untouched June 2026+
holdout. If results stay negative, consider an alternative entry
hypothesis or instrument, not blindly tune the same series.
