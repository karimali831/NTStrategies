# Ninjex AI — experimental ES one-minute bracket-path research

The current classifier cannot be called an A+ trade setup. Validation
profiles show 1,414 candidates with P(direction)>=.45, only 58 with
P(direction)>=.50, and none with P(direction)>=.55. Prediction of
30-minute close movement is not the same thing as reaching a take-profit
before a stop loss.

This read-only offline script tests a single fixed hypothesis rather
than optimizing hundreds of stop/target variations. It uses ONLY the
previously used validation split. The June 15 2026+ holdout remains
unscored. Its conclusions will be exploratory and require independent
confirmation later.

## Local PowerShell (one line, from NTStrategies repo root)

    python .\tools\ai_market_bracket_research.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --model "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\v1\es-ml-benchmark-v1.joblib" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\BracketV1"

The script saves:
- es-ml-bracket-research-v1.json (aggregate metrics);
- es-ml-bracket-research-v1.md (readable summary);
- es-ml-bracket-research-v1-trades.csv (hypothetical timestamped traces).

Send just JSON and Markdown unless an individual timestamp needs review.

## Predeclared experimental bracket

- Signal: fixed ML directional probability >=0.45 with a 0.10
  margin against the opposite direction, plus clean source quality.
- Minimum 30 minutes between selected signals per dated contract.
- Entry: next 1m historical OHLC bar open after the completed 5m
  decision, with one ES tick (0.25 points) of adverse slippage.
- Stop: 12 ES ticks (3 points); target: 24 ticks (6 points),
  both measured from the raw next-bar open; one more adverse tick
  on exit; $5 round-trip commission; one ES ($50/point).
- Maximum 30-minute holding period.
- The normal stop is ~$180 net of the assumed friction;
  target ~$270. Gap-through stop can lose more.
- Stop/target touched within the same 1m bar = pessimistic STOP first.
- Any missing future one-minute close in the 30m path = trade excluded.
- Compare ML, always-long and sign-of-12-bar-momentum directions,
  on the EXACT SAME decision timestamps.
- Report win rate, net USD, profit factor, monthly/session/contract
  distribution, max CLOSED equity drawdown and ambiguous minutes.

## Scientific limits

The model and the confidence threshold have already been examined
using the same validation dataset. This analysis is not out-of-sample
evidence of a profitable trading strategy. One-minute OHLC bars
cannot establish true intrabar stop-vs-target sequence or market
fill prices, and neither quote spread, queue, order latency, exchange
rejection, extreme gaps nor news blackouts are fully modelled.

Commission/slippage are ASSUMPTIONS, not verified live broker costs.
Closed-equity drawdown is not intraday unrealized prop-firm trailing
drawdown; the user's approximately $2k drawdown target therefore
cannot yet be certified.

The code does NOT place live, playback or simulated NinjaTrader orders.
It does NOT load or score the 2026 holdout. Nothing is changed in the
original, costly imported NinjaTrader historical database.
