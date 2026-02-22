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
        
        private void ManagePrimaryBreakEven()
        {
            if (!PrimaryBreakEvenEnabled)
                return;

            if (!primaryFilled)
                return;

            if (primaryEntryPrice <= 0)
                return;

            if (primaryStopMoved)
                return;

            if (primaryDir == 0)
                return;

            // primary unrealized ticks based on PRIMARY entry
            var upTicks =
                primaryDir > 0
                    ? (Close[0] - primaryEntryPrice) / TickSize
                    : (primaryEntryPrice - Close[0]) / TickSize;

            if (upTicks < Math.Max(1, PrimaryBeTriggerTicks))
                return;

            // Move primary stop to PRIMARY BE + PrimaryBEPlusTicks
            var bePrice = primaryEntryPrice + (primaryDir > 0 ? +1 : -1) * (Math.Max(0, PrimaryBePlusTicks) * TickSize);

            if (primaryDir > 0)
                SetStopLoss(SigPrimaryLong, CalculationMode.Price, bePrice, false);
            else
                SetStopLoss(SigPrimaryShort, CalculationMode.Price, bePrice, false);

            primaryStopMoved = true;
            LogDiag($"Primary stop moved to BE+{PrimaryBePlusTicks} @ {bePrice:F2} (upTicks={upTicks:F1})");
        }
        
        private int DollarsToTicks(double dollars)
        {
            // tickValue = PointValue * TickSize (e.g., ES: 50 * 0.25 = $12.50)
            var tickValue = Instrument.MasterInstrument.PointValue * TickSize;

            if (tickValue <= 0)
                return 0;

            var ticks = dollars / tickValue;

            // Round to nearest tick, but keep at least 1
            var rounded = (int)Math.Round(ticks, MidpointRounding.AwayFromZero);
            return Math.Max(1, rounded);
        }
        
        private double GetPrimaryUnrealizedTicks()
        {
            if (primaryDir == 0)
                return 0;

            // Use primaryEntryPrice captured from fill; if not captured yet, fall back to AvgPrice
            var entry = primaryEntryPrice > 0 ? primaryEntryPrice : Position.AveragePrice;

            if (entry <= 0)
                return 0;

            if (primaryDir > 0)
                return (Close[0] - entry) / TickSize;

            return (entry - Close[0]) / TickSize;
        }
        
        		
        private bool DailyLossHit(out double dailyPnL)
        {
            dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - startOfDayCumProfit;
            return dailyPnL <= -Math.Abs(MaxDailyLoss);
        }
		
        private bool DailyProfitHit(out double dailyPnL)
        {
            dailyPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - startOfDayCumProfit;
            return dailyPnL >= Math.Abs(MaxDailyProfit);
        }
    }
}