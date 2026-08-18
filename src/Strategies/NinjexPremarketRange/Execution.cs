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
     
        private double activeExecutionEntryPrice =
            double.NaN;

        private double activeExecutionRiskTicks;

        private DateTime activeExecutionEntryTime =
            Core.Globals.MinDate;
        
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
            
            if (orderState == OrderState.Rejected)
            {
                Diagnostic(
                    time,
                    "ORDER REJECTED Name={0} " +
                    "Type={1} Action={2} " +
                    "Qty={3} Filled={4} " +
                    "Limit={5} Stop={6} " +
                    "AvgFill={7} Error={8} NativeError={9} " +
                    "ActiveCandidate={10}",
                    name,
                    order.OrderType,
                    order.OrderAction,
                    quantity,
                    filled,
                    limitPrice,
                    stopPrice,
                    averageFillPrice,
                    error,
                    nativeError,
                    activeExecutionCandidate?
                        .CandidateId
                    ?? string.Empty);
            }

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

            if (string.Equals(name,
                    activeExecutionSignalName, StringComparison.Ordinal))
            {
                if (order.OrderState
                    == OrderState.Filled)
                {
                    executionEntryPending =
                        false;

                    var candidate =
                        activeExecutionCandidate;

                    var plannedEntry =
                        candidate?.PlannedEntryPrice
                        ?? 0;

                    var fillVsPlannedTicks =
                        candidate == null
                        || TickSize <= 0
                            ? 0
                            : candidate.Direction
                              == TradeDirection.Long
                                ? (price
                                   - plannedEntry)
                                  / TickSize
                                : (plannedEntry
                                   - price)
                                  / TickSize;

                    var riskTicks =
                        candidate?.ActualRiskTicks
                        ?? 0;

                    var targetTicks =
                        riskTicks
                        * RiskRewardRatio;

                    var expectedLiveStop =
                        candidate?.Direction
                        == TradeDirection.Long
                            ? price
                              - riskTicks
                              * TickSize
                            : price
                              + riskTicks
                              * TickSize;

                    var expectedLiveTarget =
                        candidate?.Direction
                        == TradeDirection.Long
                            ? price
                              + targetTicks
                              * TickSize
                            : price
                              - targetTicks
                              * TickSize;

                    expectedLiveStop =
                        Instrument.MasterInstrument
                            .RoundToTickSize(
                                expectedLiveStop);

                    expectedLiveTarget =
                        Instrument.MasterInstrument
                            .RoundToTickSize(
                                expectedLiveTarget);

                    activeExecutionEntryPrice =
                        price;

                    activeExecutionRiskTicks =
                        activeExecutionCandidate?
                            .ActualRiskTicks
                        ?? 0;

                    activeExecutionEntryTime =
                        time;

                    Diagnostic(
                        time,
                        "EXEC ENTRY FILLED Candidate={0} Order={1} " +
                        "Qty={2} PlannedEntry={3} Fill={4} " +
                        "FillVsPlanned={5:+0.0;-0.0;0.0}t " +
                        "Risk={6:0.0}t ExpectedStop={7} " +
                        "TargetRisk={8:0.0}t ExpectedTarget={9}",
                        candidate?.CandidateId
                        ?? string.Empty,
                        name,
                        quantity,
                        plannedEntry,
                        price,
                        fillVsPlannedTicks,
                        riskTicks,
                        expectedLiveStop,
                        targetTicks,
                        expectedLiveTarget);
                }

                return;
            }

            if (Position.MarketPosition
                == MarketPosition.Flat)
            {
                var candidate =
                    activeExecutionCandidate;

                var entryPrice =
                    activeExecutionEntryPrice;

                var riskTicks =
                    activeExecutionRiskTicks;

                var realizedTicks =
                    0.0;

                if (!double.IsNaN(entryPrice)
                    && TickSize > 0
                    && candidate != null)
                {
                    realizedTicks =
                        candidate.Direction
                        == TradeDirection.Long
                            ? (price - entryPrice)
                              / TickSize
                            : (entryPrice - price)
                              / TickSize;
                }

                var realizedR =
                    riskTicks > 0
                        ? realizedTicks
                          / riskTicks
                        : 0;

                Diagnostic(
                    time,
                    "EXEC POSITION FLAT Candidate={0} " +
                    "Execution={1} Entry={2} Exit={3} " +
                    "RealizedTicks={4:+0.0;-0.0;0.0}t " +
                    "RealizedR={5:+0.000;-0.000;0.000}R",
                    candidate?.CandidateId
                    ?? string.Empty,
                    name,
                    entryPrice,
                    price,
                    realizedTicks,
                    realizedR);

                activeExecutionCandidate =
                    null;

                activeExecutionSignalName =
                    null;

                executionEntryPending =
                    false;

                activeExecutionEntryPrice =
                    double.NaN;

                activeExecutionRiskTicks =
                    0;

                activeExecutionEntryTime =
                    Core.Globals.MinDate;
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

            activeExecutionEntryPrice =
                double.NaN;

            activeExecutionRiskTicks =
                0;

            activeExecutionEntryTime =
                Core.Globals.MinDate;
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