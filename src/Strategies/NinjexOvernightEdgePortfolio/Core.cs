#region Using declarations

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Ninjex;

#endregion


namespace NinjaTrader.NinjaScript.Strategies
{
    public enum NinjexOvernightEdgePortfolioMode
    {
        BaselineAB,
        FourModelResearchFiltered,
        FourModelNoFastEma
    }


    /// <summary>
    /// Execution strategy built from the neutral ES market-research study.
    ///
    /// Expected chart:
    ///     ES 5-minute
    ///     CME US Index Futures ETH
    ///     Chart and collector timestamps must both be US Eastern (ET).
    ///     No timezone conversion is performed.
    ///
    /// Added series:
    ///     BIP 1 = 1-minute signal series
    ///     BIP 2 = 1-tick causal execution series
    ///
    /// Baseline Model A - LONG:
    ///     - 1-minute bar sweeps below Overnight Low and closes back above it.
    ///     - Signal occurs within 60 minutes of the 09:30 RTH open.
    ///     - Completed 5-minute ATR >= 25 ticks.
    ///
    /// Baseline Model B - SHORT:
    ///     - Previous 1-minute close >= Overnight High.
    ///     - Current completed 1-minute close < Overnight High.
    ///     - Signal close is below the completed 5-minute slow EMA.
    ///     - Overnight range width <= 300 ticks.
    ///
    /// Four-model portfolio:
    ///     - LONG prior-day-close reclaim, 1-minute range <= 30 ticks,
    ///       Overnight width >= 200 ticks.
    ///     - SHORT premarket-high sweep/rejection in the first 120 minutes,
    ///       completed 5-minute ATR >= 30 ticks.
    ///     - SHORT RTH-open breakdown from 120 minutes after the open,
    ///       Premarket width >= 140 ticks.
    ///     - SHORT premarket-low breakdown, completed 5-minute ATR >= 20 ticks.
    ///       The research-filtered mode also requires close > 5-minute EMA(9).
    ///
    /// Execution:
    ///     - First causal tick after the completed 1-minute signal.
    ///     - Fixed 20-tick stop / 40-tick target.
    ///     - Maximum 60-minute holding period by default.
    ///     - Protective orders are fill-relative.
    ///     - No break-even.
    ///
    /// Portfolio controls:
    ///     - Maximum 3 entries per RTH day.
    ///     - Maximum 2 winning trades per RTH day.
    ///     - Maximum 2 losing trades per RTH day (0 disables the limit).
    ///
    /// PortfolioMode retains the validated baseline A/B portfolio and adds
    /// both filtered and unfiltered versions of the four-model portfolio.
    /// </summary>
    public class NinjexOvernightEdgePortfolio : Strategy
    {
        private const string StrategyVersion = "1.2.0";

        // Regular RTH close in ET. ETH bars after this time must not
        // overwrite the prior-day reference, even if FlattenTime changes.
        private const int RegularRthCloseTime = 160000;

        private const int ContextSeriesIndex = 0;
        private const int SignalSeriesIndex = 1;
        private const int TickSeriesIndex = 2;

        private const string LongEntrySignal = "ONL-RECLAIM-L";
        private const string ShortEntrySignal = "ONH-FAILURE-S";

        private const string PriorCloseEntrySignal = "PDC-RECLAIM-L";
        private const string PremarketHighEntrySignal = "PMH-REJECT-S";
        private const string RthOpenEntrySignal = "RTHOPEN-BREAK-S";
        private const string PremarketLowEntrySignal = "PML-BREAK-S";

        private const string BaselineLongModelName =
            "Baseline A - Overnight Low Reclaim";

        private const string BaselineShortModelName =
            "Baseline B - Overnight High Failure";

        private const string PriorCloseModelName =
            "Prior-day-close reclaim";

        private const string PremarketHighModelName =
            "Premarket-high sweep/rejection";

        private const string RthOpenModelName =
            "RTH-open breakdown";

        private const string PremarketLowModelName =
            "Premarket-low breakdown";

        private const string LongEodExitSignal = "EOD-L";
        private const string ShortEodExitSignal = "EOD-S";

        private const string LongTimeExitSignal = "TIME-L";
        private const string ShortTimeExitSignal = "TIME-S";


        private enum PendingDirection
        {
            None,
            Long,
            Short
        }


        #region Engines / indicators

        private NinjexPremarketRangeEngine overnightRangeEngine;
        private NinjexPremarketRangeEngine premarketRangeEngine;

        private ATR atr5m;
        private EMA emaFast5m;
        private EMA emaSlow5m;

        #endregion


        #region Overnight range state

        private DateTime overnightRangeDate =
            Core.Globals.MinDate;

        private double overnightHigh =
            double.NaN;

        private double overnightLow =
            double.NaN;

        private int overnightBars;

        private bool overnightRangeReady;

        private DateTime premarketRangeDate =
            Core.Globals.MinDate;

        private double premarketHigh =
            double.NaN;

        private double premarketLow =
            double.NaN;

        private int premarketBars;

        private bool premarketRangeReady;

        private DateTime currentRthReferenceDate =
            Core.Globals.MinDate;

        private double currentRthLastClose =
            double.NaN;

        private DateTime priorDayCloseDate =
            Core.Globals.MinDate;

        private double priorDayClose =
            double.NaN;

        private DateTime rthOpenDate =
            Core.Globals.MinDate;

        private double rthOpen =
            double.NaN;

        #endregion


        #region Completed 5-minute context

        private DateTime last5mTime =
            Core.Globals.MinDate;

        private double last5mAtrTicks =
            double.NaN;

        private double last5mEmaFast =
            double.NaN;

        private double last5mEmaSlow =
            double.NaN;

        #endregion


        #region Pending causal entry

        private PendingDirection pendingDirection =
            PendingDirection.None;

        private string pendingModelName =
            string.Empty;

        private string pendingEntrySignal =
            string.Empty;

        private DateTime pendingSignalTime =
            Core.Globals.MinDate;

        private DateTime pendingEarliestExecutionTime =
            Core.Globals.MinDate;

        private double pendingSignalClose =
            double.NaN;

        private double pendingSignalHigh =
            double.NaN;

        private double pendingSignalLow =
            double.NaN;

        private double pendingAtr5mTicks =
            double.NaN;

        private double pendingEmaSlow5m =
            double.NaN;

        private double pendingEmaFast5m =
            double.NaN;

        private double pendingOvernightWidthTicks =
            double.NaN;

        private double pendingPremarketWidthTicks =
            double.NaN;

        private int pendingMinutesFromOpen;

        #endregion


        #region Order / trade state

        private bool entryOrderPending;
        private bool manualExitPending;

        private Order activeEntryOrder;

        private bool activeTradeCounted;
        private PendingDirection activeTradeDirection =
            PendingDirection.None;

        private string activeModelName =
            string.Empty;

        private string activeEntrySignal =
            string.Empty;

        private DateTime submittedSignalTime =
            Core.Globals.MinDate;

        private DateTime activeMaxHoldExitTime =
            Core.Globals.MinDate;

        private int activeEntryFilledQuantity;
        private double activeEntryPriceQuantity;

        private double activeTradeGrossPnl;

        #endregion


        #region Daily portfolio state

        private DateTime activeTradingDate =
            Core.Globals.MinDate;

        private int tradesToday;
        private int winnersToday;
        private int lossesToday;

        private double grossPnlToday;

        #endregion


        #region NinjaScript lifecycle

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name =
                    "Ninjex Overnight Edge Portfolio";

                Description =
                    "Selectable baseline A/B and four-model ES edge portfolios derived from neutral market research.";

                Calculate =
                    Calculate.OnEachTick;

                EntriesPerDirection = 1;

                EntryHandling =
                    EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy =
                    false;

                ExitOnSessionCloseSeconds = 30;

                StartBehavior =
                    StartBehavior.WaitUntilFlat;

                RealtimeErrorHandling =
                    RealtimeErrorHandling.StopCancelClose;

                StopTargetHandling =
                    StopTargetHandling.PerEntryExecution;

                BarsRequiredToTrade = 30;

                IsInstantiatedOnEachOptimizationIteration =
                    false;


                //
                // Models
                //
                PortfolioMode =
                    NinjexOvernightEdgePortfolioMode
                        .FourModelResearchFiltered;

                EnableLongModel = true;
                EnableShortModel = true;


                //
                // Session / range
                //
                OvernightStartTime = 180000;
                PremarketStartTime = 30000;
                MarketOpenTime = 93000;

                EntryStartTime = 93500;
                EntryEndTime = 160000;

                FlattenTime = 160000;

                RequireCompleteOvernightRange = true;
                ExpectedOvernightBars = 186;

                RequireCompletePremarketRange = true;
                ExpectedPremarketBars = 78;


                //
                // Indicators
                //
                AtrPeriod = 14;
                EmaFastPeriod = 9;
                EmaSlowPeriod = 21;


                //
                // Model A - long overnight-low reclaim
                //
                LongMaxMinutesFromOpen = 60;
                LongMinimumAtr5mTicks = 25.0;


                //
                // Model B - short overnight-high failure
                //
                ShortMaximumOvernightWidthTicks = 300.0;


                //
                // Four-model portfolio
                //
                PriorCloseMaximumRangeTicks = 30.0;
                PriorCloseMinimumOvernightWidthTicks = 200.0;

                PremarketHighMaximumMinutesFromOpen = 120;
                PremarketHighMinimumAtr5mTicks = 30.0;

                RthOpenMinimumMinutesFromOpen = 120;
                RthOpenMinimumPremarketWidthTicks = 140.0;

                PremarketLowMinimumAtr5mTicks = 20.0;


                //
                // Risk
                //
                OrderQuantity = 1;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;

                MaxHoldMinutes = 60;

                MaxTradesPerDay = 3;
                MaxWinnersPerDay = 2;

                MaxLossesPerDay = 2;


                //
                // Diagnostics
                //
                EnableDiagnostics = true;
            }
            else if (State == State.Configure)
            {
                //
                // BIP 1: completed 1-minute signals.
                //
                AddDataSeries(
                    BarsPeriodType.Minute,
                    1);

                //
                // BIP 2: causal tick execution.
                //
                AddDataSeries(
                    BarsPeriodType.Tick,
                    1);
            }
            else if (State == State.DataLoaded)
            {
                overnightRangeEngine =
                    new NinjexPremarketRangeEngine();

                premarketRangeEngine =
                    new NinjexPremarketRangeEngine();

                atr5m =
                    ATR(
                        Closes[ContextSeriesIndex],
                        AtrPeriod);

                emaFast5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaFastPeriod);

                emaSlow5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaSlowPeriod);

                Diagnostic(
                    DateTime.Now,
                    "READY Version={0} " +
                    "Mode={1} BaselineLong={2} BaselineShort={3} " +
                    "Stop={4}t Target={5}t " +
                    "MaxHold={6}m " +
                    "MaxTrades={7} MaxWinners={8} MaxLosses={9}",
                    StrategyVersion,
                    PortfolioMode,
                    EnableLongModel,
                    EnableShortModel,
                    StopLossTicks,
                    ProfitTargetTicks,
                    MaxHoldMinutes,
                    MaxTradesPerDay,
                    MaxWinnersPerDay,
                    MaxLossesPerDay);
            }
        }


        protected override void OnBarUpdate()
        {
            if (BarsInProgress == ContextSeriesIndex)
            {
                ProcessContextSeries();
                return;
            }

            if (BarsInProgress == SignalSeriesIndex)
            {
                ProcessSignalSeries();
                return;
            }

            if (BarsInProgress == TickSeriesIndex)
            {
                ProcessTickSeries();
            }
        }

        #endregion


        #region 5-minute context / overnight range

        private void ProcessContextSeries()
        {
            if (CurrentBars[ContextSeriesIndex]
                < Math.Max(
                    Math.Max(
                        Math.Max(
                            AtrPeriod,
                            EmaFastPeriod),
                        EmaSlowPeriod),
                    30) + 2)
            {
                return;
            }

            if (!IsFirstTickOfBar)
                return;


            var barTime =
                Times[ContextSeriesIndex][1];

            var high =
                Highs[ContextSeriesIndex][1];

            var low =
                Lows[ContextSeriesIndex][1];


            //
            // Build overnight range from completed 5-minute bars.
            //
            var overnightFinalizedNow =
                overnightRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Overnight,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);

            var premarketFinalizedNow =
                premarketRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Premarket,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);


            if (overnightFinalizedNow
                && overnightRangeEngine.IsRangeComplete)
            {
                overnightRangeDate =
                    overnightRangeEngine
                        .LatestRangeDate;

                overnightHigh =
                    overnightRangeEngine
                        .LatestHigh;

                overnightLow =
                    overnightRangeEngine
                        .LatestLow;

                overnightBars =
                    overnightRangeEngine
                        .RangeBarCount;


                var finiteRange =
                    IsFinite(overnightHigh)
                    && IsFinite(overnightLow)
                    && overnightHigh > overnightLow;

                var completeRange =
                    !RequireCompleteOvernightRange
                    || overnightBars >= ExpectedOvernightBars;

                overnightRangeReady =
                    finiteRange
                    && completeRange;


                Diagnostic(
                    barTime,
                    "OVERNIGHT READY " +
                    "Date={0:yyyy-MM-dd} " +
                    "High={1} Low={2} Width={3:0.0}t " +
                    "Bars={4} Complete={5}",
                    overnightRangeDate,
                    overnightHigh,
                    overnightLow,
                    GetOvernightWidthTicks(),
                    overnightBars,
                    overnightRangeReady);
            }


            if (premarketFinalizedNow
                && premarketRangeEngine.IsRangeComplete)
            {
                premarketRangeDate =
                    premarketRangeEngine.LatestRangeDate;

                premarketHigh =
                    premarketRangeEngine.LatestHigh;

                premarketLow =
                    premarketRangeEngine.LatestLow;

                premarketBars =
                    premarketRangeEngine.RangeBarCount;

                premarketRangeReady =
                    IsFinite(premarketHigh)
                    && IsFinite(premarketLow)
                    && premarketHigh > premarketLow
                    && (!RequireCompletePremarketRange
                        || premarketBars
                            >= ExpectedPremarketBars);

                Diagnostic(
                    barTime,
                    "PREMARKET READY " +
                    "Date={0:yyyy-MM-dd} " +
                    "High={1} Low={2} Width={3:0.0}t " +
                    "Bars={4} Complete={5}",
                    premarketRangeDate,
                    premarketHigh,
                    premarketLow,
                    GetPremarketWidthTicks(),
                    premarketBars,
                    premarketRangeReady);
            }


            //
            // Cache exactly the completed 5-minute context.
            // This mirrors the neutral collector: signal rows do not
            // peek into the still-forming 5-minute candle.
            //
            last5mTime =
                barTime;

            last5mAtrTicks =
                atr5m[1] / TickSize;

            last5mEmaFast =
                emaFast5m[1];

            last5mEmaSlow =
                emaSlow5m[1];
        }

        #endregion


        #region 1-minute signal detection

        private void ProcessSignalSeries()
        {
            if (CurrentBars[SignalSeriesIndex] < 2)
                return;

            if (!IsFirstTickOfBar)
                return;


            var signalTime =
                Times[SignalSeriesIndex][1];

            var currentBarOpen =
                Opens[SignalSeriesIndex][0];

            var high =
                Highs[SignalSeriesIndex][1];

            var low =
                Lows[SignalSeriesIndex][1];

            var close =
                Closes[SignalSeriesIndex][1];

            var previousClose =
                Closes[SignalSeriesIndex][2];

            //
            // NinjaTrader time-based bars are end-stamped. At the first
            // tick of the next 1-minute bar, Times[1][0] is already one
            // minute later than the tick that made [1] complete. Arm from
            // signalTime so BIP 2 can use that first causal tick rather
            // than waiting through the whole next minute.
            //
            EnsureTradingDate(
                signalTime.Date,
                signalTime);


            var timeValue =
                ToTimeValue(
                    signalTime);


            UpdateRthReferenceLevels(
                signalTime,
                timeValue,
                currentBarOpen,
                close);


            if (timeValue < EntryStartTime
                || timeValue >= EntryEndTime)
            {
                return;
            }


            if (!CanEvaluateSignal(
                    signalTime))
            {
                return;
            }


            if (!CanTakeNewTrade(
                    signalTime,
                    true,
                    false))
            {
                return;
            }


            var minutesFromOpen =
                MinutesBetween(
                    MarketOpenTime,
                    timeValue);

            var overnightWidthTicks =
                GetOvernightWidthTicks();


            if (PortfolioMode
                == NinjexOvernightEdgePortfolioMode.BaselineAB)
            {
                ProcessBaselineSignals(
                    signalTime,
                    high,
                    low,
                    close,
                    previousClose,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            ProcessFourModelSignals(
                signalTime,
                high,
                low,
                close,
                previousClose,
                minutesFromOpen,
                overnightWidthTicks);
        }


        private void ProcessBaselineSignals(
            DateTime signalTime,
            double high,
            double low,
            double close,
            double previousClose,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            //
            // Model A:
            // Sweep below Overnight Low + completed close back above.
            //
            var longSweep =
                low < overnightLow
                && close > overnightLow;

            var longTimeOk =
                minutesFromOpen >= 0
                && minutesFromOpen
                    <= LongMaxMinutesFromOpen;

            var longAtrOk =
                IsFinite(last5mAtrTicks)
                && last5mAtrTicks
                    >= LongMinimumAtr5mTicks;

            var longQualified =
                EnableLongModel
                && longSweep
                && longTimeOk
                && longAtrOk;


            if (EnableLongModel
                && longSweep)
            {
                Diagnostic(
                    signalTime,
                    "LONG CHECK " +
                    "Qualified={0} " +
                    "ONL={1} Low={2} Close={3} " +
                    "MinutesFromOpen={4} " +
                    "ATR5={5:0.0}t MinATR={6:0.0}t",
                    longQualified,
                    overnightLow,
                    low,
                    close,
                    minutesFromOpen,
                    last5mAtrTicks,
                    LongMinimumAtr5mTicks);
            }


            //
            // Model B:
            // Previous completed 1-minute close was at/above Overnight High,
            // then the current completed close crosses back below it.
            //
            // IMPORTANT: the research predicate Px>Slow5 was direction-normalized.
            // For a SHORT, translating it back to raw price means close < EMA slow.
            //
            var shortCrossBelow =
                previousClose >= overnightHigh
                && close < overnightHigh;

            var shortEmaOk =
                IsFinite(last5mEmaSlow)
                && close < last5mEmaSlow;

            var shortWidthOk =
                IsFinite(overnightWidthTicks)
                && overnightWidthTicks
                    <= ShortMaximumOvernightWidthTicks;

            var shortQualified =
                EnableShortModel
                && shortCrossBelow
                && shortEmaOk
                && shortWidthOk;


            if (EnableShortModel
                && shortCrossBelow)
            {
                Diagnostic(
                    signalTime,
                    "SHORT CHECK " +
                    "Qualified={0} " +
                    "ONH={1} PrevClose={2} Close={3} " +
                    "EMA5Slow={4} CloseBelowEMA={5} " +
                    "ONWidth={6:0.0}t MaxWidth={7:0.0}t",
                    shortQualified,
                    overnightHigh,
                    previousClose,
                    close,
                    last5mEmaSlow,
                    shortEmaOk,
                    overnightWidthTicks,
                    ShortMaximumOvernightWidthTicks);
            }


            //
            // A completed minute can theoretically qualify both models.
            // The research did not establish an ordering rule for opposing
            // same-timestamp signals, so do not invent one.
            //
            if (longQualified
                && shortQualified)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL SKIP Reason=DualSignalAmbiguous " +
                    "High={0} Low={1} Close={2}",
                    high,
                    low,
                    close);

                return;
            }


            if (longQualified)
            {
                ArmPendingEntry(
                    BaselineLongModelName,
                    LongEntrySignal,
                    PendingDirection.Long,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (shortQualified)
            {
                ArmPendingEntry(
                    BaselineShortModelName,
                    ShortEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);
            }
        }


        private void ProcessFourModelSignals(
            DateTime signalTime,
            double high,
            double low,
            double close,
            double previousClose,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            var range1mTicks =
                TickSize > 0
                    ? (high - low) / TickSize
                    : double.NaN;

            var premarketWidthTicks =
                GetPremarketWidthTicks();


            var priorCloseCross =
                IsFinite(priorDayClose)
                && priorDayCloseDate < signalTime.Date
                && previousClose <= priorDayClose
                && close > priorDayClose;

            var priorCloseQualified =
                priorCloseCross
                && IsFinite(range1mTicks)
                && range1mTicks
                    <= PriorCloseMaximumRangeTicks
                && IsFinite(overnightWidthTicks)
                && overnightWidthTicks
                    >= PriorCloseMinimumOvernightWidthTicks;


            if (priorCloseCross)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=PDC-Reclaim " +
                    "Qualified={0} PDC={1} " +
                    "PrevClose={2} Close={3} " +
                    "Range1m={4:0.0}t MaxRange={5:0.0}t " +
                    "ONWidth={6:0.0}t MinONWidth={7:0.0}t",
                    priorCloseQualified,
                    priorDayClose,
                    previousClose,
                    close,
                    range1mTicks,
                    PriorCloseMaximumRangeTicks,
                    overnightWidthTicks,
                    PriorCloseMinimumOvernightWidthTicks);
            }


            var premarketHighSweep =
                IsFinite(premarketHigh)
                && high > premarketHigh
                && close < premarketHigh;

            var premarketHighQualified =
                premarketHighSweep
                && minutesFromOpen >= 0
                && minutesFromOpen
                    <= PremarketHighMaximumMinutesFromOpen
                && IsFinite(last5mAtrTicks)
                && last5mAtrTicks
                    >= PremarketHighMinimumAtr5mTicks;


            if (premarketHighSweep)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=PMH-Rejection " +
                    "Qualified={0} PMH={1} High={2} Close={3} " +
                    "MinutesFromOpen={4} MaxMinutes={5} " +
                    "ATR5={6:0.0}t MinATR={7:0.0}t",
                    premarketHighQualified,
                    premarketHigh,
                    high,
                    close,
                    minutesFromOpen,
                    PremarketHighMaximumMinutesFromOpen,
                    last5mAtrTicks,
                    PremarketHighMinimumAtr5mTicks);
            }


            var rthOpenCross =
                IsFinite(rthOpen)
                && rthOpenDate == signalTime.Date
                && previousClose >= rthOpen
                && close < rthOpen;

            var rthOpenQualified =
                rthOpenCross
                && minutesFromOpen
                    >= RthOpenMinimumMinutesFromOpen
                && IsFinite(premarketWidthTicks)
                && premarketWidthTicks
                    >= RthOpenMinimumPremarketWidthTicks;


            if (rthOpenCross)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=RTH-Open-Breakdown " +
                    "Qualified={0} RTHOpen={1} " +
                    "PrevClose={2} Close={3} " +
                    "MinutesFromOpen={4} MinMinutes={5} " +
                    "PMWidth={6:0.0}t MinPMWidth={7:0.0}t",
                    rthOpenQualified,
                    rthOpen,
                    previousClose,
                    close,
                    minutesFromOpen,
                    RthOpenMinimumMinutesFromOpen,
                    premarketWidthTicks,
                    RthOpenMinimumPremarketWidthTicks);
            }


            var premarketLowCross =
                IsFinite(premarketLow)
                && previousClose >= premarketLow
                && close < premarketLow;

            var useFastEmaFilter =
                PortfolioMode
                == NinjexOvernightEdgePortfolioMode
                    .FourModelResearchFiltered;

            //
            // The archived research result is based on raw close > EMA(9).
            // Earlier prose called this "below" after direction-normalizing
            // the short feature; raw-price NinjaScript must use the relation
            // below to reproduce the 274-trade research portfolio.
            //
            var fastEmaOk =
                !useFastEmaFilter
                || (IsFinite(last5mEmaFast)
                    && close > last5mEmaFast);

            var premarketLowQualified =
                premarketLowCross
                && IsFinite(last5mAtrTicks)
                && last5mAtrTicks
                    >= PremarketLowMinimumAtr5mTicks
                && fastEmaOk;


            if (premarketLowCross)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=PML-Breakdown " +
                    "Qualified={0} PML={1} " +
                    "PrevClose={2} Close={3} " +
                    "ATR5={4:0.0}t MinATR={5:0.0}t " +
                    "EMAFilter={6} EMA5Fast={7} CloseAboveEMA={8}",
                    premarketLowQualified,
                    premarketLow,
                    previousClose,
                    close,
                    last5mAtrTicks,
                    PremarketLowMinimumAtr5mTicks,
                    useFastEmaFilter,
                    last5mEmaFast,
                    IsFinite(last5mEmaFast)
                        && close > last5mEmaFast);
            }


            var qualifiedCount =
                (priorCloseQualified ? 1 : 0)
                + (premarketHighQualified ? 1 : 0)
                + (rthOpenQualified ? 1 : 0)
                + (premarketLowQualified ? 1 : 0);

            if (qualifiedCount > 1)
            {
                Diagnostic(
                    signalTime,
                    "FOUR PRIORITY Multiple={0} " +
                    "Order=PDC,PMH,RTHOpen,PML",
                    qualifiedCount);
            }


            //
            // This ordering is the ordering used by the research portfolio
            // when more than one model qualifies on the same timestamp.
            //
            if (priorCloseQualified)
            {
                ArmPendingEntry(
                    PriorCloseModelName,
                    PriorCloseEntrySignal,
                    PendingDirection.Long,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (premarketHighQualified)
            {
                ArmPendingEntry(
                    PremarketHighModelName,
                    PremarketHighEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (rthOpenQualified)
            {
                ArmPendingEntry(
                    RthOpenModelName,
                    RthOpenEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (premarketLowQualified)
            {
                ArmPendingEntry(
                    PremarketLowModelName,
                    PremarketLowEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);
            }
        }


        private void UpdateRthReferenceLevels(
            DateTime signalTime,
            int timeValue,
            double currentBarOpen,
            double completedClose)
        {
            // Minute bars are end-stamped. Include the bar ending at 16:00
            // (15:59-16:00), but exclude subsequent ETH bars. Updating this
            // reference is independent of the strategy's flatten cutoff.
            if (timeValue < MarketOpenTime
                || timeValue > RegularRthCloseTime)
            {
                return;
            }


            if (currentRthReferenceDate
                != signalTime.Date)
            {
                if (currentRthReferenceDate
                        != Core.Globals.MinDate
                    && IsFinite(currentRthLastClose))
                {
                    priorDayCloseDate =
                        currentRthReferenceDate;

                    priorDayClose =
                        currentRthLastClose;

                    Diagnostic(
                        signalTime,
                        "PRIOR DAY CLOSE READY " +
                        "SourceDate={0:yyyy-MM-dd} Close={1}",
                        priorDayCloseDate,
                        priorDayClose);
                }


                currentRthReferenceDate =
                    signalTime.Date;

                currentRthLastClose =
                    double.NaN;
            }


            if (timeValue == MarketOpenTime
                && IsFinite(currentBarOpen))
            {
                rthOpenDate =
                    signalTime.Date;

                rthOpen =
                    currentBarOpen;

                Diagnostic(
                    signalTime,
                    "RTH OPEN READY " +
                    "Date={0:yyyy-MM-dd} Open={1}",
                    rthOpenDate,
                    rthOpen);
            }


            if (IsFinite(completedClose))
            {
                currentRthLastClose =
                    completedClose;

                if (timeValue == RegularRthCloseTime)
                {
                    Diagnostic(
                        signalTime,
                        "RTH CLOSE CAPTURED Date={0:yyyy-MM-dd} Close={1}",
                        signalTime.Date,
                        currentRthLastClose);
                }
            }
        }


        private bool CanEvaluateSignal(
            DateTime signalTime)
        {
            if (!overnightRangeReady)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=OvernightRangeNotReady");

                return false;
            }


            if (overnightRangeDate
                != signalTime.Date)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=OvernightRangeDateMismatch " +
                    "RangeDate={0:yyyy-MM-dd} SignalDate={1:yyyy-MM-dd}",
                    overnightRangeDate,
                    signalTime.Date);

                return false;
            }


            if (RequireCompleteOvernightRange
                && overnightBars
                    < ExpectedOvernightBars)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=IncompleteOvernightRange " +
                    "Bars={0} Expected={1}",
                    overnightBars,
                    ExpectedOvernightBars);

                return false;
            }


            if (!premarketRangeReady)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=PremarketRangeNotReady");

                return false;
            }


            if (premarketRangeDate
                != signalTime.Date)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=PremarketRangeDateMismatch " +
                    "RangeDate={0:yyyy-MM-dd} SignalDate={1:yyyy-MM-dd}",
                    premarketRangeDate,
                    signalTime.Date);

                return false;
            }


            if (RequireCompletePremarketRange
                && premarketBars
                    < ExpectedPremarketBars)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=IncompletePremarketRange " +
                    "Bars={0} Expected={1}",
                    premarketBars,
                    ExpectedPremarketBars);

                return false;
            }


            if (!IsFinite(last5mAtrTicks)
                || !IsFinite(last5mEmaFast)
                || !IsFinite(last5mEmaSlow))
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=FiveMinuteContextNotReady");

                return false;
            }


            return true;
        }


        private void ArmPendingEntry(
            string modelName,
            string entrySignal,
            PendingDirection direction,
            DateTime signalTime,
            DateTime earliestExecutionTime,
            double high,
            double low,
            double close,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            pendingDirection =
                direction;

            pendingModelName =
                modelName ?? string.Empty;

            pendingEntrySignal =
                entrySignal ?? string.Empty;

            pendingSignalTime =
                signalTime;

            pendingEarliestExecutionTime =
                earliestExecutionTime;

            pendingSignalHigh =
                high;

            pendingSignalLow =
                low;

            pendingSignalClose =
                close;

            pendingAtr5mTicks =
                last5mAtrTicks;

            pendingEmaSlow5m =
                last5mEmaSlow;

            pendingEmaFast5m =
                last5mEmaFast;

            pendingOvernightWidthTicks =
                overnightWidthTicks;

            pendingPremarketWidthTicks =
                GetPremarketWidthTicks();

            pendingMinutesFromOpen =
                minutesFromOpen;


            Diagnostic(
                signalTime,
                "SIGNAL ARMED " +
                "Model={0} Signal={1} Direction={2} " +
                "EarliestTick={3:HH:mm:ss.fff} " +
                "Close={4} ATR5={5:0.0}t " +
                "EMA5Fast={6} EMA5Slow={7} " +
                "ONWidth={8:0.0}t PMWidth={9:0.0}t " +
                "Trades={10}/{11} Winners={12}/{13}",
                pendingModelName,
                pendingEntrySignal,
                direction,
                pendingEarliestExecutionTime,
                pendingSignalClose,
                pendingAtr5mTicks,
                pendingEmaFast5m,
                pendingEmaSlow5m,
                pendingOvernightWidthTicks,
                pendingPremarketWidthTicks,
                tradesToday,
                MaxTradesPerDay,
                winnersToday,
                MaxWinnersPerDay);
        }

        #endregion


        #region Tick execution

        private void ProcessTickSeries()
        {
            if (CurrentBars[TickSeriesIndex] < 1)
                return;


            var time =
                Times[TickSeriesIndex][0];

            var price =
                Closes[TickSeriesIndex][0];


            EnsureTradingDate(
                time.Date,
                time);


            var timeValue =
                ToTimeValue(
                    time);


            //
            // RTH flatten and pending-entry cancellation.
            //
            if (timeValue >= FlattenTime)
            {
                ClearPendingEntry(
                    time,
                    "FlattenTime");

                CancelPendingEntryOrder(
                    time);

                FlattenPosition(
                    time);

                return;
            }


            if (TryExitForMaximumHold(
                    time))
            {
                return;
            }


            if (pendingDirection
                == PendingDirection.None)
            {
                return;
            }


            //
            // The 1-minute signal is only known once that candle has
            // completed. Never execute against an older tick cached by
            // Playback; wait for a tick at/after the new minute.
            //
            if (time
                < pendingEarliestExecutionTime)
            {
                return;
            }


            if (timeValue >= EntryEndTime)
            {
                ClearPendingEntry(
                    time,
                    "EntryWindowClosed");

                return;
            }


            if (!CanTakeNewTrade(
                    time,
                    false,
                    true))
            {
                ClearPendingEntry(
                    time,
                    "DailyRiskLimit");

                return;
            }


            if (Position.MarketPosition
                != MarketPosition.Flat)
            {
                ClearPendingEntry(
                    time,
                    "PositionNotFlat");

                return;
            }


            if (entryOrderPending
                || manualExitPending)
            {
                return;
            }


            SubmitPendingEntry(
                time,
                price);
        }


        private void SubmitPendingEntry(
            DateTime time,
            double observedMarketPrice)
        {
            var direction =
                pendingDirection;

            if (direction
                == PendingDirection.None)
            {
                return;
            }


            var signalName =
                pendingEntrySignal;

            var modelName =
                pendingModelName;

            if (string.IsNullOrEmpty(signalName))
            {
                ClearPendingEntry(
                    time,
                    "MissingEntrySignal");

                return;
            }

            submittedSignalTime =
                pendingSignalTime;


            //
            // Fill-relative managed brackets.
            //
            // SetStopLoss / SetProfitTarget with CalculationMode.Ticks
            // are intentionally configured before the market entry so
            // NinjaTrader anchors the protective orders to the actual
            // execution fill rather than the pre-submission observed tick.
            //
            SetStopLoss(
                signalName,
                CalculationMode.Ticks,
                StopLossTicks,
                false);

            SetProfitTarget(
                signalName,
                CalculationMode.Ticks,
                ProfitTargetTicks);


            entryOrderPending = true;


            Diagnostic(
                time,
                "ENTRY SUBMIT " +
                "Model={0} Signal={1} Direction={2} " +
                "ObservedMarket={3} " +
                "SignalTime={4:HH:mm:ss} " +
                "Stop={5}t Target={6}t " +
                "Qty={7}",
                modelName,
                signalName,
                direction,
                observedMarketPrice,
                pendingSignalTime,
                StopLossTicks,
                ProfitTargetTicks,
                OrderQuantity);


            if (direction
                == PendingDirection.Long)
            {
                EnterLong(
                    TickSeriesIndex,
                    OrderQuantity,
                    signalName);
            }
            else
            {
                EnterShort(
                    TickSeriesIndex,
                    OrderQuantity,
                    signalName);
            }


            pendingDirection =
                PendingDirection.None;
        }

        #endregion


        #region Portfolio risk / daily state

        private bool CanTakeNewTrade(
            DateTime time,
            bool logReason,
            bool allowExistingPendingEntry)
        {
            if (Position.MarketPosition
                    != MarketPosition.Flat
                || entryOrderPending
                || manualExitPending
                || (!allowExistingPendingEntry
                    && pendingDirection
                        != PendingDirection.None))
            {
                if (logReason)
                {
                    Diagnostic(
                        time,
                        "TRADE BLOCK " +
                        "Reason=OrderOrPositionActive");
                }

                return false;
            }


            if (MaxTradesPerDay > 0
                && tradesToday
                    >= MaxTradesPerDay)
            {
                if (logReason)
                {
                    Diagnostic(
                        time,
                        "TRADE BLOCK " +
                        "Reason=MaxTrades " +
                        "Trades={0} Limit={1}",
                        tradesToday,
                        MaxTradesPerDay);
                }

                return false;
            }


            if (MaxWinnersPerDay > 0
                && winnersToday
                    >= MaxWinnersPerDay)
            {
                if (logReason)
                {
                    Diagnostic(
                        time,
                        "TRADE BLOCK " +
                        "Reason=MaxWinners " +
                        "Winners={0} Limit={1}",
                        winnersToday,
                        MaxWinnersPerDay);
                }

                return false;
            }


            if (MaxLossesPerDay > 0
                && lossesToday >= MaxLossesPerDay)
            {
                if (logReason)
                {
                    Diagnostic(
                        time,
                        "TRADE BLOCK " +
                        "Reason=MaxLosses Losses={0} Limit={1}",
                        lossesToday,
                        MaxLossesPerDay);
                }

                return false;
            }


            return true;
        }


        private void EnsureTradingDate(
            DateTime date,
            DateTime eventTime)
        {
            date =
                date.Date;


            if (activeTradingDate
                == date)
            {
                return;
            }


            if (activeTradingDate
                != Core.Globals.MinDate)
            {
                Diagnostic(
                    eventTime,
                    "DAY SUMMARY " +
                    "Date={0:yyyy-MM-dd} " +
                    "Trades={1} Winners={2} Losses={3} " +
                    "GrossPnl={4:0.00}",
                    activeTradingDate,
                    tradesToday,
                    winnersToday,
                    lossesToday,
                    grossPnlToday);
            }


            activeTradingDate =
                date;

            tradesToday = 0;
            winnersToday = 0;
            lossesToday = 0;

            grossPnlToday = 0;


            ClearPendingEntry(
                eventTime,
                "NewTradingDate");


            Diagnostic(
                eventTime,
                "NEW TRADING DATE {0:yyyy-MM-dd}",
                activeTradingDate);
        }

        #endregion


        #region Order / execution callbacks

        protected override void OnOrderUpdate(
            Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            OrderState orderState,
            DateTime time,
            ErrorCode error,
            string nativeError)
        {
            if (order == null)
                return;


            if (IsEntrySignalName(
                    order.Name))
            {
                activeEntryOrder =
                    order;


                if (orderState
                    == OrderState.Rejected
                    || orderState
                    == OrderState.Cancelled)
                {
                    entryOrderPending = false;

                    activeEntryOrder = null;

                    if (filled <= 0
                        && !activeTradeCounted)
                    {
                        submittedSignalTime =
                            Core.Globals.MinDate;

                        activeMaxHoldExitTime =
                            Core.Globals.MinDate;
                    }


                    Diagnostic(
                        time,
                        "ENTRY ORDER END " +
                        "Name={0} State={1} Error={2} Native={3}",
                        order.Name,
                        orderState,
                        error,
                        nativeError);
                }
            }


            if (order.Name == LongEodExitSignal
                || order.Name == ShortEodExitSignal
                || order.Name == LongTimeExitSignal
                || order.Name == ShortTimeExitSignal)
            {
                if (orderState == OrderState.Rejected
                    || orderState == OrderState.Cancelled
                    || orderState == OrderState.Filled)
                {
                    manualExitPending = false;
                }
            }
        }


        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (execution == null
                || execution.Order == null
                || quantity <= 0)
            {
                return;
            }


            var order =
                execution.Order;


            var isLongEntry =
                IsLongEntrySignalName(
                    order.Name);

            var isShortEntry =
                IsShortEntrySignalName(
                    order.Name);


            if (isLongEntry
                || isShortEntry)
            {
                entryOrderPending = false;


                if (!activeTradeCounted)
                {
                    activeTradeCounted = true;

                    activeTradeDirection =
                        isLongEntry
                            ? PendingDirection.Long
                            : PendingDirection.Short;

                    activeEntrySignal =
                        order.Name;

                    activeModelName =
                        GetModelNameForEntrySignal(
                            order.Name);

                    activeEntryFilledQuantity = 0;
                    activeEntryPriceQuantity = 0;

                    activeTradeGrossPnl = 0;

                    activeMaxHoldExitTime =
                        MaxHoldMinutes > 0
                        && submittedSignalTime
                            != Core.Globals.MinDate
                            ? submittedSignalTime.AddMinutes(
                                MaxHoldMinutes)
                            : Core.Globals.MinDate;

                    tradesToday++;
                }


                activeEntryFilledQuantity +=
                    quantity;

                activeEntryPriceQuantity +=
                    price * quantity;


                Diagnostic(
                    time,
                    "ENTRY FILL " +
                    "Model={0} Signal={1} Direction={2} " +
                    "Price={3} Qty={4} " +
                    "AvgEntry={5:0.########} " +
                    "TradesToday={6}",
                    activeModelName,
                    activeEntrySignal,
                    activeTradeDirection,
                    price,
                    quantity,
                    GetActiveAverageEntryPrice(),
                    tradesToday);


                return;
            }


            if (!activeTradeCounted)
                return;


            var fromEntrySignal =
                order.FromEntrySignal
                ?? string.Empty;

            var belongsToActiveTrade =
                !string.IsNullOrEmpty(activeEntrySignal)
                && fromEntrySignal
                    == activeEntrySignal;


            if (!belongsToActiveTrade)
                return;


            var averageEntry =
                GetActiveAverageEntryPrice();

            if (!IsFinite(averageEntry))
                return;


            var points =
                activeTradeDirection
                    == PendingDirection.Long
                    ? price - averageEntry
                    : averageEntry - price;


            var executionPnl =
                points
                * Instrument.MasterInstrument.PointValue
                * quantity;


            activeTradeGrossPnl +=
                executionPnl;


            Diagnostic(
                time,
                "EXIT FILL " +
                "Model={0} Signal={1} Direction={2} " +
                "Order={3} Price={4} Qty={5} " +
                "ExecutionPnl={6:0.00} " +
                "TradeGross={7:0.00}",
                activeModelName,
                activeEntrySignal,
                activeTradeDirection,
                order.Name,
                price,
                quantity,
                executionPnl,
                activeTradeGrossPnl);


            if (marketPosition == MarketPosition.Flat
                || Position.MarketPosition
                    == MarketPosition.Flat)
            {
                FinalizeActiveTrade(
                    time,
                    order.Name);
            }
        }


        private void FinalizeActiveTrade(
            DateTime time,
            string exitName)
        {
            if (!activeTradeCounted)
                return;


            grossPnlToday +=
                activeTradeGrossPnl;


            if (activeTradeGrossPnl > 0)
                winnersToday++;

            else if (activeTradeGrossPnl < 0)
                lossesToday++;


            Diagnostic(
                time,
                "TRADE COMPLETE " +
                "Model={0} Signal={1} Direction={2} Exit={3} " +
                "GrossPnl={4:0.00} " +
                "DayTrades={5} Winners={6} Losses={7} " +
                "DayGross={8:0.00}",
                activeModelName,
                activeEntrySignal,
                activeTradeDirection,
                exitName,
                activeTradeGrossPnl,
                tradesToday,
                winnersToday,
                lossesToday,
                grossPnlToday);


            activeTradeCounted = false;

            activeTradeDirection =
                PendingDirection.None;

            activeModelName =
                string.Empty;

            activeEntrySignal =
                string.Empty;

            activeEntryFilledQuantity = 0;
            activeEntryPriceQuantity = 0;

            activeTradeGrossPnl = 0;

            activeEntryOrder = null;

            submittedSignalTime =
                Core.Globals.MinDate;

            activeMaxHoldExitTime =
                Core.Globals.MinDate;

            manualExitPending = false;
        }

        #endregion


        #region Flatten / pending cleanup

        private void CancelPendingEntryOrder(
            DateTime time)
        {
            if (activeEntryOrder == null)
                return;


            if (activeEntryOrder.OrderState
                    == OrderState.Accepted
                || activeEntryOrder.OrderState
                    == OrderState.Working
                || activeEntryOrder.OrderState
                    == OrderState.Submitted)
            {
                Diagnostic(
                    time,
                    "ENTRY CANCEL " +
                    "Reason=FlattenTime " +
                    "Name={0}",
                    activeEntryOrder.Name);

                CancelOrder(
                    activeEntryOrder);
            }
        }


        private void FlattenPosition(
            DateTime time)
        {
            if (manualExitPending)
                return;


            if (Position.MarketPosition
                == MarketPosition.Long)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "FLATTEN LONG " +
                    "Qty={0}",
                    Position.Quantity);

                ExitLong(
                    TickSeriesIndex,
                    Position.Quantity,
                    LongEodExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Long));

                return;
            }


            if (Position.MarketPosition
                == MarketPosition.Short)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "FLATTEN SHORT " +
                    "Qty={0}",
                    Position.Quantity);

                ExitShort(
                    TickSeriesIndex,
                    Position.Quantity,
                    ShortEodExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Short));
            }
        }


        private bool TryExitForMaximumHold(
            DateTime time)
        {
            if (MaxHoldMinutes <= 0
                || activeMaxHoldExitTime
                    == Core.Globals.MinDate
                || time < activeMaxHoldExitTime
                || manualExitPending)
            {
                return false;
            }


            if (Position.MarketPosition
                == MarketPosition.Long)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "TIME EXIT LONG " +
                    "Deadline={0:HH:mm:ss.fff} Qty={1}",
                    activeMaxHoldExitTime,
                    Position.Quantity);

                ExitLong(
                    TickSeriesIndex,
                    Position.Quantity,
                    LongTimeExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Long));

                return true;
            }


            if (Position.MarketPosition
                == MarketPosition.Short)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "TIME EXIT SHORT " +
                    "Deadline={0:HH:mm:ss.fff} Qty={1}",
                    activeMaxHoldExitTime,
                    Position.Quantity);

                ExitShort(
                    TickSeriesIndex,
                    Position.Quantity,
                    ShortTimeExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Short));

                return true;
            }


            return false;
        }


        private void ClearPendingEntry(
            DateTime time,
            string reason)
        {
            if (pendingDirection
                != PendingDirection.None)
            {
                Diagnostic(
                    time,
                    "PENDING CLEAR " +
                    "Model={0} Signal={1} " +
                    "Direction={2} Reason={3}",
                    pendingModelName,
                    pendingEntrySignal,
                    pendingDirection,
                    reason);
            }


            pendingDirection =
                PendingDirection.None;

            pendingModelName =
                string.Empty;

            pendingEntrySignal =
                string.Empty;

            pendingSignalTime =
                Core.Globals.MinDate;

            pendingEarliestExecutionTime =
                Core.Globals.MinDate;

            pendingSignalClose =
                double.NaN;

            pendingSignalHigh =
                double.NaN;

            pendingSignalLow =
                double.NaN;

            pendingAtr5mTicks =
                double.NaN;

            pendingEmaSlow5m =
                double.NaN;

            pendingEmaFast5m =
                double.NaN;

            pendingOvernightWidthTicks =
                double.NaN;

            pendingPremarketWidthTicks =
                double.NaN;

            pendingMinutesFromOpen = 0;
        }

        #endregion


        #region Helpers

        private double GetOvernightWidthTicks()
        {
            if (!IsFinite(overnightHigh)
                || !IsFinite(overnightLow)
                || TickSize <= 0)
            {
                return double.NaN;
            }


            return
                (overnightHigh
                 - overnightLow)
                / TickSize;
        }


        private double GetPremarketWidthTicks()
        {
            if (!IsFinite(premarketHigh)
                || !IsFinite(premarketLow)
                || TickSize <= 0)
            {
                return double.NaN;
            }


            return
                (premarketHigh
                 - premarketLow)
                / TickSize;
        }


        private static bool IsEntrySignalName(
            string signalName)
        {
            return
                IsLongEntrySignalName(signalName)
                || IsShortEntrySignalName(signalName);
        }


        private static bool IsLongEntrySignalName(
            string signalName)
        {
            return
                signalName == LongEntrySignal
                || signalName == PriorCloseEntrySignal;
        }


        private static bool IsShortEntrySignalName(
            string signalName)
        {
            return
                signalName == ShortEntrySignal
                || signalName == PremarketHighEntrySignal
                || signalName == RthOpenEntrySignal
                || signalName == PremarketLowEntrySignal;
        }


        private static string GetModelNameForEntrySignal(
            string signalName)
        {
            if (signalName == LongEntrySignal)
                return BaselineLongModelName;

            if (signalName == ShortEntrySignal)
                return BaselineShortModelName;

            if (signalName == PriorCloseEntrySignal)
                return PriorCloseModelName;

            if (signalName == PremarketHighEntrySignal)
                return PremarketHighModelName;

            if (signalName == RthOpenEntrySignal)
                return RthOpenModelName;

            if (signalName == PremarketLowEntrySignal)
                return PremarketLowModelName;


            return "Unknown";
        }


        private string GetActiveEntrySignalForExit(
            PendingDirection direction)
        {
            if (!string.IsNullOrEmpty(activeEntrySignal))
                return activeEntrySignal;


            return
                direction == PendingDirection.Long
                    ? LongEntrySignal
                    : ShortEntrySignal;
        }


        private double GetActiveAverageEntryPrice()
        {
            if (activeEntryFilledQuantity <= 0)
                return double.NaN;


            return
                activeEntryPriceQuantity
                / activeEntryFilledQuantity;
        }


        private static bool IsFinite(
            double value)
        {
            return
                !double.IsNaN(value)
                && !double.IsInfinity(value);
        }


        private static int ToTimeValue(
            DateTime time)
        {
            return
                time.Hour * 10000
                + time.Minute * 100
                + time.Second;
        }


        private static int MinutesBetween(
            int startTime,
            int endTime)
        {
            var startHour =
                startTime / 10000;

            var startMinute =
                (startTime / 100) % 100;

            var endHour =
                endTime / 10000;

            var endMinute =
                (endTime / 100) % 100;


            return
                (endHour * 60 + endMinute)
                -
                (startHour * 60 + startMinute);
        }


        private void Diagnostic(
            DateTime time,
            string format,
            params object[] args)
        {
            if (!EnableDiagnostics)
                return;


            var message =
                args == null
                || args.Length == 0
                    ? format
                    : string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        format,
                        args);


            Print(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {2}",
                    time,
                    Name,
                    message));
        }

        #endregion


        #region Properties

        [NinjaScriptProperty]
        [Display(
            Name = "Portfolio Mode",
            Description = "Selects the retained baseline A/B portfolio, the exact research-filtered four-model portfolio, or its no-fast-EMA comparison.",
            GroupName = "1. Models",
            Order = 0)]
        public NinjexOvernightEdgePortfolioMode PortfolioMode
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Display(
            Name = "Enable Long Model",
            Description = "Used only when Portfolio Mode is BaselineAB.",
            GroupName = "1. Models",
            Order = 1)]
        public bool EnableLongModel
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Short Model",
            Description = "Used only when Portfolio Mode is BaselineAB.",
            GroupName = "1. Models",
            Order = 2)]
        public bool EnableShortModel
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Overnight Start Time",
            GroupName = "2. Session",
            Order = 0)]
        public int OvernightStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Premarket Start Time",
            GroupName = "2. Session",
            Order = 1)]
        public int PremarketStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Market Open Time",
            GroupName = "2. Session",
            Order = 2)]
        public int MarketOpenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Entry Start Time",
            GroupName = "2. Session",
            Order = 3)]
        public int EntryStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Entry End Time",
            GroupName = "2. Session",
            Order = 4)]
        public int EntryEndTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(
            Name = "Flatten Time",
            GroupName = "2. Session",
            Order = 5)]
        public int FlattenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Require Complete Overnight Range",
            GroupName = "2. Session",
            Order = 6)]
        public bool RequireCompleteOvernightRange
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Expected Overnight Bars",
            Description = "Expected completed 5-minute bars in a normal 18:00-09:30 overnight range.",
            GroupName = "2. Session",
            Order = 7)]
        public int ExpectedOvernightBars
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Require Complete Premarket Range",
            GroupName = "2. Session",
            Order = 8)]
        public bool RequireCompletePremarketRange
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Expected Premarket Bars",
            Description = "Expected completed 5-minute bars in the normal 03:00-09:30 premarket range.",
            GroupName = "2. Session",
            Order = 9)]
        public int ExpectedPremarketBars
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "ATR Period",
            GroupName = "3. Indicators",
            Order = 0)]
        public int AtrPeriod
        {
            get;
            set;
        } = 14;


        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "EMA Fast Period",
            GroupName = "3. Indicators",
            Order = 1)]
        public int EmaFastPeriod
        {
            get;
            set;
        } = 9;


        [NinjaScriptProperty]
        [Range(1, 240)]
        [Display(
            Name = "EMA Slow Period",
            GroupName = "3. Indicators",
            Order = 2)]
        public int EmaSlowPeriod
        {
            get;
            set;
        } = 21;


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "Long Max Minutes From Open",
            GroupName = "4. Long Model",
            Order = 0)]
        public int LongMaxMinutesFromOpen
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "Long Minimum ATR5 Ticks",
            GroupName = "4. Long Model",
            Order = 1)]
        public double LongMinimumAtr5mTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(
            Name = "Short Maximum Overnight Width Ticks",
            GroupName = "5. Short Model",
            Order = 0)]
        public double ShortMaximumOvernightWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1.0, 1000.0)]
        [Display(
            Name = "PDC Maximum 1-Minute Range Ticks",
            GroupName = "6. Four Model",
            Order = 0)]
        public double PriorCloseMaximumRangeTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(
            Name = "PDC Minimum Overnight Width Ticks",
            GroupName = "6. Four Model",
            Order = 1)]
        public double PriorCloseMinimumOvernightWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "PMH Maximum Minutes From Open",
            GroupName = "6. Four Model",
            Order = 2)]
        public int PremarketHighMaximumMinutesFromOpen
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "PMH Minimum ATR5 Ticks",
            GroupName = "6. Four Model",
            Order = 3)]
        public double PremarketHighMinimumAtr5mTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "RTH Open Minimum Minutes From Open",
            GroupName = "6. Four Model",
            Order = 4)]
        public int RthOpenMinimumMinutesFromOpen
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 5000.0)]
        [Display(
            Name = "RTH Open Minimum Premarket Width Ticks",
            GroupName = "6. Four Model",
            Order = 5)]
        public double RthOpenMinimumPremarketWidthTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(
            Name = "PML Minimum ATR5 Ticks",
            Description = "FourModelResearchFiltered additionally requires raw 1-minute close above the completed 5-minute EMA Fast.",
            GroupName = "6. Four Model",
            Order = 6)]
        public double PremarketLowMinimumAtr5mTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(
            Name = "Order Quantity",
            GroupName = "7. Risk",
            Order = 0)]
        public int OrderQuantity
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(
            Name = "Stop Loss Ticks",
            GroupName = "7. Risk",
            Order = 1)]
        public int StopLossTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 5000)]
        [Display(
            Name = "Profit Target Ticks",
            GroupName = "7. Risk",
            Order = 2)]
        public int ProfitTargetTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(
            Name = "Max Hold Minutes",
            Description = "Maximum minutes from the completed signal to a market exit. 0 disables this limit; 60 mirrors the research horizon.",
            GroupName = "7. Risk",
            Order = 3)]
        public int MaxHoldMinutes
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Trades Per Day",
            Description = "0 disables this limit.",
            GroupName = "7. Risk",
            Order = 4)]
        public int MaxTradesPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Winners Per Day",
            Description = "0 disables this limit.",
            GroupName = "7. Risk",
            Order = 5)]
        public int MaxWinnersPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Losing Trades Per Day",
            Description = "Maximum completed losing trades per ET trading date. 0 disables this limit; 1 stops after the first loss; 2 stops after the second loss. Losses use gross trade PnL, matching the winner counter.",
            GroupName = "7. Risk",
            Order = 6)]
        public int MaxLossesPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Diagnostics",
            GroupName = "8. Diagnostics",
            Order = 0)]
        public bool EnableDiagnostics
        {
            get;
            set;
        }

        #endregion
    }
}
