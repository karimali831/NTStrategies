using System;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class MicroPBV4 : Strategy
    {
        private bool TrendConfirm(
            out string failReason,
            out bool trendUp,
            out bool trendDown)
        {
            failReason = "none";
            trendUp = false;
            trendDown = false;

            var m = GetEmaStruct(EmaSlopeMetric.DirectionalAbs);
            if (!m.HasBars)
            {
                failReason = "insufficient-bars";
                return false;
            }
            
            TrendTicks(30, out var lb, out var bars, out var ticks, out var upTicks,
                out var downTicks);

            trendUp = upTicks > downTicks;
            trendDown = downTicks > upTicks;

            return trendUp || trendDown;
        }
        

        // Convenience overload if you don't care about trendUp/trendDown flags
        private bool TrendConfirm(out string failReason)
        {
            return TrendConfirm(out failReason, out _, out _);
        }

        private bool TrendConfirm()
        {
            return TrendConfirm(out _);
        }
        

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
            
            var closedBarsSinceOpen = 0;
            if (_sessionStartBarIdx >= 0)
                closedBarsSinceOpen = Math.Max(0, CurrentBar - _sessionStartBarIdx);

            var minutesPerBar = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? BarsPeriod.Value : 1;
            var minFromOpen = closedBarsSinceOpen * minutesPerBar;

            lb = minFromOpen < MinMinutesFromOpen
                ? closedBarsSinceOpen
                : // 0,1,2,3...
                Math.Min(lookbackBars, closedBarsSinceOpen);
            
            hasBars = true;

            var hh = High[0];
            var ll = Low[0];

            for (var i = 1; i <= lb; i++)
            {
                hh = Math.Max(hh, High[i]);
                ll = Math.Min(ll, Low[i]);
            }

            rangeTicks = (hh - ll) / TickSize;
            upTicks = 0.0;
            downTicks = 0.0;

            for (var i = 0; i < lb; i++)
            {
                var d = Close[i] - Close[i + 1];
                var ticks = Math.Abs(d) / TickSize;

                if (d > 0)
                    upTicks += ticks;
                else if (d < 0)
                    downTicks += ticks;
            }
        }

        private bool StrongTrend(out double rangeTicks, out int lookBackBars, out double strongMinTicks)
        {
            rangeTicks = 0;
            lookBackBars = 3;   
       
            TrendConfirm(out _, out var trendUp, out var trendDown);
            TrendTicks(lookBackBars, out _, out _, out _, out var longRangeTicks, out var shortRangeTicks);

            strongMinTicks = 200;

            if (trendUp)
            {
                rangeTicks = longRangeTicks;
                if (longRangeTicks >= strongMinTicks)
                {
                    return true;
                }
            }
            else if (trendDown)
            {
                rangeTicks = shortRangeTicks;
                if (shortRangeTicks >= strongMinTicks)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
