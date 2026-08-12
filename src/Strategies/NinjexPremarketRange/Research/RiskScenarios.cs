using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private readonly List<RiskScenarioBatch>
            activeRiskScenarioBatches =
                new List<RiskScenarioBatch>();

        private readonly List<RiskScenario>
            riskScenarios =
                new List<RiskScenario>();


        private void ConfigureRiskScenarios()
        {
            riskScenarios.Clear();

            if (!EnableRiskScenarioAnalysis)
                return;

            var stopCaps =
                new[]
                {
                    30,
                    40,
                    50,
                    60,
                    70,
                    80,
                    100,
                    120,
                    140,
                    160,
                    180,
                    200
                };

            var riskRewards =
                new[]
                {
                    1.0,
                    1.5,
                    2.0,
                    2.5,
                    3.0,
                    3.5,
                    4.0
                };

            foreach (var stopCap in stopCaps)
            {
                foreach (var rr in riskRewards)
                {
                    riskScenarios.Add(
                        new RiskScenario(
                            stopCap,
                            rr));
                }
            }

            Diagnostic(
                lastMarketTime
                != Core.Globals.MinDate
                    ? lastMarketTime
                    : DateTime.Now,
                "RISK SCENARIO GRID CONFIGURED Count={0}",
                riskScenarios.Count);
        }

        private bool IsRiskScenarioCandidate(
            EntryCandidate candidate)
        {
            if (!EnableRiskScenarioAnalysis
                || candidate == null)
            {
                return false;
            }

            // v2.4:
            // Risk-scenario research deliberately operates on a much
            // broader universe than the executable/canonical model.
            //
            // DO NOT filter here by:
            //   - StrongCandleQualified
            //   - breakout attempt number
            //   - entry-distance limits
            //
            // Those are analysis dimensions for the dashboard.
            if (!string.Equals(
                    candidate.ModelName,
                    "BreakoutConfirmation",
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (candidate.PlannedEntryTime
                == DateTime.MinValue)
            {
                return false;
            }

            if (candidate.PlannedEntryPrice <= 0)
                return false;

            return candidate.StructuralRiskTicks > 0;
        }

        private void CreateRiskScenarioTrades(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice)
        {
            if (!IsRiskScenarioCandidate(
                    candidate))
            {
                return;
            }

            var baseSettings =
                BuildTradeManagementSettings();

            var batch =
                new RiskScenarioBatch(
                    candidate,
                    entryTime,
                    entryPrice,
                    baseSettings,
                    riskScenarios);

            if (batch.ScenarioCount == 0)
                return;

            activeRiskScenarioBatches.Add(
                batch);

            Diagnostic(
                entryTime,
                "RISK SCENARIO BATCH CREATED " +
                "Candidate={0} Count={1} StructuralRisk={2:0.0}t",
                candidate.CandidateId,
                batch.ScenarioCount,
                candidate.StructuralRiskTicks);
        }

        private void ProcessRiskScenarioTrades(
            DateTime tickTime,
            double tickPrice)
        {
            for (var i =
                     activeRiskScenarioBatches.Count - 1;
                 i >= 0;
                 i--)
            {
                var batch =
                    activeRiskScenarioBatches[i];

                batch.ProcessTick(
                    tickTime,
                    tickPrice,
                    ExportRiskScenarioTrade);

                if (!batch.IsClosed)
                    continue;

                activeRiskScenarioBatches.RemoveAt(
                    i);
            }
        }

        private void ForceCloseRiskScenarioTrades(
            DateTime time,
            double price,
            string reason)
        {
            if (double.IsNaN(price)
                || price <= 0)
            {
                return;
            }

            for (var i =
                     activeRiskScenarioBatches.Count - 1;
                 i >= 0;
                 i--)
            {
                var batch =
                    activeRiskScenarioBatches[i];

                batch.ForceClose(
                    time,
                    price,
                    reason,
                    ExportRiskScenarioTrade);

                activeRiskScenarioBatches.RemoveAt(
                    i);
            }
        }
    }
}