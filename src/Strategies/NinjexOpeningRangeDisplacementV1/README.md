# Ninjex Opening Range Displacement V1 (v1.0.1)

## Install / setup

- Strategy: `NinjexOpeningRangeDisplacementV1`
- Optional chart indicator: `NinjexFirstFiveMinuteRange`
- Use an **ES or NQ 1-minute** primary chart (the indicator also supports 5-minute charts).
- **Set NinjaTrader platform time zone to Eastern (ET)**. The code does **not** convert timestamps. ET means EST in winter and EDT in summer.
- Ensure minute data cover all five 09:31–09:35 ET bar-close timestamps. ETH or RTH sessions can work if their minute data include this interval.
- Compile through NinjaTrader's NinjaScript editor (or existing NTStrategies project with NinjaTrader references). No secondary data series, Tick Replay, or order-flow data are used.
- The strategy and standalone indicator both consume the **same** `NinjexFirstFiveRangeEngine` range-calculation class; the strategy does **not** draw any lines. Add `NinjexFirstFiveMinuteRange` to the chart separately and leave **Show Lines = true** for visuals.

## Rules

1. On completed minute bars, derive ORH and ORL from the five bars ending 09:31, 09:32, 09:33, 09:34 and 09:35 ET (or the single completed 09:30–09:35 five-minute bar for the visual indicator).
2. At/after 09:35 ET, identify the **first directional candle-body close** beyond ORH/ORL. High/low wick penetration without a close is insufficient. Breakout candle must be directionally bullish/bearish.
3. Link that breakout to a **three-completed-candle FVG**: bullish `Low[0] > High[2]`; bearish `High[0] < Low[2]`. Candle 2 can be the breakout; candle 3 must already be complete. A breakout may also be candle 1 or 3 of the triple.
4. Confirm immediately when candle 3 closes (the actual historical market order fills at the next bar under NinjaTrader standard OHLC fill processing). Require the confirmation close and the FVG's outer edge to be beyond the OR boundary.
5. If the breakout does **not** produce a qualifying FVG by the end of its next two candles, expire the attempt. Require a **new touch back into the opening range** and a new directional breakout close before trying again. No unrelated FVGs farther into an existing trend are eligible.
6. Long stop = low of the qualifying **breakout candle**, short stop = its high. No fixed-tick stop substitution. Reject a trade when the stop is not at least one tick behind the signal close.
7. Default target = **2R from the actual entry fill** relative to the breakout-candle stop. No stop or target is registered before entry. After *each actual fill*, stop price legality is checked against the execution fill and the latest available conservative market reference. A valid fill receives a newly generated unique OCO stop/2R-target pair. An invalid fill triggers an immediate market-exit attempt, **without submitting an invalid stop**, and pauses fresh entries for that ET day.
8. No pyramiding. Max entries per ET date default 0 (unlimited); enter only flat. NinjaTrader session-close flatten enabled.

### Momentum research (disabled on the initial run)

All valid minutes generate a CSV `BAR` row with OHLC, ATR14 (ticks), ADX14, absolute body (ticks), candle size (ticks), body/ATR and body/candle fraction. `BODY_BREAKOUT` rows also capture **breakout-candle** ATR, ADX, body/ATR and whether a range retest preceded the signal. You can later enable three optional, independent filters:

- ATR14 >= MinAtrTicks
- ADX14 >= MinAdx
- Breakout candle body/ATR14 >= MinBreakoutBodyAtr

Minimum FVG width defaults to one instrument tick; adjust on later research runs. ES/NQ have different volatility; do not assume one fixed numerical threshold works for both.

### File logs (v1.0.1)

Each **strategy instance/run** now generates up to three consolidated UTF-8 CSVs, regardless of historical date span:

`Documents/NinjaTrader 8/NinjexLogs/NinjexOpeningRangeDisplacementV1/`

Common prefix:

`NinjexOpeningRangeDisplacementV1_v1.0.1_<instrument>_<account>_<firstLogDate>_<runid>_`

- `research.csv` — one `BAR` event for each eligible completed 1-minute candle, ATR/ADX and body measurements.
- `signals.csv` — first-five-minute levels, breakouts, FVG confirmations/rejections, retests and daily state changes.
- `executions.csv` — every entry/exit fill, order ID, OCO ID, order-state transition, rejection, stop/target, fill-based USD risk, exit gross PnL, emergency exits and trade completion.

The same run identifier ties these files together. Files are append-only, auto-flushed, and independently protected against errors. Version 1.0.0's older daily files are retained and can still be inspected; no migration is needed.

#### Protective bracket change from v1.0.0

- **Root failure**: old `SetStopLoss(...Price...)` could submit a stale buy-to-cover stop below market (or sell stop above market) after next-bar/slippage movement; dynamic target changes could complicate the generated OCO life-cycle.
- **New design**: `IsUnmanaged=true`; one unique GUID-derived OCO *per entry execution*; validated stop and actual-fill 2R target submitted from `OnExecutionUpdate`.
- The breakout-candle stop is **never moved arbitrarily** to make a rejected stop legal. If fill/current market has passed the candle stop, submit market exit immediately and pause entries until next ET date.
- NinjaTrader's `RealtimeErrorHandling.StopCancelClose` remains enabled. Any order rejection is logged and prevents further entries. No exceptions are ignored.
- Strategies using unmanaged order submission are more sensitive to order-event sequences: **validate this on Playback before live use.** When changing versions, disable the old strategy and ensure all account-side working orders are gone before starting the new version.

## Pre-live validation

**Not yet compiled against a running NinjaTrader installation or execution-tested in Strategy Analyzer.** Before trading live:

1. Compile in NT8 on Windows and load indicator/strategy on an ES 1-minute chart.
2. Compare the 09:35 indicator high/low to the actual 09:30–09:35 5-minute candle across several sessions, then repeat with NQ and historical rollover contracts.
3. Inspect a wick-only breakout (no trade), breakout whose FVG is confirmed at +1 or +2 candles (trade), failed breakout that does not retest (no trade), failed breakout followed by retest/new FVG (trade), and both long/short directions.
4. Check that a signal logged at third candle close produces the intended next-bar market entry, and that the stop is the **breaking candle's high/low** rather than FVG candle 3.
5. Verify the execution-logged fill/stop/target are actually 2R. Check rejected orders, gap fills, historical commission settings and session close.
6. Compare Strategy Analyzer standard fill resolution with Replay if fill quality matters. One-minute OHLC cannot prove which of stop and target traded first within a candle. Tick Replay is not required for signal formation.

This is a research baseline, not a claim of profitable or execution-proven performance.

## Pure engine regression tests

`tests/FirstFiveRangeEngineTests.cs` contains a standalone executable smoke test with no NinjaTrader assembly dependency. Compile that file and `src/Shared/NinjexFirstFiveRangeEngine.cs` together using a .NET Framework C# compiler; tests cover full/missing 1-minute ranges, single 5-minute candle, no post-09:35 drift and ET date rollover. These tests are **provided but not executed here** because no C# compiler is available in the current environment.

`tests/OpeningBracketMathTests.cs` validates long/short target arithmetic, invalid buy-to-cover stops below market, invalid sell stops above market, and adverse next-bar gaps. The tests are provided but not run here without a C# runtime.
