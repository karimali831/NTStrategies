#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis
{
    public sealed class CandleMetricsCalculator
    {
        public CandleMetrics Calculate(
            CandleSnapshot candle,
            IReadOnlyList<CandleSnapshot> history,
            int lookback,
            double tickSize)
        {
            var metrics = new CandleMetrics();

            if (candle == null || tickSize <= 0)
                return metrics;

            metrics.RangeTicks = candle.Range / tickSize;
            metrics.BodyTicks = candle.Body / tickSize;
            metrics.UpperWickTicks = candle.UpperWick / tickSize;
            metrics.LowerWickTicks = candle.LowerWick / tickSize;

            if (candle.Range > 0)
            {
                metrics.BodyPercent = candle.Body / candle.Range * 100.0;
                metrics.CloseLocationPercent = (candle.Close - candle.Low) / candle.Range * 100.0;
            }

            var sample = history == null
                ? new List<CandleSnapshot>()
                : history
                    .Where(x => x != null && x.Time < candle.Time)
                    .OrderByDescending(x => x.Time)
                    .Take(Math.Max(1, lookback))
                    .ToList();

            if (sample.Count == 0)
                return metrics;

            metrics.AverageBodyTicks = sample.Average(x => x.Body / tickSize);
            metrics.AverageVolume = sample.Average(x => x.Volume);

            if (metrics.AverageBodyTicks > 0)
                metrics.RelativeBodyMultiple = metrics.BodyTicks / metrics.AverageBodyTicks;

            if (metrics.AverageVolume > 0)
                metrics.RelativeVolumeMultiple = candle.Volume / metrics.AverageVolume;

            return metrics;
        }
    }
}
