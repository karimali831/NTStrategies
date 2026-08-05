using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    /// <summary>
    /// Temporary v2.2 parity adapter.
    ///
    /// Candidate models emit immutable CandidateSignal objects.
    /// Existing exporters, risk preparation and hypothetical trades
    /// continue to consume mutable EntryCandidate records.
    /// </summary>
    public static class CandidateSignalAdapter
    {
        public static EntryCandidate ToEntryCandidate(
            CandidateSignal signal)
        {
            if (signal == null)
                throw new ArgumentNullException(nameof(signal));

            var qualification =
                signal.Qualification
                ?? CandidateQualificationSnapshot.Passed;

            var details =
                signal.ModelDetails
                ?? CandidateModelDetails.Empty;

            return new EntryCandidate
            {
                CandidateId = signal.CandidateId,
                BreakoutEventId = signal.BreakoutEventId,
                ModelName = signal.ModelId,
                ModelVersion = signal.ModelVersion,

                Direction = signal.Direction,
                SignalTime = signal.SignalTime,
                SignalBarIndex = signal.SignalBarIndex,
                RangeLevel = signal.RangeLevel,

                ConfirmationCandle =
                    signal.ConfirmationCandle,

                Metrics =
                    signal.Metrics ?? new CandleMetrics(),

                Features =
                    signal.Features
                    ?? CandidateFeatureSnapshot.Empty,

                BarsAfterBreakout =
                    details.BarsAfterBreakout,

                RetestInsideDepthTicks =
                    details.RetestInsideDepthTicks,

                RetestOutsideDistanceTicks =
                    details.RetestOutsideDistanceTicks,

                StrongCandleQualified =
                    signal.IsQualified,

                DirectionPassed =
                    qualification.DirectionPassed,

                BodyPassed =
                    qualification.BodyPassed,

                CloseLocationPassed =
                    qualification.CloseLocationPassed,

                RelativeBodyPassed =
                    qualification.RelativeBodyPassed,

                QualificationCode =
                    signal.QualificationCode,

                QualificationReason =
                    signal.QualificationReason,

                FinalStatus =
                    signal.IsQualified
                        ? "SignalQualified"
                        : "SignalRejected",

                StructuralStopPrice =
                    signal.StructuralStopPrice
            };
        }
    }
}