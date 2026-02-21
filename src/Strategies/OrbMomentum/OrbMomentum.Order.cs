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
        
        private void ManageRunner()
        {
            if (!EnableRunner)
                return;

            if (!RunnerBreakEvenEnabled)
                return;

            if (!runnerFilled)
                return;
            
            if (runnerEntryPrice <= 0)
                return;

            if (runnerStopMoved)
                return;

            if (primaryDir == 0)
                return;

            // runner unrealized ticks based on RUNNER entry
            var upTicks =
                primaryDir > 0
                    ? (Close[0] - runnerEntryPrice) / TickSize
                    : (runnerEntryPrice - Close[0]) / TickSize;
			
            if (upTicks < RunnerTriggerTicks)
                return;
			
            // Move runner stop to RUNNER BE + RunnerPlusTicks
            var bePrice = runnerEntryPrice + (primaryDir > 0 ? +1 : -1) * (RunnerPlusTicks * TickSize);

            if (primaryDir > 0)
                SetStopLoss(SigRunnerLong, CalculationMode.Price, bePrice, false);
            else
                SetStopLoss(SigRunnerShort, CalculationMode.Price, bePrice, false);

            runnerStopMoved = true;
            LogDiag($"Runner stop moved to BE+{RunnerPlusTicks} @ {bePrice:F2} (upTicks={upTicks:F1})");
        }
    }
}