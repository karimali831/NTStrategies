# Overnight Edge Portfolio: live/replay audit, October 2026

Audit date: 2026-11-01. Instrument: ES 12-26, one contract. Time stamps are ET, as recorded by the strategy diagnostic files. This is a **historical execution audit**, not a performance calibration or trading-rule change.

## Sources and scope

- User-supplied `MergedTrades_Historical_LiveT1_Template_17_Sept_25_to_29_Oct_26.csv` (279 replay trades from September 2025 through 29 October 2026; no reported commissions).
- User-supplied `NinjaTrader Grid 2026-11-01 03-24 AM Live.csv` (12 completed actual-account ES trades, 1–29 October 2026).
- User-supplied `OvernightEdgePortfolio - Live.zip` (version `1.4.0-run8-production` logs; individual log files can contain historical processing or retrospective runs).
- Current `master` implementation in `Core.cs`, `Orders.cs`, `Risk.cs`, `Signals.cs`, and `VerifiedLiveOverrides.cs`.
- Row-level reconciliation: [overnight-edge-verified-live-2026-10.csv](overnight-edge-verified-live-2026-10.csv). Account identifiers are intentionally omitted.

The account export starts on October 1, so September **actual** executions cannot be reconciled against the replay with these inputs. This is not evidence of no September live trades.

## October 1–29 reconciliation

| Metric | Replay | Actual account |
| --- | ---: | ---: |
| Completed trades | 13 | 12 |
| Wins / losses | 4 / 9 | 4 / 8 |
| Gross USD | -25.00 | +137.50 |
| Reported fees USD | 0.00 | 37.20 |
| Net after reported fees USD | -25.00 | +100.30 |

There are 11 minute-and-direction-matched trades, one account-only trade, and two replay-only trades. Matching uses entry date, minute, instrument and direction; the account trade report has blank strategy and signal names.

Dollar reconciliation from replay gross `-$25.00` to actual account net `+$100.30`:

1. Two Oct 26 replay-only stop losses missing from actual account: `+$500.00` difference.
2. Oct 1 at 13:15 account-only short stop loss missing from replay: `-$250.00` difference.
3. Matched trade execution variance: `-$87.50` (Oct 15: `-$25`, Oct 20: `-$12.50`, Oct 23: `-$50`).
4. Actual commissions: `-$37.20`.

Thus `-25 + 500 - 250 - 87.50 - 37.20 = +100.30`. Other one-tick entry and exit shifts on matched trades offset and did not alter gross P&L.

### Material exceptions

- **Oct 23, 15:56 short**: replay entry 7660.50 / exit 7661.00, grid exit labeled `Stop loss`, `-$25` gross. The **original production** instance `833e66f8cc03` records entry 7660.50 and an explicit `FLATTEN SHORT` at 16:00:00.002 followed by an **`EOD-S`** fill at 7662.00 at 16:00:00.226, `-$75` gross. The 4-tick mismatch is real, but calling it stop-loss slippage is incorrect: the actual exit was the end-of-day market flatten. The replay's `Stop loss` label at 16:00:01 is inconsistent with this cause, and its `$600` MAE is not independently validated. The strategy permits entries until 16:00 while also flattening at 16:00.
- **Oct 1, 13:15 account-only loss**: the account shows a third short trade (`-$250` gross) after two `+$500` gross winners. Current defaults cap the strategy at **two winners per day**. The account report has no strategy attribution and no date-matched production log. Determine if it was the strategy, a different strategy, a copied/manual order, or an older/restored instance before classifying this as a risk-cap defect.
- **Oct 26, 09:50 and 09:57 replay-only losses**: two replay `PMH-REJECT-S` losses totaling `-$500` gross are absent from the actual account export. The Oct 26 log copies were **created on Oct 27 and Oct 30** and show retrospective executions; they cannot prove that these orders executed on the actual account Oct 26 or that the live strategy was enabled then. Require original VPS/NinjaTrader uptime and order logs.

Live trade performance's MAE equals MFE and equals the absolute gross trade result for **all 12 account rows**, so do not use its MAE/MFE fields as reliable intratrade excursion data without tick-level independent verification.

## Since live deployment: 14 September–29 October replay

There are 25 replay trades on 15 of 34 weekdays, `+$112.50` **gross**, nine wins and 16 losses. Replay's zero commission column is not a real net result: at the observed `$3.10` commission per trade, the illustrative net would be `+$35.00`, before any additional slippage. The replay's maximum closed-trade peak-to-trough drawdown within this post-deployment slice is `$1,025` gross.

| Entry model | Trades | Wins | Gross USD |
| --- | ---: | ---: | ---: |
| Prior-day-close reclaim | 4 | 3 | +1,250.00 |
| Premarket-high rejection | 8 | 0 | -1,987.50 |
| Premarket-low breakdown | 10 | 5 | +875.00 |
| RTH-open breakdown | 3 | 1 | -25.00 |
| **Total** | **25** | **9** | **+112.50** |

All eight post-deployment PMH rejections lost, while the other three models together contributed `+$2,100.00`. Before Sept 14, the same replay CSV contains 87 PMH entries, 37 wins, and `+$6,012.50` gross. This is a meaningful model-level deterioration; the small recent sample does not establish whether the cause is regime change, historical overfitting, a data difference, or code-version drift.

The last 25-trade `+$112.50` gross result is among the weakest ~7% of **overlapping, in-sample** rolling 25-trade windows in the same replay export (17 of 255 windows at or below). This is descriptive, not an independent significance test.

## Trade frequency

October 1–29 had 13 replay trades across 9 trading days of 21 weekdays. The Oct 2–Oct 15 interval has eight intervening weekdays with no replay trades. Longer historical no-trade gaps occur in this same file (15 weekdays from Dec 18, 2025 to Jan 9, 2026; ten in May–June 2026), so sparse trading by itself does not demonstrate an inactive strategy.

The 25 most recent trades span 42 calendar days. Prior overlapping 25-trade windows had a median span of ~29.5 days, but about 31% were at least this long. Frequency is slower than the median, not historically exceptional. The *recent outcomes*, especially the PMH streak, are the more material concern.

## Relevant code/configuration and deployment distinctions

- `Core.cs` sets `Calculate.OnEachTick`, primary five-minute context plus one-minute signals and tick execution, 20-tick initial stop, 40-tick target, 60-minute maximum hold, 3 trades/2 winners/2 losses daily caps.
- `EntryEndTime` and `FlattenTime` both default to `160000`. Oct 23 proves a position can be opened four minutes before compulsory flatten.
- `Signals.cs` applies a PDC early-premarket width condition, PMH first-120-minute ATR condition, PML raw-close-above-EMA condition, and an RTH-open breakdown no earlier than 120 minutes from open. These selective crossings naturally create quiet days.
- Repository history shows restart-recovery changes on Oct 1–2, a PDC early width filter added Oct 7, and Run 8 modularization/freeze Oct 10. The current replay version may therefore not match the binary that originally produced every early-October live trade. Recover the exact deployed build/config by date before alleging a regression.
- Log files are separated by `CreatedUtc` and strategy instance. A filename's trading date is **not** sufficient to establish real-time production origin. Some Oct 15/16/22/23/26 entries from later-created instances differ materially from original-day account fills.

## Integrity boundary for VerifiedLiveOverrides

`VerifiedLiveOverrides.cs` currently permits verified RTH-open reference corrections and **price-level bracket** overrides keyed to signal timestamp. It is disabled by default. It does **not** rewrite arbitrary entry/exit execution fills, mark account-only executions as strategy trades, or represent the exchange's slippage and `EOD-S` market close.

This audit therefore records verified actual executions **separately**. It deliberately does **not** move a protective stop to a worse fill price, convert an EOD close into a stop, invent October 26 actual executions, or change future trading behavior to make a past replay P&L look equal to live.

## Recommended follow-up, in order

1. Export **Orders** and **Executions** for Oct 1, 23 and 26, plus original production uptime/start-stop logs; verify the Oct 1 third-trade source and Oct 26 missing real-time trading window.
2. Reproduce Oct 23 in one-tick market replay with matching strategy binary/config and the same session start/loaded bars, inspect exact stop-market and EOD order submission/fill sequencing, and compare market trade/bid/ask ticks around 16:00. Do not use the inconsistent replay MAE field to diagnose actual slippage.
3. Add a separate candidate entry cutoff experiment (15:30/15:45 ET, model-specific if appropriate); replay across all contracts and estimate impact on the full sample and drawdown before any deployment. Excluding all historical entries at/after 15:45 would also have removed profitable trades; do not adopt a cutoff solely from Oct 23.
4. Investigate PMH rejection's eight-loss sequence with per-signal research telemetry and market regime variables. Do not disable PMH just because the recent slice is poor: its earlier out-of-period contribution was positive.
5. Add immutable release SHA/config, UTC creation time, order IDs, order stop/limit levels, exchange fill timestamps, daily process heartbeat and session coverage into future log audits. Keep true live execution records distinct from synthetic historical processing.
6. Obtain September 14–30 live execution exports if a complete since-go-live realized P&L comparison is required.

**No production strategy logic or existing verified bracket override has been changed in this audit.**
