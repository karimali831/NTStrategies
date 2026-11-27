# Ninjex AI v5 — conditional expectancy for structural events

## Evidence that motivated this experiment

The v4 ES structural-event study (train period Sep 2023–Aug 2025)
selected 11,075 non-overlapping setups: -$380,775, -$34.38/trade,
PF 0.635. Matched opposite direction was -$325,462.50,
-$29.39/trade. Both are negative after assumed $30 round-trip
costs (two ticks of adverse slippage + $5 commission).

By class, breakout SHORT was -$36.88/trade versus -$24.84 for the
opposite LONG on identical events, a negative directional difference
that weakens markedly from early to late training. Blindly reversing
these signals is also unprofitable.

The v4 trigger frequency was high (24,778 labelled, 11,075 selected
after 30m spacing), and every overall training month lost money.
Therefore the right next question is whether observable context can
predict the *rare* worthwhile trades within these structure events
and reject the rest, not merely find a higher win rate overall.

## Frozen design (no grid searching)

Reconstruct EXACTLY the prior v4 events and labels (completed 5m
signals, previous twelve 5m highs/lows, 12tick SL / 24tick TP / 30m
time exit, next 1m open, one adverse tick on each side, $5 fee).

Export an additional event-level TRAIN CSV containing only completed
as-of predictors, event types and two hypothetical side outcomes.
The old v4 aggregate metrics remain unchanged.

Fit two fixed, simple, regularized HistGradientBoostingRegressor
models on EARLY_TRAIN events dated Sep 2023–Aug 2024:
one predicts after-cost LONG result, other after-cost SHORT result.
Features are the existing as-of prior OHLCV-derived 5m/15m/60m
indicators, time-of-day sin/cos and one-hot event category.
NO future label or contract identity is an input.

The frozen trade policy for the later Sep 2024–Aug 2025 train
challenge: model must predict at least $20 NET USD in its best
direction AND at least $10 forecast advantage over the opposite
direction, with at most two selected nonoverlapping events per ET
date. Otherwise STAND ASIDE. No retraining on late-challenge data.

Report actual after-cost performance for this policy, plus same-time
always LONG and always SHORT baselines, model forecast errors versus
early-training unconditional baseline, and early-training-cutoff
forecast score bins. Late score bins are descriptive, not policies.

The later period HAS ALREADY been examined in v4 summaries. It is
NOT a pristine "untouched holdout" or independent proof. Reserved
2026-06-15+ holdout is not scored here. Prior Sep 2025–Jun 2026
validation is also excluded to stop further adaptive overfitting.

## Command

Pull latest NTStrategies/master, open PowerShell at
C:\Projects\NTStrategies, then run:

    python .\tools\ai_market_event_meta.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\EventMetaV5"

Please send ONLY:
- es-meta-v5.json
- es-meta-v5.md

Also generated are es-structure-v4-train-events.csv and
es-meta-v5-late-predictions.csv; these are for local audit, not needed
unless we investigate individual trade discrepancies.

No new NinjaTrader export or data download should be required.
The v5 command reruns the same v4 source extraction locally.

## Decision rules after results

- If the fixed +$20 forecast policy selects no trades, this is a
  defensible "no edge" result. Do not lower thresholds merely to
  create trades in the already-seen late period.
- If there are positive trades, review sample size, same-time always
  LONG/SHORT comparisons, forecast calibration, temporal consistency
  and worst drawdown. It remains exploratory and needs FUTURE
  independent validation.
- If it fails, abandon generic 60min high/low structure triggers
  rather than continue tuning them. Next research hypothesis would
  involve higher-information sessions, multi-timeframe regime
  transitions, opening-range/previous-day liquidity, or a different
  holding horizon. Freeze each hypothesis before seeing results.
- All tests use one-minute OHLC, not true tick path, exchange queue,
  real quotes, news filtering, or prop-firm intraday unrealized
  trailing drawdown. Do not trade this output live.
