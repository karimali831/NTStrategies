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
        
        private EmaStruct GetEmaStruct(EmaSlopeMetric slopeMetricMode = EmaSlopeMetric.NetMove)
        {
            var r = new EmaStruct();

            var lb = Math.Max(1, EmaSlopeLookbackBars);
            if (CurrentBar <  lb)
            {
                r.HasBars = false;
                r.SlopeOk = false;
                r.SepOk = false;
                r.StructureOk = false;
                return r;
            }

            r.HasBars = true;

            var c  = Close[0];
            var f0 = emaFast[0];
            var s0 = emaSlow[0];

            r.PriceAboveFast = c > f0;
            r.PriceBelowFast = c < f0;
            r.PriceAboveBoth = r.PriceAboveFast && c > s0;
            r.PriceBelowBoth = r.PriceBelowFast && c < s0;

            r.SepTicks = Math.Abs(f0 - s0) / TickSize;

            var fPast = emaFast[lb];
            r.SlopeDirTicks = (f0 - fPast) / TickSize;
            r.NetMoveTicks  = Math.Abs(f0 - fPast) / TickSize;

            r.PathTicks = 0.0;
            for (var i = 0; i < lb; i++)
            {
                var a = emaFast[i];
                var b = emaFast[i + 1];
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

            r.EmaCrossover = false;
            for (var i = 0; i < lb; i++)
            {
                var d0 = emaFast[i] - emaSlow[i];
                var d1 = emaFast[i + 1] - emaSlow[i + 1];

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
        
        private int BarsSinceEmaCross(int maxLookbackBars)
        {
            // Need i+1 indexing, so cap to CurrentBar-1
            var max = Math.Min(maxLookbackBars, CurrentBar - 1);
            if (max < 1)
                return int.MaxValue;

            for (var i = 0; i <= max; i++)
            {
                var fastNow  = emaFast[i];
                var slowNow  = emaSlow[i];
                var fastPrev = emaFast[i + 1];
                var slowPrev = emaSlow[i + 1];

                var crossedUp   = fastNow > slowNow && fastPrev <= slowPrev;
                var crossedDown = fastNow < slowNow && fastPrev >= slowPrev;

                if (crossedUp || crossedDown)
                    return i; // barsAgo
            }

            return int.MaxValue;
        }
    }
}