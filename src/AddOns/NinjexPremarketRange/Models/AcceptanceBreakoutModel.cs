using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Interfaces;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class AcceptanceBreakoutModel : IEntryCandidateModel
    {
        private sealed class AcceptanceState
        {
            public BreakoutSignalSnapshot Breakout { get; set; }
            public int ExpiryBarIndex { get; set; }
        }

        private readonly List<AcceptanceState> states = new List<AcceptanceState>();

        public string ModelId => "AcceptanceBreakout";
        public string ModelVersion => "1.0.0";
        public bool IsEnabled { get; set; }

        public int MinimumConsecutiveClosesOutside { get; set; }
        public int MaximumBarsAfterBreakout { get; set; }
        public double MinimumExcursionTicks { get; set; }
        public double MinimumCloseDistanceTicks { get; set; }
        public bool AllowLaterAttempts { get; set; }
        public int MinimumPriorFailedAttempts { get; set; }

        public void Reset(RangeSessionSnapshot session)
        {
            states.Clear();
        }

        public void OnBreakout(BreakoutSignalSnapshot breakout)
        {
            if (!IsEnabled || breakout == null)
                return;

            states.Add(new AcceptanceState
            {
                Breakout = breakout,
                ExpiryBarIndex = breakout.BreakoutBarIndex + Math.Max(0, MaximumBarsAfterBreakout)
            });
        }

        public IReadOnlyList<CandidateSignal> Evaluate(CandidateModelContext context)
        {
            var output = new List<CandidateSignal>();
            if (!IsEnabled || context?.Bar == null || context.Session == null)
                return output.AsReadOnly();

            for (var i = states.Count - 1; i >= 0; i--)
            {
                var state = states[i];
                if (state?.Breakout == null)
                {
                    states.RemoveAt(i);
                    continue;
                }

                var breakout = state.Breakout;
                var barsAfter = context.Bar.BarIndex - breakout.BreakoutBarIndex;
                if (barsAfter < 0)
                    continue;

                if (context.Bar.BarIndex > state.ExpiryBarIndex)
                {
                    states.RemoveAt(i);
                    continue;
                }

                var features = context.CaptureFeatures(breakout);
                if (features == null)
                    continue;

                var outside = breakout.Direction == TradeDirection.Long
                    ? context.Bar.Close > breakout.RangeLevel
                    : context.Bar.Close < breakout.RangeLevel;

                if (!outside)
                {
                    states.RemoveAt(i);
                    continue;
                }

                if (breakout.AttemptNumber > 1)
                {
                    if (!AllowLaterAttempts
                        || features.PriorReturnsInside60Minutes < MinimumPriorFailedAttempts)
                    {
                        states.RemoveAt(i);
                        continue;
                    }
                }

                if (features.ConsecutiveClosesOutside < MinimumConsecutiveClosesOutside
                    || features.MaximumExcursionSinceBreakoutTicks < MinimumExcursionTicks
                    || features.MinimumCloseDistanceOutsideTicks < MinimumCloseDistanceTicks)
                {
                    continue;
                }

                var structuralStop = breakout.Direction == TradeDirection.Long
                    ? context.Bar.Low
                    : context.Bar.High;

                output.Add(new CandidateSignal(
                    breakout.EventId + "-ACCEPTANCE",
                    breakout.EventId,
                    ModelId,
                    ModelVersion,
                    breakout.Direction,
                    context.Bar.Time,
                    context.Bar.BarIndex,
                    breakout.RangeLevel,
                    structuralStop,
                    context.Bar,
                    context.Metrics,
                    features,
                    CandidateQualificationSnapshot.Passed,
                    new CandidateModelDetails(barsAfter, 0, 0),
                    true,
                    CandidateQualificationCodes.AcceptanceQualified,
                    string.Format(
                        "Acceptance qualified after {0} consecutive closes outside; excursion={1:0.0} ticks; minimum close distance={2:0.0} ticks.",
                        features.ConsecutiveClosesOutside,
                        features.MaximumExcursionSinceBreakoutTicks,
                        features.MinimumCloseDistanceOutsideTicks)));

                states.RemoveAt(i);
            }

            return output.AsReadOnly();
        }

        public void OnBreakoutResolved(string breakoutEventId)
        {
            if (string.IsNullOrWhiteSpace(breakoutEventId))
                return;

            for (var i = states.Count - 1; i >= 0; i--)
            {
                if (states[i]?.Breakout != null
                    && string.Equals(states[i].Breakout.EventId, breakoutEventId, StringComparison.Ordinal))
                {
                    states.RemoveAt(i);
                }
            }
        }
    }
}
