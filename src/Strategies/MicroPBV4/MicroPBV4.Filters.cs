using System;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        private bool PassesEntryDistanceFilter(int sigBarsAgo, out double priorBarRangeTicks)
        {
            priorBarRangeTicks = 0;
            
            if (MaxPriorBarRangeTicks <= 0)
                return true;
 
            // "prior bar" relative to the signal bar
            var b = Math.Max(1, sigBarsAgo + 1);
 
            if (CurrentBar < b)
                return true;
 
            priorBarRangeTicks = (High[b] - Low[b]) / TickSize;
            
            // bypass when trend strength is high (align it with the same bar we measured)
                if (adx[b] >= 30)
                return true;
            
            return priorBarRangeTicks <= MaxPriorBarRangeTicks;
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
