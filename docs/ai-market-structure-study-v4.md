# Ninjex AI v4 — structural event study (training history only)

## Why we are changing direction

The v3 ATR bracket landscape over Sep 2023–Aug 2025 used 7,288
matched hourly training decisions: fixed 12/24 ES and two ATR-adjusted
variants were all negative both long and short. The 7,288 sample
excluded 3,210 decisions for the stop-size risk cap and 547 for missing
future minute candles; it therefore cannot describe the whole market.

Our first directional ML classifier did improve log loss slightly, but
its bracket simulation lost money. Our second expectancy ML model
identified ten exploratory LONG entries, +$450, IDENTICAL to buying
at the same timestamps; no directional model advantage.

The rational next question is not "which stop/target will win?" but
"does a specific, observable price-structure event carry a repeatable
directional edge after realistic hypothetical costs?"

## New frozen hypotheses

Each signal is evaluated at a COMPLETED 5-minute bar. Use the previous
twelve consecutive completed five-minute bars (60 minutes) for levels.
The current triggering candle MUST NOT be part of the level.

- BREAKOUT_LONG: current close >= prior 12-bar HIGH + one ES tick and
  current close > current open; mirrored SHORT breaks previous LOW.
- REJECTION_SHORT: current high >= previous HIGH + one tick, but
  current close <= previous HIGH - one tick and close < open.
  Mirrored REJECTION_LONG sweeps previous LOW and closes back above it.
- If more than one definition matches, skip ambiguous triggers.
- If previous 12 historical bars are not contiguous, skip the signal.
- Require clean as-of feature observation and identical 5m closing
  price in derived source and raw dated-contract export.
- Select the first valid event chronologically, enforcing at least 30
  minutes between ALL event types within the dated contract. This is
  not a separate four-system portfolio trading concurrently.

Each event is simulated in the structural direction AND the opposite
direction at exactly the same timestamp. Uses same fixed 12tick stop /
24tick target / 30-minute max exit, next 1m open, 1tick adverse
slippage on entry+exit, $5 roundtrip fee, one ES contract.
Any incomplete future 30-minute path excludes the event; if target
and stop occur within the same minute, assume stop first.

Reports counts, mean after-cost PnL, win rate, profit factor, closed
equity drawdown, matched opposite-direction PnL, paired differences
with descriptive trading-day bootstrap, plus contrasts across early
(through Aug 2024) vs late training (Sep 2024–Aug 2025), session and
dated contract.

Nothing is optimised and no ML model is trained at this stage. A
favourable subgroup would remain a *hypothesis*, not proof.

## One local command

Pull latest NTStrategies master, then run from C:\Projects\NTStrategies:

    python .\tools\ai_market_structure_events.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\StructureV4"

Output files:
- es-structure-v4.json
- es-structure-v4.md

Only these two are needed for review; existing huge source ZIPs and
training datasets need not be uploaded. No NinjaTrader backtest or
export must be repeated.

## Scientific and trading limits

- Only TRAIN rows strictly before Sep 1 2025 ET are scored. The
  existing Sep 2025–Jun 2026 validation and final 2026-06-15+ holdout
  remain entirely unused for this investigation.
- Historical one-minute OHLC does not contain full tick sequence,
  bid/ask, order-book conditions, latency, prop-firm trailing
  drawdown or news exclusion. The stop-first assumption only
  addresses within-minute uncertainty, not all execution issues.
- There is NO guarantee this sample produces a profitable setup.
  Candidate statistics may change materially with exact market
  replay/tick execution and higher/lower timeframes.
- Chosen vs opposite on the SAME events is a stronger check of
  direction than comparing to random hourly entries, but selection
  of a favourable family after viewing results still creates
  multiple-testing bias. Do not promote a winner on TRAIN alone.
- The output's closed-equity drawdown is not the user's ~$2,000
  intraday unrealized trailing drawdown risk measure.
- The program never places trades, connects to NinjaTrader,
  mutates imported data, or calls the OpenAI API.

If the event study is promising consistently across early/late
training, NEXT define one candidate before using untouched/new
chronological validation with tick-accurate execution verification.
If not, abandon these event definitions and research a different,
causally motivated setup rather than exhaustively searching for
profitable thresholds.
