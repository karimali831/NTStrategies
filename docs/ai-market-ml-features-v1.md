# Ninjex AI: first session-aware ML research dataset

This is the first OFFLINE FEATURE ENGINEERING step after the 13-contract
historical ES basket export. It does not place orders and does not
train an ML model yet.

## One local command after collecting all contracts

Pull this PR. You need Python 3.10+ and the tzdata package on Windows.
The historical source files stay exactly where NinjaTrader exported them.
Open PowerShell in the NTStrategies repository root:

    python -m pip install tzdata

    python .\tools\ai_market_ml_features.py --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv"

This reads the 13 original dated-contract folders listed in the
existing full-basket audit JSON. It writes ONLY to the separate Derived
directory, never edits purchased historical data, and produces:

- es-2023-2026-ml-features-v1.csv
- es-2023-2026-ml-features-v1.manifest.json

Send just the manifest JSON and the printed [NinjexAI-ML] summary.
There is no need to upload the multi-GB original source archives.

## Research policy

The report is marked PROVISIONAL_MASKED_NOT_TRAINED. The full basket
audit still needs to resolve expected holiday closures and several
actual source gaps before claiming complete data certification.

For the v1 feature dataset:

- One row per usable completed 5m decision candle; observed raw data
  only, no imputed or forward-filled OHLCV data.
- The model's as-of higher timeframes are the last COMPLETED 15m/60m
  bar, never a forming 60m bar or a future completed candle. Each is
  verified by exact OHLCV reconstruction from historical 5m candles.
- Odd-session partial 60m closes and source 15m/60m OHLCV mismatches
  are excluded from higher timeframe features, without erasing the
  underlying observed 5m records.
- There is a conservative 12-bar exclusion window after any break
  in 5m continuity. This includes exchange maintenance and holidays.
- 30m and 60m forward directional/magnitude labels are calculated only
  when ALL future 5m closes are contiguous and in the same research
  split. No trade fills, stop-vs-target order or PnL are inferred.
- Sparse one-minute source bars are NOT filled; the enclosing usable 5m
  row is retained with fine1mComplete=0 and qualityFlags tagged.
- Asia is Tokyo 09:00-17:00 local; London 08:00-16:30 London local;
  New York 09:30-16:00 Eastern. Sessions can overlap and use real
  time-zone/DST rules. These are explicit hypotheses, not learned edges.
- Time-based splits use Eastern date and DO NOT shuffle:
  train before 2025-09-01, validation from 2025-09-01 to 2026-06-14,
  untouched holdout from 2026-06-15 onward. Labels at split boundaries
  are purged if their outcome would cross into a future split.

## Next stage: learn, measure and challenge discovered edges

The next PR should fit an initial baseline (e.g. LightGBM or CatBoost)
to these features and evaluate, on entirely unseen periods:
- horizon-based directional probability and calibrated reliability;
- differences by Asia/London/New York, volatility, trend and range;
- net expectancy of *fully specified* candidate trade rules;
- false-discovery / data-mining checks, drift and out-of-sample stability.

The v1 labels are RESEARCH returns, not proven executable trades. Tick
data, real spread/slippage, order latency, and per-contract fill
simulation are still required before concluding any strategy is viable.

No user OpenAI subscription or paid API key is needed to build features
or run classical ML training locally.
