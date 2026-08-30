#region Using declarations

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Ninjex;

#endregion


namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Neutral ES market-state research collector.
    ///
    /// PURPOSE
    /// -------
    /// This strategy places NO trades.
    ///
    /// It records one observation for each completed 1-minute bar
    /// during the configured RTH observation window.
    ///
    /// Each observation contains:
    ///
    ///     - 1-minute OHLCV/candle structure
    ///     - compressed tick microstructure
    ///     - 1-minute ATR / EMA context
    ///     - 5-minute OHLC / ATR / ADX / EMA context
    ///     - custom RTH VWAP
    ///     - Overnight high / low
    ///     - Premarket high / low
    ///     - Previous RTH high / low / close
    ///     - RTH open
    ///     - Opening range
    ///     - distances to all key levels
    ///     - simple interaction flags
    ///     - time/day/session context
    ///
    /// It then tracks each observation forward for:
    ///
    ///     5 / 10 / 15 / 30 / 60 minutes
    ///
    /// recording:
    ///
    ///     - Long MFE / MAE
    ///     - Short MFE / MAE
    ///     - close return
    ///     - first-hit approximations for several
    ///       fixed target/stop geometries
    ///
    /// IMPORTANT:
    ///
    /// Barrier ordering is based on completed 1-minute bars.
    /// If target and stop occur inside the same bar, the result is
    /// explicitly marked "Ambiguous", rather than inventing an order.
    ///
    /// Once a promising cohort is discovered, use Market Replay /
    /// tick execution for final validation.
    ///
    /// EXPECTED PRIMARY SERIES:
    ///
    ///     ES 5-minute
    ///     CME US Index Futures ETH
    ///
    /// ADDED SERIES:
    ///
    ///     BIP 1 = 1-minute
    ///     BIP 2 = 1-tick
    /// </summary>
    public class NinjexEsMarketResearchCollector : Strategy
    {
        private const string CollectorVersion = "1.0.0";

        private const int ContextSeriesIndex = 0;
        private const int MinuteSeriesIndex = 1;
        private const int TickSeriesIndex = 2;

        private static readonly CultureInfo Inv =
            CultureInfo.InvariantCulture;


        #region Engines / indicators

        private NinjexPremarketRangeEngine overnightRangeEngine;
        private NinjexPremarketRangeEngine premarketRangeEngine;

        private ATR atr1m;
        private ATR atr5m;

        private EMA emaFast1m;
        private EMA emaSlow1m;

        private EMA emaFast5m;
        private EMA emaSlow5m;

        private ADX adx5m;

        #endregion


        #region Export

        private string runId;
        private string outputDirectory;

        private StreamWriter observationsWriter;
        private StreamWriter sessionsWriter;
        private StreamWriter manifestWriter;

        private int observationsWritten;
        private int sessionsWritten;

        #endregion


        #region Current RTH day

        private DateTime activeRthDate =
            Core.Globals.MinDate;

        private bool hasCurrentRthData;

        private double rthOpen =
            double.NaN;

        private double currentRthHigh =
            double.NaN;

        private double currentRthLow =
            double.NaN;

        private double currentRthClose =
            double.NaN;


        private double priorDayHigh =
            double.NaN;

        private double priorDayLow =
            double.NaN;

        private double priorDayClose =
            double.NaN;


        private double sessionOvernightHigh =
            double.NaN;

        private double sessionOvernightLow =
            double.NaN;

        private DateTime sessionOvernightHighTime =
            Core.Globals.MinDate;

        private DateTime sessionOvernightLowTime =
            Core.Globals.MinDate;

        private int sessionOvernightBars;


        private double sessionPremarketHigh =
            double.NaN;

        private double sessionPremarketLow =
            double.NaN;

        private DateTime sessionPremarketHighTime =
            Core.Globals.MinDate;

        private DateTime sessionPremarketLowTime =
            Core.Globals.MinDate;

        private int sessionPremarketBars;


        private bool openingRangeReady;

        private double openingRangeHigh =
            double.NaN;

        private double openingRangeLow =
            double.NaN;


        private double cumulativeVwapPriceVolume;

        private double cumulativeVwapVolume;

        private double currentVwap =
            double.NaN;


        private int observationsToday;

        #endregion


        #region Last 5-minute context

        private DateTime last5mTime =
            Core.Globals.MinDate;

        private double last5mOpen =
            double.NaN;

        private double last5mHigh =
            double.NaN;

        private double last5mLow =
            double.NaN;

        private double last5mClose =
            double.NaN;

        private double last5mVolume;

        private double last5mAtrTicks =
            double.NaN;

        private double last5mAdx =
            double.NaN;

        private double last5mEmaFast =
            double.NaN;

        private double last5mEmaSlow =
            double.NaN;

        private double last5mEmaFastSlopeTicks =
            double.NaN;

        private double last5mEmaSlowSlopeTicks =
            double.NaN;
        
        private DateTime lastTickDiagnosticDate =
            Core.Globals.MinDate;

        #endregion


        #region Tick aggregation

        private TickMinuteStats activeTickStats;

        private readonly Dictionary<DateTime, TickMinuteStats>
            completedTickStats =
                new Dictionary<DateTime, TickMinuteStats>();

        private double previousTickPrice =
            double.NaN;

        #endregion


        #region Forward tracking

        private readonly List<ForwardObservation>
            activeForwardObservations =
                new List<ForwardObservation>();

        #endregion


        #region Lifecycle

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name =
                    "Ninjex ES Market Research Collector";

                Description =
                    "Neutral ES market-state and forward-outcome data collector.";

                Calculate =
                    Calculate.OnEachTick;

                IsOverlay = false;

                IsExitOnSessionCloseStrategy =
                    false;

                BarsRequiredToTrade = 30;

                IsInstantiatedOnEachOptimizationIteration =
                    false;


                //
                // Range definitions.
                //
                OvernightStartTime = 180000;

                PremarketStartTime = 30000;

                MarketOpenTime = 93000;


                //
                // RTH research.
                //
                ObservationStartTime = 93500;

                ObservationEndTime = 160000;

                RthEndTime = 160000;

                OpeningRangeMinutes = 15;


                //
                // Indicators.
                //
                AtrPeriod = 14;

                AdxPeriod = 14;

                EmaFastPeriod = 9;

                EmaSlowPeriod = 21;

                RelativeVolumeLookback = 20;


                //
                // Forward study.
                //
                MaximumForwardMinutes = 60;


                //
                // Key-level interaction tolerance.
                //
                LevelTouchToleranceTicks = 1;


                //
                // Export.
                //
                OutputFolderName =
                    "NinjexData";

                OutputFilePrefix =
                    "es_market_research";


                EnableDiagnostics = true;
            }
            else if (State == State.Configure)
            {
                //
                // Primary chart should be ES 5-minute.
                //

                AddDataSeries(
                    BarsPeriodType.Minute,
                    1);

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


                atr1m =
                    ATR(
                        Closes[MinuteSeriesIndex],
                        AtrPeriod);

                atr5m =
                    ATR(
                        Closes[ContextSeriesIndex],
                        AtrPeriod);


                emaFast1m =
                    EMA(
                        Closes[MinuteSeriesIndex],
                        EmaFastPeriod);

                emaSlow1m =
                    EMA(
                        Closes[MinuteSeriesIndex],
                        EmaSlowPeriod);


                emaFast5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaFastPeriod);

                emaSlow5m =
                    EMA(
                        Closes[ContextSeriesIndex],
                        EmaSlowPeriod);


                adx5m =
                    ADX(
                        Closes[ContextSeriesIndex],
                        AdxPeriod);


                InitializeExport();
            }
            else if (State == State.Terminated)
            {
                try
                {
                    FinalizeTickMinute();

                    FinalizeRemainingForwardObservations(
                        "StrategyTerminated");

                    FinalizeCurrentSession(
                        "StrategyTerminated");

                    FlushWriters();
                }
                finally
                {
                    DisposeWriters();
                }
            }
        }


        protected override void OnBarUpdate()
        {
            if (BarsInProgress == TickSeriesIndex)
            {
                ProcessTickSeries();
                return;
            }

            if (BarsInProgress == ContextSeriesIndex)
            {
                ProcessFiveMinuteSeries();
                return;
            }

            if (BarsInProgress == MinuteSeriesIndex)
            {
                ProcessOneMinuteSeries();
            }
        }

        #endregion


        #region 5-minute context / range construction

        private void ProcessFiveMinuteSeries()
        {
            if (CurrentBars[ContextSeriesIndex] < 30)
                return;

            if (!IsFirstTickOfBar)
                return;


            var barTime =
                Times[ContextSeriesIndex][1];

            var open =
                Opens[ContextSeriesIndex][1];

            var high =
                Highs[ContextSeriesIndex][1];

            var low =
                Lows[ContextSeriesIndex][1];

            var close =
                Closes[ContextSeriesIndex][1];

            var volume =
                Volumes[ContextSeriesIndex][1];


            //
            // Build both key-level sets concurrently.
            //
            var overnightFinalized =
                overnightRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Overnight,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);


            var premarketFinalized =
                premarketRangeEngine
                    .ProcessCompletedBar(
                        barTime,
                        high,
                        low,
                        KeyLevelsMode.Premarket,
                        PremarketStartTime,
                        OvernightStartTime,
                        MarketOpenTime);


            if (overnightFinalized
                && overnightRangeEngine.IsRangeComplete)
            {
                sessionOvernightHigh =
                    overnightRangeEngine.LatestHigh;

                sessionOvernightLow =
                    overnightRangeEngine.LatestLow;

                sessionOvernightHighTime =
                    overnightRangeEngine.HighBarTime;

                sessionOvernightLowTime =
                    overnightRangeEngine.LowBarTime;

                sessionOvernightBars =
                    overnightRangeEngine.RangeBarCount;


                Diagnostic(
                    barTime,
                    "OVERNIGHT READY Date={0:yyyy-MM-dd} High={1} Low={2} Bars={3}",
                    overnightRangeEngine.LatestRangeDate,
                    sessionOvernightHigh,
                    sessionOvernightLow,
                    sessionOvernightBars);
            }


            if (premarketFinalized
                && premarketRangeEngine.IsRangeComplete)
            {
                sessionPremarketHigh =
                    premarketRangeEngine.LatestHigh;

                sessionPremarketLow =
                    premarketRangeEngine.LatestLow;

                sessionPremarketHighTime =
                    premarketRangeEngine.HighBarTime;

                sessionPremarketLowTime =
                    premarketRangeEngine.LowBarTime;

                sessionPremarketBars =
                    premarketRangeEngine.RangeBarCount;


                Diagnostic(
                    barTime,
                    "PREMARKET READY Date={0:yyyy-MM-dd} High={1} Low={2} Bars={3}",
                    premarketRangeEngine.LatestRangeDate,
                    sessionPremarketHigh,
                    sessionPremarketLow,
                    sessionPremarketBars);
            }


            //
            // Persist completed 5-minute context.
            //
            last5mTime =
                barTime;

            last5mOpen =
                open;

            last5mHigh =
                high;

            last5mLow =
                low;

            last5mClose =
                close;

            last5mVolume =
                volume;


            if (CurrentBars[ContextSeriesIndex]
                > Math.Max(
                    EmaSlowPeriod,
                    AtrPeriod) + 2)
            {
                last5mAtrTicks =
                    atr5m[1] / TickSize;

                last5mAdx =
                    adx5m[1];

                last5mEmaFast =
                    emaFast5m[1];

                last5mEmaSlow =
                    emaSlow5m[1];

                last5mEmaFastSlopeTicks =
                    (emaFast5m[1]
                     - emaFast5m[2])
                    / TickSize;

                last5mEmaSlowSlopeTicks =
                    (emaSlow5m[1]
                     - emaSlow5m[2])
                    / TickSize;
            }
        }

        #endregion


        #region 1-minute observations

        private void ProcessOneMinuteSeries()
        {
            if (CurrentBars[MinuteSeriesIndex] < 30)
                return;

            if (!IsFirstTickOfBar)
                return;


            var barTime =
                Times[MinuteSeriesIndex][1];

            var open =
                Opens[MinuteSeriesIndex][1];

            var high =
                Highs[MinuteSeriesIndex][1];

            var low =
                Lows[MinuteSeriesIndex][1];

            var close =
                Closes[MinuteSeriesIndex][1];

            var volume =
                Volumes[MinuteSeriesIndex][1];


            EnsureRthDate(
                barTime.Date,
                barTime);


            var timeValue =
                ToTime(barTime);


//
// Never allow forward labels to consume
// bars at/after the configured RTH end.
//
            if (timeValue >= RthEndTime)
            {
                FinalizeRemainingForwardObservations(
                    "RthEnd");

                return;
            }


//
// This completed bar belongs to the future path
// of every earlier observation.
//
            UpdateForwardObservations(
                barTime,
                high,
                low,
                close);


            if (timeValue < ObservationStartTime
                || timeValue >= ObservationEndTime)
            {
                return;
            }


            if (!HasRequiredKeyLevels())
            {
                Diagnostic(
                    barTime,
                    "OBSERVATION SKIP Reason=KeyLevelsNotReady");

                return;
            }


            var row =
                BuildObservation(
                    barTime,
                    open,
                    high,
                    low,
                    close,
                    volume);


            activeForwardObservations.Add(
                new ForwardObservation(
                    row,
                    TickSize));


            observationsToday++;
        }


        private MarketObservation BuildObservation(
            DateTime time,
            double open,
            double high,
            double low,
            double close,
            double volume)
        {
            var row =
                new MarketObservation();


            row.RunId =
                runId;

            row.Contract =
                Instrument?.FullName
                ?? string.Empty;

            row.TradingDate =
                activeRthDate;

            row.Time =
                time;

            row.DayOfWeek =
                time.DayOfWeek.ToString();

            row.MinutesFromOpen =
                MinutesBetween(
                    MarketOpenTime,
                    ToTime(time));


            //
            // 1-minute bar.
            //
            row.Open1m =
                open;

            row.High1m =
                high;

            row.Low1m =
                low;

            row.Close1m =
                close;

            row.Volume1m =
                volume;


            var range =
                high - low;

            var body =
                Math.Abs(
                    close - open);


            row.Range1mTicks =
                range / TickSize;

            row.Body1mTicks =
                body / TickSize;

            row.BodyPercent =
                range > 0
                    ? body / range * 100.0
                    : 0;


            row.CloseLocationPercent =
                range > 0
                    ? (close - low)
                      / range
                      * 100.0
                    : 0;


            row.UpperWickPercent =
                range > 0
                    ? (
                        high
                        - Math.Max(
                            open,
                            close)
                    ) / range * 100.0
                    : 0;


            row.LowerWickPercent =
                range > 0
                    ? (
                        Math.Min(
                            open,
                            close)
                        - low
                    ) / range * 100.0
                    : 0;


            row.Bullish =
                close > open;

            row.Bearish =
                close < open;


            //
            // 1-minute indicators.
            //
            row.Atr1mTicks =
                atr1m[1] / TickSize;

            row.EmaFast1m =
                emaFast1m[1];

            row.EmaSlow1m =
                emaSlow1m[1];

            row.EmaFastSlope1mTicks =
                (emaFast1m[1]
                 - emaFast1m[2])
                / TickSize;

            row.EmaSlowSlope1mTicks =
                (emaSlow1m[1]
                 - emaSlow1m[2])
                / TickSize;


            row.PriceVsEmaFast1mTicks =
                (close - row.EmaFast1m)
                / TickSize;

            row.PriceVsEmaSlow1mTicks =
                (close - row.EmaSlow1m)
                / TickSize;


            row.RelativeVolume =
                CalculateRelativeVolume(
                    volume);


            //
            // 5-minute context.
            //
            row.Context5mTime =
                last5mTime;

            row.Open5m =
                last5mOpen;

            row.High5m =
                last5mHigh;

            row.Low5m =
                last5mLow;

            row.Close5m =
                last5mClose;

            row.Volume5m =
                last5mVolume;

            row.Atr5mTicks =
                last5mAtrTicks;

            row.Adx5m =
                last5mAdx;

            row.EmaFast5m =
                last5mEmaFast;

            row.EmaSlow5m =
                last5mEmaSlow;

            row.EmaFastSlope5mTicks =
                last5mEmaFastSlopeTicks;

            row.EmaSlowSlope5mTicks =
                last5mEmaSlowSlopeTicks;


            row.PriceVsEmaFast5mTicks =
                IsFinite(last5mEmaFast)
                    ? (close - last5mEmaFast)
                      / TickSize
                    : double.NaN;

            row.PriceVsEmaSlow5mTicks =
                IsFinite(last5mEmaSlow)
                    ? (close - last5mEmaSlow)
                      / TickSize
                    : double.NaN;


            //
            // VWAP.
            //
            row.Vwap =
                currentVwap;

            row.DistanceFromVwapTicks =
                IsFinite(currentVwap)
                    ? (close - currentVwap)
                      / TickSize
                    : double.NaN;


            //
            // Key levels.
            //
            row.OvernightHigh =
                sessionOvernightHigh;

            row.OvernightLow =
                sessionOvernightLow;

            row.OvernightWidthTicks =
                (
                    sessionOvernightHigh
                    - sessionOvernightLow
                ) / TickSize;


            row.PremarketHigh =
                sessionPremarketHigh;

            row.PremarketLow =
                sessionPremarketLow;

            row.PremarketWidthTicks =
                (
                    sessionPremarketHigh
                    - sessionPremarketLow
                ) / TickSize;


            row.PriorDayHigh =
                priorDayHigh;

            row.PriorDayLow =
                priorDayLow;

            row.PriorDayClose =
                priorDayClose;

            row.RthOpen =
                rthOpen;

            row.OpeningRangeHigh =
                openingRangeHigh;

            row.OpeningRangeLow =
                openingRangeLow;

            row.OpeningRangeReady =
                openingRangeReady;


            //
            // Distances.
            //
            row.DistanceOvernightHighTicks =
                SignedDistanceTicks(
                    close,
                    sessionOvernightHigh);

            row.DistanceOvernightLowTicks =
                SignedDistanceTicks(
                    close,
                    sessionOvernightLow);

            row.DistancePremarketHighTicks =
                SignedDistanceTicks(
                    close,
                    sessionPremarketHigh);

            row.DistancePremarketLowTicks =
                SignedDistanceTicks(
                    close,
                    sessionPremarketLow);

            row.DistancePriorDayHighTicks =
                SignedDistanceTicks(
                    close,
                    priorDayHigh);

            row.DistancePriorDayLowTicks =
                SignedDistanceTicks(
                    close,
                    priorDayLow);

            row.DistancePriorCloseTicks =
                SignedDistanceTicks(
                    close,
                    priorDayClose);

            row.DistanceRthOpenTicks =
                SignedDistanceTicks(
                    close,
                    rthOpen);

            row.DistanceOpeningRangeHighTicks =
                SignedDistanceTicks(
                    close,
                    openingRangeHigh);

            row.DistanceOpeningRangeLowTicks =
                SignedDistanceTicks(
                    close,
                    openingRangeLow);


            //
            // Position inside important ranges.
            //
            row.PositionInOvernightRangePercent =
                PositionInRange(
                    close,
                    sessionOvernightLow,
                    sessionOvernightHigh);

            row.PositionInPremarketRangePercent =
                PositionInRange(
                    close,
                    sessionPremarketLow,
                    sessionPremarketHigh);


            //
            // Simple interaction flags.
            //
            PopulateLevelInteractionFlags(
                row,
                high,
                low,
                close);


            //
            // Determine nearest reference level.
            //
            PopulateNearestLevel(
                row,
                close);


            //
            // Tick microstructure.
            //
            PopulateTickStatistics(
                row,
                time);


            return row;
        }

        #endregion


        #region RTH state

        private void EnsureRthDate(
            DateTime date,
            DateTime eventTime)
        {
            date =
                date.Date;


            if (activeRthDate
                == date)
            {
                return;
            }


            if (activeRthDate
                != Core.Globals.MinDate)
            {
                FinalizeRemainingForwardObservations(
                    "NewTradingDate");

                FinalizeCurrentSession(
                    "NewTradingDate");


                if (hasCurrentRthData)
                {
                    priorDayHigh =
                        currentRthHigh;

                    priorDayLow =
                        currentRthLow;

                    priorDayClose =
                        currentRthClose;
                }
            }


            activeRthDate =
                date;

            hasCurrentRthData = false;

            rthOpen =
                double.NaN;

            currentRthHigh =
                double.NaN;

            currentRthLow =
                double.NaN;

            currentRthClose =
                double.NaN;


            openingRangeReady = false;

            openingRangeHigh =
                double.NaN;

            openingRangeLow =
                double.NaN;


            cumulativeVwapPriceVolume = 0;

            cumulativeVwapVolume = 0;

            currentVwap =
                double.NaN;


            observationsToday = 0;


            //
            // Clear the per-day copies.
            //
            sessionOvernightHigh =
                double.NaN;

            sessionOvernightLow =
                double.NaN;

            sessionOvernightHighTime =
                Core.Globals.MinDate;

            sessionOvernightLowTime =
                Core.Globals.MinDate;

            sessionOvernightBars = 0;


            sessionPremarketHigh =
                double.NaN;

            sessionPremarketLow =
                double.NaN;

            sessionPremarketHighTime =
                Core.Globals.MinDate;

            sessionPremarketLowTime =
                Core.Globals.MinDate;

            sessionPremarketBars = 0;


            Diagnostic(
                eventTime,
                "NEW RTH DATE {0:yyyy-MM-dd}",
                activeRthDate);
        }


        private void UpdateRthSessionState(
            DateTime time,
            double open,
            double high,
            double low,
            double close,
            double volume)
        {
            var timeValue =
                ToTime(time);


            if (timeValue < MarketOpenTime
                || timeValue >= RthEndTime)
            {
                return;
            }


            if (!hasCurrentRthData)
            {
                hasCurrentRthData = true;

                rthOpen =
                    open;

                currentRthHigh =
                    high;

                currentRthLow =
                    low;
            }
            else
            {
                currentRthHigh =
                    Math.Max(
                        currentRthHigh,
                        high);

                currentRthLow =
                    Math.Min(
                        currentRthLow,
                        low);
            }


            currentRthClose =
                close;


            //
            // RTH VWAP from typical price.
            //
            var typical =
                (
                    high
                    + low
                    + close
                ) / 3.0;


            cumulativeVwapPriceVolume +=
                typical * volume;

            cumulativeVwapVolume +=
                volume;


            if (cumulativeVwapVolume > 0)
            {
                currentVwap =
                    cumulativeVwapPriceVolume
                    / cumulativeVwapVolume;
            }


            //
            // Opening range.
            //
            var openingRangeEnd =
                AddMinutesToTime(
                    MarketOpenTime,
                    OpeningRangeMinutes);


            if (timeValue >= MarketOpenTime
                && timeValue < openingRangeEnd)
            {
                if (!IsFinite(
                        openingRangeHigh))
                {
                    openingRangeHigh =
                        high;

                    openingRangeLow =
                        low;
                }
                else
                {
                    openingRangeHigh =
                        Math.Max(
                            openingRangeHigh,
                            high);

                    openingRangeLow =
                        Math.Min(
                            openingRangeLow,
                            low);
                }
            }


            if (timeValue >= openingRangeEnd
                && IsFinite(openingRangeHigh)
                && IsFinite(openingRangeLow))
            {
                openingRangeReady = true;
            }
        }

        #endregion


        #region Tick compression

        private void ProcessTickSeries()
        {
            if (CurrentBars[TickSeriesIndex] < 1)
                return;

            var time =
                Times[TickSeriesIndex][0];

            var price =
                Closes[TickSeriesIndex][0];

            if (time.Date != lastTickDiagnosticDate)
            {
                lastTickDiagnosticDate =
                    time.Date;

                Diagnostic(
                    time,
                    "TICK SERIES ACTIVE Date={0:yyyy-MM-dd} Price={1}",
                    time.Date,
                    price);
            }

            var minute =
                TruncateMinute(
                    time);


            if (activeTickStats == null
                || activeTickStats.Minute != minute)
            {
                FinalizeTickMinute();

                activeTickStats =
                    new TickMinuteStats(
                        minute,
                        price);
            }


            activeTickStats.TickCount++;

            activeTickStats.High =
                Math.Max(
                    activeTickStats.High,
                    price);

            activeTickStats.Low =
                Math.Min(
                    activeTickStats.Low,
                    price);

            activeTickStats.Last =
                price;


            if (IsFinite(previousTickPrice))
            {
                if (price > previousTickPrice)
                    activeTickStats.UpTicks++;

                else if (price < previousTickPrice)
                    activeTickStats.DownTicks++;

                else
                    activeTickStats.UnchangedTicks++;
            }


            previousTickPrice =
                price;
        }


        private void FinalizeTickMinute()
        {
            if (activeTickStats == null)
                return;


            completedTickStats[
                activeTickStats.Minute
            ] =
                activeTickStats;


            //
            // Only retain a small rolling window.
            //
            var cutoff =
                activeTickStats.Minute
                    .AddMinutes(-10);


            var oldKeys =
                completedTickStats.Keys
                    .Where(
                        x => x < cutoff)
                    .ToList();


            foreach (var key in oldKeys)
                completedTickStats.Remove(key);


            activeTickStats = null;
        }


        private void PopulateTickStatistics(
            MarketObservation row,
            DateTime barTime)
        {
            TickMinuteStats stats = null;

            var key =
                TruncateMinute(
                    barTime);


            if (!completedTickStats.TryGetValue(
                    key,
                    out stats))
            {
                //
                // Depending on NT timestamp convention, the
                // completed minute may be labelled one minute later.
                //
                completedTickStats.TryGetValue(
                    key.AddMinutes(-1),
                    out stats);
            }


            if (stats == null)
                return;


            row.TickStatsMinute =
                stats.Minute;

            row.TickCount =
                stats.TickCount;

            row.UpTicks =
                stats.UpTicks;

            row.DownTicks =
                stats.DownTicks;

            row.UnchangedTicks =
                stats.UnchangedTicks;


            var directional =
                stats.UpTicks
                + stats.DownTicks;


            row.UpTickPercent =
                directional > 0
                    ? stats.UpTicks
                      / (double)directional
                      * 100.0
                    : 0;


            row.TickRangeTicks =
                (
                    stats.High
                    - stats.Low
                ) / TickSize;


            row.TickNetChangeTicks =
                (
                    stats.Last
                    - stats.First
                ) / TickSize;
        }

        #endregion


        #region Forward outcome tracking

        private void UpdateForwardObservations(
            DateTime barTime,
            double high,
            double low,
            double close)
        {
            if (activeForwardObservations.Count == 0)
                return;


            for (var i =
                    activeForwardObservations.Count - 1;
                 i >= 0;
                 i--)
            {
                var tracker =
                    activeForwardObservations[i];


                if (barTime
                    <= tracker.Row.Time)
                {
                    continue;
                }


                tracker.Update(
                    high,
                    low,
                    close);


                var elapsedMinutes =
                    (int)Math.Round(
                        (
                            barTime
                            - tracker.Row.Time
                        ).TotalMinutes);


                CaptureForwardHorizon(
                    tracker,
                    elapsedMinutes);


                if (elapsedMinutes
                    >= MaximumForwardMinutes)
                {
                    tracker.Row.ForwardComplete =
                        true;

                    tracker.Row.ForwardMinutesObserved =
                        elapsedMinutes;

                    WriteObservation(
                        tracker.Row);

                    activeForwardObservations
                        .RemoveAt(i);
                }
            }
        }


        private void CaptureForwardHorizon(
            ForwardObservation tracker,
            int elapsedMinutes)
        {
            if (elapsedMinutes >= 5
                && !tracker.Row.H5Complete)
            {
                tracker.Capture(
                    tracker.Row.H5,
                    5);

                tracker.Row.H5Complete = true;
            }


            if (elapsedMinutes >= 10
                && !tracker.Row.H10Complete)
            {
                tracker.Capture(
                    tracker.Row.H10,
                    10);

                tracker.Row.H10Complete = true;
            }


            if (elapsedMinutes >= 15
                && !tracker.Row.H15Complete)
            {
                tracker.Capture(
                    tracker.Row.H15,
                    15);

                tracker.Row.H15Complete = true;
            }


            if (elapsedMinutes >= 30
                && !tracker.Row.H30Complete)
            {
                tracker.Capture(
                    tracker.Row.H30,
                    30);

                tracker.Row.H30Complete = true;
            }


            if (elapsedMinutes >= 60
                && !tracker.Row.H60Complete)
            {
                tracker.Capture(
                    tracker.Row.H60,
                    60);

                tracker.Row.H60Complete = true;
            }
        }


        private void FinalizeRemainingForwardObservations(
            string reason)
        {
            if (activeForwardObservations.Count == 0)
                return;


            foreach (var tracker
                     in activeForwardObservations)
            {
                tracker.Row.ForwardComplete =
                    false;

                tracker.Row.ForwardFinalizeReason =
                    reason;

                tracker.Row.ForwardMinutesObserved =
                    tracker.MinutesObserved;


                WriteObservation(
                    tracker.Row);
            }


            activeForwardObservations.Clear();
        }

        #endregion


        #region Level features

        private void PopulateLevelInteractionFlags(
            MarketObservation row,
            double high,
            double low,
            double close)
        {
            var tolerance =
                LevelTouchToleranceTicks
                * TickSize;


            PopulateOneLevelFlags(
                sessionOvernightHigh,
                high,
                low,
                close,
                tolerance,
                out row.TouchOvernightHigh,
                out row.CloseAboveOvernightHigh,
                out row.CloseBelowOvernightHigh);


            PopulateOneLevelFlags(
                sessionOvernightLow,
                high,
                low,
                close,
                tolerance,
                out row.TouchOvernightLow,
                out row.CloseAboveOvernightLow,
                out row.CloseBelowOvernightLow);


            PopulateOneLevelFlags(
                sessionPremarketHigh,
                high,
                low,
                close,
                tolerance,
                out row.TouchPremarketHigh,
                out row.CloseAbovePremarketHigh,
                out row.CloseBelowPremarketHigh);


            PopulateOneLevelFlags(
                sessionPremarketLow,
                high,
                low,
                close,
                tolerance,
                out row.TouchPremarketLow,
                out row.CloseAbovePremarketLow,
                out row.CloseBelowPremarketLow);


            PopulateOneLevelFlags(
                priorDayHigh,
                high,
                low,
                close,
                tolerance,
                out row.TouchPriorDayHigh,
                out _,
                out _);


            PopulateOneLevelFlags(
                priorDayLow,
                high,
                low,
                close,
                tolerance,
                out row.TouchPriorDayLow,
                out _,
                out _);


            //
            // One-bar sweep / reclaim descriptors.
            //
            row.SweepBelowOvernightLowAndCloseAbove =
                IsFinite(sessionOvernightLow)
                && low < sessionOvernightLow
                && close > sessionOvernightLow;


            row.SweepAboveOvernightHighAndCloseBelow =
                IsFinite(sessionOvernightHigh)
                && high > sessionOvernightHigh
                && close < sessionOvernightHigh;


            row.SweepBelowPremarketLowAndCloseAbove =
                IsFinite(sessionPremarketLow)
                && low < sessionPremarketLow
                && close > sessionPremarketLow;


            row.SweepAbovePremarketHighAndCloseBelow =
                IsFinite(sessionPremarketHigh)
                && high > sessionPremarketHigh
                && close < sessionPremarketHigh;
        }


        private static void PopulateOneLevelFlags(
            double level,
            double high,
            double low,
            double close,
            double tolerance,
            out bool touch,
            out bool closeAbove,
            out bool closeBelow)
        {
            touch = false;
            closeAbove = false;
            closeBelow = false;


            if (!IsFinite(level))
                return;


            touch =
                low <= level + tolerance
                && high >= level - tolerance;


            closeAbove =
                close > level;

            closeBelow =
                close < level;
        }


        private void PopulateNearestLevel(
            MarketObservation row,
            double close)
        {
            var levels =
                new[]
                {
                    new NamedLevel(
                        "OvernightHigh",
                        sessionOvernightHigh),

                    new NamedLevel(
                        "OvernightLow",
                        sessionOvernightLow),

                    new NamedLevel(
                        "PremarketHigh",
                        sessionPremarketHigh),

                    new NamedLevel(
                        "PremarketLow",
                        sessionPremarketLow),

                    new NamedLevel(
                        "PriorDayHigh",
                        priorDayHigh),

                    new NamedLevel(
                        "PriorDayLow",
                        priorDayLow),

                    new NamedLevel(
                        "PriorClose",
                        priorDayClose),

                    new NamedLevel(
                        "RthOpen",
                        rthOpen),

                    new NamedLevel(
                        "OpeningRangeHigh",
                        openingRangeHigh),

                    new NamedLevel(
                        "OpeningRangeLow",
                        openingRangeLow)
                };


            NamedLevel nearest = null;

            var nearestDistance =
                double.MaxValue;


            foreach (var level in levels)
            {
                if (!IsFinite(level.Price))
                    continue;


                var distance =
                    Math.Abs(
                        close - level.Price);


                if (distance >= nearestDistance)
                    continue;


                nearestDistance =
                    distance;

                nearest =
                    level;
            }


            if (nearest == null)
                return;


            row.NearestLevel =
                nearest.Name;

            row.NearestLevelPrice =
                nearest.Price;

            row.NearestLevelDistanceTicks =
                nearestDistance
                / TickSize;
        }

        #endregion


        #region Session export

        private void FinalizeCurrentSession(
            string reason)
        {
            if (activeRthDate
                == Core.Globals.MinDate)
            {
                return;
            }


            if (!hasCurrentRthData
                && observationsToday == 0)
            {
                return;
            }


            sessionsWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(runId),
                    Csv(
                        Instrument?.FullName
                        ?? string.Empty),
                    Date(
                        activeRthDate),
                    Num(sessionOvernightHigh),
                    Num(sessionOvernightLow),
                    DateTimeValue(
                        sessionOvernightHighTime),
                    DateTimeValue(
                        sessionOvernightLowTime),
                    sessionOvernightBars.ToString(Inv),
                    Num(sessionPremarketHigh),
                    Num(sessionPremarketLow),
                    DateTimeValue(
                        sessionPremarketHighTime),
                    DateTimeValue(
                        sessionPremarketLowTime),
                    sessionPremarketBars.ToString(Inv),
                    Num(priorDayHigh),
                    Num(priorDayLow),
                    Num(priorDayClose),
                    Num(rthOpen),
                    Num(currentRthHigh),
                    Num(currentRthLow),
                    Num(currentRthClose),
                    Num(openingRangeHigh),
                    Num(openingRangeLow),
                    observationsToday.ToString(Inv),
                    Csv(reason)));


            sessionsWritten++;
        }

        #endregion


        #region Export setup

        private void InitializeExport()
        {
            runId =
                DateTime.UtcNow
                    .ToString(
                        "yyyyMMdd_HHmmss",
                        Inv);


            outputDirectory =
                Path.Combine(
                    Core.Globals.UserDataDir,
                    OutputFolderName);


            Directory.CreateDirectory(
                outputDirectory);


            var instrument =
                SanitizeFileName(
                    Instrument?.FullName
                    ?? "ES");


            var prefix =
                string.Format(
                    Inv,
                    "{0}_{1}_{2}",
                    OutputFilePrefix,
                    runId,
                    instrument);


            observationsWriter =
                CreateWriter(
                    prefix
                    + "_observations.csv");


            sessionsWriter =
                CreateWriter(
                    prefix
                    + "_sessions.csv");


            manifestWriter =
                CreateWriter(
                    prefix
                    + "_manifest.csv");


            WriteObservationHeader();

            WriteSessionHeader();

            WriteManifest();


            Diagnostic(
                DateTime.Now,
                "EXPORT READY Folder={0} RunId={1}",
                outputDirectory,
                runId);
        }


        private StreamWriter CreateWriter(
            string fileName)
        {
            return new StreamWriter(
                Path.Combine(
                    outputDirectory,
                    fileName),
                false);
        }


        private void WriteManifest()
        {
            manifestWriter.WriteLine(
                "Key,Value");


            WriteManifestValue(
                "RunId",
                runId);

            WriteManifestValue(
                "CollectorVersion",
                CollectorVersion);

            WriteManifestValue(
                "Instrument",
                Instrument?.FullName
                ?? string.Empty);

            WriteManifestValue(
                "PrimarySeries",
                "Expected 5-minute");

            WriteManifestValue(
                "SecondarySeries",
                "1-minute");

            WriteManifestValue(
                "PrecisionSeries",
                "1-tick");

            WriteManifestValue(
                "OvernightStartTime",
                OvernightStartTime);

            WriteManifestValue(
                "PremarketStartTime",
                PremarketStartTime);

            WriteManifestValue(
                "MarketOpenTime",
                MarketOpenTime);

            WriteManifestValue(
                "ObservationStartTime",
                ObservationStartTime);

            WriteManifestValue(
                "ObservationEndTime",
                ObservationEndTime);

            WriteManifestValue(
                "OpeningRangeMinutes",
                OpeningRangeMinutes);

            WriteManifestValue(
                "AtrPeriod",
                AtrPeriod);

            WriteManifestValue(
                "AdxPeriod",
                AdxPeriod);

            WriteManifestValue(
                "EmaFastPeriod",
                EmaFastPeriod);

            WriteManifestValue(
                "EmaSlowPeriod",
                EmaSlowPeriod);

            WriteManifestValue(
                "RelativeVolumeLookback",
                RelativeVolumeLookback);

            WriteManifestValue(
                "MaximumForwardMinutes",
                MaximumForwardMinutes);

            WriteManifestValue(
                "ForwardDataPrecision",
                "1-minute OHLC; same-bar target/stop order marked Ambiguous");

            WriteManifestValue(
                "TickData",
                "Compressed to per-minute tick statistics; raw ticks not exported");


            manifestWriter.Flush();
        }


        private void WriteManifestValue(
            string key,
            object value)
        {
            manifestWriter.WriteLine(
                Csv(key)
                + ","
                + Csv(
                    Convert.ToString(
                        value,
                        Inv)
                    ?? string.Empty));
        }

        #endregion


        #region CSV headers

        private void WriteSessionHeader()
        {
            sessionsWriter.WriteLine(
                "RunId,Contract,TradingDate,"
                + "OvernightHigh,OvernightLow,OvernightHighTime,OvernightLowTime,OvernightBars,"
                + "PremarketHigh,PremarketLow,PremarketHighTime,PremarketLowTime,PremarketBars,"
                + "PriorDayHigh,PriorDayLow,PriorDayClose,"
                + "RthOpen,RthHigh,RthLow,RthClose,"
                + "OpeningRangeHigh,OpeningRangeLow,"
                + "ObservationCount,FinalizeReason");
        }


        private void WriteObservationHeader()
        {
            observationsWriter.WriteLine(
                "RunId,Contract,TradingDate,Time,DayOfWeek,MinutesFromOpen,"
                +
                "Open1m,High1m,Low1m,Close1m,Volume1m,"
                +
                "Range1mTicks,Body1mTicks,BodyPercent,CloseLocationPercent,"
                +
                "UpperWickPercent,LowerWickPercent,Bullish,Bearish,"
                +
                "Atr1mTicks,EmaFast1m,EmaSlow1m,EmaFastSlope1mTicks,EmaSlowSlope1mTicks,"
                +
                "PriceVsEmaFast1mTicks,PriceVsEmaSlow1mTicks,RelativeVolume,"
                +
                "Context5mTime,Open5m,High5m,Low5m,Close5m,Volume5m,"
                +
                "Atr5mTicks,Adx5m,EmaFast5m,EmaSlow5m,EmaFastSlope5mTicks,EmaSlowSlope5mTicks,"
                +
                "PriceVsEmaFast5mTicks,PriceVsEmaSlow5mTicks,"
                +
                "Vwap,DistanceFromVwapTicks,"
                +
                "OvernightHigh,OvernightLow,OvernightWidthTicks,"
                +
                "PremarketHigh,PremarketLow,PremarketWidthTicks,"
                +
                "PriorDayHigh,PriorDayLow,PriorDayClose,RthOpen,"
                +
                "OpeningRangeReady,OpeningRangeHigh,OpeningRangeLow,"
                +
                "DistanceOvernightHighTicks,DistanceOvernightLowTicks,"
                +
                "DistancePremarketHighTicks,DistancePremarketLowTicks,"
                +
                "DistancePriorDayHighTicks,DistancePriorDayLowTicks,"
                +
                "DistancePriorCloseTicks,DistanceRthOpenTicks,"
                +
                "DistanceOpeningRangeHighTicks,DistanceOpeningRangeLowTicks,"
                +
                "PositionInOvernightRangePercent,PositionInPremarketRangePercent,"
                +
                "NearestLevel,NearestLevelPrice,NearestLevelDistanceTicks,"
                +
                "TouchOvernightHigh,TouchOvernightLow,"
                +
                "TouchPremarketHigh,TouchPremarketLow,"
                +
                "TouchPriorDayHigh,TouchPriorDayLow,"
                +
                "CloseAboveOvernightHigh,CloseBelowOvernightHigh,"
                +
                "CloseAboveOvernightLow,CloseBelowOvernightLow,"
                +
                "CloseAbovePremarketHigh,CloseBelowPremarketHigh,"
                +
                "CloseAbovePremarketLow,CloseBelowPremarketLow,"
                +
                "SweepBelowOvernightLowAndCloseAbove,"
                +
                "SweepAboveOvernightHighAndCloseBelow,"
                +
                "SweepBelowPremarketLowAndCloseAbove,"
                +
                "SweepAbovePremarketHighAndCloseBelow,"
                +
                "TickStatsMinute,TickCount,UpTicks,DownTicks,UnchangedTicks,"
                +
                "UpTickPercent,TickRangeTicks,TickNetChangeTicks,"
                +
                HorizonHeader("H5")
                + ","
                + HorizonHeader("H10")
                + ","
                + HorizonHeader("H15")
                + ","
                + HorizonHeader("H30")
                + ","
                + HorizonHeader("H60")
                + ","
                +
                "Long10Target10Stop,Short10Target10Stop,"
                +
                "Long20Target10Stop,Short20Target10Stop,"
                +
                "Long40Target20Stop,Short40Target20Stop,"
                +
                "Long80Target40Stop,Short80Target40Stop,"
                +
                "ForwardComplete,ForwardMinutesObserved,ForwardFinalizeReason");
        }


        private static string HorizonHeader(
            string prefix)
        {
            return
                prefix + "Complete,"
                + prefix + "LongMfeTicks,"
                + prefix + "LongMaeTicks,"
                + prefix + "ShortMfeTicks,"
                + prefix + "ShortMaeTicks,"
                + prefix + "CloseReturnTicks";
        }

        #endregion


        #region CSV observation writer

        private void WriteObservation(
            MarketObservation r)
        {
            observationsWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(r.RunId),
                    Csv(r.Contract),
                    Date(r.TradingDate),
                    DateTimeValue(r.Time),
                    Csv(r.DayOfWeek),
                    r.MinutesFromOpen.ToString(Inv),

                    Num(r.Open1m),
                    Num(r.High1m),
                    Num(r.Low1m),
                    Num(r.Close1m),
                    r.Volume1m.ToString(Inv),

                    Num(r.Range1mTicks),
                    Num(r.Body1mTicks),
                    Num(r.BodyPercent),
                    Num(r.CloseLocationPercent),
                    Num(r.UpperWickPercent),
                    Num(r.LowerWickPercent),
                    Bool(r.Bullish),
                    Bool(r.Bearish),

                    Num(r.Atr1mTicks),
                    Num(r.EmaFast1m),
                    Num(r.EmaSlow1m),
                    Num(r.EmaFastSlope1mTicks),
                    Num(r.EmaSlowSlope1mTicks),
                    Num(r.PriceVsEmaFast1mTicks),
                    Num(r.PriceVsEmaSlow1mTicks),
                    Num(r.RelativeVolume),

                    DateTimeValue(r.Context5mTime),
                    Num(r.Open5m),
                    Num(r.High5m),
                    Num(r.Low5m),
                    Num(r.Close5m),
                    r.Volume5m.ToString(Inv),

                    Num(r.Atr5mTicks),
                    Num(r.Adx5m),
                    Num(r.EmaFast5m),
                    Num(r.EmaSlow5m),
                    Num(r.EmaFastSlope5mTicks),
                    Num(r.EmaSlowSlope5mTicks),
                    Num(r.PriceVsEmaFast5mTicks),
                    Num(r.PriceVsEmaSlow5mTicks),

                    Num(r.Vwap),
                    Num(r.DistanceFromVwapTicks),

                    Num(r.OvernightHigh),
                    Num(r.OvernightLow),
                    Num(r.OvernightWidthTicks),

                    Num(r.PremarketHigh),
                    Num(r.PremarketLow),
                    Num(r.PremarketWidthTicks),

                    Num(r.PriorDayHigh),
                    Num(r.PriorDayLow),
                    Num(r.PriorDayClose),
                    Num(r.RthOpen),

                    Bool(r.OpeningRangeReady),
                    Num(r.OpeningRangeHigh),
                    Num(r.OpeningRangeLow),

                    Num(r.DistanceOvernightHighTicks),
                    Num(r.DistanceOvernightLowTicks),
                    Num(r.DistancePremarketHighTicks),
                    Num(r.DistancePremarketLowTicks),
                    Num(r.DistancePriorDayHighTicks),
                    Num(r.DistancePriorDayLowTicks),
                    Num(r.DistancePriorCloseTicks),
                    Num(r.DistanceRthOpenTicks),
                    Num(r.DistanceOpeningRangeHighTicks),
                    Num(r.DistanceOpeningRangeLowTicks),

                    Num(r.PositionInOvernightRangePercent),
                    Num(r.PositionInPremarketRangePercent),

                    Csv(r.NearestLevel),
                    Num(r.NearestLevelPrice),
                    Num(r.NearestLevelDistanceTicks),

                    Bool(r.TouchOvernightHigh),
                    Bool(r.TouchOvernightLow),
                    Bool(r.TouchPremarketHigh),
                    Bool(r.TouchPremarketLow),
                    Bool(r.TouchPriorDayHigh),
                    Bool(r.TouchPriorDayLow),

                    Bool(r.CloseAboveOvernightHigh),
                    Bool(r.CloseBelowOvernightHigh),
                    Bool(r.CloseAboveOvernightLow),
                    Bool(r.CloseBelowOvernightLow),
                    Bool(r.CloseAbovePremarketHigh),
                    Bool(r.CloseBelowPremarketHigh),
                    Bool(r.CloseAbovePremarketLow),
                    Bool(r.CloseBelowPremarketLow),

                    Bool(
                        r.SweepBelowOvernightLowAndCloseAbove),

                    Bool(
                        r.SweepAboveOvernightHighAndCloseBelow),

                    Bool(
                        r.SweepBelowPremarketLowAndCloseAbove),

                    Bool(
                        r.SweepAbovePremarketHighAndCloseBelow),

                    DateTimeValue(r.TickStatsMinute),
                    r.TickCount.ToString(Inv),
                    r.UpTicks.ToString(Inv),
                    r.DownTicks.ToString(Inv),
                    r.UnchangedTicks.ToString(Inv),
                    Num(r.UpTickPercent),
                    Num(r.TickRangeTicks),
                    Num(r.TickNetChangeTicks),

                    HorizonValues(
                        r.H5Complete,
                        r.H5),

                    HorizonValues(
                        r.H10Complete,
                        r.H10),

                    HorizonValues(
                        r.H15Complete,
                        r.H15),

                    HorizonValues(
                        r.H30Complete,
                        r.H30),

                    HorizonValues(
                        r.H60Complete,
                        r.H60),

                    Csv(
                        r.Long10Target10Stop),

                    Csv(
                        r.Short10Target10Stop),

                    Csv(
                        r.Long20Target10Stop),

                    Csv(
                        r.Short20Target10Stop),

                    Csv(
                        r.Long40Target20Stop),

                    Csv(
                        r.Short40Target20Stop),

                    Csv(
                        r.Long80Target40Stop),

                    Csv(
                        r.Short80Target40Stop),

                    Bool(r.ForwardComplete),

                    r.ForwardMinutesObserved
                        .ToString(Inv),

                    Csv(
                        r.ForwardFinalizeReason)));


            observationsWritten++;


            if (observationsWritten % 1000 == 0)
            {
                observationsWriter.Flush();

                sessionsWriter.Flush();


                Diagnostic(
                    DateTime.Now,
                    "EXPORT PROGRESS Observations={0} Sessions={1}",
                    observationsWritten,
                    sessionsWritten);
            }
        }


        private static string HorizonValues(
            bool complete,
            ForwardHorizon h)
        {
            return string.Join(
                ",",
                Bool(complete),
                Num(h.LongMfeTicks),
                Num(h.LongMaeTicks),
                Num(h.ShortMfeTicks),
                Num(h.ShortMaeTicks),
                Num(h.CloseReturnTicks));
        }

        #endregion


        #region Helpers

        private bool HasRequiredKeyLevels()
        {
            return
                IsFinite(sessionOvernightHigh)
                && IsFinite(sessionOvernightLow)
                && IsFinite(sessionPremarketHigh)
                && IsFinite(sessionPremarketLow);
        }


        private double CalculateRelativeVolume(
            double currentVolume)
        {
            if (RelativeVolumeLookback <= 0)
                return double.NaN;


            var available =
                Math.Min(
                    RelativeVolumeLookback,
                    CurrentBars[
                        MinuteSeriesIndex] - 2);


            if (available <= 0)
                return double.NaN;


            double total = 0;

            var count = 0;


            for (var i = 2;
                 i < 2 + available;
                 i++)
            {
                total +=
                    Volumes[
                        MinuteSeriesIndex][i];

                count++;
            }


            if (count == 0)
                return double.NaN;


            var average =
                total / count;


            if (average <= 0)
                return double.NaN;


            return
                currentVolume
                / average;
        }


        private double SignedDistanceTicks(
            double price,
            double level)
        {
            if (!IsFinite(level))
                return double.NaN;


            return
                (price - level)
                / TickSize;
        }


        private static double PositionInRange(
            double price,
            double low,
            double high)
        {
            if (!IsFinite(low)
                || !IsFinite(high)
                || high <= low)
            {
                return double.NaN;
            }


            return
                (price - low)
                / (high - low)
                * 100.0;
        }


        private static DateTime TruncateMinute(
            DateTime time)
        {
            return new DateTime(
                time.Year,
                time.Month,
                time.Day,
                time.Hour,
                time.Minute,
                0);
        }


        private static int ToTime(
            DateTime time)
        {
            return
                time.Hour * 10000
                + time.Minute * 100
                + time.Second;
        }


        private static int AddMinutesToTime(
            int timeValue,
            int minutes)
        {
            var hours =
                timeValue / 10000;

            var mins =
                (
                    timeValue
                    / 100
                ) % 100;


            var date =
                new DateTime(
                    2000,
                    1,
                    1,
                    hours,
                    mins,
                    0);


            var result =
                date.AddMinutes(
                    minutes);


            return
                result.Hour * 10000
                + result.Minute * 100;
        }


        private static int MinutesBetween(
            int startTime,
            int endTime)
        {
            var startHour =
                startTime / 10000;

            var startMinute =
                (
                    startTime
                    / 100
                ) % 100;


            var endHour =
                endTime / 10000;

            var endMinute =
                (
                    endTime
                    / 100
                ) % 100;


            return
                (
                    endHour * 60
                    + endMinute
                )
                -
                (
                    startHour * 60
                    + startMinute
                );
        }


        private static bool IsFinite(
            double value)
        {
            return
                !double.IsNaN(value)
                && !double.IsInfinity(value);
        }


        private static string Num(
            double value)
        {
            return IsFinite(value)
                ? value.ToString(
                    "0.########",
                    Inv)
                : string.Empty;
        }


        private static string Bool(
            bool value)
        {
            return value
                ? "1"
                : "0";
        }


        private static string Date(
            DateTime value)
        {
            return value
                == Core.Globals.MinDate
                ? string.Empty
                : value.ToString(
                    "yyyy-MM-dd",
                    Inv);
        }


        private static string DateTimeValue(
            DateTime value)
        {
            return value
                == Core.Globals.MinDate
                ? string.Empty
                : value.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    Inv);
        }


        private static string Csv(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;


            if (!value.Contains(",")
                && !value.Contains("\"")
                && !value.Contains("\r")
                && !value.Contains("\n"))
            {
                return value;
            }


            return
                "\""
                + value.Replace(
                    "\"",
                    "\"\"")
                + "\"";
        }


        private static string SanitizeFileName(
            string value)
        {
            foreach (var c
                     in Path
                         .GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        c,
                        '_');
            }


            return
                value.Replace(
                    ' ',
                    '_');
        }


        private void FlushWriters()
        {
            observationsWriter?.Flush();

            sessionsWriter?.Flush();

            manifestWriter?.Flush();
        }


        private void DisposeWriters()
        {
            observationsWriter?.Dispose();

            sessionsWriter?.Dispose();

            manifestWriter?.Dispose();

            observationsWriter = null;

            sessionsWriter = null;

            manifestWriter = null;
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
                        Inv,
                        format,
                        args);


            Print(
                string.Format(
                    Inv,
                    "{0:yyyy-MM-dd HH:mm:ss.fff} | {1} | {2}",
                    time,
                    Name,
                    message));
        }

        #endregion


        #region Internal research models

        private sealed class NamedLevel
        {
            public NamedLevel(
                string name,
                double price)
            {
                Name = name;

                Price = price;
            }


            public string Name { get; }

            public double Price { get; }
        }


        private sealed class TickMinuteStats
        {
            public TickMinuteStats(
                DateTime minute,
                double first)
            {
                Minute = minute;

                First = first;

                Last = first;

                High = first;

                Low = first;
            }


            public DateTime Minute;

            public int TickCount;

            public int UpTicks;

            public int DownTicks;

            public int UnchangedTicks;

            public double First;

            public double Last;

            public double High;

            public double Low;
        }


        private sealed class ForwardHorizon
        {
            public double LongMfeTicks =
                double.NaN;

            public double LongMaeTicks =
                double.NaN;

            public double ShortMfeTicks =
                double.NaN;

            public double ShortMaeTicks =
                double.NaN;

            public double CloseReturnTicks =
                double.NaN;
        }


        private sealed class MarketObservation
        {
            public string RunId;

            public string Contract;

            public DateTime TradingDate;

            public DateTime Time;

            public string DayOfWeek;

            public int MinutesFromOpen;


            public double Open1m;

            public double High1m;

            public double Low1m;

            public double Close1m;

            public double Volume1m;


            public double Range1mTicks;

            public double Body1mTicks;

            public double BodyPercent;

            public double CloseLocationPercent;

            public double UpperWickPercent;

            public double LowerWickPercent;

            public bool Bullish;

            public bool Bearish;


            public double Atr1mTicks;

            public double EmaFast1m;

            public double EmaSlow1m;

            public double EmaFastSlope1mTicks;

            public double EmaSlowSlope1mTicks;

            public double PriceVsEmaFast1mTicks;

            public double PriceVsEmaSlow1mTicks;

            public double RelativeVolume;


            public DateTime Context5mTime;

            public double Open5m;

            public double High5m;

            public double Low5m;

            public double Close5m;

            public double Volume5m;

            public double Atr5mTicks;

            public double Adx5m;

            public double EmaFast5m;

            public double EmaSlow5m;

            public double EmaFastSlope5mTicks;

            public double EmaSlowSlope5mTicks;

            public double PriceVsEmaFast5mTicks;

            public double PriceVsEmaSlow5mTicks;


            public double Vwap;

            public double DistanceFromVwapTicks;


            public double OvernightHigh;

            public double OvernightLow;

            public double OvernightWidthTicks;


            public double PremarketHigh;

            public double PremarketLow;

            public double PremarketWidthTicks;


            public double PriorDayHigh;

            public double PriorDayLow;

            public double PriorDayClose;

            public double RthOpen;


            public bool OpeningRangeReady;

            public double OpeningRangeHigh;

            public double OpeningRangeLow;


            public double DistanceOvernightHighTicks;

            public double DistanceOvernightLowTicks;

            public double DistancePremarketHighTicks;

            public double DistancePremarketLowTicks;

            public double DistancePriorDayHighTicks;

            public double DistancePriorDayLowTicks;

            public double DistancePriorCloseTicks;

            public double DistanceRthOpenTicks;

            public double DistanceOpeningRangeHighTicks;

            public double DistanceOpeningRangeLowTicks;


            public double PositionInOvernightRangePercent;

            public double PositionInPremarketRangePercent;


            public string NearestLevel;

            public double NearestLevelPrice =
                double.NaN;

            public double NearestLevelDistanceTicks =
                double.NaN;


            public bool TouchOvernightHigh;

            public bool TouchOvernightLow;

            public bool TouchPremarketHigh;

            public bool TouchPremarketLow;

            public bool TouchPriorDayHigh;

            public bool TouchPriorDayLow;


            public bool CloseAboveOvernightHigh;

            public bool CloseBelowOvernightHigh;

            public bool CloseAboveOvernightLow;

            public bool CloseBelowOvernightLow;

            public bool CloseAbovePremarketHigh;

            public bool CloseBelowPremarketHigh;

            public bool CloseAbovePremarketLow;

            public bool CloseBelowPremarketLow;


            public bool SweepBelowOvernightLowAndCloseAbove;

            public bool SweepAboveOvernightHighAndCloseBelow;

            public bool SweepBelowPremarketLowAndCloseAbove;

            public bool SweepAbovePremarketHighAndCloseBelow;


            public DateTime TickStatsMinute =
                Core.Globals.MinDate;

            public int TickCount;

            public int UpTicks;

            public int DownTicks;

            public int UnchangedTicks;

            public double UpTickPercent =
                double.NaN;

            public double TickRangeTicks =
                double.NaN;

            public double TickNetChangeTicks =
                double.NaN;


            public ForwardHorizon H5 =
                new ForwardHorizon();

            public ForwardHorizon H10 =
                new ForwardHorizon();

            public ForwardHorizon H15 =
                new ForwardHorizon();

            public ForwardHorizon H30 =
                new ForwardHorizon();

            public ForwardHorizon H60 =
                new ForwardHorizon();


            public bool H5Complete;

            public bool H10Complete;

            public bool H15Complete;

            public bool H30Complete;

            public bool H60Complete;


            public string Long10Target10Stop =
                "Neither";

            public string Short10Target10Stop =
                "Neither";

            public string Long20Target10Stop =
                "Neither";

            public string Short20Target10Stop =
                "Neither";

            public string Long40Target20Stop =
                "Neither";

            public string Short40Target20Stop =
                "Neither";

            public string Long80Target40Stop =
                "Neither";

            public string Short80Target40Stop =
                "Neither";


            public bool ForwardComplete;

            public int ForwardMinutesObserved;

            public string ForwardFinalizeReason;
        }


        private sealed class BarrierState
        {
            public BarrierState(
                int targetTicks,
                int stopTicks)
            {
                TargetTicks =
                    targetTicks;

                StopTicks =
                    stopTicks;
            }


            public int TargetTicks;

            public int StopTicks;

            public string LongOutcome =
                "Neither";

            public string ShortOutcome =
                "Neither";
        }


        private sealed class ForwardObservation
        {
            private readonly double tickSize;

            private double maxHigh;

            private double minLow;

            private double latestClose;

            private readonly BarrierState b10_10 =
                new BarrierState(
                    10,
                    10);

            private readonly BarrierState b20_10 =
                new BarrierState(
                    20,
                    10);

            private readonly BarrierState b40_20 =
                new BarrierState(
                    40,
                    20);

            private readonly BarrierState b80_40 =
                new BarrierState(
                    80,
                    40);


            public ForwardObservation(
                MarketObservation row,
                double tickSize)
            {
                Row = row;

                this.tickSize =
                    tickSize;

                maxHigh =
                    row.Close1m;

                minLow =
                    row.Close1m;

                latestClose =
                    row.Close1m;
            }


            public MarketObservation Row
            {
                get;
            }


            public int MinutesObserved
            {
                get;
                private set;
            }


            public void Update(
                double high,
                double low,
                double close)
            {
                maxHigh =
                    Math.Max(
                        maxHigh,
                        high);

                minLow =
                    Math.Min(
                        minLow,
                        low);

                latestClose =
                    close;

                MinutesObserved++;


                UpdateBarrier(
                    b10_10,
                    high,
                    low);

                UpdateBarrier(
                    b20_10,
                    high,
                    low);

                UpdateBarrier(
                    b40_20,
                    high,
                    low);

                UpdateBarrier(
                    b80_40,
                    high,
                    low);


                Row.Long10Target10Stop =
                    b10_10.LongOutcome;

                Row.Short10Target10Stop =
                    b10_10.ShortOutcome;

                Row.Long20Target10Stop =
                    b20_10.LongOutcome;

                Row.Short20Target10Stop =
                    b20_10.ShortOutcome;

                Row.Long40Target20Stop =
                    b40_20.LongOutcome;

                Row.Short40Target20Stop =
                    b40_20.ShortOutcome;

                Row.Long80Target40Stop =
                    b80_40.LongOutcome;

                Row.Short80Target40Stop =
                    b80_40.ShortOutcome;
            }


            public void Capture(
                ForwardHorizon target,
                int minutes)
            {
                var reference =
                    Row.Close1m;


                target.LongMfeTicks =
                    Math.Max(
                        0,
                        (
                            maxHigh
                            - reference
                        ) / tickSize);


                target.LongMaeTicks =
                    Math.Max(
                        0,
                        (
                            reference
                            - minLow
                        ) / tickSize);


                target.ShortMfeTicks =
                    Math.Max(
                        0,
                        (
                            reference
                            - minLow
                        ) / tickSize);


                target.ShortMaeTicks =
                    Math.Max(
                        0,
                        (
                            maxHigh
                            - reference
                        ) / tickSize);


                target.CloseReturnTicks =
                    (
                        latestClose
                        - reference
                    ) / tickSize;
            }


            private void UpdateBarrier(
                BarrierState state,
                double high,
                double low)
            {
                var reference =
                    Row.Close1m;


                if (state.LongOutcome
                    == "Neither")
                {
                    var targetHit =
                        high
                        >= reference
                           + state.TargetTicks
                           * tickSize;


                    var stopHit =
                        low
                        <= reference
                           - state.StopTicks
                           * tickSize;


                    state.LongOutcome =
                        ResolveBarrier(
                            targetHit,
                            stopHit);
                }


                if (state.ShortOutcome
                    == "Neither")
                {
                    var targetHit =
                        low
                        <= reference
                           - state.TargetTicks
                           * tickSize;


                    var stopHit =
                        high
                        >= reference
                           + state.StopTicks
                           * tickSize;


                    state.ShortOutcome =
                        ResolveBarrier(
                            targetHit,
                            stopHit);
                }
            }


            private static string ResolveBarrier(
                bool targetHit,
                bool stopHit)
            {
                if (targetHit
                    && stopHit)
                {
                    return "Ambiguous";
                }


                if (targetHit)
                    return "Target";


                if (stopHit)
                    return "Stop";


                return "Neither";
            }
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
            Name = "Premarket Start Time",
            Order = 2,
            GroupName = "Session")]
        public int PremarketStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Market Open Time",
            Order = 3,
            GroupName = "Session")]
        public int MarketOpenTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Observation Start Time",
            Order = 4,
            GroupName = "Session")]
        public int ObservationStartTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Observation End Time",
            Order = 5,
            GroupName = "Session")]
        public int ObservationEndTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "RTH End Time",
            Order = 6,
            GroupName = "Session")]
        public int RthEndTime
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(
            Name = "Opening Range Minutes",
            Order = 7,
            GroupName = "Session")]
        public int OpeningRangeMinutes
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(
            Name = "ATR Period",
            Order = 1,
            GroupName = "Features")]
        public int AtrPeriod
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(
            Name = "ADX Period",
            Order = 2,
            GroupName = "Features")]
        public int AdxPeriod
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(
            Name = "EMA Fast Period",
            Order = 3,
            GroupName = "Features")]
        public int EmaFastPeriod
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(
            Name = "EMA Slow Period",
            Order = 4,
            GroupName = "Features")]
        public int EmaSlowPeriod
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(
            Name = "Relative Volume Lookback",
            Order = 5,
            GroupName = "Features")]
        public int RelativeVolumeLookback
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(5, 120)]
        [Display(
            Name = "Maximum Forward Minutes",
            Order = 1,
            GroupName = "Forward Analysis")]
        public int MaximumForwardMinutes
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(
            Name = "Level Touch Tolerance Ticks",
            Order = 1,
            GroupName = "Key Levels")]
        public int LevelTouchToleranceTicks
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Output Folder Name",
            Order = 1,
            GroupName = "Export")]
        public string OutputFolderName
        {
            get;
            set;
        }


        [NinjaScriptProperty]
        [Display(
            Name = "Output File Prefix",
            Order = 2,
            GroupName = "Export")]
        public string OutputFilePrefix
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