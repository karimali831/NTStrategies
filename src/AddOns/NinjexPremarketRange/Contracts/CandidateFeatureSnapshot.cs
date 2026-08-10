using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class CandidateFeatureSnapshot
    {
        public static readonly CandidateFeatureSnapshot Empty =
            new CandidateFeatureSnapshot(
                DateTime.MinValue, string.Empty,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0, 0, 0,
                0, 0, 0, false);

        public DateTime CapturedAt { get; }
        public string BreakoutEventId { get; }

        public double Atr1MinuteTicks { get; }
        public double Atr5MinuteTicks { get; }
        public double Adx5Minute { get; }
        public double PremarketRangeToAtr5Minute { get; }
        public double BreakoutDistanceToAtr1Minute { get; }
        public double StructuralRiskToAtr1Minute { get; }

        public int ConsecutiveClosesOutside { get; }
        public int BarsContinuouslyOutside { get; }
        public double MinimumCloseDistanceOutsideTicks { get; }
        public double MaximumExcursionSinceBreakoutTicks { get; }

        public int AttemptNumber { get; }
        public int PriorSameDirectionAttempts { get; }
        public int PriorOppositeDirectionAttempts { get; }
        public int PriorReturnsInside15Minutes { get; }
        public int PriorReturnsInside30Minutes { get; }
        public int PriorReturnsInside60Minutes { get; }
        public double MinutesSincePreviousAttempt { get; }
        public double PreviousAttemptMfeTicks { get; }
        public int PreviousAttemptBarsUntilReturnInside { get; }
        public bool BothRangeSidesBroken { get; }

        public CandidateFeatureSnapshot(
            DateTime capturedAt,
            string breakoutEventId,
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
            int previousAttemptBarsUntilReturnInside,
            bool bothRangeSidesBroken)
        {
            CapturedAt = capturedAt;
            BreakoutEventId = breakoutEventId ?? string.Empty;
            Atr1MinuteTicks = atr1MinuteTicks;
            Atr5MinuteTicks = atr5MinuteTicks;
            Adx5Minute = adx5Minute;
            PremarketRangeToAtr5Minute = premarketRangeToAtr5Minute;
            BreakoutDistanceToAtr1Minute = breakoutDistanceToAtr1Minute;
            StructuralRiskToAtr1Minute = structuralRiskToAtr1Minute;
            ConsecutiveClosesOutside = consecutiveClosesOutside;
            BarsContinuouslyOutside = barsContinuouslyOutside;
            MinimumCloseDistanceOutsideTicks = minimumCloseDistanceOutsideTicks;
            MaximumExcursionSinceBreakoutTicks = maximumExcursionSinceBreakoutTicks;
            AttemptNumber = attemptNumber;
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

        // Backward-compatible constructor used by parity-era callers.
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
            : this(
                DateTime.MinValue,
                string.Empty,
                atr1MinuteTicks,
                atr5MinuteTicks,
                adx5Minute,
                premarketRangeToAtr5Minute,
                breakoutDistanceToAtr1Minute,
                structuralRiskToAtr1Minute,
                consecutiveClosesOutside,
                barsContinuouslyOutside,
                minimumCloseDistanceOutsideTicks,
                maximumExcursionSinceBreakoutTicks,
                attemptNumber,
                priorSameDirectionAttempts,
                priorOppositeDirectionAttempts,
                priorReturnsInside15Minutes,
                priorReturnsInside30Minutes,
                priorReturnsInside60Minutes,
                minutesSincePreviousAttempt,
                previousAttemptMfeTicks,
                0,
                bothRangeSidesBroken)
        {
        }

        public CandidateFeatureSnapshot WithStructuralRisk(double structuralRiskToAtr1Minute)
        {
            return new CandidateFeatureSnapshot(
                CapturedAt, BreakoutEventId,
                Atr1MinuteTicks, Atr5MinuteTicks, Adx5Minute,
                PremarketRangeToAtr5Minute, BreakoutDistanceToAtr1Minute,
                structuralRiskToAtr1Minute,
                ConsecutiveClosesOutside, BarsContinuouslyOutside,
                MinimumCloseDistanceOutsideTicks, MaximumExcursionSinceBreakoutTicks,
                AttemptNumber, PriorSameDirectionAttempts, PriorOppositeDirectionAttempts,
                PriorReturnsInside15Minutes, PriorReturnsInside30Minutes,
                PriorReturnsInside60Minutes, MinutesSincePreviousAttempt,
                PreviousAttemptMfeTicks, PreviousAttemptBarsUntilReturnInside,
                BothRangeSidesBroken);
        }
    }
}
