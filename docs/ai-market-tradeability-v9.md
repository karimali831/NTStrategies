# Ninjex AI v9 — measure available opportunity before developing another AI signal

## Evidence from the supplied v8 result

The frozen v7 upper-opening-range rejection hypothesis did not
generalize: LONG after the first REJECT_SHORT, 42 entries in
Sep 2025–Jun 2026, **-$1,910 net, -$45.48/trade, PF 0.6341,
max CLOSED DD $2,430**. The same-time SHORT made -$760, PF
0.842. Among 40 event days with enough same-contract/same-clock
controls, the upper-rejection LONG underperformed comparable
other-day LONG opportunities by -$19.58/entry. Descriptive interval
[-$77.03,+$49.65] is inconclusive and understates control uncertainty.

It was +$3,027.50 on 82 earlier TRAIN entries only because
REJECT_SHORT->LONG was selected post hoc from v7 categories. V8
falsifies that candidate. Neither direction met the aspiration of
>= $10k per year with ~$2k prop-firm intraday drawdown.

We are retiring v4-v8 hypotheses rather than reinterpreting each
failed pattern with reversed trades or a tuned stop/target.

## This is intentionally not a ninth candidate strategy

Research should answer a more fundamental question: **Does the ES
5m/1m historical opportunity distribution contain enough after-cost
movement for a selective directional strategy with this stop/target
and holding duration to be plausible at all?**

Without fitting a model or looking for special patterns:

1. Use a strict deterministic schedule of each completed 5m bar
   closing 10:00, 10:30, ... 15:30 America/New_York. One decision
   every 30min, up to 12 per market day. This is one nonoverlapping
   RTH baseline sample, regardless of signal direction or volatility.
2. Only TRAIN dates before Sep 1 2025 ET; require the complete
   derived 5m as-of record and reconciled original dated ES 5m
   close. Reject source-quality issues and incomplete 30m 1m paths.
3. At the identical entry timestamps calculate hindsight LONG and
   SHORT with the already-frozen 12tick SL/24tick TP/30minute exit,
   next observed 1m open, 1tick adverse slippage on EACH side and
   $5 round-trip commission.
4. Also calculate the higher of the two realized returns, purely as
   a **clairvoyant, non-executable oracle ceiling**. Never treat
   this as an AI model or evidence that real trading could choose
   the winner ex ante. Quantify how frequently both sides lose
   after costs even with this fixed bracket.
5. Compare raw 30-minute range, absolute close displacement,
   gross-vs-net economics, earlier versus later TRAIN years, three
   fixed RTH time periods and fixed ATR14_5m volatility bands.
   These are DESCRIPTIVE GROUPS, not optimized model features,
   profit-picking signals or deployable filters.
6. The $30 assumed round-trip cost removes $30 expectancy per
   entry. If a 200-trade/year system is sought, the $10k annual
   goal requires $50 mean AFTER-COST net per trade at that
   frequency; leverage and drawdown must also be audited separately.

No ML is trained, no adaptive candidate filters are selected, and
neither previously inspected validation nor the reserved final
holdout is scored. The original raw 1m and 5m readers STOP before
Sep 1 2025 NY local; original data are never modified.

## Run v9 locally on the existing source dataset

Pull latest NTStrategies/master; in PowerShell at
C:\Projects\NTStrategies:

    python .\tools\ai_market_tradeability_v9.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\TradeabilityV9"

Send only:
- es-tradeability-v9.json
- es-tradeability-v9.md

es-tradeability-v9-decisions.csv is a supplementary local timestamp
audit file. No NinjaTrader rerun or tick export is required.

## Research interpretation and next project work

- Oracle profit proves nothing about an ex-ante signal; a market
  may contain many large moves that no available as-of feature can
  predict.
- If both sides lose frequently, the fixed 12/24tick 30m bracket
  is an expensive economic hurdle even for correct market timing.
- If a considerable share of timestamps has a profitable side,
  there is still NO edge: that may warrant a separate, causally
  hypothesized directional-prediction study using different as-of
  information such as prior-day RTH inventory, overnight positions,
  opening drive, volume distribution, or true market order flow.
- Do not use ATR/time bins from THIS v9 report to choose a
  profitable setup retrospectively.
- Distinguish current historical research OHLCV from the user's
  original NinjaTrader imported tick data. The existing NinjaScript
  exporter deliberately produces 1m/5m/15m/60m OHLCV, not ticks or
  bid/ask aggressor data. Future execution validation and new
  order-flow hypotheses would require a separate, selective
  tick/quote provenance path.
- Historical output's CLOSED equity drawdown cannot certify
  prop-firm intraday unrealized drawdown, scale sizing, news-event
  rules, or broker fills.
- There is NO guarantee three years of historical price data can
  yield a genuine profitable strategy. Keep the reserved June15+
  2026 holdout sealed until an independently preregistered
  sufficiently convincing candidate exists.
