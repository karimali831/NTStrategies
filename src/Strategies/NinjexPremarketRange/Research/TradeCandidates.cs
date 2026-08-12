using System;
using System.Globalization;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch : Strategy
    {
        private TradeManagementSettings BuildTradeManagementSettings()
        {
            return new TradeManagementSettings
            {
                TickSize = TickSize,
                PointValue = Instrument.MasterInstrument.PointValue,
                Quantity = Quantity,
                RiskRewardRatio = RiskRewardRatio,
                BreakEvenTriggerTicks = BEProfitTriggerTicks,
                BreakEvenPlusTicks = BEPlusTicks,

                Step1 = new TrailStepSettings
                {
                    ProfitTriggerTicks = Step1ProfitTriggerTicks,
                    StopLossTicks = Step1StopLossTicks,
                    FrequencyTicks = Step1FrequencyTicks
                },

                Step2 = new TrailStepSettings
                {
                    ProfitTriggerTicks = Step2ProfitTriggerTicks,
                    StopLossTicks = Step2StopLossTicks,
                    FrequencyTicks = Step2FrequencyTicks
                },

                Step3 = new TrailStepSettings
                {
                    ProfitTriggerTicks = Step3ProfitTriggerTicks,
                    StopLossTicks = Step3StopLossTicks,
                    FrequencyTicks = Step3FrequencyTicks
                }
            };
        }
        
        private bool ShouldCaptureBroadResearchCandidate(
            EntryCandidate candidate)
        {
            if (!EnableRiskScenarioAnalysis
                || candidate == null)
            {
                return false;
            }

            return string.Equals(
                candidate.ModelName,
                "BreakoutConfirmation",
                StringComparison.Ordinal);
        }


        private bool IsInsideCanonicalEntryDistance(
            double entryDistanceTicks)
        {
            return
                entryDistanceTicks
                >= EntryMinimumDistanceTicksFromRange
                && entryDistanceTicks
                <= EntryMaximumDistanceTicksFromRange;
        }


        private bool IsCanonicalTradeCandidate(
            EntryCandidate candidate)
        {
            if (candidate == null)
                return false;

            if (!candidate.StrongCandleQualified)
                return false;

            if (!IsInsideCanonicalEntryDistance(
                    candidate.EntryDistanceTicks))
            {
                return false;
            }

            return candidate.ActualRiskTicks > 0;
        }

        private void RegisterCandidate(
            EntryCandidate candidate)
        {
            if (candidate == null)
                return;

            entryCandidates.Add(candidate);

            ExportCandidate(
                "Created",
                candidate);

            FlushExportWriters();

            Diagnostic(
                candidate.SignalTime,
                "CANDIDATE {0} Model={1} " +
                "Qualified={2} Code={3} Reason={4}",
                candidate.CandidateId,
                candidate.ModelName,
                candidate.StrongCandleQualified,
                candidate.QualificationCode,
                candidate.QualificationReason);

            // v2.4:
            // BreakoutConfirmation candidates participating in broad
            // research must survive until their next-bar open is known,
            // even when today's signal model rejected them.
            //
            // Other models retain their existing terminal behaviour.
            if (!candidate.StrongCandleQualified
                && !ShouldCaptureBroadResearchCandidate(
                    candidate))
            {
                FinalizeCandidate(
                    candidate,
                    "SignalRejected",
                    candidate.SignalTime);
            }
        }
        
        private string ResolveCandidateFinalStatus(
            EntryCandidate candidate,
            bool precision)
        {
            if (candidate == null)
                return "FinalizedUnknown";

            if (!candidate.StrongCandleQualified)
                return "SignalRejected";

            if (!IsInsideCanonicalEntryDistance(
                    candidate.EntryDistanceTicks))
            {
                return "RejectedEntryDistance";
            }

            if (candidate.ActualRiskTicks <= 0)
                return "RejectedInvalidRisk";

            return precision
                ? "FilledPrecision"
                : "FilledNoPrecisionManagement";
        }
        
        private void FinalizeCandidate(
            EntryCandidate candidate,
            string finalStatus,
            DateTime finalizedAt,
            string reasonSuffix = null)
        {
            if (candidate == null
                || candidate.IsFinalized)
            {
                return;
            }

            candidate.FinalStatus =
                string.IsNullOrWhiteSpace(finalStatus)
                    ? "FinalizedUnknown"
                    : finalStatus;

            candidate.FinalizedAt = finalizedAt;
            candidate.IsFinalized = true;

            if (!string.IsNullOrWhiteSpace(reasonSuffix))
            {
                if (!string.IsNullOrWhiteSpace(
                        candidate.QualificationReason))
                {
                    candidate.QualificationReason += " ";
                }

                candidate.QualificationReason += reasonSuffix;
            }

            ExportCandidate(
                "Final",
                candidate);

            FlushExportWriters();

            Diagnostic(
                finalizedAt,
                "CANDIDATE FINAL {0} Model={1} Status={2}",
                candidate.CandidateId,
                candidate.ModelName,
                candidate.FinalStatus);
        }
        
        private bool HasPrecisionTickAtOrAfter(
            DateTime entryTime)
        {
            if (sessionQuality == null
                || !sessionQuality.HasTickData
                || sessionQuality.LastTickTime
                == DateTime.MinValue)
            {
                return false;
            }

            return sessionQuality.LastTickTime
                   >= entryTime;
        }
        
        private void CreateCandidateSimulations(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice)
        {
            if (candidate == null)
                return;

            // Preserve the existing canonical research trade universe.
            if (IsCanonicalTradeCandidate(candidate))
            {
                CreateTradeVariants(
                    candidate,
                    entryTime,
                    entryPrice);
            }

            // v2.4 broad research universe.
            CreateRiskScenarioTrades(
                candidate,
                entryTime,
                entryPrice);
        }

        private void CreateTradeVariants(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice)
        {
            if (candidate == null)
                return;

            var settings =
                BuildTradeManagementSettings();

            activeTrades.Add(
                new HypotheticalTrade(
                    candidate,
                    entryTime,
                    entryPrice,
                    settings,
                    "FixedTarget",
                    false,
                    false));

            if (BEProfitTriggerTicks > 0)
            {
                activeTrades.Add(
                    new HypotheticalTrade(
                        candidate,
                        entryTime,
                        entryPrice,
                        settings,
                        "BreakEven",
                        true,
                        false));
            }

            if (IsAnyTrailStepEnabled())
            {
                activeTrades.Add(
                    new HypotheticalTrade(
                        candidate,
                        entryTime,
                        entryPrice,
                        settings,
                        "ThreeStepTrail",
                        false,
                        true));
            }

            if (BEProfitTriggerTicks > 0
                && IsAnyTrailStepEnabled())
            {
                activeTrades.Add(
                    new HypotheticalTrade(
                        candidate,
                        entryTime,
                        entryPrice,
                        settings,
                        "BreakEvenPlusTrail",
                        true,
                        true));
            }
        }
        
        private void ActivatePendingPrecisionCandidates(
            DateTime tickTime)
        {
            if (!EnablePrecisionTickAnalysis)
                return;

            foreach (var candidate
                     in entryCandidates.ToList())
            {
                if (candidate == null
                    || candidate.IsFinalized
                    || candidate.PlannedEntryPrice <= 0
                    || candidate.PlannedEntryTime
                    == DateTime.MinValue
                    || !string.Equals(
                        candidate.FinalStatus,
                        "AwaitingPrecisionTick",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (tickTime.Date
                    != candidate.PlannedEntryTime.Date)
                {
                    continue;
                }

                if (tickTime
                    < candidate.PlannedEntryTime)
                {
                    continue;
                }

                CreateCandidateSimulations(
                    candidate,
                    candidate.PlannedEntryTime,
                    candidate.PlannedEntryPrice);

                var finalStatus =
                    ResolveCandidateFinalStatus(
                        candidate,
                        true);

                var suffix =
                    BuildCandidateResearchReasonSuffix(
                        candidate);

                FinalizeCandidate(
                    candidate,
                    finalStatus,
                    tickTime,
                    suffix);
            }
        }
        
        private string BuildCandidateResearchReasonSuffix(
            EntryCandidate candidate)
        {
            if (candidate == null)
                return null;

            if (!candidate.StrongCandleQualified)
            {
                return
                    "v2.4 broad research captured hypothetical " +
                    "next-bar entry and risk scenarios despite " +
                    "signal-model rejection.";
            }

            if (!IsInsideCanonicalEntryDistance(
                    candidate.EntryDistanceTicks))
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Canonical entry rejected because distance was " +
                    "{0:0.0} ticks; configured canonical range is " +
                    "{1}-{2} ticks. v2.4 risk scenarios were still captured.",
                    candidate.EntryDistanceTicks,
                    EntryMinimumDistanceTicksFromRange,
                    EntryMaximumDistanceTicksFromRange);
            }

            return null;
        }
        
        private void FinalizePendingCandidates(
            DateTime time,
            string reason)
        {
            foreach (var candidate
                     in entryCandidates.ToList())
            {
                if (candidate == null
                    || candidate.IsFinalized)
                {
                    continue;
                }

                if (string.Equals(
                        candidate.FinalStatus,
                        "AwaitingPrecisionTick",
                        StringComparison.Ordinal))
                {
                    var status =
                        candidate.StrongCandleQualified
                            ? "SkippedNoTickData"
                            : "SignalRejected";

                    FinalizeCandidate(
                        candidate,
                        status,
                        time,
                        "Precision research simulation could not begin because the tick stream did not reach the planned entry timestamp before the candidate lifecycle ended.");

                    continue;
                }

                if (!candidate.StrongCandleQualified)
                {
                    FinalizeCandidate(
                        candidate,
                        "SignalRejected",
                        time);

                    continue;
                }

                string finalStatus;

                if (string.Equals(
                        reason,
                        "FlattenTime",
                        StringComparison.Ordinal))
                {
                    finalStatus =
                        "RejectedNoEntryBeforeFlatten";
                }
                else
                {
                    finalStatus =
                        "RejectedSessionEnded";
                }

                FinalizeCandidate(
                    candidate,
                    finalStatus,
                    time,
                    $"Candidate ended without a valid next-bar entry. Lifecycle reason={reason}.");
            }
        }

        private void FillPendingCandidates(
            DateTime entryTime,
            double entryPrice)
        {
            if (sessionContext == null
                || double.IsNaN(entryPrice)
                || entryPrice <= 0
                || TickSize <= 0)
            {
                return;
            }

            foreach (var candidate
                     in entryCandidates.ToList())
            {
                if (candidate == null
                    || candidate.IsFinalized)
                {
                    continue;
                }

                var broadResearchCandidate =
                    ShouldCaptureBroadResearchCandidate(
                        candidate);

                // Existing behaviour for candidates outside the broad
                // v2.4 research universe.
                if (!candidate.StrongCandleQualified
                    && !broadResearchCandidate)
                {
                    continue;
                }

                // Already assessed at its requested next-bar open.
                if (candidate.PlannedEntryPrice > 0)
                    continue;

                if (entryTime <= candidate.SignalTime)
                    continue;

                if (ToTime(entryTime) >= FlattenTime)
                {
                    FinalizeCandidate(
                        candidate,
                        candidate.StrongCandleQualified
                            ? "RejectedNoEntryBeforeFlatten"
                            : "SignalRejected",
                        entryTime,
                        "No valid next-bar observation was available before flatten time.");

                    continue;
                }

                var entryDistanceTicks =
                    candidate.Direction
                    == TradeDirection.Long
                        ? (entryPrice
                           - candidate.RangeLevel)
                          / TickSize
                        : (candidate.RangeLevel
                           - entryPrice)
                          / TickSize;

                // v2.4:
                // Always record the observed next-bar entry first.
                // Entry distance is data, not a research gate.
                candidate.PlannedEntryTime =
                    entryTime;

                candidate.PlannedEntryPrice =
                    entryPrice;

                candidate.EntryDistanceTicks =
                    entryDistanceTicks;

                // Also calculate structural risk BEFORE applying
                // canonical Model-A restrictions.
                PrepareCandidateRisk(
                    candidate,
                    entryPrice);

                var validStructuralRisk =
                    candidate.StructuralRiskTicks > 0;

                var canonicalCandidate =
                    IsCanonicalTradeCandidate(
                        candidate);

                //
                // Actual execution remains strictly canonical.
                //
                // Execution.cs independently rechecks:
                // model, signal qualification, Attempt <= 3,
                // entry distance and risk before submitting.
                //
                if (canonicalCandidate)
                {
                    TrySubmitExecutableModelA(
                        candidate,
                        entryTime);
                }

                if (!validStructuralRisk)
                {
                    FinalizeCandidate(
                        candidate,
                        candidate.StrongCandleQualified
                            ? "RejectedInvalidRisk"
                            : "SignalRejected",
                        entryTime,
                        "Broad research could not simulate this candidate because structural risk was zero or negative.");

                    continue;
                }

                if (!EnablePrecisionTickAnalysis)
                {
                    var finalStatus =
                        ResolveCandidateFinalStatus(
                            candidate,
                            false);

                    FinalizeCandidate(
                        candidate,
                        finalStatus,
                        entryTime,
                        BuildCandidateResearchReasonSuffix(
                            candidate));

                    continue;
                }

                if (HasPrecisionTickAtOrAfter(
                        entryTime))
                {
                    CreateCandidateSimulations(
                        candidate,
                        entryTime,
                        entryPrice);

                    var finalStatus =
                        ResolveCandidateFinalStatus(
                            candidate,
                            true);

                    FinalizeCandidate(
                        candidate,
                        finalStatus,
                        entryTime,
                        BuildCandidateResearchReasonSuffix(
                            candidate));
                }
                else
                {
                    candidate.FinalStatus =
                        "AwaitingPrecisionTick";

                    Diagnostic(
                        entryTime,
                        "CANDIDATE {0} awaiting precision tick " +
                        "at/after {1:HH:mm:ss.fff} " +
                        "Qualified={2} Distance={3:0.0}t " +
                        "StructuralRisk={4:0.0}t BroadResearch={5}",
                        candidate.CandidateId,
                        entryTime,
                        candidate.StrongCandleQualified,
                        candidate.EntryDistanceTicks,
                        candidate.StructuralRiskTicks,
                        broadResearchCandidate);
                }
            }
        }

        private void PrepareCandidateRisk(
            EntryCandidate candidate,
            double entryPrice)
        {
            if (candidate == null
                || TickSize <= 0)
            {
                return;
            }

            var structuralRiskTicks =
                candidate.Direction == TradeDirection.Long
                    ? (entryPrice
                       - candidate.StructuralStopPrice)
                      / TickSize
                    : (candidate.StructuralStopPrice
                       - entryPrice)
                      / TickSize;

            structuralRiskTicks =
                Math.Max(
                    0,
                    structuralRiskTicks);

            candidate.StructuralRiskTicks =
                structuralRiskTicks;

            candidate.ActualRiskTicks =
                Math.Min(
                    MaximumInitialStopTicks,
                    structuralRiskTicks);

            candidate.StopWasCapped =
                structuralRiskTicks
                > MaximumInitialStopTicks;

            var atr1MinuteTicks = candidate.Features?.Atr1MinuteTicks ?? 0;
            if (candidate.Features != null)
            {
                candidate.Features = candidate.Features.WithStructuralRisk(
                    atr1MinuteTicks > 0
                        ? structuralRiskTicks / atr1MinuteTicks
                        : 0);
            }

            candidate.PlannedStopPrice =
                candidate.Direction
                == TradeDirection.Long
                    ? entryPrice
                      - candidate.ActualRiskTicks
                      * TickSize
                    : entryPrice
                      + candidate.ActualRiskTicks
                      * TickSize;
        }

        private bool IsAnyTrailStepEnabled()
        {
            return Step1ProfitTriggerTicks > 0
                   || Step2ProfitTriggerTicks > 0
                   || Step3ProfitTriggerTicks > 0;
        }
    }
}