using System;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        private bool PassesEntryDistanceFilter(int barsAgoToCheck, out double barRangeTicks)
        {
            barRangeTicks = 0;

            if (MaxPriorBarRangeTicks <= 0)
                return true;

            var b = Math.Max(0, barsAgoToCheck);

            if (CurrentBar < b)
                return true;

            barRangeTicks = (High[b] - Low[b]) / TickSize;
            return barRangeTicks <= MaxPriorBarRangeTicks;
        }

        private bool PassesWickFilter()
        {
            var o = Open[0];
            var c = Close[0];
            var h = High[0];
            var l = Low[0];

            var range = Math.Max(h - l, TickSize);
            var body = Math.Abs(c - o);

            var upperWick = h - Math.Max(o, c);
            var lowerWick = Math.Min(o, c) - l;

            var upperTicks = upperWick / TickSize;
            var lowerTicks = lowerWick / TickSize;

            if (MaxBothWicksTicks > 0 && upperTicks >= MaxBothWicksTicks && lowerTicks >= MaxBothWicksTicks)
                return false;

            if (WickBlockSingleWick && MaxSingleWickTicks > 0 && (upperTicks >= MaxSingleWickTicks || lowerTicks >= MaxSingleWickTicks))
                return false;
            
            return true;
        }
    }
}
