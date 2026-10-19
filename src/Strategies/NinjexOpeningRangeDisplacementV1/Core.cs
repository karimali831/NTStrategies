using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Deterministic, bar-close-only 1-minute OR breakout + FVG strategy.
    /// No tick series, Tick Replay, bid/ask or intrabar signal assumptions.
    /// </summary>
    public partial class NinjexOpeningRangeDisplacementV1 : Strategy
    {
        internal const string Version = "1.0.1";
        private const string LongSignal = "ORFvgLong";
        private const string ShortSignal = "ORFvgShort";

        private NinjexFirstFiveRangeEngine firstFiveRange;
        private ATR atr14;
        private ADX adx14;
        private DateTime tradingDate = DateTime.MinValue;
        private double orHigh = double.NaN;
        private double orLow = double.NaN;

        private BreakoutAttempt longAttempt;
        private BreakoutAttempt shortAttempt;
        private bool longNeedsRetest;
        private bool shortNeedsRetest;
        private bool longRetested;
        private bool shortRetested;
        private bool reportedOpeningRange;
        private int submittedToday;
        private bool entryPending;
        private int pendingDirection;
        private double pendingStop;
        private double pendingTarget;
        private double pendingReferenceClose;
        private int lastProcessedBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NinjexOpeningRangeDisplacementV1";
                Description = "V1: first-five-minute RTH opening range; close-confirmed break, 3-candle FVG and breakout-candle stop.";
                Calculate = Calculate.OnBarClose;
                BarsRequiredToTrade = 0;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsUnmanaged = true; // Explicit per-fill OCO handling; cannot mix managed order methods.
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = true;
                IncludeCommission = true;
                TradingQuantity = 1;
                RiskRewardRatio = 2.0;
                EntryStartTime = 93500;
                EntryEndTime = 155500;
                MinFvgTicks = 1;
                MaxTradesPerDay = 0;
                EnableAtrFilter = false;
                MinAtrTicks = 20;
                EnableAdxFilter = false;
                MinAdx = 20;
                EnableBodyAtrFilter = false;
                MinBreakoutBodyAtr = 0.5;
                EnableFileLogging = true;
                EnablePrintDiagnostics = false;
            }
            else if (State == State.DataLoaded)
            {
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
                    throw new InvalidOperationException("NinjexOpeningRangeDisplacementV1 requires a 1-minute primary chart.");

                string zone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo.Id;
                if (zone != "Eastern Standard Time" && zone != "America/New_York" && zone != "US/Eastern")
                    throw new InvalidOperationException("Set NinjaTrader's platform time zone to Eastern (ET). No time conversion is performed.");

                firstFiveRange = new NinjexFirstFiveRangeEngine();
                atr14 = ATR(14);
                adx14 = ADX(14);
            }
            else if (State == State.Terminated)
            {
                CloseDiagnosticFile();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < 0 || lastProcessedBar == CurrentBar)
                return;
            lastProcessedBar = CurrentBar;

            DateTime now = Time[0]; // NinjaTrader platform/chart configured ET.
            if (now.Date != tradingDate)
                BeginTradingDay(now.Date);

            // Always feed the range engine the completed 09:31-09:35 bars,
            // including bars before entries are eligible.
            firstFiveRange.Accept(now, High[0], Low[0], 1);

            if (ToTime(now) < 93500 || ToTime(now) > EntryEndTime)
                return;

            orHigh = firstFiveRange.Ready ? firstFiveRange.High : double.NaN;
            orLow = firstFiveRange.Ready ? firstFiveRange.Low : double.NaN;
            if (double.IsNaN(orHigh) || double.IsNaN(orLow) || orHigh <= orLow)
            {
                WriteDiagnostic("RANGE_NOT_READY", 0, null, "Incomplete/missing 09:30-09:35 ET minute data.");
                return;
            }

            if (!reportedOpeningRange)
            {
                reportedOpeningRange = true;
                WriteDiagnostic("RANGE_READY", 0, null, "Completed 09:30-09:35 ET range.");
            }

            // Record an unobstructed, analysis-ready feature set; filters are disabled by default.
            WriteDiagnostic("BAR", 0, null, "OHLC and momentum snapshot");

            if (CurrentBar < 2)
                return;

            // Observe both directions even while in a position, preserving retest history.
            ProcessDirection(+1);
            ProcessDirection(-1);
        }

        private void BeginTradingDay(DateTime date)
        {
            tradingDate = date;
            longAttempt = null;
            shortAttempt = null;
            longNeedsRetest = false;
            shortNeedsRetest = false;
            longRetested = false;
            shortRetested = false;
            reportedOpeningRange = false;
            orHigh = orLow = double.NaN;
            submittedToday = 0;
            pausedToday = false;
            // Never erase an outstanding order/bracket merely due to a calendar rollover.
            WriteDiagnostic("SESSION_RESET", 0, null,
                "StrategyVersion=" + Version
                + "; Range=09:30-09:35 ET"
                + "; EntryWindow=" + EntryStartTime + "-" + EntryEndTime
                + "; Quantity=" + TradingQuantity
                + "; RR=" + RiskRewardRatio
                + "; MinFvgTicks=" + MinFvgTicks
                + "; MaxTradesPerDay=" + MaxTradesPerDay
                + "; ATRFilter=" + EnableAtrFilter + "; MinAtrTicks=" + MinAtrTicks
                + "; ADXFilter=" + EnableAdxFilter + "; MinAdx=" + MinAdx
                + "; BodyAtrFilter=" + EnableBodyAtrFilter
                + "; MinBodyAtr=" + MinBreakoutBodyAtr
                + "; TickSize=" + TickSize
                + "; PointValue=" + Instrument.MasterInstrument.PointValue
                + "; TradingHours=" + Bars.TradingHours.Name);
        }

        private bool InEntryWindow()
        {
            int t = ToTime(Time[0]);
            return t >= EntryStartTime && t <= EntryEndTime;
        }
    }
}