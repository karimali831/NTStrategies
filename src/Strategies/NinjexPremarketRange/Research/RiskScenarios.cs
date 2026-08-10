using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private readonly List<HypotheticalTrade>
            activeRiskScenarioTrades =
                new List<HypotheticalTrade>();

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
                    60,
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
                    3.0
                };

            foreach (var stopCap
                     in stopCaps)
            {
                foreach (var rr
                         in riskRewards)
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

            if (!candidate.StrongCandleQualified)
                return false;

            if (!string.Equals(
                    candidate.ModelName,
                    "BreakoutConfirmation",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var attempt =
                GetBreakoutAttempt(
                    candidate.BreakoutEventId);

            if (attempt <= 0
                || attempt > 3)
            {
                return false;
            }

            if (candidate.EntryDistanceTicks
                    < EntryMinimumDistanceTicksFromRange
                || candidate.EntryDistanceTicks
                    > EntryMaximumDistanceTicksFromRange)
            {
                return false;
            }

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

            foreach (var scenario
                     in riskScenarios)
            {
                activeRiskScenarioTrades.Add(
                    new HypotheticalTrade(
                        candidate,
                        entryTime,
                        entryPrice,
                        baseSettings,
                        scenario));
            }

            Diagnostic(
                entryTime,
                "RISK SCENARIOS CREATED Candidate={0} Count={1} StructuralRisk={2:0.0}t",
                candidate.CandidateId,
                riskScenarios.Count,
                candidate.StructuralRiskTicks);
        }


        private void ProcessRiskScenarioTrades(
            DateTime tickTime,
            double tickPrice)
        {
            foreach (var trade
                     in activeRiskScenarioTrades
                         .ToList())
            {
                trade.ProcessTick(
                    tickTime,
                    tickPrice);

                if (!trade.IsClosed)
                    continue;

                ExportRiskScenarioTrade(
                    trade);

                activeRiskScenarioTrades.Remove(
                    trade);
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

            foreach (var trade
                     in activeRiskScenarioTrades
                         .ToList())
            {
                trade.ForceClose(
                    time,
                    price,
                    reason);

                ExportRiskScenarioTrade(
                    trade);

                activeRiskScenarioTrades.Remove(
                    trade);
            }
        }
    }
}