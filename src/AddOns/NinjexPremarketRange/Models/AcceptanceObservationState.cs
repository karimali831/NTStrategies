using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class AcceptanceObservationState
    {
        public BreakoutSignalSnapshot Breakout { get; }
        public int ConsecutiveClosesOutside { get; set; }
        public int BarsContinuouslyOutside { get; set; }
        public double MinimumCloseDistanceOutsideTicks { get; set; }
        public double MaximumExcursionTicks { get; set; }
        public bool ReturnedInside { get; set; }
        public int LastProcessedBarIndex { get; set; }

        public AcceptanceObservationState(BreakoutSignalSnapshot breakout)
        {
            Breakout = breakout ?? throw new ArgumentNullException(nameof(breakout));
            MinimumCloseDistanceOutsideTicks = double.MaxValue;
            LastProcessedBarIndex = int.MinValue;
        }
    }
}
