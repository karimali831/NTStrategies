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
                "CANDIDATE {0} Qualified={1} Reason={2}",
                candidate.CandidateId,
                candidate.StrongCandleQualified,
                candidate.QualificationReason);
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

            foreach (var candidate in entryCandidates.ToList())
            {
                if (candidate == null)
                    continue;

                if (!candidate.StrongCandleQualified
                    || candidate.PlannedEntryPrice > 0)
                {
                    continue;
                }

                if (entryTime <= candidate.SignalTime
                    || ToTime(entryTime) >= FlattenTime)
                {
                    continue;
                }

                var entryDistanceTicks =
                    candidate.Direction == TradeDirection.Long
                        ? (entryPrice - candidate.RangeLevel)
                          / TickSize
                        : (candidate.RangeLevel - entryPrice)
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
                    candidate.FinalStatus =
                        "RejectedEntryDistance";

                    candidate.QualificationReason +=
                        $" Entry rejected at next 1-minute bar open because distance was {entryDistanceTicks:0.0} ticks; permitted range is {EntryMinimumDistanceTicksFromRange}-{EntryMaximumDistanceTicksFromRange} ticks.";

                    ExportCandidate(
                        "Final",
                        candidate);

                    FlushExportWriters();
                    continue;
                }

                PrepareCandidateRisk(
                    candidate,
                    entryPrice);

                if (candidate.ActualRiskTicks <= 0)
                {
                    candidate.FinalStatus =
                        "RejectedInvalidRisk";

                    candidate.QualificationReason +=
                        " Rejected because calculated risk was zero or negative.";

                    ExportCandidate(
                        "Final",
                        candidate);

                    FlushExportWriters();
                    continue;
                }

                if (EnablePrecisionTickAnalysis
                    && (sessionQuality == null
                        || !sessionQuality.HasTickData))
                {
                    candidate.FinalStatus =
                        "SkippedNoTickData";

                    candidate.QualificationReason +=
                        " Precision trade simulation skipped because no tick data was available for the session.";

                    ExportCandidate(
                        "Final",
                        candidate);

                    FlushExportWriters();
                    continue;
                }

                candidate.FinalStatus =
                    EnablePrecisionTickAnalysis
                        ? "FilledPrecision"
                        : "FilledNoPrecisionManagement";

                if (EnablePrecisionTickAnalysis)
                {
                    CreateTradeVariants(
                        candidate,
                        entryTime,
                        entryPrice);
                }

                ExportCandidate(
                    "Final",
                    candidate);

                FlushExportWriters();
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