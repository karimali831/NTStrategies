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
            
            // -------- Trend context (single source of truth) --------
            TrendTicks(30, out _, out _, out _, out var upTicks,
                out var downTicks);
            
            var alignedUp   = emaFast[0] > emaSlow[0];
            var alignedDown = emaFast[0] < emaSlow[0];

            // trendUp   = alignedUp   && m.PriceAboveFast && upTicks > downTicks;
            // trendDown = alignedDown && m.PriceBelowFast && downTicks > upTicks;

            var greaterTrendUpTicks = upTicks > downTicks;
            var greaterTrendDownTicks = downTicks > upTicks;
            
            trendUp   = alignedUp   && greaterTrendUpTicks;
            trendDown = alignedDown && greaterTrendDownTicks;
            
            if (!trendUp && !trendDown)
            {
                if (!alignedUp && !alignedDown)
                    failReason = "emas-flat-or-crossed";
                // else if (alignedUp && !m.PriceAboveFast)
                //     failReason = "price-not-above-fast-ema";
                // else if (alignedDown && !m.PriceBelowFast)
                //     failReason = "price-not-below-fast-ema";
                else if (alignedUp && greaterTrendDownTicks)
                    failReason = $"upticks-lesser-than-downticks (ut: {upTicks} dt: ${downTicks})";
                else if (alignedDown && greaterTrendUpTicks)
                    failReason = $"downticks-lesser-than-upticks (dt: {downTicks} ut: ${upTicks})";
                else
                    failReason = "trend-unknown";
            }

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

        private bool StrongTrend(bool longSide, out double rangeTicks, out int lookBackBars, out double strongMinTicks)
        {
            rangeTicks = 0;
            lookBackBars = 5;   
            strongMinTicks = 200;       
            
            TrendTicks(lookBackBars, out _, out _, out _, out var longRangeTicks, out var shortRangeTicks);

            if (longSide)
            {
                rangeTicks = longRangeTicks;
                return longRangeTicks >= strongMinTicks;
            }

            rangeTicks = shortRangeTicks;
            return shortRangeTicks >= strongMinTicks;
        }
    }
}
