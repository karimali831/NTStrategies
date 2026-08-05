using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis
{
    public sealed class PriorAttemptTracker
    {
        private readonly List<PriorBreakoutObservation> observations =
            new List<PriorBreakoutObservation>();

        public void Reset()
        {
            observations.Clear();
        }

        public void Register(BreakoutSignalSnapshot breakout)
        {
            if (breakout == null || observations.Any(x => x.EventId == breakout.EventId))
                return;

            observations.Add(new PriorBreakoutObservation(breakout));
        }

        public void Resolve(BreakoutResolutionSnapshot resolution)
        {
            if (resolution == null)
                return;

            var observation = observations.FirstOrDefault(
                x => string.Equals(x.EventId, resolution.EventId, StringComparison.Ordinal));
            observation?.Resolve(resolution);
        }

        public PriorAttemptSnapshot Capture(BreakoutSignalSnapshot current, DateTime capturedAt)
        {
            if (current == null)
                return PriorAttemptSnapshot.Empty;

            var prior = observations
                .Where(x => !string.Equals(x.EventId, current.EventId, StringComparison.Ordinal)
                            && x.BreakoutTime <= current.BreakoutTime)
                .OrderBy(x => x.BreakoutTime)
                .ToList();

            var previous = prior.LastOrDefault();
            return new PriorAttemptSnapshot(
                prior.Count(x => x.Direction == current.Direction),
                prior.Count(x => x.Direction != current.Direction),
                CountReturnedInside(prior, capturedAt, 15),
                CountReturnedInside(prior, capturedAt, 30),
                CountReturnedInside(prior, capturedAt, 60),
                previous == null ? 0 : Math.Max(0, (capturedAt - previous.BreakoutTime).TotalMinutes),
                previous?.FinalMfeTicks ?? 0,
                previous?.BarsUntilReturnInside ?? 0,
                observations.Any(x => x.Direction == TradeDirection.Long)
                && observations.Any(x => x.Direction == TradeDirection.Short));
        }

        private static int CountReturnedInside(
            IEnumerable<PriorBreakoutObservation> prior,
            DateTime capturedAt,
            int minutes)
        {
            var start = capturedAt.AddMinutes(-minutes);
            return prior.Count(x => x.IsResolved
                                    && x.ReturnedInside
                                    && x.ResolutionTime >= start
                                    && x.ResolutionTime <= capturedAt);
        }
    }

    public sealed class PriorAttemptSnapshot
    {
        public static readonly PriorAttemptSnapshot Empty =
            new PriorAttemptSnapshot(0, 0, 0, 0, 0, 0, 0, 0, false);

        public int PriorSameDirectionAttempts { get; }
        public int PriorOppositeDirectionAttempts { get; }
        public int PriorReturnsInside15Minutes { get; }
        public int PriorReturnsInside30Minutes { get; }
        public int PriorReturnsInside60Minutes { get; }
        public double MinutesSincePreviousAttempt { get; }
        public double PreviousAttemptMfeTicks { get; }
        public int PreviousAttemptBarsUntilReturnInside { get; }
        public bool BothRangeSidesBroken { get; }

        public PriorAttemptSnapshot(
            int priorSameDirectionAttempts,
            int priorOppositeDirectionAttempts,
            int priorReturnsInside15Minutes,
            int priorReturnsInside30Minutes,
            int priorReturnsInside60Minutes,
            double minutesSincePreviousAttempt,
            double previousAttemptMfeTicks,
            int previousAttemptBarsUntilReturnInside,
            bool bothRangeSidesBroken)
        {
            PriorSameDirectionAttempts = priorSameDirectionAttempts;
            PriorOppositeDirectionAttempts = priorOppositeDirectionAttempts;
            PriorReturnsInside15Minutes = priorReturnsInside15Minutes;
            PriorReturnsInside30Minutes = priorReturnsInside30Minutes;
            PriorReturnsInside60Minutes = priorReturnsInside60Minutes;
            MinutesSincePreviousAttempt = minutesSincePreviousAttempt;
            PreviousAttemptMfeTicks = previousAttemptMfeTicks;
            PreviousAttemptBarsUntilReturnInside = previousAttemptBarsUntilReturnInside;
            BothRangeSidesBroken = bothRangeSidesBroken;
        }
    }
}
