namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private bool PrepareLongManagedBracket(double approximateEntryPrice, double candleStopPrice)
        {
            double stopPrice;

            if (MaxStopTicks > 0)
                stopPrice = approximateEntryPrice - MaxStopTicks * TickSize;
            else
                stopPrice = candleStopPrice;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            var risk = approximateEntryPrice - stopPrice;

            if (risk <= TickSize)
            {
                LogDiag($"LONG blocked: invalid risk. Entry={approximateEntryPrice}, Stop={stopPrice}");
                return false;
            }

            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(approximateEntryPrice + risk * 2.0);

            activeEntryPrice = approximateEntryPrice;
            activeStopPrice = stopPrice;
            activeTargetPrice = targetPrice;
            activeDirection = 1;
            autoBreakevenApplied = false;

            SetStopLoss(LongEntryName, CalculationMode.Price, activeStopPrice, false);
            SetProfitTarget(LongEntryName, CalculationMode.Price, activeTargetPrice);

            return true;
        }

        private bool PrepareShortManagedBracket(double approximateEntryPrice, double candleStopPrice)
        {
            double stopPrice;

            if (MaxStopTicks > 0)
                stopPrice = approximateEntryPrice + MaxStopTicks * TickSize;
            else
                stopPrice = candleStopPrice;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            var risk = stopPrice - approximateEntryPrice;

            if (risk <= TickSize)
            {
                LogDiag($"SHORT blocked: invalid risk. Entry={approximateEntryPrice}, Stop={stopPrice}");
                return false;
            }

            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(approximateEntryPrice - risk * 2.0);

            activeEntryPrice = approximateEntryPrice;
            activeStopPrice = stopPrice;
            activeTargetPrice = targetPrice;
            activeDirection = -1;
            autoBreakevenApplied = false;

            SetStopLoss(ShortEntryName, CalculationMode.Price, activeStopPrice, false);
            SetProfitTarget(ShortEntryName, CalculationMode.Price, activeTargetPrice);

            return true;
        }

    }
}