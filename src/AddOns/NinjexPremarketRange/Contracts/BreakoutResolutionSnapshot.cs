using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class BreakoutResolutionSnapshot
    {
        public string EventId { get; }
        public TradeDirection Direction { get; }
        public DateTime BreakoutTime { get; }
        public DateTime ResolutionTime { get; }
        public bool ReturnedInside { get; }
        public double FinalMfeTicks { get; }
        public int BarsUntilReturnInside { get; }

        public BreakoutResolutionSnapshot(
            string eventId,
            TradeDirection direction,
            DateTime breakoutTime,
            DateTime resolutionTime,
            bool returnedInside,
            double finalMfeTicks,
            int barsUntilReturnInside)
        {
            EventId = eventId ?? string.Empty;
            Direction = direction;
            BreakoutTime = breakoutTime;
            ResolutionTime = resolutionTime;
            ReturnedInside = returnedInside;
            FinalMfeTicks = finalMfeTicks;
            BarsUntilReturnInside = barsUntilReturnInside;
        }

        public static BreakoutResolutionSnapshot From(BreakoutEvent breakout)
        {
            if (breakout == null)
                return null;

            return new BreakoutResolutionSnapshot(
                breakout.EventId,
                breakout.Direction,
                breakout.BreakoutTime,
                breakout.ResolutionTime,
                breakout.ReturnedInside,
                breakout.MfeTicks,
                breakout.BarsUntilReturnInside);
        }
    }
}
