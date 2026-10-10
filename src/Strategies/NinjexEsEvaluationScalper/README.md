# Ninjex ES Evaluation Scalper v1.1

Research baseline for **$3,000 net profit before $2,000 trailing drawdown within 21 calendar days**, targeting a >=50% rolling-start pass rate. That target is **unvalidated** until the replay results are analyzed. It is a frequent retail scalper, not exchange HFT. The existing Overnight Edge strategy is untouched.

## First profile

| Setting | Baseline |
|---|---|
| Instrument / chart | Individual ES contract; 1-minute primary |
| Calculation | OnEachTick; ordered 1-tick secondary stream drives signals |
| Entries (Eastern, EST/EDT) | 09:35–11:30 and 13:00–15:30; flat outside those windows |
| Size | Up to 2 ES; configurable 1–3; reduced by risk budget |
| Planned risk / trade | $400 including estimated round-trip commission |
| Stop | Sweep/retest extreme plus 2 ticks; floor 12 ticks or 0.75 × completed-minute ATR, whichever is wider; ceiling 28 |
| Target | 1.5R; no break-even or trailing stop in the initial baseline |
| Daily limits | $650 loss / $1,200 profit; unrealized liquidation equity included for flattening |
| Frequency controls | Up to 12 submitted entries/day; 90 seconds between closed trade and next entry |
| Maximum hold | 300 seconds |
| Estimated fees | $2.50 per contract per side; replace with your actual costs before the full run |
| `EnforceEvaluationLimits` | **False for the continuous year-long research run** |
| Live accounts | Disabled by default; Playback/Sim accounts permitted |

Position size also fits the remaining daily loss budget. The optional evaluation guard uses a $1,900 intraday trailing trigger, reserving $100 before the requested $2,000 boundary; it locks entries after that trigger or a flat realized $3,000 net profit. This is a strategy-local generic profile, not broker account monitoring, firm-specific EOD/capped trailing rules, or a fill guarantee. Enable it for a single evaluation attempt, not the continuous baseline. It resets on a new strategy instance; restarting mid-evaluation does not preserve the account's prior high-water mark.

## Signals and as-of context

- **Sweep reclaim:** a sweep of at least 3 ticks requires a completed rejection candle. Its rejection wick must occupy at least 35% of the range, and the close must be in the favorable 35% of the range and at least 2 ticks back beyond the level. Price must then hold the reclaimed side for 2 seconds and advance 2 more confirmation ticks. The extreme sets the structural stop. Setups expire after 180 seconds.
- **Momentum retest:** a completed displacement candle must cross and close at least 4 ticks beyond the level, with body >=4 ticks and >=0.5 × prior completed-minute ATR, occupying at least 60% of its range. Only a later retest within 2 ticks and a 4-tick bounce qualifies. Completed-minute EMA9/EMA21 direction and session VWAP must agree with the direction.
- Levels: previous RTH high/low/close, overnight high/low, premarket high/low, completed first five-minute opening range, and the previous five completed consecutive RTH minutes' high/low.
- Prior RTH requires observed open and close; overnight requires the previous evening's 18:00 start. Missing/incomplete context is omitted and logged. Recent levels use completed minutes, never a future bar's high/low.
- Candidates are detected while other trades are open too. Each records whether it was submitted or blocked, with stop, size, context and reason. When several levels qualify together, the first eligible candidate gets the position; the rest record `Occupied`.
- A per-level 180-second reuse limit prevents repeatedly trading the same static level. Rolling levels can refresh on each completed minute.

## Platform setup and short test

1. Copy **all seven `.cs` files in this folder together** into an NT8 NinjaScript strategy folder, or build/sync through the existing repository workflow. Compile in NinjaTrader. The `.csproj` includes each production file explicitly; test doubles under `tests/` must never be copied into NinjaTrader.
2. Set NinjaTrader Tools → Options → General → Time zone to **Eastern**. Use **CME US Index Futures ETH**, a **1-minute** ES chart, and **Do not merge** contracts. Tick Replay is not required for Market Replay. Use sufficient loaded days for the previous RTH and overnight warm-up.
3. Connect Playback with historical tick data. Start flat on **Playback101**, independently of the live Overnight Edge setup. Remove/re-add `NinjexEsEvaluationScalper` and reset to the **v1.1 defaults**; previously saved strategy templates can retain v1 risk values. Keep `EnforceEvaluationLimits=False` and `ExportRawTicks=True`.
4. First short test: **ES 12-25, September 16–19, 2025**. Begin playback at September 15, 18:00 ET, set `FirstTradeDate=2025-09-16`, `LastTradeDate=2025-09-19`. Let each session run through at least 16:00. Start at normal speed to check orders, then increase speed once brackets/fills are verified.
5. Confirm actual entries have stops and targets, no rejected orders, flat positions outside windows, one completed trade row per round trip, and a finished export folder. Check Orders/Executions and the CSV ledger agree on quantity, price and configured fee estimates.
6. Disable the strategy **after it is flat** to finalize the compressed files. Zip the entire `Documents\NinjaTrader 8\NinjexData\ES_Evaluation_*` folder and send it for review. Include NinjaTrader Log/Output and the strategy trade-performance export with commissions if available. A four-day test verifies mechanics, not the three-week pass rate.

**Historical warm-up never submits orders.** Actual entries are submitted only in `State.Realtime`, which includes Playback. Accordingly, Strategy Analyzer historical-only runs produce context/candidate observations but **no trades**. Managed SetStopLoss/SetProfitTarget brackets submit on the first Bars context (0), consistent with NT guidance; the added tick stream (1) drives decision timing. This implementation is intended for the requested Market Replay workflow.

## Full replay allocations

After the short test passes, use disjoint trade-date allocations with prior-evening warm-up:

| Contract | Trade dates | Warm-up evening |
|---|---|---|
| ES 12-25 | 2025-09-15 → 2025-12-12 | 2025-09-14 18:00, if available |
| ES 03-26 | 2025-12-15 → 2026-03-13 | 2025-12-14 18:00 |
| ES 06-26 | 2026-03-16 → 2026-06-12 | 2026-03-15 18:00 |
| ES 09-26 | 2026-06-15 → 2026-09-11 | 2026-06-14 18:00 |
| ES 12-26 | 2026-09-14 → latest available completed session | 2026-09-13 18:00 |

The requested endpoint is October 9, 2026 at implementation. On the earliest available September 15 data, missing September 14 overnight coverage means overnight levels cannot be trusted; they are omitted. Prior RTH from an older contract is not silently imported. Keep those start-date limitations in the analysis. Finish each contract allocation flat; do not overlap trade dates or mix repeated short tests into the full-year dataset.

## Research exports

| File | Meaning |
|---|---|
| `manifest.csv` | Version, instrument, zone, complete parameter snapshot, realtime start and open quantity at termination |
| `context.csv` | Minute snapshots of as-of levels, VWAP, ATR, EMA9/21 and 15/30/60-minute trend timestamps, readiness, direction and strength |
| `minutes.csv.gz`, `timeframes.csv` | Full ETH completed-minute OHLCV and 15/30/60-minute OHLCV, including warm-up state and completeness flags |
| `candidates.csv` | Both entry families, context, stop/size and blocked/submitted decisions |
| `orders.csv`, `fills.csv` | Actual NT strategy order callbacks and executions, including partial fills |
| `trades.csv` | Completed round trips, gross/estimated fees/net and observed open-position dollar MFE/MAE |
| `equity.csv.gz` | Every equity/quantity/realized change plus minute heartbeats; ordered observation time/sequence and separate execution time. `ExportEveryTickEquity=True` restores every-tick rows |
| `ticks.csv.gz` | Optional Last ticks during entry windows/open positions; price, volume and open quantity |
| `shadows.csv` | Passive candidate diagnostics: 8/12/16/20-tick stops × 1/1.5/2R, 300-second maximum horizon |
| `sessions.csv`, `quality.csv` | Daily boundaries and gaps, ordering, missing context and faults |

Shadow barriers use observed Last ticks without costs, bid/ask execution, actual order matching or slippage. They include blocked candidates and cannot be used as the actual evaluation pass rate. `Censored` paths are unfinished at termination. Raw ticks are scoped to trading windows/open positions, not a full ETH archival feed. Tick/Last volume is not order-book liquidity or aggressor-side delta. Files flush every 30 seconds of market time; clean strategy termination finalizes gzip. Research write errors stop execution rather than silently lose the audit trail.

## Rolling actual-fill analysis

Python 3, standard library only:

```powershell
python src/Tools/analyze_es_evaluation.py "C:\Replay\ES_Evaluation_ES_12-25_run" "C:\Replay\ES_Evaluation_ES_03-26_run" --output "C:\Replay\EvaluationAnalysis"
```

Supply all contract folders for the complete run. It joins disjoint realized-net curves, preserving fees, and starts a new analytical evaluation at each observed session. A pass requires **flat realized net >=$3,000**, before the drawdown floor is touched and before the exclusive date 21 calendar days after the start. Drawdown uses tick-by-tick liquidation equity with the running high-water mark; equality to $2,000 fails. A secondary `--drawdown-model eod` report updates the high-water mark from each observed day's closing net while checking its floor intraday. These are generic uncapped models.

Outputs: `attempts_intraday.csv` and `summary_intraday.json` (or `_eod` equivalents). They include failures/timeouts, maximum drawdown, time to pass, candidate decisions, hypothetical diagnostic counts, actual trade frequency and fee-adjusted profit. The primary pass-rate denominator includes only quality-qualified starts with a fully elapsed 21-day observation horizon; immature final starts are not preferentially counted just because they passed early.

Missing weekdays and short sessions are flagged, not quietly treated as complete. Add `--closed-date YYYY-MM-DD` only for verified nontrading days. Partial holiday sessions remain flagged under the normal-window baseline. Gap detection and first/last observations do not prove data completeness. Overlapping starts are correlated; they are not independent trials. v1 Playback execution timestamps can run up to one second ahead of the next tick. The analyzer preserves written observation order and clamps bounded legacy timestamp reversals; it rejects larger reversals and enforces the v1.1 observation sequence. Quiet overnight intervals are not automatically classified as RTH data gaps. The initial report reuses the continuous strategy's actual fills: each attempt stops analytically at pass/fail, while daily sizing/risk choices are not independently rerun for each account. Confirm promising profiles with separate account-specific replay starts and untouched holdout periods before asserting a >=50% pass probability. Tune using an earlier research partition and validate on later unseen dates; report both, rather than selecting the best full-year fit.

## Validation performed during implementation

- C# pure engine compiled and fixture tests passed: gradual sweeps, shallow-touch rejection, momentum retests, out-of-order data, session windows, CSV escaping, gzip and shadow censoring.
- All production C# compiled against **test doubles**, with partial-fill/exit weighted-price/fee accounting, managed order context and order-rejection lock tests passing.
- Python analyzer: ten tests passed, covering intratrade failure, net/flat pass requirement, calendar expiry, EOD mode, contract offsets/overlap, mature-window denominator and missing dates.
- New refinement fixtures passed: higher-timeframe completion/as-of/readiness, no unfinished-bar look-ahead, completed rejection and hold, displacement-before-retest, quiet gaps, and sparse equity retaining every excursion and heartbeat.
- Actual NinjaTrader assemblies and a Playback session are unavailable in the implementation environment. Native NinjaTrader compilation, bracket lifecycle and actual market behavior remain to be verified in the short test; fixture compilation is not a NinjaTrader compile result.

## Direction modes and research status

Higher timeframes are aggregated from the same ordered tick stream, using completed minutes only. EMA5/13 separation, EMA5 slope and close versus EMA5 must agree, with separation >=0.10 of that frame's ATR. Thirteen valid bars are required for readiness, and stale frames do not qualify as current. Incomplete bars are exported with a false completeness flag and reset directional readiness. These are deterministic research definitions, not an optimized or validated edge.

- `TrendFilterMode=0`: record direction but do not filter entries.
- `TrendFilterMode=1` (**default**): veto an entry if both current 15- and 30-minute trends oppose it. Unavailable/neutral trends are recorded and do not veto. The 60-minute frame is recorded for analysis.
- `TrendFilterMode=2`: require current 15-minute alignment.
- `TrendFilterMode=3`: require current 15-, 30- and 60-minute alignment. This can substantially reduce trade frequency, especially while frames warm up.

`UseRollingLevelEntries=False` excludes the rolling five-minute levels from actual entries while keeping their qualified candidates and passive paths for diagnostics. `UseConfirmedEntries=True` enables the candle/hold definitions above. Disabling it restores tick-touch timing, with other selected parameters unchanged. The policy remains OnEachTick even though setup confirmation uses a completed candle.

The first uploaded baseline's fill/fee ledger reconciled, with no order errors and a flat ending. It showed losses in both families, excessive use of the minimum stop and poor rolling-level results. Simple stop/reward changes and VWAP-slope filters did not establish a stable positive edge across earlier/later diagnostic partitions. The original raw Last export covered the entry windows rather than full ETH; it cannot reconstruct complete 15/30/60-minute price candles throughout the day. v1.1 adds those bars so the next replay can test context properly. Candle confirmations, the mild trend veto and volatility stops are **research hypotheses requiring fresh actual-fill replay**, not claims that the pass-rate goal is solved.

Before the uninterrupted run, use the documented four-session smoke test with v1.1, including at least one full overnight warm-up. Confirm version, new parameters, readable finalized gzip files, monotonically increasing equity sequence, OHLCV/timeframe exports, and actual protected trades. Then run the entire disjoint contract allocation through the latest available completed session. November 10 in the initial upload ended around 14:09, so it is an incomplete final session for the normal-window baseline.
