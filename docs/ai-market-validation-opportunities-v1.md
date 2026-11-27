# Ninjex AI — conditional validation opportunity diagnostics v1

The first trained gradient-boosting model beat the session-only baseline
on 50,988 validation observations. That is useful statistical signal,
but **not** a demonstrated tradeable A+ setup. The next question is
whether it becomes more informative in specific, repeatable market
conditions, and whether its high-confidence predictions are reliable.

This script scores only the existing validation split with the saved
local research model. The untouched June–November 2026 holdout remains
sealed. It creates no broker orders or PnL reports.

## Run locally on your existing ES files

Pull the latest NTStrategies master, then from the repository root:

    python .\tools\ai_market_validation_opportunities.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --model "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\v1\es-ml-benchmark-v1.joblib" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\OpportunityV1"

The command creates:
- `es-ml-opportunity-profile-v1.json`: threshold/group counts and
  realized 30-minute label outcomes, relative to predicted class
  probabilities;
- `es-ml-opportunity-profile-v1.md`: short human-readable summary.

Send only those two files plus console output. Do NOT upload your large
feature CSV or the original purchased data; nothing is modified.

## What it actually measures

Use fixed directional probability cutoffs .45 / .50 / .55 / .60 / .65.
Eligible rows require:
1. clean 1-minute parent coverage and qualityFlags OK,
2. P(direction) >= P(NEUTRAL),
3. P(direction) - P(opposite direction) >= .10.

For each threshold the report displays:
- number of eligible validation observations;
- realized UP/DOWN 30-minute closing-return class frequency;
- mean model-assigned probability of that exact class;
- a deterministic nonoverlapping (>= 30 minutes per contract) sample;
- mean signed 30-minute close change in ATR units, NOT executable PnL;
- breakdown by predicted direction, Asian/London/New York session,
  London/NY overlap, volatility level, trend efficiency and
  cross-timeframe EMA-relative position, and dated contract;
- monthly sample counts to flag temporal concentration.

Volatility and efficiency tercile cutoffs are computed from TRAINING
ONLY, avoiding another validation-tuned threshold. Sessions are made
exclusive for descriptive breakdown; the underlying model already
uses overlapping session flags.

**These thresholds and subsets are diagnostic, not prevalidated
trading rules.** The model was selected on the same validation data,
so picking the highest-performing cohort afterward biases the
reported event rate upward. A separate forward window or properly
predeclared new validation protocol is needed before any final
holdout claim. Even high prediction accuracy does not include
stop-target order, realistic fills, commissions, slippage, drawdown
or prop firm restrictions.

The model file is joblib/pickle-family serialized content. Use ONLY
the trusted file generated on your own machine by the earlier
Ninjex training command; never load an untrusted downloaded model.

If a subgroup contains under 50 observations, it is marked as
`smallSample` and cannot support a confident hypothesis.

## Next milestone

Prioritise one coherent trade setup hypothesis informed by this
diagnostic output, then test stop/target-first-touch labels using the
existing ES tick archive (and explicit bid/ask/fill assumptions).
Only after freezing the protocol and risk constraints should we
evaluate the June–November 2026 holdout. No live trading until
historical execution fidelity and prop-firm risk controls pass.
