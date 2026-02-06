using System;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class MicroPBV3 : Strategy
    {
        private void TrendTicks(
            int lookbackBars,
            out int lb, 
            out bool hasBars,
            out double rangeTicks,
            out double upTicks,
            out double downTicks)
        {
            lb = 0;
            hasBars = false;
            upTicks = 0;
            downTicks = 0;
            
            var barsAgo = SigClosed();

            var closedBarsSinceOpen = 0;
            if (_sessionStartBarIdx >= 0)
                closedBarsSinceOpen = Math.Max(0, CurrentBar - _sessionStartBarIdx - barsAgo);

            var minutesPerBar = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? BarsPeriod.Value : 1;
            var minFromOpen = closedBarsSinceOpen * minutesPerBar;

            lb = minFromOpen < MinMinutesFromOpen
                ? closedBarsSinceOpen
                : // 0,1,2,3...
                Math.Min(lookbackBars, closedBarsSinceOpen);
            
            hasBars = true;

            var hh = High[barsAgo];
            var ll = Low[barsAgo];

            for (var i = 1; i <= lb; i++)
            {
                hh = Math.Max(hh, High[barsAgo + i]);
                ll = Math.Min(ll, Low[barsAgo + i]);
            }

            rangeTicks = (hh - ll) / TickSize;
            upTicks = 0.0;
            downTicks = 0.0;

            for (var i = 0; i < lb; i++)
            {
                var d = Close[barsAgo + i] - Close[barsAgo + i + 1];
                var ticks = Math.Abs(d) / TickSize;

                if (d > 0)
                    upTicks += ticks;
                else if (d < 0)
                    downTicks += ticks;
            }
        }
    }
}
