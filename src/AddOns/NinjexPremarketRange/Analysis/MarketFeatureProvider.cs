using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Interfaces;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis
{
    public sealed class MarketFeatureProvider : ICandidateFeatureProvider
    {
        private readonly PriorAttemptTracker priorAttempts = new PriorAttemptTracker();
        private readonly AcceptanceContextTracker acceptance = new AcceptanceContextTracker();

        private RangeSessionSnapshot session;
        private CandleSnapshot currentBar;
        private double atr1MinuteTicks;
        private double atr5MinuteTicks;
        private double adx5Minute;

        public void Reset(RangeSessionSnapshot sessionSnapshot)
        {
            session = sessionSnapshot;
            currentBar = null;
            atr1MinuteTicks = 0;
            atr5MinuteTicks = 0;
            adx5Minute = 0;
            priorAttempts.Reset();
            acceptance.Reset();
        }

        public void UpdateCompletedBar(
            CandleSnapshot bar,
            double currentAtr1MinuteTicks,
            double currentAtr5MinuteTicks,
            double currentAdx5Minute)
        {
            currentBar = bar;
            atr1MinuteTicks = currentAtr1MinuteTicks;
            atr5MinuteTicks = currentAtr5MinuteTicks;
            adx5Minute = currentAdx5Minute;

            if (bar != null && session != null)
                acceptance.Update(bar, session.TickSize);
        }

        public void OnBreakoutRegistered(BreakoutSignalSnapshot breakout)
        {
            if (breakout == null)
                return;

            priorAttempts.Register(breakout);
            acceptance.Register(breakout, session?.TickSize ?? 0);
        }

        public void OnBreakoutResolved(BreakoutResolutionSnapshot resolution)
        {
            if (resolution == null)
                return;

            priorAttempts.Resolve(resolution);
            acceptance.Resolve(resolution.EventId);
        }

        public CandidateFeatureSnapshot Capture(CandidateFeatureContext context)
        {
            if (context?.Breakout == null)
                return CandidateFeatureSnapshot.Empty;

            var capturedAt = context.Bar?.Time
                             ?? currentBar?.Time
                             ?? context.Breakout.BreakoutTime;

            var sessionValue = context.Session ?? session;
            var atr1 = context.Atr1MinuteTicks > 0
                ? context.Atr1MinuteTicks
                : atr1MinuteTicks;
            var atr5 = context.Atr5MinuteTicks > 0
                ? context.Atr5MinuteTicks
                : atr5MinuteTicks;
            var adx5 = context.Adx5Minute > 0
                ? context.Adx5Minute
                : adx5Minute;

            var prior = priorAttempts.Capture(context.Breakout, capturedAt);
            var acceptanceSnapshot = acceptance.Capture(context.Breakout.EventId);

            return new CandidateFeatureSnapshot(
                capturedAt,
                context.Breakout.EventId,
                atr1,
                atr5,
                adx5,
                atr5 > 0 && sessionValue != null
                    ? sessionValue.RangeTicks / atr5
                    : 0,
                atr1 > 0
                    ? context.Breakout.DistanceOutsideTicks / atr1
                    : 0,
                0,
                acceptanceSnapshot.ConsecutiveClosesOutside,
                acceptanceSnapshot.BarsContinuouslyOutside,
                acceptanceSnapshot.MinimumCloseDistanceOutsideTicks,
                acceptanceSnapshot.MaximumExcursionTicks,
                context.Breakout.AttemptNumber,
                prior.PriorSameDirectionAttempts,
                prior.PriorOppositeDirectionAttempts,
                prior.PriorReturnsInside15Minutes,
                prior.PriorReturnsInside30Minutes,
                prior.PriorReturnsInside60Minutes,
                prior.MinutesSincePreviousAttempt,
                prior.PreviousAttemptMfeTicks,
                prior.PreviousAttemptBarsUntilReturnInside,
                prior.BothRangeSidesBroken);
        }

        public CandidateFeatureSnapshot Capture(BreakoutSignalSnapshot breakout)
        {
            return Capture(new CandidateFeatureContext(
                session,
                breakout,
                currentBar,
                atr1MinuteTicks,
                atr5MinuteTicks,
                adx5Minute));
        }
    }
}
