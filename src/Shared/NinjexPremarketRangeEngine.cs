#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.Ninjex
{
    public sealed class NinjexPremarketRangeEngine
    {
        private bool activeRangeFinalized;

        public DateTime ActiveRangeDate { get; private set; }
            = Core.Globals.MinDate;

        public DateTime LatestRangeDate { get; private set; }
            = Core.Globals.MinDate;

        public DateTime HighBarTime { get; private set; }
            = Core.Globals.MinDate;

        public DateTime LowBarTime { get; private set; }
            = Core.Globals.MinDate;

        public double LatestHigh { get; private set; }
            = double.NaN;

        public double LatestLow { get; private set; }
            = double.NaN;

        public bool HasRangeData { get; private set; }

        public bool IsRangeComplete =>
            activeRangeFinalized
            && LatestRangeDate != Core.Globals.MinDate
            && IsValidLevel(LatestHigh)
            && IsValidLevel(LatestLow)
            && LatestHigh > LatestLow;

        public void Reset()
        {
            ActiveRangeDate = Core.Globals.MinDate;
            LatestRangeDate = Core.Globals.MinDate;

            HighBarTime = Core.Globals.MinDate;
            LowBarTime = Core.Globals.MinDate;

            LatestHigh = double.NaN;
            LatestLow = double.NaN;

            HasRangeData = false;
            activeRangeFinalized = false;
        }

        public bool ProcessCompletedBar(
            DateTime barCloseTime,
            double barHigh,
            double barLow,
            int rangeStartTime,
            int marketOpenTime)
        {
            if (barHigh <= 0
                || barLow <= 0
                || barHigh < barLow)
            {
                return false;
            }

            var barDate = barCloseTime.Date;

            if (ActiveRangeDate != barDate)
                StartNewRange(barDate);

            if (activeRangeFinalized)
                return false;

            var timeValue = ToTime(barCloseTime);
            var startValue = NormalizeTimeInput(rangeStartTime);
            var openValue = NormalizeTimeInput(marketOpenTime);

            // A 5-minute bar stamped 03:05 represents 03:00-03:05.
            // Therefore, the premarket range includes bars stamped
            // 03:05 through 09:30.
            var isRangeBar =
                timeValue > startValue
                && timeValue <= openValue;

            if (isRangeBar)
                AddBar(barCloseTime, barHigh, barLow);

            if (timeValue >= openValue && HasRangeData)
            {
                LatestRangeDate = ActiveRangeDate;
                activeRangeFinalized = true;
                return true;
            }

            return false;
        }

        private void StartNewRange(DateTime date)
        {
            ActiveRangeDate = date;
            LatestRangeDate = Core.Globals.MinDate;

            HighBarTime = Core.Globals.MinDate;
            LowBarTime = Core.Globals.MinDate;

            LatestHigh = double.NaN;
            LatestLow = double.NaN;

            HasRangeData = false;
            activeRangeFinalized = false;
        }

        private void AddBar(
            DateTime time,
            double high,
            double low)
        {
            if (!HasRangeData
                || double.IsNaN(LatestHigh)
                || high > LatestHigh)
            {
                LatestHigh = high;
                HighBarTime = time;
            }

            if (!HasRangeData
                || double.IsNaN(LatestLow)
                || low < LatestLow)
            {
                LatestLow = low;
                LowBarTime = time;
            }

            HasRangeData = true;
        }

        private static int NormalizeTimeInput(int value)
        {
            return value > 0 && value < 2400
                ? value * 100
                : value;
        }

        private static int ToTime(DateTime time)
        {
            return time.Hour * 10000
                   + time.Minute * 100
                   + time.Second;
        }

        private static bool IsValidLevel(double value)
        {
            return !double.IsNaN(value)
                   && !double.IsInfinity(value)
                   && value > 0;
        }
    }
}