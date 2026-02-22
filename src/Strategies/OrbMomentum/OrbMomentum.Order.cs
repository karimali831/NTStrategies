#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class OrbMomentum : Strategy
    {
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            // Count entries (primary + runner) as trades toward MaxTradesPerDay
            if (execution.Order.OrderState == OrderState.Filled)
            {
                var sig = execution.Order.Name;

                if (sig == SigPrimaryLong || sig == SigPrimaryShort)
                {
                    tradesToday++;
                    primaryEntryPrice = price;
                    primaryFilled = true;
                    lastTradeTime = time;

                    LogDiag($"FILL primary: sig={sig} price={price:F2} qty={quantity} tradesToday={tradesToday}");
                }
                else if (sig == SigRunnerLong || sig == SigRunnerShort)
                {
                    tradesToday++;
                    lastTradeTime = time;

                    runnerFilled = true;
                    runnerEntryPrice = price;

                    LogDiag($"FILL runner: sig={sig} price={price:F2} qty={quantity} tradesToday={tradesToday}");
                }
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            // If we go flat, clear per-trade state so we can take the next primary (if allowed)
            if (marketPosition == MarketPosition.Flat)
            {
                // If we just exited a trade, require a pullback before allowing another primary in same direction
                reEntryDir = primaryDir;
                reEntryWaitPullback = reEntryDir != 0;
				
                // clear trade state
                primaryDir = 0;
                primaryEntryPrice = 0;
                primarySubmitted = false;
                primaryStopMoved = false;
                primaryFilled = false;
				
                runnerSubmitted = false;
                runnerStopMoved = false;
                runnerFilled = false;
                runnerEntryPrice = 0;
                runnerArmed = false;
                runnerPullbackSeen = false;
                runnerArmBar = -1;
				
                LogDiag("Position flat: cleared trade state");
            }
        }
        
        protected override void OnOrderUpdate(
            Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            // Only log your strategy's stop/target orders
            var name = order.Name ?? "";
            if (name != SigPrimaryLong && name != SigPrimaryShort && name != SigRunnerLong && name != SigRunnerShort)
                return;

            // Filter to the important moments
            if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled || orderState == OrderState.Working)
            {
                LogDiag($"ORDER {orderState}: name={name} type={order.OrderType} qty={quantity} " +
                        $"lim={limitPrice:F2} stp={stopPrice:F2} err={error} cmt={comment}");
            }
        }
    }
}