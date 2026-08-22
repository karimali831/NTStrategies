using System;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private EntryCandidate activeExecutionCandidate;
        
         private void TrySubmitExecutableCandidate(
            EntryCandidate candidate,
            DateTime requestedEntryTime)
        {
            if (!IsExecutableCandidate(
                    candidate))
            {
                return;
            }

            if (executionEntryPending)
            {
                Diagnostic(
                    requestedEntryTime,
                    "MODEL A SKIP {0} Reason=EntryPending",
                    candidate.CandidateId);

                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                Diagnostic(
                    requestedEntryTime,
                    "MODEL A SKIP {0} Reason=PositionNotFlat Position={1}",
                    candidate.CandidateId,
                    Position.MarketPosition);

                return;
            }

            var signalName =
                BuildExecutionSignalName(
                    candidate);

            var riskTicks =
                candidate.ActualRiskTicks;

            var targetTicks =
                riskTicks
                * RiskRewardRatio;

            SetStopLoss(
                signalName,
                CalculationMode.Ticks,
                riskTicks,
                false);

            SetProfitTarget(
                signalName,
                CalculationMode.Ticks,
                targetTicks);

            activeExecutionCandidate =
                candidate;

            activeExecutionSignalName =
                signalName;

            executionEntryPending =
                true;

            var marketAtSubmit =
                Closes[TickSeriesIndex][0];

            var marketVsPlannedTicks =
                candidate.Direction
                == TradeDirection.Long
                    ? (marketAtSubmit
                       - candidate.PlannedEntryPrice)
                      / TickSize
                    : (candidate.PlannedEntryPrice
                       - marketAtSubmit)
                      / TickSize;
            
            Diagnostic(
                requestedEntryTime,
                "EXEC ENTRY SUBMIT Candidate={0} Direction={1} " +
                "Attempt={2} PlannedEntry={3} MarketAtSubmit={4} " +
                "MarketVsPlanned={5:+0.0;-0.0;0.0}t " +
                "EntryDistance={6:0.0}t StructuralRisk={7:0.0}t " +
                "ExecutionRisk={8:0.0}t TargetRisk={9:0.0}t " +
                "StopCapped={10} Qty={11}",
                candidate.CandidateId,
                candidate.Direction,
                GetBreakoutAttempt(
                    candidate.BreakoutEventId),
                candidate.PlannedEntryPrice,
                marketAtSubmit,
                marketVsPlannedTicks,
                candidate.EntryDistanceTicks,
                candidate.StructuralRiskTicks,
                riskTicks,
                targetTicks,
                candidate.StopWasCapped,
                Quantity);

            if (candidate.Direction
                == TradeDirection.Long)
            {
                EnterLong(
                    TickSeriesIndex,
                    Quantity,
                    signalName);
            }
            else
            {
                EnterShort(
                    TickSeriesIndex,
                    Quantity,
                    signalName);
            }
        }
        
        private bool IsExecutableCandidate(
            EntryCandidate candidate)
        {
            if (candidate == null)
                return false;

            if (!EnableTradeExecution)
                return false;

            var policy = BuildExecutionCandidatePolicy();

            if (!string.Equals(
                    candidate.ModelName,
                    policy.ModelId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (candidate.Direction
                == TradeDirection.Long
                && !policy.AllowLongs)
            {
                return false;
            }

            if (candidate.Direction
                == TradeDirection.Short
                && !policy.AllowShorts)
            {
                return false;
            }

            if (policy.RequireQualifiedSignal
                && !candidate.StrongCandleQualified)
            {
                return false;
            }

            var attempt =
                GetBreakoutAttempt(
                    candidate.BreakoutEventId);

            if (attempt <= 0)
                return false;

            if (policy.AttemptMin.HasValue
                && attempt
                < policy.AttemptMin.Value)
            {
                return false;
            }

            if (policy.AttemptMax.HasValue
                && attempt
                > policy.AttemptMax.Value)
            {
                return false;
            }

            if (policy.EnableEntryDistanceFilter)
            {
                if (policy.EntryDistanceMinTicks.HasValue
                    && candidate.EntryDistanceTicks
                    < policy.EntryDistanceMinTicks.Value)
                {
                    return false;
                }

                if (policy.EntryDistanceMaxTicks.HasValue
                    && candidate.EntryDistanceTicks
                    > policy.EntryDistanceMaxTicks.Value)
                {
                    return false;
                }
            }

            if (candidate.ActualRiskTicks <= 0)
                return false;

            if (candidate.PlannedStopPrice <= 0)
                return false;

            if (candidate.PlannedEntryPrice <= 0)
                return false;

            return true;
        }
        
        private ExecutionCandidatePolicy
            BuildExecutionCandidatePolicy()
        {
            return new ExecutionCandidatePolicy
            {
                ModelId =
                    "BreakoutConfirmation",

                AllowLongs =
                    ExecuteLongs,

                AllowShorts =
                    ExecuteShorts,

                AttemptMin =
                    ExecutionAttemptMin,

                AttemptMax =
                    ExecutionAttemptMax,

                RequireQualifiedSignal =
                    RequireQualifiedExecutionSignal,

                EnableEntryDistanceFilter =
                    EnableExecutionEntryDistanceFilter,

                EntryDistanceMinTicks =
                    EnableExecutionEntryDistanceFilter
                        ? ExecutionEntryMinimumDistanceTicks
                        : (double?)null,

                EntryDistanceMaxTicks =
                    EnableExecutionEntryDistanceFilter
                        ? ExecutionEntryMaximumDistanceTicks
                        : (double?)null
            };
        }
        
        private string BuildExecutionSignalName(
            EntryCandidate candidate)
        {
            // Keep NinjaTrader order names short and deterministic.

            var side =
                candidate.Direction
                == TradeDirection.Long
                    ? "L"
                    : "S";

            var attempt =
                GetBreakoutAttempt(
                    candidate.BreakoutEventId);

            return string.Format(
                CultureInfo.InvariantCulture,
                "PMA-{0}-{1:00}",
                side,
                attempt);
        }

        private int GetBreakoutAttempt(
            string breakoutEventId)
        {
            if (string.IsNullOrWhiteSpace(
                    breakoutEventId))
            {
                return 0;
            }

            // Current IDs:
            // yyyyMMdd-LONG-01
            // yyyyMMdd-SHORT-03
            //
            // Use the final token so execution remains
            // independent of LONG/SHORT text.

            var lastDash =
                breakoutEventId.LastIndexOf('-');

            if (lastDash < 0
                || lastDash
                >= breakoutEventId.Length - 1)
            {
                return 0;
            }

            var token =
                breakoutEventId.Substring(
                    lastDash + 1);

            return int.TryParse(
                token,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var attempt)
                ? attempt
                : 0;
        }
    }
}