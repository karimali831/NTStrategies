using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private void ManageBreakEven()
        {
            if (BreakEvenProfitTriggerUsd <= 0)
                return;

            if (breakEvenMoved)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            var positionQuantity = Math.Max(1, Math.Abs(Position.Quantity));

            var unrealizedUsd = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

            if (unrealizedUsd < BreakEvenProfitTriggerUsd)
                return;

            var plusDistance = CurrencyToPriceDistance(BreakEvenPlusUsd, positionQuantity);
            var avgEntry = Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(avgEntry + plusDistance);

                if (newStop < Close[0])
                {
                    SetStopLoss(LongSignal, CalculationMode.Price, newStop, false);
                    breakEvenMoved = true;
                    DebugPrint("Long BE moved. NewStop=" + newStop + " UnrealizedUsd=" + unrealizedUsd);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                var newStop = Instrument.MasterInstrument.RoundToTickSize(avgEntry - plusDistance);

                if (newStop > Close[0])
                {
                    SetStopLoss(ShortSignal, CalculationMode.Price, newStop, false);
                    breakEvenMoved = true;
                    DebugPrint("Short BE moved. NewStop=" + newStop + " UnrealizedUsd=" + unrealizedUsd);
                }
            }
        }
    }
}