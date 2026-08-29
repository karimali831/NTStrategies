using System;


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

            if (ActiveRangeDate != rangeDate || Mode != mode)
                StartNewRange(rangeDate, mode);

            if (activeRangeFinalized)
                return false;

            bool isRangeBar = mode == KeyLevelsMode.Overnight
                ? timeValue > overnightStartValue || timeValue <= openValue
                : timeValue > premarketStartValue && timeValue <= openValue;

            if (isRangeBar)
                AddBar(barCloseTime, barHigh, barLow);

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
                return true;
            }

            return false;
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