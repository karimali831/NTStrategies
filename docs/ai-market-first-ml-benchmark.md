# Ninjex AI — first actual offline machine-learning benchmark

This is the first *model training* milestone. Earlier Ninjex AI Market
observations were a deterministic rule-based classifier. The historical
feature CSV is a derived dataset, not a trained model.

The experiment predicts which of three classes the **30-minute future
ES close movement** belongs to, normalized by ATR(14) at the decision
time:

- DOWN if future price move <= -0.5 ATR;
- UP if future price move >= +0.5 ATR;
- NEUTRAL otherwise.

The 0.5 ATR threshold, input features, time splits and model
hyperparameters are fixed BEFORE looking at validation predictions.
No price entry, fill, broker orders or trade PnL is inferred.

## Windows setup (no OpenAI API needed)

Pull the latest NTStrategies master. Open PowerShell in the repository
root. Use the Python interpreter that generated the feature CSV (your
Python 3.14 is supported by scikit-learn 1.8 and later):

    python -m pip install -r .\tools\requirements-ai-market-ml.txt

    python .\tools\ai_market_train_baseline.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\v1"

The tool checks the companion features manifest and fails if the input
counts or split definitions do not match.

Files created:
- es-ml-benchmark-v1.json — metrics and session breakdowns;
- es-ml-benchmark-v1.md — readable validation comparison;
- es-ml-benchmark-v1.joblib — local, non-deployable research models.

Send only the JSON and Markdown report. The joblib file is a local
pickle-family artifact and should not be uploaded from untrusted sources
or loaded by a live execution process.

## Scientific evaluation controls

Trains using dates before Sep 1 2025, compares models on Sep 1 2025
through Jun 14 2026, and NEVER evaluates or uses rows from the
Jun 15 2026 onward holdout. Holdout input rows are counted for schema
verification, then removed before predictor/target construction.

Fixed baselines:
1. prior — constant probabilities based on training class frequencies;
2. sessionPrior — smoothed class frequencies by combinations of
   Tokyo/London/New York session flags, fitted only on training.

Models:
3. logistic — imputed/scaled multinomial logistic regression;
4. histGradientBoosting — a regularized histogram gradient-boosted
   multiclass classifier, no random early stopping.

Metrics on VALIDATION only:
- multinomial log loss and multiclass Brier score (probability quality);
- accuracy, balanced accuracy, macro F1;
- max-confidence expected calibration error;
- observed outcomes by session (overlaps allowed);
- paired day-block bootstrap of model-vs-session-prior log-loss difference.

Model selection uses the lowest validation log loss of the two ML
models, and is reported with its difference from the session baseline.
No automated threshold tuning or A+ classification takes place yet.

**A validation improvement is not proof of a tradeable edge.**
30-minute outcomes overlap at five-minute spacing, are serially
dependent, and do not capture intrabar stop/target ordering. Raw
source coverage still has flagged holidays, sparse 1m minutes and
a few 5m gaps. Treat results as provisional until official calendar
and source issues are reconciled.

Only when we finish the research protocol, freeze model + decision
rules, and assess realistic tick-based execution costs and drawdown
should the untouched holdout be evaluated. The objective remains robust
cross-year performance rather than cherry-picking a period.

The program never accesses OpenAI, NinjaTrader, a brokerage account,
or your production .NET API. It cannot place orders.

## Interpreter compatibility

As of November 2026 scikit-learn 1.8+ publishes Python 3.14 support.
If pip reports incompatibilities, verify which interpreter runs from
PowerShell with python --version and use an isolated Python 3.12 or 3.14
virtual environment rather than modifying NinjaTrader's runtime.
