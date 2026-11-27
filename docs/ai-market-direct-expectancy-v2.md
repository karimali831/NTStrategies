# Ninjex AI research v2 — predict net trade expectancy directly

The November 27 bracket report found 700 hypothetical ML-directed ES
trades net -$22,187.50 (PF .71, closed-equity drawdown $22,727.50).
Every validation month was negative. The old target (UP/DOWN/NEUTRAL
30-minute closing-price move >=0.5 ATR) predicted direction slightly
better than priors but did not identify executable A+ setups.

We therefore change the SUPERVISED LEARNING TARGET, not merely tune
the existing threshold, trade timeframe or stop/target until positive.

## New experiment

At each clean completed five-minute ES candle in the TRAINING and
ALREADY INSPECTED VALIDATION periods:

1. Simulate BOTH potential LONG and SHORT entries at the next one-minute
   source bar OPEN. This is an idealized research entry, not a broker fill.
2. Use the EXACT SAME fixed conditions from bracket v1: 12 tick stop,
   24 tick target, 30m maximum, +1 adverse tick per side, $5 round-trip
   commission and one ES contract ($50/point).
3. Require all 30 future minute-close bars to exist. No inferred ticks,
   fabricated candle, no re-import; ambiguous stop+target in the same
   minute always counts as STOP.
4. For every eligible decision, generate TWO continuous labels:
   simulated after-cost net USD for LONG and for SHORT. Also record
   actual first-hit TARGET flags, exit reason and ambiguity.
5. Train separate gradient-boosted REGRESSORS on TRAINING only to
   predict hypothetical net USD for LONG and SHORT using the existing
   closed 5m/15m/60m as-of feature allowlist plus Asia/London/NY flags.
   Never use a future label or contract identifier as an input feature.
6. On prior-used VALIDATION, choose the side with higher predicted net,
   but DO NOT enter at all unless max predicted net is >$20. At most one
   hypothetical entry per 30-minute contract window. These are fixed
   research-policy constants, not tuned to achieve a desired result.
7. Compare the chosen direction on identical timestamps against
   always-long, always-short and prior-12-bar momentum directions.
   Output net USD, PF, win rates, closed-equity DD and per-contract/
   session breakdown, or zero trades when ML correctly declines.

This version does not train a calibrated probability that TP will
precede SL. It directly estimates expectancy; target-before-stop flags
are kept for a subsequent classifier experiment if justified.

The validation data have already been inspected and used for model
selection, so its new results remain EXPLORATORY. We will not claim a
profitable edge solely on its apparent performance. June 15 2026
onwards remains the unopened holdout.

## One command in Windows PowerShell (repo root)

Pull latest NTStrategies/master then run:

    python .\tools\ai_market_direct_expectancy.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\DirectExpectancyV2"

It reads existing local historical data and writes:
- es-direct-expectancy-v2.json — full diagnostics and model comparisons;
- es-direct-expectancy-v2.md — concise human-readable report;
- es-direct-expectancy-v2-decisions.csv — selected hypothetical entries;
- es-direct-expectancy-v2.joblib — local-only fitted research regressors.

Send only JSON and Markdown, keeping historical CSVs and joblib local.

## Research / risk limitations

- Correctly predicting hypothetical *average PnL* does not guarantee a
  profitable trading system. We need sustained cross-year robustness,
  risk limits and completely untouched test data later.
- The short/long outcomes share future bars but are labels only and
  never input features.
- Label windows which cross an ET split boundary are purged. Rows with
  incomplete parent 1m or any missing future minute are dropped. No
  historical price data is modified or interpolated.
- One-minute OHLC stop-first is deliberately conservative; actual
  intrabar price ordering and bid/ask fills require source tick replay.
- One contract costs/slippage and 12/24 tick brackets remain research
  assumptions, not a calibrated live simulation or optimization.
- Maximum closed-equity DD is NOT an intraday or trailing prop drawdown.
  No live orders or NinjaTrader trading actions are possible.
- If the model declines to trade or loses across validation, keep that
  negative result; do NOT hunt on the same validation for a winning
  filter. Instead reconsider setup definitions, timeframes and labels
  and run a genuinely new forward validation protocol.
