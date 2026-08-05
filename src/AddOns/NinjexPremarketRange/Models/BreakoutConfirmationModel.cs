using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class BreakoutConfirmationModel
        : IEntryCandidateModel
    {
        private readonly List<BreakoutSignalSnapshot>
            pendingBreakouts =
                new List<BreakoutSignalSnapshot>();

        public string ModelId => "BreakoutConfirmation";

        public string ModelVersion => "1.0.0";

        public bool IsEnabled { get; set; }

        public double MinimumBodyPercent { get; set; }

        public double MinimumCloseLocationPercent { get; set; }

        public double MinimumRelativeBodyMultiple { get; set; }

        public void Reset(RangeSessionSnapshot session)
        {
            pendingBreakouts.Clear();
        }

        public void OnBreakout(
            BreakoutSignalSnapshot breakout)
        {
            if (!IsEnabled || breakout == null)
                return;

            pendingBreakouts.Add(breakout);
        }

        public IReadOnlyList<CandidateSignal> Evaluate(
            CandidateModelContext context)
        {
            var candidates =
                new List<CandidateSignal>();

            if (!IsEnabled
                || context == null
                || context.Bar == null)
            {
                return candidates.AsReadOnly();
            }

            for (var i = pendingBreakouts.Count - 1;
                 i >= 0;
                 i--)
            {
                var breakout = pendingBreakouts[i];

                if (breakout == null)
                {
                    pendingBreakouts.RemoveAt(i);
                    continue;
                }

                // Preserve v2.1 behavior exactly:
                // the breakout candle is the confirmation candle.
                if (breakout.BreakoutBarIndex
                    != context.Bar.BarIndex)
                {
                    // A pending event older than the current completed
                    // bar can no longer be evaluated by this model.
                    if (breakout.BreakoutBarIndex
                        < context.Bar.BarIndex)
                    {
                        pendingBreakouts.RemoveAt(i);
                    }

                    continue;
                }

                candidates.Add(
                    BuildSignal(
                        context,
                        breakout));

                pendingBreakouts.RemoveAt(i);
            }

            return candidates.AsReadOnly();
        }

        public void OnBreakoutResolved(
            string breakoutEventId)
        {
            if (string.IsNullOrWhiteSpace(
                    breakoutEventId))
            {
                return;
            }

            for (var i = pendingBreakouts.Count - 1;
                 i >= 0;
                 i--)
            {
                var breakout = pendingBreakouts[i];

                if (breakout != null
                    && string.Equals(
                        breakout.EventId,
                        breakoutEventId,
                        StringComparison.Ordinal))
                {
                    pendingBreakouts.RemoveAt(i);
                }
            }
        }

        private CandidateSignal BuildSignal(
            CandidateModelContext context,
            BreakoutSignalSnapshot breakout)
        {
            var metrics =
                context.Metrics ?? new CandleMetrics();

            var directionOk =
                breakout.Direction == TradeDirection.Long
                    ? context.Bar.IsBullish
                    : context.Bar.IsBearish;

            var directionalCloseLocation =
                breakout.Direction == TradeDirection.Long
                    ? metrics.CloseLocationPercent
                    : 100.0
                      - metrics.CloseLocationPercent;

            var bodyOk =
                metrics.BodyPercent
                >= MinimumBodyPercent;

            var closeOk =
                directionalCloseLocation
                >= MinimumCloseLocationPercent;

            var relativeBodyOk =
                metrics.RelativeBodyMultiple
                >= MinimumRelativeBodyMultiple;

            var qualified =
                directionOk
                && bodyOk
                && closeOk
                && relativeBodyOk;

            var qualificationCode =
                ResolveQualificationCode(
                    directionOk,
                    bodyOk,
                    closeOk,
                    relativeBodyOk);

            var reason = qualified
                ? "Strong breakout candle qualified."
                : $"Rejected: Direction={directionOk}, " +
                  $"Body={metrics.BodyPercent:0.0}%/{MinimumBodyPercent:0.0}%, " +
                  $"CloseLocation={directionalCloseLocation:0.0}%/{MinimumCloseLocationPercent:0.0}%, " +
                  $"RelativeBody={metrics.RelativeBodyMultiple:0.00}x/{MinimumRelativeBodyMultiple:0.00}x.";

            var structuralStop =
                breakout.Direction == TradeDirection.Long
                    ? context.Bar.Low
                    : context.Bar.High;

            return new CandidateSignal(
                breakout.EventId + "-BREAKOUT",
                breakout.EventId,
                ModelId,
                ModelVersion,
                breakout.Direction,
                context.Bar.Time,
                context.Bar.BarIndex,
                breakout.RangeLevel,
                structuralStop,
                context.Bar,
                metrics,
                context.Features,
                new CandidateQualificationSnapshot(
                    directionOk,
                    bodyOk,
                    closeOk,
                    relativeBodyOk),
                CandidateModelDetails.Empty,
                qualified,
                qualificationCode,
                reason);
        }

        private static string ResolveQualificationCode(
            bool directionOk,
            bool bodyOk,
            bool closeOk,
            bool relativeBodyOk)
        {
            if (directionOk
                && bodyOk
                && closeOk
                && relativeBodyOk)
            {
                return CandidateQualificationCodes.Qualified;
            }

            var failureCount = 0;

            if (!directionOk)
                failureCount++;

            if (!bodyOk)
                failureCount++;

            if (!closeOk)
                failureCount++;

            if (!relativeBodyOk)
                failureCount++;

            if (failureCount > 1)
            {
                return CandidateQualificationCodes
                    .MultipleConditionsRejected;
            }

            if (!directionOk)
            {
                return CandidateQualificationCodes
                    .DirectionRejected;
            }

            if (!bodyOk)
            {
                return CandidateQualificationCodes
                    .BodyRejected;
            }

            if (!closeOk)
            {
                return CandidateQualificationCodes
                    .CloseLocationRejected;
            }

            return CandidateQualificationCodes
                .RelativeBodyRejected;
        }
    }
}