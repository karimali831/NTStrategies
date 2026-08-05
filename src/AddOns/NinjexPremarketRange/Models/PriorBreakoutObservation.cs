using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class PriorBreakoutObservation
    {
        public string EventId { get; }
        public TradeDirection Direction { get; }
        public DateTime BreakoutTime { get; }
        public int AttemptNumber { get; }

        public bool IsResolved { get; private set; }
        public bool ReturnedInside { get; private set; }
        public DateTime ResolutionTime { get; private set; }
        public double FinalMfeTicks { get; private set; }
        public int BarsUntilReturnInside { get; private set; }

        public PriorBreakoutObservation(BreakoutSignalSnapshot breakout)
        {
            if (breakout == null)
                throw new ArgumentNullException(nameof(breakout));

            EventId = breakout.EventId;
            Direction = breakout.Direction;
            BreakoutTime = breakout.BreakoutTime;
            AttemptNumber = breakout.AttemptNumber;
        }

        public void Resolve(BreakoutResolutionSnapshot resolution)
        {
            if (resolution == null || !string.Equals(EventId, resolution.EventId, StringComparison.Ordinal))
                return;

            IsResolved = true;
            ReturnedInside = resolution.ReturnedInside;
            ResolutionTime = resolution.ResolutionTime;
            FinalMfeTicks = resolution.FinalMfeTicks;
            BarsUntilReturnInside = resolution.BarsUntilReturnInside;
        }
    }
}
