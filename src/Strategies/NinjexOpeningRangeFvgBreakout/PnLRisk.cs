using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private double GetOpenPositionPnl()
        {
            if (Position.MarketPosition == MarketPosition.Flat || Position.Quantity <= 0)
                return 0;

            var currentPrice = Close[0];

            if (Position.MarketPosition == MarketPosition.Long)
            {
                var bid = GetCurrentBid();
                currentPrice = bid > 0 ? bid : Close[0];
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                var ask = GetCurrentAsk();
                currentPrice = ask > 0 ? ask : Close[0];
            }

            return Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);
        }

        private double GetDailyTotalPnl()
        {
            return GetDailyRealizedPnl() + GetOpenPositionPnl();
        }
        
        private void EnforceDailyPnlLimits()
        {
            if (dailyPnlLimitHit)
                return;

            var dailyPnl = GetDailyTotalPnl();

            var profitHit = MaxDailyProfit > 0 && dailyPnl >= MaxDailyProfit;
            var lossHit = MaxDailyLoss > 0 && dailyPnl <= -MaxDailyLoss;

            if (!profitHit && !lossHit)
                return;

            dailyPnlLimitHit = true;

            LogDiag(
                $"Daily PnL limit hit. DailyPnL={dailyPnl:0.00}, MaxDailyProfit={MaxDailyProfit}, MaxDailyLoss={MaxDailyLoss}");

            pendingEntry = false;
            pendingLong = false;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ExitLong("DailyPnlLimitLongExit", LongEntryName);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ExitShort("DailyPnlLimitShortExit", ShortEntryName);
            }
        }
    }
}