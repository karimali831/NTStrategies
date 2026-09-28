using System;
using System.Globalization;
using System.IO;


namespace NinjaTrader.NinjaScript.Ninjex
{
    public enum KeyLevelsMode
    {
        Premarket,
        Overnight
    }

    public sealed class NinjexPremarketRangeEngine
    {
        private bool activeRangeFinalized;

        // Stateful range construction requires monotonic completed bars. NinjaTrader
        // can replay older warm-up rows while a Playback strategy transitions from
        // loaded history to the live Playback stream. Track each mode independently
        // so a duplicate/backward bar cannot reset or truncate an already-building
        // range. This has no effect on normal chronological data.
        private DateTime lastPremarketBarCloseTime = Core.Globals.MinDate;
        private DateTime lastOvernightBarCloseTime = Core.Globals.MinDate;

        // Temporary forensic diagnostics for Playback startup/range construction.
        // This is observational only and can be removed once the Nov-2025 DST/startup
        // discrepancy has been fully explained.
        private static readonly object RangeDiagnosticSync = new object();
        private static readonly string RangeDiagnosticPath = BuildRangeDiagnosticPath();

        public KeyLevelsMode Mode { get; private set; } = KeyLevelsMode.Premarket;
        public DateTime ActiveRangeDate { get; private set; } = Core.Globals.MinDate;
        public DateTime LatestRangeDate { get; private set; } = Core.Globals.MinDate;
        public DateTime HighBarTime { get; private set; } = Core.Globals.MinDate;
        public DateTime LowBarTime { get; private set; } = Core.Globals.MinDate;
        public double LatestHigh { get; private set; } = double.NaN;
        public double LatestLow { get; private set; } = double.NaN;
        public bool HasRangeData { get; private set; }
        public int RangeBarCount { get; private set; }

        public bool IsRangeComplete =>
            activeRangeFinalized
            && LatestRangeDate != Core.Globals.MinDate
            && IsValidLevel(LatestHigh)
            && IsValidLevel(LatestLow)
            && LatestHigh > LatestLow;

        // Backward-compatible overload: existing strategies remain in
        // premarket mode until they explicitly pass KeyLevelsMode.
        public bool ProcessCompletedBar(
            DateTime barCloseTime,
            double barHigh,
            double barLow,
            int rangeStartTime,
            int marketOpenTime)
        {
            return ProcessCompletedBar(
                barCloseTime,
                barHigh,
                barLow,
                KeyLevelsMode.Premarket,
                rangeStartTime,
                180000,
                marketOpenTime);
        }

        public bool ProcessCompletedBar(
            DateTime barCloseTime,
            double barHigh,
            double barLow,
            KeyLevelsMode mode,
            int premarketStartTime,
            int overnightStartTime,
            int marketOpenTime)
        {
            if (barHigh <= 0 || barLow <= 0 || barHigh < barLow)
                return false;

            int timeValue = ToTime(barCloseTime);
            int premarketStartValue = NormalizeTimeInput(premarketStartTime);
            int overnightStartValue = NormalizeTimeInput(overnightStartTime);
            int openValue = NormalizeTimeInput(marketOpenTime);

            DateTime rangeDate = GetRangeDate(
                barCloseTime,
                timeValue,
                mode,
                overnightStartValue);

            var traceBoundary =
                mode == KeyLevelsMode.Overnight
                && ShouldTraceBoundary(timeValue);

            if (traceBoundary)
            {
                WriteRangeDiagnostic(
                    "INPUT",
                    mode,
                    barCloseTime,
                    timeValue,
                    rangeDate,
                    ActiveRangeDate,
                    RangeBarCount,
                    RangeBarCount,
                    barHigh,
                    barLow);
            }

            if (!AcceptMonotonicBar(barCloseTime, mode))
            {
                WriteRangeDiagnostic(
                    "REJECT_NON_MONOTONIC",
                    mode,
                    barCloseTime,
                    timeValue,
                    rangeDate,
                    ActiveRangeDate,
                    RangeBarCount,
                    RangeBarCount,
                    barHigh,
                    barLow);

                return false;
            }

            if (ActiveRangeDate != rangeDate || Mode != mode)
            {
                var beforeCount = RangeBarCount;
                var previousActiveDate = ActiveRangeDate;

                StartNewRange(rangeDate, mode);

                if (mode == KeyLevelsMode.Overnight
                    && (traceBoundary || previousActiveDate > rangeDate))
                {
                    WriteRangeDiagnostic(
                        "START_NEW_RANGE",
                        mode,
                        barCloseTime,
                        timeValue,
                        rangeDate,
                        previousActiveDate,
                        beforeCount,
                        RangeBarCount,
                        barHigh,
                        barLow);
                }
            }

            if (activeRangeFinalized)
                return false;

            bool isRangeBar = mode == KeyLevelsMode.Overnight
                ? timeValue > overnightStartValue || timeValue <= openValue
                : timeValue > premarketStartValue && timeValue <= openValue;

            if (isRangeBar)
            {
                var beforeCount = RangeBarCount;
                AddBar(barCloseTime, barHigh, barLow);

                if (traceBoundary)
                {
                    WriteRangeDiagnostic(
                        "ADD_RANGE_BAR",
                        mode,
                        barCloseTime,
                        timeValue,
                        rangeDate,
                        ActiveRangeDate,
                        beforeCount,
                        RangeBarCount,
                        barHigh,
                        barLow);
                }
            }
            else if (traceBoundary)
            {
                WriteRangeDiagnostic(
                    "NOT_RANGE_BAR",
                    mode,
                    barCloseTime,
                    timeValue,
                    rangeDate,
                    ActiveRangeDate,
                    RangeBarCount,
                    RangeBarCount,
                    barHigh,
                    barLow);
            }

            // An overnight range must only finalize on its range date,
            // never on the prior evening where timeValue is also >= openValue.
            // Comparing dates also permits safe finalization on the first bar
            // after 09:30 if the exact 09:30 bar is missing.
            bool canFinalize =
                mode == KeyLevelsMode.Premarket
                || barCloseTime.Date == ActiveRangeDate;

            if (canFinalize && timeValue >= openValue && HasRangeData)
            {
                LatestRangeDate = ActiveRangeDate;
                activeRangeFinalized = true;

                if (mode == KeyLevelsMode.Overnight)
                {
                    WriteRangeDiagnostic(
                        "FINALIZE",
                        mode,
                        barCloseTime,
                        timeValue,
                        rangeDate,
                        ActiveRangeDate,
                        RangeBarCount,
                        RangeBarCount,
                        barHigh,
                        barLow);
                }

                return true;
            }

            return false;
        }

        private bool AcceptMonotonicBar(
            DateTime barCloseTime,
            KeyLevelsMode mode)
        {
            var lastTime = mode == KeyLevelsMode.Overnight
                ? lastOvernightBarCloseTime
                : lastPremarketBarCloseTime;

            if (lastTime != Core.Globals.MinDate
                && barCloseTime <= lastTime)
            {
                return false;
            }

            if (mode == KeyLevelsMode.Overnight)
                lastOvernightBarCloseTime = barCloseTime;
            else
                lastPremarketBarCloseTime = barCloseTime;

            return true;
        }

        private static DateTime GetRangeDate(
            DateTime barCloseTime,
            int timeValue,
            KeyLevelsMode mode,
            int overnightStartValue)
        {
            return mode == KeyLevelsMode.Overnight
                   && timeValue > overnightStartValue
                ? barCloseTime.Date.AddDays(1)
                : barCloseTime.Date;
        }

        private void StartNewRange(DateTime date, KeyLevelsMode mode)
        {
            Mode = mode;
            ActiveRangeDate = date;
            LatestRangeDate = Core.Globals.MinDate;
            HighBarTime = Core.Globals.MinDate;
            LowBarTime = Core.Globals.MinDate;
            LatestHigh = double.NaN;
            LatestLow = double.NaN;
            HasRangeData = false;
            RangeBarCount = 0;
            activeRangeFinalized = false;
        }

        private void AddBar(DateTime time, double high, double low)
        {
            if (!HasRangeData || double.IsNaN(LatestHigh) || high > LatestHigh)
            {
                LatestHigh = high;
                HighBarTime = time;
            }

            if (!HasRangeData || double.IsNaN(LatestLow) || low < LatestLow)
            {
                LatestLow = low;
                LowBarTime = time;
            }

            HasRangeData = true;
            RangeBarCount++;
        }

        private static bool ShouldTraceBoundary(int timeValue)
        {
            return (timeValue >= 174500 && timeValue <= 191500)
                   || (timeValue >= 92000 && timeValue <= 93500);
        }

        private static string BuildRangeDiagnosticPath()
        {
            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir,
                    "NinjexResearch",
                    "OvernightEdgePortfolio");

                Directory.CreateDirectory(directory);

                return Path.Combine(
                    directory,
                    "range_engine_diag_"
                    + DateTime.Now.ToString(
                        "yyyyMMdd_HHmmss_fff",
                        CultureInfo.InvariantCulture)
                    + ".csv");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void WriteRangeDiagnostic(
            string action,
            KeyLevelsMode mode,
            DateTime barCloseTime,
            int timeValue,
            DateTime rangeDate,
            DateTime activeRangeDate,
            int beforeCount,
            int afterCount,
            double high,
            double low)
        {
            if (string.IsNullOrEmpty(RangeDiagnosticPath))
                return;

            try
            {
                var line = string.Join(
                    ",",
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture),
                    action,
                    mode.ToString(),
                    barCloseTime.ToString(
                        "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture),
                    timeValue.ToString(CultureInfo.InvariantCulture),
                    rangeDate == Core.Globals.MinDate
                        ? string.Empty
                        : rangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    activeRangeDate == Core.Globals.MinDate
                        ? string.Empty
                        : activeRangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    beforeCount.ToString(CultureInfo.InvariantCulture),
                    afterCount.ToString(CultureInfo.InvariantCulture),
                    high.ToString("0.########", CultureInfo.InvariantCulture),
                    low.ToString("0.########", CultureInfo.InvariantCulture));

                lock (RangeDiagnosticSync)
                {
                    if (!File.Exists(RangeDiagnosticPath))
                    {
                        File.AppendAllText(
                            RangeDiagnosticPath,
                            "LoggedAt,Action,Mode,BarCloseTime,TimeValue,DerivedRangeDate,ActiveRangeDate,BeforeCount,AfterCount,High,Low"
                            + Environment.NewLine);
                    }

                    File.AppendAllText(
                        RangeDiagnosticPath,
                        line + Environment.NewLine);
                }
            }
            catch
            {
                // Diagnostics must never interfere with range construction.
            }
        }

        private static int NormalizeTimeInput(int value)
        {
            return value > 0 && value < 2400 ? value * 100 : value;
        }

        private static int ToTime(DateTime time)
        {
            return time.Hour * 10000 + time.Minute * 100 + time.Second;
        }

        private static bool IsValidLevel(double value)
        {
            return !double.IsNaN(value)
                   && !double.IsInfinity(value)
                   && value > 0;
        }
    }
}
