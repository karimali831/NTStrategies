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