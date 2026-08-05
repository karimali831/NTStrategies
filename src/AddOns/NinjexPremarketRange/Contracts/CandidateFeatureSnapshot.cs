using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    /// <summary>
    /// Immutable market features captured at the exact model decision point.
    ///
    /// Values may be zero during the v2.1-to-v2.2 parity phase.
    /// No property may contain information observed after SignalTime.
    /// </summary>
    public sealed class CandidateFeatureSnapshot
    {
        public static readonly CandidateFeatureSnapshot Empty =
            new CandidateFeatureSnapshot(
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                false);

        // Volatility/trend context
        public double Atr1MinuteTicks { get; }

        public double Atr5MinuteTicks { get; }

        public double Adx5Minute { get; }

        public double PremarketRangeToAtr5Minute { get; }

        public double BreakoutDistanceToAtr1Minute { get; }

        public double StructuralRiskToAtr1Minute { get; }

        // Acceptance context
        public int ConsecutiveClosesOutside { get; }

        public int BarsContinuouslyOutside { get; }

        public double MinimumCloseDistanceOutsideTicks { get; }

        public double MaximumExcursionSinceBreakoutTicks { get; }

        // Prior-attempt context
        public int AttemptNumber { get; }

        public int PriorSameDirectionAttempts { get; }

        public int PriorOppositeDirectionAttempts { get; }

        public int PriorReturnsInside15Minutes { get; }

        public int PriorReturnsInside30Minutes { get; }

        public int PriorReturnsInside60Minutes { get; }

        public double MinutesSincePreviousAttempt { get; }

        public double PreviousAttemptMfeTicks { get; }

        public bool BothRangeSidesBroken { get; }

        public CandidateFeatureSnapshot(
            double atr1MinuteTicks,
            double atr5MinuteTicks,
            double adx5Minute,
            double premarketRangeToAtr5Minute,
            double breakoutDistanceToAtr1Minute,
            double structuralRiskToAtr1Minute,
            int consecutiveClosesOutside,
            int barsContinuouslyOutside,
            double minimumCloseDistanceOutsideTicks,
            double maximumExcursionSinceBreakoutTicks,
            int attemptNumber,
            int priorSameDirectionAttempts,
            int priorOppositeDirectionAttempts,
            int priorReturnsInside15Minutes,
            int priorReturnsInside30Minutes,
            int priorReturnsInside60Minutes,
            double minutesSincePreviousAttempt,
            double previousAttemptMfeTicks,
            bool bothRangeSidesBroken)
        {
            Atr1MinuteTicks = atr1MinuteTicks;
            Atr5MinuteTicks = atr5MinuteTicks;
            Adx5Minute = adx5Minute;
            PremarketRangeToAtr5Minute =
                premarketRangeToAtr5Minute;
            BreakoutDistanceToAtr1Minute =
                breakoutDistanceToAtr1Minute;
            StructuralRiskToAtr1Minute =
                structuralRiskToAtr1Minute;

            ConsecutiveClosesOutside =
                consecutiveClosesOutside;
            BarsContinuouslyOutside =
                barsContinuouslyOutside;
            MinimumCloseDistanceOutsideTicks =
                minimumCloseDistanceOutsideTicks;
            MaximumExcursionSinceBreakoutTicks =
                maximumExcursionSinceBreakoutTicks;

            AttemptNumber = attemptNumber;
            PriorSameDirectionAttempts =
                priorSameDirectionAttempts;
            PriorOppositeDirectionAttempts =
                priorOppositeDirectionAttempts;
            PriorReturnsInside15Minutes =
                priorReturnsInside15Minutes;
            PriorReturnsInside30Minutes =
                priorReturnsInside30Minutes;
            PriorReturnsInside60Minutes =
                priorReturnsInside60Minutes;
            MinutesSincePreviousAttempt =
                minutesSincePreviousAttempt;
            PreviousAttemptMfeTicks =
                previousAttemptMfeTicks;
            BothRangeSidesBroken =
                bothRangeSidesBroken;
        }

        public CandidateFeatureSnapshot WithBreakoutContext(
            int attemptNumber,
            double breakoutDistanceTicks,
            double atr1MinuteTicks)
        {
            var normalizedBreakoutDistance =
                atr1MinuteTicks > 0
                    ? breakoutDistanceTicks / atr1MinuteTicks
                    : 0;

            return new CandidateFeatureSnapshot(
                Atr1MinuteTicks,
                Atr5MinuteTicks,
                Adx5Minute,
                PremarketRangeToAtr5Minute,
                normalizedBreakoutDistance,
                StructuralRiskToAtr1Minute,
                ConsecutiveClosesOutside,
                BarsContinuouslyOutside,
                MinimumCloseDistanceOutsideTicks,
                MaximumExcursionSinceBreakoutTicks,
                attemptNumber,
                PriorSameDirectionAttempts,
                PriorOppositeDirectionAttempts,
                PriorReturnsInside15Minutes,
                PriorReturnsInside30Minutes,
                PriorReturnsInside60Minutes,
                MinutesSincePreviousAttempt,
                PreviousAttemptMfeTicks,
                BothRangeSidesBroken);
        }
    }
}