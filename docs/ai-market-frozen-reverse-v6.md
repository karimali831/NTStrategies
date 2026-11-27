# Ninjex AI v6 — frozen reversal falsification, no new strategy

## Why

Uploaded v5 trained on 5,438 early structural event timestamps and
challenged 5,637 later TRAIN events. The frozen policy selected 59
entries, -$2,357.50 (-$39.96/trade, PF 0.67, closed DD $2,447.50).
At those exact events, always-LONG was -$1,907.50, always-SHORT
was +$1,267.50 and reversing v5 ML direction was +$1,717.50
(PF 1.32, closed DD $1,362.50). This is a small, low-frequency,
post-hoc observation, NOT a discovered countertrend trading edge.

In v5, the top 244 most optimistic ML-scored events (using frozen
EARLY training score bins) predicted +$15.76 mean but realized
-$26.93 mean, and model error was almost identical to an
unconditional early-training mean predictor.

V6 is a one-shot experiment to *falsify* the apparent model inversion
rather than claim that changing the sign creates profit.

## Strict, frozen forward protocol

- Rebuild v4 training-only event CSV using the existing source dataset.
- Refit EXACT SAME v5 two HGBRegressor models only on 2023-09 through
  2024-08 EARLY_TRAIN events. Later TRAIN is not used to fit.
- No model retraining, feature addition, stop/target alteration, risk
  parameter sweep or signal threshold change.
- Extract the SAME four v4 completed-5m, prior 12-bar breakout and
  rejection triggers from the **already used**
  2025-09-01 through 2026-06-14 ET validation period.
- Reject bad source quality, missing historical context, source
  mismatches and incomplete future 30 x 1m OHLC paths. Exclude
  any trade outcome spanning 2026-06-15 Eastern.
- Unmodified v5 policy: best direction model forecast >=+$20
  net USD, >=+$10 over opposite direction, max 2 per Eastern day,
  all event families combined with 30-minute spacing.
- Simulate the SAME 59-trade policy family on new dates:
  * original v5 model direction;
  * **opposite v5 model direction**;
  * always LONG at the same timestamps;
  * always SHORT at the same timestamps.
- Fixed one ES, 12-tick stop, 24-tick target, 30-minute exit,
  next minute OHLC open, one adverse tick on each side, $5 round trip,
  stop FIRST if both levels touched in the same minute.
- Report monthly, contract and session performance, matched baseline
  comparisons and day-block bootstrap confidence intervals for
  reversed net and reversed-versus-always-short mean differences.
- No filters are selected after examining these results.

The June15 2026+ holdout is not scored, and raw OHLC data from it
is not decoded: the CSV source reader stops at the first holdout
timestamp even on the 09-26 rollover. The 2025-09–2026-06
validation period was previously inspected in v1-v2, so v6 results
are NOT genuinely independent out-of-sample confirmation.

## Run one command in Windows PowerShell

Pull latest NTStrategies/master, then from C:\Projects\NTStrategies:

    python .\tools\ai_market_frozen_reverse_v6.py --features "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\es-2023-2026-ml-features-v1.csv" --audit "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json" --output-dir "C:\Users\Administrator\Documents\Ninjex\AiMarket\Derived\Benchmarks\FrozenReverseV6"

Send:
- es-frozen-reverse-v6.json
- es-frozen-reverse-v6.md

A local selected-decision CSV and a v4 training rebuild are also
saved for detailed audit; no NinjaTrader historical rerun necessary.

## What will count as interesting?

Reversed ML signals would need POSITIVE after-cost net returns,
reproducible spread across multiple chronological quarters,
and an advantage over ALWAYS-SHORT at IDENTICAL timestamps. One
or two winning months or 20–50 trades are not enough. A positive
reversal could still be weak and far below the goal of >=$10k/year
within around $2k total drawdown.

A negative or underwhelming result means RETIRE this simple
60-minute-breakout/rejection event-conditioned AI trade generator.
Do not change thresholds or tweak its direction on the same
previously used validation. Instead pre-register a different,
information-rich ES setup or collect genuinely new forward data.

The script NEVER sends NinjaTrader orders. Its max CLOSED equity
drawdown is not unrealized/intraday trailing prop-firm drawdown.
Historical one-minute OHLC, artificial slippage and commission
cannot certify tick-accurate execution, spread, order latency,
real broker fees or news compliance.
