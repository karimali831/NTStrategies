#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class BreakoutConfirmationModel : IEntryModel
    {
        private readonly List<BreakoutEvent> pendingBreakouts =
            new List<BreakoutEvent>();

        public string Name
        {
            get { return "BreakoutConfirmation"; }
        }

        public bool IsEnabled { get; set; }

        public double MinimumBodyPercent { get; set; }
        public double MinimumCloseLocationPercent { get; set; }
        public double MinimumRelativeBodyMultiple { get; set; }

        public void Reset(RangeSessionContext session)
        {
            pendingBreakouts.Clear();
        }

        public void OnBreakout(BreakoutEvent breakoutEvent)
        {
            if (breakoutEvent != null)
                pendingBreakouts.Add(breakoutEvent);
        }

        public IEnumerable<EntryCandidate> Evaluate(ModelBarContext context)
        {
            var candidates = new List<EntryCandidate>();

            if (!IsEnabled || context == null || context.Bar == null)
                return candidates;

            for (var i = pendingBreakouts.Count - 1; i >= 0; i--)
            {
                var breakout = pendingBreakouts[i];

                // The breakout candle itself is the confirmation candle.
                if (breakout.BreakoutBarIndex != context.Bar.BarIndex)
                    continue;

                var candidate = BuildCandidate(context, breakout);
                candidates.Add(candidate);
                pendingBreakouts.RemoveAt(i);
            }

            return candidates;
        }

        private EntryCandidate BuildCandidate(
            ModelBarContext context,
            BreakoutEvent breakout)
        {
            var metrics = context.Metrics ?? new CandleMetrics();
            var directionOk = breakout.Direction == TradeDirection.Long
                ? context.Bar.IsBullish
                : context.Bar.IsBearish;

            double directionalCloseLocation =
                breakout.Direction == TradeDirection.Long
                    ? metrics.CloseLocationPercent
                    : 100.0 - metrics.CloseLocationPercent;

            bool bodyOk = metrics.BodyPercent >= MinimumBodyPercent;
            bool closeOk =
                directionalCloseLocation >= MinimumCloseLocationPercent;
            bool relativeBodyOk =
                metrics.RelativeBodyMultiple >= MinimumRelativeBodyMultiple;

            bool qualified =
                directionOk
                && bodyOk
                && closeOk
                && relativeBodyOk;

            string reason = qualified
                ? "Strong breakout candle qualified."
                : string.Format(
                    "Rejected: Direction={0}, Body={1:0.0}%/{2:0.0}%, CloseLocation={3:0.0}%/{4:0.0}%, RelativeBody={5:0.00}x/{6:0.00}x.",
                    directionOk,
                    metrics.BodyPercent,
                    MinimumBodyPercent,
                    directionalCloseLocation,
                    MinimumCloseLocationPercent,
                    metrics.RelativeBodyMultiple,
                    MinimumRelativeBodyMultiple);

            return new EntryCandidate
            {
                CandidateId = breakout.EventId + "-BREAKOUT",
                BreakoutEventId = breakout.EventId,
                ModelName = Name,
                Direction = breakout.Direction,
                SignalTime = context.Bar.Time,
                SignalBarIndex = context.Bar.BarIndex,
                RangeLevel = breakout.RangeLevel,
                ConfirmationCandle = context.Bar,
                Metrics = metrics,
                BarsAfterBreakout = 0,
                StrongCandleQualified = qualified,
                DirectionPassed = directionOk,
                BodyPassed = bodyOk,
                CloseLocationPassed = closeOk,
                RelativeBodyPassed = relativeBodyOk,
                QualificationReason = reason,
                FinalStatus = qualified ? "SignalQualified" : "SignalRejected",
                StructuralStopPrice = breakout.Direction == TradeDirection.Long
                    ? context.Bar.Low
                    : context.Bar.High
            };
        }
    }
}
