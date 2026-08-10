using System;
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

            // v2.2.1:
            // Model-rejected candidates are already terminal.
            // Every CandidateId must receive exactly one Final row.
            if (!candidate.StrongCandleQualified)
            {
                FinalizeCandidate(
                    candidate,
                    "SignalRejected",
                    candidate.SignalTime);
            }
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
            return sessionQuality != null
                   && sessionQuality.HasTickData
                   && sessionQuality.LastTickTime
                   != DateTime.MinValue
                   && sessionQuality.LastTickTime
                   >= entryTime;
        }
        
        private void CreateAllHypotheticalTrades(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice)
        {
            CreateTradeVariants(
                candidate,
                entryTime,
                entryPrice);

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
                    || !candidate.StrongCandleQualified
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
                
                CreateAllHypotheticalTrades(
                    candidate,
                    candidate.PlannedEntryTime,
                    candidate.PlannedEntryPrice);

                FinalizeCandidate(
                    candidate,
                    "FilledPrecision",
                    tickTime,
                    "Precision simulation activated when tick data reached the planned entry timestamp.");
            }
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

                // Model-rejected candidates should already have been
                // finalized immediately in RegisterCandidate().
                if (!candidate.StrongCandleQualified)
                {
                    FinalizeCandidate(
                        candidate,
                        "SignalRejected",
                        time);

                    continue;
                }

                if (string.Equals(
                        candidate.FinalStatus,
                        "AwaitingPrecisionTick",
                        StringComparison.Ordinal))
                {
                    FinalizeCandidate(
                        candidate,
                        "SkippedNoTickData",
                        time,
                        "Precision trade simulation could not begin because the tick stream did not reach the planned entry timestamp before the candidate lifecycle ended.");

                    continue;
                }

                string status;

                if (string.Equals(
                        reason,
                        "FlattenTime",
                        StringComparison.Ordinal))
                {
                    status =
                        "RejectedNoEntryBeforeFlatten";
                }
                else
                {
                    status =
                        "RejectedSessionEnded";
                }

                FinalizeCandidate(
                    candidate,
                    status,
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

                if (!candidate.StrongCandleQualified)
                    continue;

                // Already assessed at its requested next-bar open.
                // It may now simply be awaiting the precision tick stream.
                if (candidate.PlannedEntryPrice > 0)
                    continue;

                if (entryTime <= candidate.SignalTime)
                    continue;

                // This should normally be handled by the explicit
                // session-finalization path, but retain the guard.
                if (ToTime(entryTime) >= FlattenTime)
                {
                    FinalizeCandidate(
                        candidate,
                        "RejectedNoEntryBeforeFlatten",
                        entryTime,
                        "No valid next-bar entry was available before flatten time.");

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

                candidate.PlannedEntryTime =
                    entryTime;

                candidate.PlannedEntryPrice =
                    entryPrice;

                candidate.EntryDistanceTicks =
                    entryDistanceTicks;

                if (entryDistanceTicks
                        < EntryMinimumDistanceTicksFromRange
                    || entryDistanceTicks
                        > EntryMaximumDistanceTicksFromRange)
                {
                    FinalizeCandidate(
                        candidate,
                        "RejectedEntryDistance",
                        entryTime,
                        $"Entry rejected at next 1-minute bar open because distance was {entryDistanceTicks:0.0} ticks; permitted range is {EntryMinimumDistanceTicksFromRange}-{EntryMaximumDistanceTicksFromRange} ticks.");

                    continue;
                }

                PrepareCandidateRisk(
                    candidate,
                    entryPrice);

                if (candidate.ActualRiskTicks <= 0)
                {
                    FinalizeCandidate(
                        candidate,
                        "RejectedInvalidRisk",
                        entryTime,
                        "Rejected because calculated risk was zero or negative.");

                    continue;
                }

                // Actual NinjaTrader execution consumes the exact
                // research candidate after its next-bar entry,
                // distance and risk have been established.
                TrySubmitExecutableModelA(
                    candidate,
                    entryTime);

                if (!EnablePrecisionTickAnalysis)
                {
                    FinalizeCandidate(
                        candidate,
                        "FilledNoPrecisionManagement",
                        entryTime);

                    continue;
                }

                // v2.2.1:
                //
                // Do not reject simply because the tick BIP has not yet
                // executed for this timestamp. The one-minute series may
                // be processed first.
                //
                // If the precision stream has already reached this entry
                // timestamp, activate immediately. Otherwise leave the
                // candidate awaiting the first tick at/after entry.
                if (HasPrecisionTickAtOrAfter(entryTime))
                {
                    CreateAllHypotheticalTrades(
                        candidate,
                        entryTime,
                        entryPrice);

                    FinalizeCandidate(
                        candidate,
                        "FilledPrecision",
                        entryTime);
                }
                else
                {
                    candidate.FinalStatus =
                        "AwaitingPrecisionTick";

                    Diagnostic(
                        entryTime,
                        "CANDIDATE {0} awaiting precision tick at/after {1:HH:mm:ss.fff}",
                        candidate.CandidateId,
                        entryTime);
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