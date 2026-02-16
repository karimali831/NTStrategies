#region Using declarations

using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        private struct EmaStruct
        {
            public bool HasBars;

            public bool PriceAboveFast;
            public bool PriceBelowFast;
            public bool PriceAboveBoth;
            public bool PriceBelowBoth;

            public double SepTicks;

            public double SlopeDirTicks;       // signed
            public double NetMoveTicks;        // abs
            public double PathTicks;           // path length
            public double Eff;                 // net/path

            public double SlopeStrengthTicks;  // net * eff
            public double SlopeMetricTicks;    // chosen metric

            public bool SlopeOk;
            public bool SepOk;
            public bool EmaCrossover;
            public bool StructureOk;
        }
        
        private enum EmaSlopeMetric
        {
            NetMove,        // abs(f0 - fPast)
            Strength,       // netMove * efficiency
            DirectionalAbs  // abs(signed slope)
        }
        
     // Keep your existing enum + struct types as-is.
    private EmaStruct GetEmaStruct(EmaSlopeMetric slopeMetricMode = EmaSlopeMetric.NetMove)
    {
        return GetEmaStruct(slopeMetricMode, 0);
    }

    // NEW: compute EMA structure AS-OF a specific bar (barsAgo)
    private EmaStruct GetEmaStruct(EmaSlopeMetric slopeMetricMode, int barsAgo)
    {
        var r = new EmaStruct();

        var idx = Math.Max(0, barsAgo);
        var lb  = Math.Max(1, EmaSlopeLookbackBars);

        // We access: idx, idx+lb, and (idx+i+1) inside loops
        if (CurrentBar < idx + lb + 1)
        {
            r.HasBars     = false;
            r.SlopeOk     = false;
            r.SepOk       = false;
            r.StructureOk = false;
            return r;
        }

        r.HasBars = true;

        // as-of idx
        var c  = Close[idx];
        var f0 = emaFast[idx];
        var s0 = emaSlow[idx];

        r.PriceAboveFast = c > f0;
        r.PriceBelowFast = c < f0;
        r.PriceAboveBoth = r.PriceAboveFast && c > s0;
        r.PriceBelowBoth = r.PriceBelowFast && c < s0;

        r.SepTicks = Math.Abs(f0 - s0) / TickSize;

        // slope over lookback: idx -> idx+lb
        var fPast = emaFast[idx + lb];
        r.SlopeDirTicks = (f0 - fPast) / TickSize;
        r.NetMoveTicks  = Math.Abs(f0 - fPast) / TickSize;

        // path over lookback: sum of |emaFast[idx+i] - emaFast[idx+i+1]|
        r.PathTicks = 0.0;
        for (var i = 0; i < lb; i++)
        {
            var a = emaFast[idx + i];
            var b = emaFast[idx + i + 1];
            r.PathTicks += Math.Abs(a - b) / TickSize;
        }

        r.Eff = r.PathTicks <= 1e-9 ? 0.0 : (r.NetMoveTicks / r.PathTicks);
        r.SlopeStrengthTicks = r.NetMoveTicks * r.Eff;

        switch (slopeMetricMode)
        {
            case EmaSlopeMetric.Strength:
                r.SlopeMetricTicks = r.SlopeStrengthTicks;
                break;
            case EmaSlopeMetric.DirectionalAbs:
                r.SlopeMetricTicks = Math.Abs(r.SlopeDirTicks);
                break;
            case EmaSlopeMetric.NetMove:
            default:
                r.SlopeMetricTicks = r.NetMoveTicks;
                break;
        }

        r.SlopeOk = MinEmaSlopeTicks <= 0 || r.SlopeMetricTicks >= MinEmaSlopeTicks;
        r.SepOk   = MinEmaSeparationTicks <= 0 || r.SepTicks >= MinEmaSeparationTicks;

        // Crossover detection within the lookback window as-of idx:
        // compare fast-slow at (idx+i) vs (idx+i+1)
        r.EmaCrossover = false;
        for (var i = 0; i < lb; i++)
        {
            var d0 = emaFast[idx + i]     - emaSlow[idx + i];
            var d1 = emaFast[idx + i + 1] - emaSlow[idx + i + 1];

            if (d0 == 0 || d1 == 0 || (d0 > 0 && d1 < 0) || (d0 < 0 && d1 > 0))
            {
                r.EmaCrossover = true;
                break;
            }
        }

        // CLEAN SLATE structure logic for pullback trend-following:
        r.StructureOk = r.SlopeOk && r.SepOk && !r.EmaCrossover;
        return r;
    }
        
        private int BarsSinceEmaCrossAsOf(int lookback, int barsAgo)
        {
            var idx = Math.Max(0, barsAgo);
            var max = Math.Min(CurrentBar - idx - 1, lookback);

            for (int i = 0; i <= max; i++)
            {
                int b = idx + i;

                var diffNow  = emaFast[b]     - emaSlow[b];
                var diffPrev = emaFast[b + 1] - emaSlow[b + 1];

                if (diffNow == 0)
                    return i;

                if (Math.Sign(diffNow) != Math.Sign(diffPrev))
                    return i;
            }

            return lookback + 1;
        }

        private int BarsSinceEmaCrossNow(int lookback)
        {
            return BarsSinceEmaCrossAsOf(lookback, 0);
        }

        private bool PullbackTouchedFastEmaPrevBar(bool longSide, int barsAgo, out double emaTouch, out double distTicks)
        {
            emaTouch = 0;
            distTicks = double.NaN;

            if (CurrentBar < barsAgo)
                return false;

            emaTouch = emaFast[barsAgo];

            if (longSide)
            {
                var prox = Math.Max(0, LongTouchTicks) * TickSize;
                distTicks = (Low[barsAgo] - emaTouch) / TickSize;
                return Low[barsAgo] <= (emaTouch + prox);
            }
            else
            {
                var prox = Math.Max(0, ShortTouchTicks) * TickSize;
                distTicks = (High[barsAgo] - emaTouch) / TickSize;
                return High[barsAgo] >= (emaTouch - prox);
            }
        }
    }
}