using System;

namespace NinjaTrader.NinjaScript.Ninjex
{
    /// <summary>
    /// Shared OHLC-only first-five-minute opening range accumulator.
    /// Call once per COMPLETED 1m or 5m bar whose timestamp is already ET.
    /// Makes the strategy and optional visual indicator use identical levels.
    /// </summary>
    public sealed class NinjexFirstFiveRangeEngine
    {
        private DateTime activeDate = DateTime.MinValue;
        private int pieces;

        public DateTime ActiveDate { get { return activeDate; } }
        public bool Ready { get; private set; }
        public double High { get; private set; }
        public double Low { get; private set; }

        public void Accept(DateTime barEndET, double barHigh, double barLow, int minutePeriod)
        {
            if (minutePeriod != 1 && minutePeriod != 5)
                throw new ArgumentException("First-five-minute range supports completed 1m or 5m bars only.");

            if (activeDate != barEndET.Date)
            {
                activeDate = barEndET.Date;
                pieces = 0;
                Ready = false;
                High = double.MinValue;
                Low = double.MaxValue;
            }

            if (Ready)
                return;

            DateTime first = activeDate.AddHours(9).AddMinutes(30);
            DateTime last = first.AddMinutes(5);
            bool contains = minutePeriod == 1
                ? barEndET > first && barEndET <= last
                : barEndET == last;

            if (contains)
            {
                High = Math.Max(High, barHigh);
                Low = Math.Min(Low, barLow);
                pieces++;
            }

            // Never synthesize incomplete ranges when early 1-minute history is missing.
            if (barEndET >= last && pieces == (minutePeriod == 1 ? 5 : 1) && High > Low)
                Ready = true;
        }
    }
}