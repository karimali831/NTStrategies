#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
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
                    
                    activeExecutionEntryQuantity =
                        Math.Abs(Position.Quantity);

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
                    
                    var tradingDate =
                        activeTradingDate != Core.Globals.MinDate
                            ? activeTradingDate.Date
                            : time.Date;
                    
                    var entrySnapshot =
                        executionEquityTracker.Update(
                            time,
                            tradingDate,
                            executionRealizedPnl,
                            0m,
                            candidate?.CandidateId,
                            price,
                            activeExecutionEntryQuantity,
                            candidate?.Direction);
                    
                    ExportExecutionEquitySnapshot(
                        entrySnapshot);
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
                
                var pointValue =
                    Instrument.MasterInstrument.PointValue;

                var realizedPnl =
                    realizedTicks
                    * TickSize
                    * pointValue
                    * Math.Abs(
                        activeExecutionEntryQuantity);

                executionRealizedPnl +=
                    Convert.ToDecimal(
                        realizedPnl);

                Diagnostic(
                    time,
                    "EXEC POSITION FLAT Candidate={0} " +
                    "Execution={1} Entry={2} Exit={3} " +
                    "RealizedTicks={4:+0.0;-0.0;0.0}t " +
                    "RealizedR={5:+0.000;-0.000;0.000}R " +
                    "TradePnl={6:+0.00;-0.00;0.00} " +
                    "CumulativePnl={7:+0.00;-0.00;0.00}",
                    candidate?.CandidateId
                    ?? string.Empty,
                    name,
                    entryPrice,
                    price,
                    realizedTicks,
                    realizedR,
                    realizedPnl,
                    executionRealizedPnl);
                
                var tradingDate =
                    activeTradingDate != Core.Globals.MinDate
                        ? activeTradingDate.Date
                        : time.Date;
                
                var flatSnapshot =
                    executionEquityTracker.Update(
                        time,
                        tradingDate,
                        executionRealizedPnl,
                        0m,
                        candidate?.CandidateId,
                        price,
                        0,
                        candidate?.Direction);
                
                ExportExecutionEquitySnapshot(
                    flatSnapshot);

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

                activeExecutionEntryQuantity =
                    0;

                activeExecutionEntryTime =
                    Core.Globals.MinDate;
            }
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

            activeExecutionEntryQuantity =
                0;

            activeExecutionEntryTime =
                Core.Globals.MinDate;
        }

        private void FlattenExecutablePosition(
            DateTime time,
            string reason)
        {
            if (Position.MarketPosition == MarketPosition.Flat)
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