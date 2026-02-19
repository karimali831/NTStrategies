
using System;

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class BaselineTrendPullback : Strategy
    {
        private BarFeatures ComputeFeatures(int barsAgo)
        {
            var ef = emaFast[barsAgo];
            var es = emaSlow[barsAgo];

            var longBias = ef > es;
            var shortBias = ef < es;

            // Slope in ticks (positive = rising)
            var efPast = emaFast[barsAgo + TrendSlopeLookbackBars];
            var esPast = emaSlow[barsAgo + TrendSlopeLookbackBars];

            var fastSlopeTicks = (ef - efPast) / TickSize;
            var slowSlopeTicks = (es - esPast) / TickSize;

            var sepTicks = Math.Abs(ef - es) / TickSize;

            // Structure checks
            var slopeOk = longBias
                ? fastSlopeTicks >= MinTrendSlopeTicks && slowSlopeTicks >= 0
                : shortBias && -fastSlopeTicks >= MinTrendSlopeTicks && -slowSlopeTicks >= 0;

            var sepOk = sepTicks >= MinEmaSepTicks;

            var structureOk = (longBias || shortBias) && slopeOk && sepOk;
            var structureFail =
                !(longBias || shortBias) ? "no-bias-emas-flat"
                : !slopeOk ? "slope-too-weak"
                : !sepOk ? "ema-sep-too-small"
                : "none";

            // Regime
            var adxVal = adx[barsAgo];
            var atrVal = atr[barsAgo];

            // ATR median lookback
            var atrMedian = ComputeAtrMedian(barsAgo + 1, AtrMedianLookbackBars);

            var adxOk = adxVal >= MinAdx && adxVal <= MaxAdx;
            var atrOk = atrVal >= atrMedian;

            var regimeOk = adxOk && atrOk;
            var regimeFail =
                !adxOk ? $"adx-outside({MinAdx}-{MaxAdx})"
                : !atrOk ? "atr-below-median"
                : "none";

            // Candle quality
            var o = Open[barsAgo];
            var h = High[barsAgo];
            var l = Low[barsAgo];
            var c = Close[barsAgo];

            var range = h - l;
            var rangeTicks = range / TickSize;

            var body = Math.Abs(c - o);
            var wick = range - body;
            var wickPct = (range <= TickSize) ? 0 : (wick / range);

            var rangeOk = rangeTicks >= MinRangeTicks;
            var wickOk = wickPct <= MaxWickPct;

            var candleOk = rangeOk && wickOk;
            var candleFail =
                !rangeOk ? "range-too-small"
                : !wickOk ? "wick-too-large"
                : "none";

            // Pullback distance to fast EMA (signed, in ticks; + means price above EMA)
            var distToFastTicks = (c - ef) / TickSize;

            return new BarFeatures
            {
                Time = Time[barsAgo],
                Bar = CurrentBar - barsAgo,
                Close = c,
                LongBias = longBias,
                ShortBias = shortBias,

                EmaFast = ef,
                EmaSlow = es,
                FastSlopeTicks = fastSlopeTicks,
                SlowSlopeTicks = slowSlopeTicks,
                SepTicks = sepTicks,

                Adx = adxVal,
                Atr = atrVal,
                AtrMedian = atrMedian,

                WickPct = wickPct,
                RangeTicks = rangeTicks,

                DistToFastTicks = distToFastTicks,

                StructureOk = structureOk,
                StructureFailReason = structureFail,

                RegimeOk = regimeOk,
                RegimeFailReason = regimeFail,

                CandleOk = candleOk,
                CandleFailReason = candleFail,
            };
        }

        private double ComputeAtrMedian(int startBarsAgo, int count)
        {
            // Median of ATR over a window [startBarsAgo .. startBarsAgo+count-1]
            // startBarsAgo=1 means excluding current bar (to avoid lookahead bias)
            count = Math.Max(5, count);
            var vals = new double[count];

            for (var i = 0; i < count; i++)
                vals[i] = atr[startBarsAgo + i];

            Array.Sort(vals);

            var mid = vals.Length / 2;
            if (vals.Length % 2 == 1)
                return vals[mid];

            return (vals[mid - 1] + vals[mid]) / 2.0;
        }

        private bool TouchedFastEma(bool longSide)
        {
            int look = Math.Min(TouchLookbackBars, CurrentBar - 1);
            if (look <= 0)
                return false;

            double proximity = Math.Max(0, TouchTicks) * TickSize;

            if (longSide)
            {
                for (var i = 1; i <= look; i++)
                    if (Low[i] <= emaFast[i] + proximity)
                        return true;
            }
            else
            {
                for (var i = 1; i <= look; i++)
                    if (High[i] >= emaFast[i] - proximity)
                        return true;
            }

            return false;
        }

        private bool LongTriggerOk(BarFeatures f)
        {
            if (RequireCloseBackAcrossFastEma && Close[0] <= emaFast[0])
                return false;

            if (RequireSignalCandleInTrendDir && Close[0] < Open[0])
                return false;

            return true;
        }

        private bool ShortTriggerOk(BarFeatures f)
        {
            if (RequireCloseBackAcrossFastEma && Close[0] >= emaFast[0])
                return false;

            if (RequireSignalCandleInTrendDir && Close[0] > Open[0])
                return false;

            return true;
        }
    }
}