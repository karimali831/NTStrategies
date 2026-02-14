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
            var alignedUp   = emaFast[0] > emaSlow[0];
            var alignedDown = emaFast[0] < emaSlow[0];

            var slopeUpOk   = TrendSlopeMinTicks == 0 || m.SlopeDirTicks >= TrendSlopeMinTicks;
            var slopeDownOk = TrendSlopeMinTicks == 0 || -m.SlopeDirTicks >= TrendSlopeMinTicks;

            trendUp   = alignedUp   && slopeUpOk   && m.PriceAboveFast;
            trendDown = alignedDown && slopeDownOk && m.PriceBelowFast;

            if (!trendUp && !trendDown)
            {
                if (!alignedUp && !alignedDown)
                    failReason = "emas-flat-or-crossed";
                else if (alignedUp && !slopeUpOk)
                    failReason = $"ema-slope-too-weak-up ({m.SlopeDirTicks} < {TrendSlopeMinTicks})";
                else if (alignedDown && !slopeDownOk)
                    failReason = $"ema-slope-too-weak-down ({-m.SlopeDirTicks} < {TrendSlopeMinTicks})";
                else if (alignedUp && !m.PriceAboveFast)
                    failReason = "price-not-above-fast-ema";
                else if (alignedDown && !m.PriceBelowFast)
                    failReason = "price-not-below-fast-ema";
                else
                    failReason = "trend-unknown";
            }

            return trendUp || trendDown;
        }
        
        private bool ConfirmLongEntry(int barsAgo, out string failReason)
        {
            failReason = "none";

            if (!(Close[barsAgo] > emaFast[barsAgo] && Close[barsAgo] > emaSlow[barsAgo]))
            {
                failReason = "close-not-above-both-emas";
                return false;
            }

            if (Close[barsAgo] < Open[barsAgo])
            {
                failReason = "not-bullish-candle";
                return false;
            }

            var bodyTicks = Math.Abs(Close[barsAgo] - Open[barsAgo]) / TickSize;
            if (StrongBodyTicks > 0 && bodyTicks < StrongBodyTicks)
            {
                failReason = $"body-too-small ({bodyTicks} < {StrongBodyTicks})";
                return false;
            }

            return true;
        }

        private bool ConfirmShortEntry(int barsAgo, out string failReason)
        {
            failReason = "none";

            if (!(Close[barsAgo] < emaFast[barsAgo] && Close[barsAgo] < emaSlow[barsAgo]))
            {
                failReason = "close-not-below-both-emas";
                return false;
            }

            if (Close[barsAgo] > Open[barsAgo])
            {
                failReason = "not-bearish-candle";
                return false;
            }

            var bodyTicks = Math.Abs(Close[barsAgo] - Open[barsAgo]) / TickSize;
            if (StrongBodyTicks > 0 && bodyTicks < StrongBodyTicks)
            {
                failReason = $"body-too-small ({bodyTicks} < {StrongBodyTicks})";
                return false;
            }

            return true;
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
        
        private bool IsTrendUpSimple()
        {
            var m = GetEmaStruct();
            return m.HasBars && m.PriceAboveFast && emaFast[0] > emaSlow[0];
        }

        private bool IsTrendDownSimple()
        {
            var m = GetEmaStruct();
            return m.HasBars && m.PriceBelowFast && emaFast[0] < emaSlow[0];
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
    }
}
