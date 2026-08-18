#region Using declarations
using System;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private const string ExecutableModelId =
            "BreakoutConfirmation";
        
        private string activeExecutionSignalName;

        private EntryCandidate activeExecutionCandidate;

        private bool executionEntryPending;
        
        protected override void OnOrderUpdate(
            Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            OrderState orderState,
            DateTime time,
            ErrorCode error,
            string nativeError)
        {
            if (order == null)
                return;

            var name =
                order.Name ?? string.Empty;

            if (!string.Equals(
                    name,
                    activeExecutionSignalName,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (orderState == OrderState.Rejected
                || orderState == OrderState.Cancelled)
            {
                Diagnostic(
                    time,
                    "MODEL A ENTRY FAILED Candidate={0} " +
                    "Order={1} State={2} Error={3} NativeError={4}",
                    activeExecutionCandidate?.CandidateId
                    ?? string.Empty,
                    name,
                    orderState,
                    error,
                    nativeError);

                executionEntryPending = false;

                activeExecutionCandidate = null;
                activeExecutionSignalName = null;
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (execution?.Order == null)
                return;

            var order =
                execution.Order;

            var name =
                order.Name ?? string.Empty;

            if (string.Equals(
                    name,
                    activeExecutionSignalName,
                    StringComparison.Ordinal))
            {
                if (order.OrderState
                    == OrderState.Filled)
                {
                    executionEntryPending =
                        false;

                    Diagnostic(
                        time,
                        "MODEL A ENTRY FILLED Candidate={0} " +
                        "Order={1} Qty={2} Price={3}",
                        activeExecutionCandidate?.CandidateId
                        ?? string.Empty,
                        name,
                        quantity,
                        price);
                }

                return;
            }

            if (Position.MarketPosition
                == MarketPosition.Flat)
            {
                Diagnostic(
                    time,
                    "MODEL A POSITION FLAT Execution={0} " +
                    "Price={1}",
                    name,
                    price);

                activeExecutionCandidate =
                    null;

                activeExecutionSignalName =
                    null;

                executionEntryPending =
                    false;
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

            if (Position.MarketPosition
                != MarketPosition.Flat)
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

            var stopPrice =
                Instrument.MasterInstrument
                    .RoundToTickSize(
                        candidate.PlannedStopPrice);

            var targetDistance =
                candidate.ActualRiskTicks
                * RiskRewardRatio
                * TickSize;

            var targetPrice =
                candidate.Direction
                    == TradeDirection.Long
                        ? candidate.PlannedEntryPrice
                          + targetDistance
                        : candidate.PlannedEntryPrice
                          - targetDistance;

            targetPrice =
                Instrument.MasterInstrument
                    .RoundToTickSize(
                        targetPrice);

            //
            // Managed approach:
            // configure bracket BEFORE submitting
            // the associated entry signal.
            //

            SetStopLoss(
                signalName,
                CalculationMode.Price,
                stopPrice,
                false);

            SetProfitTarget(
                signalName,
                CalculationMode.Price,
                targetPrice);

            activeExecutionCandidate =
                candidate;

            activeExecutionSignalName =
                signalName;

            executionEntryPending =
                true;

            Diagnostic(
                requestedEntryTime,
                "MODEL A ENTRY SUBMIT Candidate={0} Direction={1} " +
                "Attempt={2} PlannedEntry={3} Distance={4:0.0}t " +
                "Risk={5:0.0}t Stop={6} Target={7} Qty={8}",
                candidate.CandidateId,
                candidate.Direction,
                GetBreakoutAttempt(
                    candidate.BreakoutEventId),
                candidate.PlannedEntryPrice,
                candidate.EntryDistanceTicks,
                candidate.ActualRiskTicks,
                stopPrice,
                targetPrice,
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

        
        private void ResetExecutionDay()
        {
            activeExecutionCandidate =
                null;

            activeExecutionSignalName =
                null;

            executionEntryPending =
                false;
        }


        private void FlattenExecutablePosition(
            DateTime time,
            string reason)
        {
            if (Position.MarketPosition
                == MarketPosition.Flat)
            {
                return;
            }

            Diagnostic(
                time,
                "MODEL A FLATTEN Position={0} Reason={1}",
                Position.MarketPosition,
                reason);

            if (Position.MarketPosition
                == MarketPosition.Long)
            {
                ExitLong(
                    EntrySeriesIndex,
                    Position.Quantity,
                    "PMA-FLAT",
                    activeExecutionSignalName);
            }
            else if (Position.MarketPosition
                     == MarketPosition.Short)
            {
                ExitShort(
                    EntrySeriesIndex,
                    Position.Quantity,
                    "PMA-FLAT",
                    activeExecutionSignalName);
            }
        }
    }
}