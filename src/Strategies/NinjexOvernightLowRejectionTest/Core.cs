#region Using declarations

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Ninjex;

#endregion


namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Experimental execution-only strategy used to test whether
    /// bullish rejection/reclaim behaviour at the overnight low
    /// has a viable long-side edge.
    ///
    /// This is intentionally isolated from the main
    /// NinjexPremarketRange research architecture.
    ///
    /// Data model:
    ///     BIP 0 = chart/context series (expected 5 minute)
    ///     BIP 1 = 1 minute signal series
    ///     BIP 2 = 1 tick execution series
    ///
    /// Initial hypothesis:
    ///     1. Build overnight range from 18:00 -> 09:30.
    ///     2. After 09:35, price trades at/below overnight low.
    ///     3. Completed 1 minute candle closes back above overnight low.
    ///     4. Rejection candle must be bullish and meet minimum body %.
    ///     5. Enter long on the first causal tick after confirmation.
    ///     6. Stop uses rejection-candle structural low, capped at MaxStopTicks.
    ///     7. Fixed RiskRewardRatio target.
    /// </summary>
    public class NinjexOvernightLowRejectionTest : Strategy
    {
        private const int ContextSeriesIndex = 0;
        private const int EntrySeriesIndex = 1;
        private const int TickSeriesIndex = 2;

        private const string EntrySignalName = "ONL-LONG";

        private NinjexPremarketRangeEngine overnightRangeEngine;

        private DateTime activeTradingDate =
            Core.Globals.MinDate;

        private double overnightLow =
            double.NaN;

        private double overnightHigh =
            double.NaN;

        private bool rangeReady;

        //
        // Rejection-attempt state.
        //
        private int rejectionAttempt;

        private bool touchEpisodeActive;

        //
        // Pending causal entry.
        //
        private bool pendingLongEntry;

        private DateTime pendingSignalTime =
            Core.Globals.MinDate;

        private int pendingAttempt;

        private double pendingStructuralStop =
            double.NaN;

        private double pendingSignalClose =
            double.NaN;

        private double pendingSignalLow =
            double.NaN;

        //
        // Active execution state.
        //
        private bool entryOrderPending;

        private double activeEntryPrice =
            double.NaN;

        private double activeStopPrice =
            double.NaN;

        private double activeTargetPrice =
            double.NaN;

        private int activeRiskTicks;

        private int tradesToday;


        #region NinjaScript lifecycle

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description =
                    "Experimental overnight-low bullish rejection strategy.";

                Name =
                    "Ninjex Overnight Low Rejection Test";

                Calculate =
                    Calculate.OnEachTick;

                EntriesPerDirection = 1;

                EntryHandling =
                    EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy =
                    false;

                ExitOnSessionCloseSeconds =
                    30;

                IsInstantiatedOnEachOptimizationIteration =
                    false;

                StartBehavior =
                    StartBehavior.WaitUntilFlat;

                RealtimeErrorHandling =
                    RealtimeErrorHandling.StopCancelClose;

                StopTargetHandling =
                    StopTargetHandling.PerEntryExecution;

                BarsRequiredToTrade = 20;


                //
                // Session / range
                //
                OvernightStartTime = 180000;
                MarketOpenTime = 93000;

                EntryStartTime = 93500;
                EntryEndTime = 160000;

                FlattenTime = 165500;


                //
                // Signal
                //
                MinimumPenetrationTicks = 0;

                MinimumReclaimTicks = 1;

                MinimumBodyPercent = 50;
                
                MinimumCloseLocationPercent = 60;

                ResetDistanceTicks = 2;

                AttemptMin = 1;

                AttemptMax = 3;


                //
                // Risk
                //
                Quantity = 1;

                MaximumInitialStopTicks = 40;

                RiskRewardRatio = 2.0;


                //
                // Diagnostics
                //
                EnableDiagnostics = true;
            }
            else if (State == State.Configure)
            {
                //
                // BIP 1: completed 1-minute signal candles.
                //
                AddDataSeries(
                    BarsPeriodType.Minute,
                    1);

                //
                // BIP 2: causal execution.
                //
                AddDataSeries(
                    BarsPeriodType.Tick,
                    1);
            }
            else if (State == State.DataLoaded)
            {
                overnightRangeEngine =
                    new NinjexPremarketRangeEngine();
            }
        }


        protected override void OnBarUpdate()
        {
            if (CurrentBars[ContextSeriesIndex] < 2
                || CurrentBars[EntrySeriesIndex] < 2
                || CurrentBars[TickSeriesIndex] < 1)
            {
                return;
            }

            if (BarsInProgress == ContextSeriesIndex)
            {
                ProcessContextSeries();
                return;
            }

            if (BarsInProgress == EntrySeriesIndex)
            {
                ProcessOneMinuteSeries();
                return;
            }

            if (BarsInProgress == TickSeriesIndex)
            {
                ProcessTickSeries();
            }
        }

        #endregion


        #region Overnight range

        private void ProcessContextSeries()
        {
            //
            // We only process the completed context bar.
            //
            if (!IsFirstTickOfBar)
                return;

            var barTime =
                Times[ContextSeriesIndex][1];

            EnsureTradingDate(
                ResolveTradingDate(
                    barTime));

            var finalizedNow =
                overnightRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        Highs[ContextSeriesIndex][1],
                        Lows[ContextSeriesIndex][1],
                        KeyLevelsMode.Overnight,
                        30000,
                        OvernightStartTime,
                        MarketOpenTime);

            if (!finalizedNow)
                return;

            if (!overnightRangeEngine.IsRangeComplete)
                return;

            overnightHigh =
                overnightRangeEngine.LatestHigh;

            overnightLow =
                overnightRangeEngine.LatestLow;

            rangeReady =
                !double.IsNaN(overnightHigh)
                && !double.IsNaN(overnightLow)
                && overnightHigh > overnightLow;

            if (!rangeReady)
                return;

            Diagnostic(
                barTime,
                "OVERNIGHT RANGE READY " +
                "Date={0:yyyy-MM-dd} " +
                "High={1} Low={2} " +
                "HighTime={3:HH:mm} " +
                "LowTime={4:HH:mm} " +
                "Bars={5}",
                activeTradingDate,
                overnightHigh,
                overnightLow,
                overnightRangeEngine.HighBarTime,
                overnightRangeEngine.LowBarTime,
                overnightRangeEngine.RangeBarCount);
        }

        #endregion


        #region Signal detection

        private void ProcessOneMinuteSeries()
        {
            //
            // Work only when a new 1-minute candle starts,
            // therefore [1] is the completed confirmation candle.
            //
            if (!IsFirstTickOfBar)
                return;

            var barTime =
                Times[EntrySeriesIndex][1];

            var tradingDate =
                ResolveTradingDate(
                    barTime);

            EnsureTradingDate(
                tradingDate);

            if (!rangeReady)
                return;

            var timeValue =
                ToTime(barTime);

            if (timeValue < EntryStartTime
                || timeValue >= EntryEndTime)
            {
                return;
            }
            
            var open =
                Opens[EntrySeriesIndex][1];

            var high =
                Highs[EntrySeriesIndex][1];

            var low =
                Lows[EntrySeriesIndex][1];

            var close =
                Closes[EntrySeriesIndex][1];


            //
            // Always maintain touch-episode state,
            // even while an entry/position is active.
            //
            if (touchEpisodeActive
                && low
                > overnightLow
                  + ResetDistanceTicks * TickSize)
            {
                touchEpisodeActive = false;
            }


            //
            // Do not generate a new setup while an entry
            // or position is active.
            //
            if (pendingLongEntry
                || entryOrderPending
                || Position.MarketPosition
                    != MarketPosition.Flat)
            {
                return;
            }


            //
            // Must trade at or through overnight low.
            //
            var penetrationTicks =
                (overnightLow - low)
                / TickSize;

            var touchedLow =
                low <= overnightLow
                    - MinimumPenetrationTicks
                    * TickSize;

            if (!touchedLow)
                return;


            //
            // One attempt per distinct touch episode.
            //
            if (touchEpisodeActive)
                return;

            touchEpisodeActive = true;

            rejectionAttempt++;


            var reclaimTicks =
                (close - overnightLow)
                / TickSize;

            var bullish =
                close > open;

            var candleRange =
                high - low;

            var closeLocationPercent =
                candleRange > 0
                    ? (close - low)
                      / candleRange
                      * 100.0
                    : 0;

            var body =
                Math.Abs(
                    close - open);

            var bodyPercent =
                candleRange > 0
                    ? body
                      / candleRange
                      * 100.0
                    : 0;


            Diagnostic(
                barTime,
                "OVERNIGHT LOW TEST " +
                "Attempt={0} " +
                "Level={1} Low={2} Close={3} " +
                "Penetration={4:0.0}t " +
                "Reclaim={5:0.0}t " +
                "CloseLocation={6:0.0}% " +
                "Body={7:0.0}% " +
                "Bullish={8}",
                rejectionAttempt,
                overnightLow,
                low,
                close,
                penetrationTicks,
                reclaimTicks,
                closeLocationPercent,
                bodyPercent,
                bullish);


            if (rejectionAttempt < AttemptMin
                || rejectionAttempt > AttemptMax)
            {
                return;
            }


            var reclaimed =
                close >= overnightLow
                    + MinimumReclaimTicks
                    * TickSize;

            if (!reclaimed)
                return;


            //
            // Signal accepted.
            //
            pendingLongEntry = true;

            pendingSignalTime =
                barTime;

            pendingAttempt =
                rejectionAttempt;

            pendingStructuralStop =
                low;

            pendingSignalClose =
                close;

            pendingSignalLow =
                low;


            Diagnostic(
                barTime,
                "LONG SIGNAL " +
                "Attempt={0} " +
                "OvernightLow={1} " +
                "SignalClose={2} " +
                "StructuralStop={3} " +
                "Penetration={4:0.0}t " +
                "Reclaim={5:0.0}t " +
                "CloseLocation={6:0.0}% " +
                "Body={7:0.0}% " +
                "Bullish={8}",
                pendingAttempt,
                overnightLow,
                pendingSignalClose,
                pendingStructuralStop,
                penetrationTicks,
                reclaimTicks,
                closeLocationPercent,
                bodyPercent,
                bullish);

            if (rejectionAttempt < AttemptMin
                || rejectionAttempt > AttemptMax)
            {
                return;
            }
            
            //
            // Signal accepted.
            //
            pendingLongEntry = true;

            pendingSignalTime =
                barTime;

            pendingAttempt =
                rejectionAttempt;

            pendingStructuralStop =
                low;

            pendingSignalClose =
                close;

            pendingSignalLow =
                low;


            Diagnostic(
                barTime,
                "LONG SIGNAL " +
                "Attempt={0} " +
                "OvernightLow={1} " +
                "SignalClose={2} " +
                "StructuralStop={3} " +
                "CloseLocation={4:0.0}% " +
                "Body={5:0.0}% " +
                "Reclaim={6:0.0}t",
                pendingAttempt,
                overnightLow,
                pendingSignalClose,
                pendingStructuralStop,
                closeLocationPercent,
                bodyPercent,
                reclaimTicks);
        }

        #endregion


        #region Tick execution

        private void ProcessTickSeries()
        {
            var time =
                Times[TickSeriesIndex][0];

            var price =
                Closes[TickSeriesIndex][0];

            EnsureTradingDate(
                ResolveTradingDate(
                    time));


            //
            // End-of-day flatten.
            //
            if (ToTime(time) >= FlattenTime)
            {
                pendingLongEntry = false;

                FlattenPosition(
                    time,
                    "FlattenTime");

                return;
            }


            if (!pendingLongEntry)
                return;

            if (time <= pendingSignalTime)
                return;

            if (ToTime(time) >= EntryEndTime)
            {
                pendingLongEntry = false;

                Diagnostic(
                    time,
                    "LONG ENTRY SKIP " +
                    "Reason=EntryWindowClosed " +
                    "Attempt={0}",
                    pendingAttempt);

                return;
            }

            if (Position.MarketPosition
                != MarketPosition.Flat)
            {
                pendingLongEntry = false;
                return;
            }

            if (entryOrderPending)
                return;


            SubmitPendingLong(
                time,
                price);
        }


        private void SubmitPendingLong(
            DateTime time,
            double marketPrice)
        {
            if (double.IsNaN(
                    pendingStructuralStop)
                || pendingStructuralStop <= 0
                || marketPrice <= 0
                || TickSize <= 0)
            {
                pendingLongEntry = false;
                return;
            }


            //
            // Structural risk is measured from the first causal
            // executable tick to the rejection candle low.
            //
            var structuralRiskTicks =
                (marketPrice
                 - pendingStructuralStop)
                / TickSize;

            if (structuralRiskTicks <= 0)
            {
                Diagnostic(
                    time,
                    "LONG ENTRY SKIP " +
                    "Reason=InvalidStructuralRisk " +
                    "Attempt={0} Market={1} Stop={2} " +
                    "Risk={3:0.0}t",
                    pendingAttempt,
                    marketPrice,
                    pendingStructuralStop,
                    structuralRiskTicks);

                pendingLongEntry = false;
                return;
            }


            var riskTicks =
                Math.Min(
                    MaximumInitialStopTicks,
                    structuralRiskTicks);


            //
            // Managed orders using ticks are deliberate here.
            //
            // NinjaTrader anchors the protective orders to the
            // actual market-order fill rather than our observed
            // pre-submission price. This prevents stale absolute
            // stop/target prices when Playback moves quickly.
            //
            var roundedRiskTicks =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        riskTicks
                        - 0.0000001));


            var targetTicks =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        roundedRiskTicks
                        * RiskRewardRatio
                        - 0.0000001));


            activeRiskTicks =
                roundedRiskTicks;


            SetStopLoss(
                EntrySignalName,
                CalculationMode.Ticks,
                roundedRiskTicks,
                false);

            SetProfitTarget(
                EntrySignalName,
                CalculationMode.Ticks,
                targetTicks);


            entryOrderPending = true;

            pendingLongEntry = false;


            Diagnostic(
                time,
                "LONG ENTRY SUBMIT " +
                "Attempt={0} " +
                "Market={1} " +
                "OvernightLow={2} " +
                "SignalClose={3} " +
                "SignalLow={4} " +
                "StructuralRisk={5:0.0}t " +
                "ExecutionRisk={6}t " +
                "Target={7}t " +
                "Qty={8}",
                pendingAttempt,
                marketPrice,
                overnightLow,
                pendingSignalClose,
                pendingSignalLow,
                structuralRiskTicks,
                roundedRiskTicks,
                targetTicks,
                Quantity);


            EnterLong(
                EntrySeriesIndex,
                Quantity,
                EntrySignalName);
        }

        #endregion


        #region Order / execution events

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

            if (!string.Equals(
                    order.Name,
                    EntrySignalName,
                    StringComparison.Ordinal))
            {
                return;
            }


            if (orderState == OrderState.Rejected
                || orderState == OrderState.Cancelled)
            {
                Diagnostic(
                    time,
                    "LONG ENTRY FAILED " +
                    "Order={0} State={1} " +
                    "Error={2} NativeError={3}",
                    order.Name,
                    orderState,
                    error,
                    nativeError);

                entryOrderPending = false;

                activeRiskTicks = 0;
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
            if (execution?.Order == null)
                return;


            var orderName =
                execution.Order.Name
                ?? string.Empty;


            if (string.Equals(
                    orderName,
                    EntrySignalName,
                    StringComparison.Ordinal)
                && execution.Order.OrderState
                    == OrderState.Filled)
            {
                entryOrderPending = false;

                activeEntryPrice =
                    execution.Order.AverageFillPrice;

                activeStopPrice =
                    Instrument.MasterInstrument
                        .RoundToTickSize(
                            activeEntryPrice
                            - activeRiskTicks
                            * TickSize);

                var targetTicks =
                    Math.Max(
                        1,
                        (int)Math.Ceiling(
                            activeRiskTicks
                            * RiskRewardRatio
                            - 0.0000001));

                activeTargetPrice =
                    Instrument.MasterInstrument
                        .RoundToTickSize(
                            activeEntryPrice
                            + targetTicks
                            * TickSize);


                tradesToday++;


                Diagnostic(
                    time,
                    "LONG ENTRY FILLED " +
                    "Attempt={0} " +
                    "Qty={1} Fill={2} " +
                    "Risk={3}t " +
                    "ExpectedStop={4} " +
                    "ExpectedTarget={5}",
                    pendingAttempt,
                    quantity,
                    activeEntryPrice,
                    activeRiskTicks,
                    activeStopPrice,
                    activeTargetPrice);

                return;
            }


            //
            // Protective exit / manual flatten completed.
            //
            if (Position.MarketPosition
                == MarketPosition.Flat
                && !double.IsNaN(
                    activeEntryPrice))
            {
                var realizedTicks =
                    (price
                     - activeEntryPrice)
                    / TickSize;

                var realizedR =
                    activeRiskTicks > 0
                        ? realizedTicks
                          / activeRiskTicks
                        : 0;


                Diagnostic(
                    time,
                    "LONG POSITION FLAT " +
                    "Execution={0} " +
                    "Entry={1} Exit={2} " +
                    "RealizedTicks={3:0.0}t " +
                    "RealizedR={4:0.000}R",
                    orderName,
                    activeEntryPrice,
                    price,
                    realizedTicks,
                    realizedR);


                ResetActiveExecution();
            }
        }

        #endregion


        #region Trading-date state

        private DateTime ResolveTradingDate(
            DateTime time)
        {
            //
            // Overnight bars after 18:00 belong to the following
            // trading/range date.
            //
            if (ToTime(time) > OvernightStartTime)
                return time.Date.AddDays(1);

            return time.Date;
        }


        private void EnsureTradingDate(
            DateTime tradingDate)
        {
            tradingDate =
                tradingDate.Date;

            if (activeTradingDate
                == tradingDate)
            {
                return;
            }


            if (activeTradingDate
                != Core.Globals.MinDate)
            {
                //
                // Safety fallback in case the previous session had
                // no normal flatten observation.
                //
                FlattenPosition(
                    tradingDate,
                    "NewTradingDate");
            }


            activeTradingDate =
                tradingDate;

            overnightHigh =
                double.NaN;

            overnightLow =
                double.NaN;

            rangeReady = false;

            rejectionAttempt = 0;

            touchEpisodeActive = false;

            pendingLongEntry = false;

            pendingSignalTime =
                Core.Globals.MinDate;

            pendingAttempt = 0;

            pendingStructuralStop =
                double.NaN;

            pendingSignalClose =
                double.NaN;

            pendingSignalLow =
                double.NaN;

            entryOrderPending = false;

            tradesToday = 0;

            ResetActiveExecution();


            Diagnostic(
                tradingDate,
                "NEW TRADING DATE {0:yyyy-MM-dd}",
                tradingDate);
        }

        #endregion


        #region Flatten / reset

        private void FlattenPosition(
            DateTime time,
            string reason)
        {
            if (Position.MarketPosition
                != MarketPosition.Long)
            {
                return;
            }


            Diagnostic(
                time,
                "LONG FLATTEN REQUEST " +
                "Reason={0} Qty={1}",
                reason,
                Position.Quantity);


            ExitLong(
                EntrySeriesIndex,
                Position.Quantity,
                "ONL-FLAT",
                EntrySignalName);
        }


        private void ResetActiveExecution()
        {
            entryOrderPending = false;

            activeEntryPrice =
                double.NaN;

            activeStopPrice =
                double.NaN;

            activeTargetPrice =
                double.NaN;

            activeRiskTicks = 0;
        }

        #endregion


        #region Diagnostics

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
                        format,
                        args);

            Print(
                string.Format(
                    "{0:yyyy-MM-dd HH:mm:ss.fff} | " +
                    "{1} | {2}",
                    time,
                    Name,
                    message));
        }

        #endregion


        #region Helpers

        private static int ToTime(
            DateTime time)
        {
            return
                time.Hour * 10000
                + time.Minute * 100
                + time.Second;
        }

        #endregion


        #region Properties

        [NinjaScriptProperty]
        [Display(
            Name = "Overnight Start Time",
            Order = 1,
            GroupName = "Session")]
        public int OvernightStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Market Open Time",
            Order = 2,
            GroupName = "Session")]
        public int MarketOpenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Entry Start Time",
            Order = 3,
            GroupName = "Session")]
        public int EntryStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Entry End Time",
            Order = 4,
            GroupName = "Session")]
        public int EntryEndTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Flatten Time",
            Order = 5,
            GroupName = "Session")]
        public int FlattenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Minimum Penetration Ticks",
            Order = 1,
            GroupName = "Entry")]
        public int MinimumPenetrationTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Minimum Reclaim Ticks",
            Order = 2,
            GroupName = "Entry")]
        public int MinimumReclaimTicks
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Minimum Close Location %",
            Order = 3,
            GroupName = "Entry")]
        public double MinimumCloseLocationPercent
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Minimum Body %",
            Order = 3,
            GroupName = "Entry")]
        public double MinimumBodyPercent
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(
            Name = "Reset Distance Ticks",
            Description =
                "Price must move this many ticks above the overnight low before a new rejection attempt can begin.",
            Order = 4,
            GroupName = "Entry")]
        public int ResetDistanceTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(
            Name = "Attempt Min",
            Order = 5,
            GroupName = "Entry")]
        public int AttemptMin
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(
            Name = "Attempt Max",
            Order = 6,
            GroupName = "Entry")]
        public int AttemptMax
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(
            Name = "Quantity",
            Order = 1,
            GroupName = "Risk")]
        public int Quantity
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(
            Name = "Maximum Initial Stop Ticks",
            Order = 2,
            GroupName = "Risk")]
        public int MaximumInitialStopTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(
            Name = "Risk To Reward Ratio",
            Order = 3,
            GroupName = "Risk")]
        public double RiskRewardRatio
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Enable Diagnostics",
            Order = 1,
            GroupName = "Diagnostics")]
        public bool EnableDiagnostics
        {
            get;
            set;
        }

        #endregion
    }
}