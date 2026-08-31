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
    /// <summary>
    /// Execution strategy built from the neutral ES market-research study.
    ///
    /// Expected chart:
    ///     ES 5-minute
    ///     CME US Index Futures ETH
    ///     Same chart timezone used by the market-research collector
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
    ///     - 1-minute bar sweeps above Overnight High and closes back below it.
    ///     - Signal close remains above the completed 5-minute slow EMA.
    ///     - Overnight range width <= 300 ticks.
    ///
    /// Execution:
    ///     - First causal tick after the completed 1-minute signal.
    ///     - Fixed 20-tick stop / 40-tick target.
    ///     - Protective orders are fill-relative.
    ///     - No break-even.
    ///
    /// Portfolio controls:
    ///     - Maximum 3 entries per RTH day.
    ///     - Maximum 2 winning trades per RTH day.
    ///     - Do not stop after the first loss by default.
    ///
    /// Both models live in one strategy deliberately: this preserves the
    /// shared daily portfolio limits found in research. Each model can be
    /// independently disabled for isolated validation.
    /// </summary>
    public class NinjexOvernightEdgePortfolio : Strategy
    {
        private const string StrategyVersion = "1.0.0";

        private const int ContextSeriesIndex = 0;
        private const int SignalSeriesIndex = 1;
        private const int TickSeriesIndex = 2;

        private const string LongEntrySignal = "ONL-RECLAIM-L";
        private const string ShortEntrySignal = "ONH-FAILURE-S";

        private const string LongEodExitSignal = "EOD-L";
        private const string ShortEodExitSignal = "EOD-S";


        private enum PendingDirection
        {
            None,
            Long,
            Short
        }


        #region Engines / indicators

        private NinjexPremarketRangeEngine overnightRangeEngine;

        private ATR atr5m;
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

        #endregion


        #region Completed 5-minute context

        private DateTime last5mTime =
            Core.Globals.MinDate;

        private double last5mAtrTicks =
            double.NaN;

        private double last5mEmaSlow =
            double.NaN;

        #endregion


        #region Pending causal entry

        private PendingDirection pendingDirection =
            PendingDirection.None;

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

        private double pendingOvernightWidthTicks =
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
                    "Two-model ES overnight-extreme execution strategy derived from neutral market research.";

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


                //
                // Indicators
                //
                AtrPeriod = 14;
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
                // Risk
                //
                OrderQuantity = 1;

                StopLossTicks = 20;
                ProfitTargetTicks = 40;

                MaxTradesPerDay = 3;
                MaxWinnersPerDay = 2;

                StopAfterFirstLoss = false;


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

                atr5m =
                    ATR(
                        Closes[ContextSeriesIndex],
                        AtrPeriod);

                emaSlow5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaSlowPeriod);

                Diagnostic(
                    DateTime.Now,
                    "READY Version={0} " +
                    "Long={1} Short={2} " +
                    "Stop={3}t Target={4}t " +
                    "MaxTrades={5} MaxWinners={6}",
                    StrategyVersion,
                    EnableLongModel,
                    EnableShortModel,
                    StopLossTicks,
                    ProfitTargetTicks,
                    MaxTradesPerDay,
                    MaxWinnersPerDay);
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
                        AtrPeriod,
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
            var finalizedNow =
                overnightRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Overnight,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);


            if (finalizedNow
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


            //
            // Cache exactly the completed 5-minute context.
            // This mirrors the neutral collector: signal rows do not
            // peek into the still-forming 5-minute candle.
            //
            last5mTime =
                barTime;

            last5mAtrTicks =
                atr5m[1] / TickSize;

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

            var currentMinuteTime =
                Times[SignalSeriesIndex][0];

            EnsureTradingDate(
                signalTime.Date,
                signalTime);


            var timeValue =
                ToTimeValue(
                    signalTime);


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


            var open =
                Opens[SignalSeriesIndex][1];

            var high =
                Highs[SignalSeriesIndex][1];

            var low =
                Lows[SignalSeriesIndex][1];

            var close =
                Closes[SignalSeriesIndex][1];


            var minutesFromOpen =
                MinutesBetween(
                    MarketOpenTime,
                    timeValue);

            var overnightWidthTicks =
                GetOvernightWidthTicks();


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
            // Sweep above Overnight High + completed close back below,
            // while close is still above the completed 5-minute slow EMA.
            //
            var shortSweep =
                high > overnightHigh
                && close < overnightHigh;

            var shortEmaOk =
                IsFinite(last5mEmaSlow)
                && close > last5mEmaSlow;

            var shortWidthOk =
                IsFinite(overnightWidthTicks)
                && overnightWidthTicks
                    <= ShortMaximumOvernightWidthTicks;

            var shortQualified =
                EnableShortModel
                && shortSweep
                && shortEmaOk
                && shortWidthOk;


            if (EnableShortModel
                && shortSweep)
            {
                Diagnostic(
                    signalTime,
                    "SHORT CHECK " +
                    "Qualified={0} " +
                    "ONH={1} High={2} Close={3} " +
                    "EMA5Slow={4} CloseAboveEMA={5} " +
                    "ONWidth={6:0.0}t MaxWidth={7:0.0}t",
                    shortQualified,
                    overnightHigh,
                    high,
                    close,
                    last5mEmaSlow,
                    shortEmaOk,
                    overnightWidthTicks,
                    ShortMaximumOvernightWidthTicks);
            }


            //
            // A pathological 1-minute candle can technically sweep both
            // extremes. The research did not establish an ordering rule
            // for such a candle, so do not invent one.
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
                    PendingDirection.Long,
                    signalTime,
                    currentMinuteTime,
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
                    PendingDirection.Short,
                    signalTime,
                    currentMinuteTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);
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


            if (!IsFinite(last5mAtrTicks)
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

            pendingOvernightWidthTicks =
                overnightWidthTicks;

            pendingMinutesFromOpen =
                minutesFromOpen;


            Diagnostic(
                signalTime,
                "SIGNAL ARMED " +
                "Model={0} " +
                "EarliestTick={1:HH:mm:ss.fff} " +
                "Close={2} ATR5={3:0.0}t " +
                "EMA5Slow={4} ONWidth={5:0.0}t " +
                "Trades={6}/{7} Winners={8}/{9}",
                direction,
                pendingEarliestExecutionTime,
                pendingSignalClose,
                pendingAtr5mTicks,
                pendingEmaSlow5m,
                pendingOvernightWidthTicks,
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
                direction == PendingDirection.Long
                    ? LongEntrySignal
                    : ShortEntrySignal;


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
                "Model={0} ObservedMarket={1} " +
                "SignalTime={2:HH:mm:ss} " +
                "Stop={3}t Target={4}t " +
                "Qty={5}",
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
                    LongEntrySignal);
            }
            else
            {
                EnterShort(
                    TickSeriesIndex,
                    OrderQuantity,
                    ShortEntrySignal);
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


            if (StopAfterFirstLoss
                && lossesToday >= 1)
            {
                if (logReason)
                {
                    Diagnostic(
                        time,
                        "TRADE BLOCK " +
                        "Reason=StopAfterFirstLoss");
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


            if (order.Name == LongEntrySignal
                || order.Name == ShortEntrySignal)
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
                || order.Name == ShortEodExitSignal)
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
                order.Name
                == LongEntrySignal;

            var isShortEntry =
                order.Name
                == ShortEntrySignal;


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

                    activeEntryFilledQuantity = 0;
                    activeEntryPriceQuantity = 0;

                    activeTradeGrossPnl = 0;

                    tradesToday++;
                }


                activeEntryFilledQuantity +=
                    quantity;

                activeEntryPriceQuantity +=
                    price * quantity;


                Diagnostic(
                    time,
                    "ENTRY FILL " +
                    "Model={0} Price={1} Qty={2} " +
                    "AvgEntry={3:0.########} " +
                    "TradesToday={4}",
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
                (activeTradeDirection
                    == PendingDirection.Long
                    && fromEntrySignal
                        == LongEntrySignal)
                ||
                (activeTradeDirection
                    == PendingDirection.Short
                    && fromEntrySignal
                        == ShortEntrySignal);


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
                "Model={0} Order={1} Price={2} Qty={3} " +
                "ExecutionPnl={4:0.00} " +
                "TradeGross={5:0.00}",
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
                "Model={0} Exit={1} " +
                "GrossPnl={2:0.00} " +
                "DayTrades={3} Winners={4} Losses={5} " +
                "DayGross={6:0.00}",
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

            activeEntryFilledQuantity = 0;
            activeEntryPriceQuantity = 0;

            activeTradeGrossPnl = 0;

            activeEntryOrder = null;

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
                    LongEntrySignal);

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
                    ShortEntrySignal);
            }
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
                    "Model={0} Reason={1}",
                    pendingDirection,
                    reason);
            }


            pendingDirection =
                PendingDirection.None;

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

            pendingOvernightWidthTicks =
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
            Name = "Enable Long Model",
            GroupName = "1. Models",
            Order = 0)]
        public bool EnableLongModel
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Short Model",
            GroupName = "1. Models",
            Order = 1)]
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
            Name = "EMA Slow Period",
            GroupName = "3. Indicators",
            Order = 1)]
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
        [Range(1, 100)]
        [Display(
            Name = "Order Quantity",
            GroupName = "6. Risk",
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
            GroupName = "6. Risk",
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
            GroupName = "6. Risk",
            Order = 2)]
        public int ProfitTargetTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Max Trades Per Day",
            Description = "0 disables this limit.",
            GroupName = "6. Risk",
            Order = 3)]
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
            GroupName = "6. Risk",
            Order = 4)]
        public int MaxWinnersPerDay
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Stop After First Loss",
            GroupName = "6. Risk",
            Order = 5)]
        public bool StopAfterFirstLoss
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Diagnostics",
            GroupName = "7. Diagnostics",
            Order = 0)]
        public bool EnableDiagnostics
        {
            get;
            set;
        }

        #endregion
    }
}
