using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private double activeEntryPrice;
        private double activeStopPrice;
        private double activeTargetPrice;
        private int activeDirection; // 1 = long, -1 = short, 0 = flat
        private bool autoBreakevenApplied;
        
        private bool IsStopSizeAllowed(bool isLong, double approximateEntryPrice, double stopPrice)
        {
            if (MaxStopTicks <= 0)
                return true;

            var riskTicks = isLong
                ? (approximateEntryPrice - stopPrice) / TickSize
                : (stopPrice - approximateEntryPrice) / TickSize;

            return riskTicks > 0 && riskTicks <= MaxStopTicks;
        }
        
        private void ManageActiveBracket()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            if (activeDirection == 0 || activeEntryPrice <= 0)
                return;

            if (Position.Quantity <= 0)
                return;

            if (activeDirection == 1)
                ManageLongBracket();
            else if (activeDirection == -1)
                ManageShortBracket();
        }
        
        private void ManageLongBracket()
        {
            var profitTicks = (High[0] - activeEntryPrice) / TickSize;
            var newStop = activeStopPrice;

            if (AutoBreakevenProfitTriggerTicks > 0 &&
                !autoBreakevenApplied &&
                profitTicks >= AutoBreakevenProfitTriggerTicks)
            {
                var beStop = activeEntryPrice + AutoBreakevenPlusTicks * TickSize;

                if (beStop > newStop)
                {
                    newStop = beStop;
                    autoBreakevenApplied = true;
                    LogDiag($"LONG auto-BE triggered. NewStop={newStop}");
                }
            }

            var trailStop = GetLongTrailStop(profitTicks);

            if (trailStop > newStop)
                newStop = trailStop;

            newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);

            if (newStop > activeStopPrice && newStop < Close[0])
            {
                activeStopPrice = newStop;
                ExitLongStopMarket(0, true, Position.Quantity, activeStopPrice, LongStopName, LongEntryName);
                LogDiag($"LONG stop moved. NewStop={activeStopPrice}");
            }
        }
        
        private void ManageShortBracket()
        {
            var profitTicks = (activeEntryPrice - Low[0]) / TickSize;
            var newStop = activeStopPrice;

            if (AutoBreakevenProfitTriggerTicks > 0 &&
                !autoBreakevenApplied &&
                profitTicks >= AutoBreakevenProfitTriggerTicks)
            {
                var beStop = activeEntryPrice - AutoBreakevenPlusTicks * TickSize;

                if (beStop < newStop)
                {
                    newStop = beStop;
                    autoBreakevenApplied = true;
                    LogDiag($"SHORT auto-BE triggered. NewStop={newStop}");
                }
            }

            var trailStop = GetShortTrailStop(profitTicks);

            if (trailStop < newStop)
                newStop = trailStop;

            newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);

            if (newStop < activeStopPrice && newStop > Close[0])
            {
                activeStopPrice = newStop;
                ExitShortStopMarket(0, true, Position.Quantity, activeStopPrice, ShortStopName, ShortEntryName);
                LogDiag($"SHORT stop moved. NewStop={activeStopPrice}");
            }
        }
        
                private double GetLongTrailStop(double profitTicks)
        {
            var step = GetActiveTrailStep(profitTicks);

            if (step.StopLossTicks <= 0 || step.FrequencyTicks <= 0)
                return activeStopPrice;

            var desiredStop = High[0] - step.StopLossTicks * TickSize;
            var minimumMove = step.FrequencyTicks * TickSize;

            if (desiredStop >= activeStopPrice + minimumMove)
                return desiredStop;

            return activeStopPrice;
        }

        private double GetShortTrailStop(double profitTicks)
        {
            var step = GetActiveTrailStep(profitTicks);

            if (step.StopLossTicks <= 0 || step.FrequencyTicks <= 0)
                return activeStopPrice;

            var desiredStop = Low[0] + step.StopLossTicks * TickSize;
            var minimumMove = step.FrequencyTicks * TickSize;

            if (desiredStop <= activeStopPrice - minimumMove)
                return desiredStop;

            return activeStopPrice;
        }

        private TrailStep GetActiveTrailStep(double profitTicks)
        {
            var active = new TrailStep();

            if (Trail1ProfitTriggerTicks > 0 && profitTicks >= Trail1ProfitTriggerTicks)
            {
                active.ProfitTriggerTicks = Trail1ProfitTriggerTicks;
                active.StopLossTicks = Trail1StopLossTicks;
                active.FrequencyTicks = Math.Max(1, Trail1FrequencyTicks);
            }

            if (Trail2ProfitTriggerTicks > 0 && profitTicks >= Trail2ProfitTriggerTicks)
            {
                active.ProfitTriggerTicks = Trail2ProfitTriggerTicks;
                active.StopLossTicks = Trail2StopLossTicks;
                active.FrequencyTicks = Math.Max(1, Trail2FrequencyTicks);
            }

            if (Trail3ProfitTriggerTicks > 0 && profitTicks >= Trail3ProfitTriggerTicks)
            {
                active.ProfitTriggerTicks = Trail3ProfitTriggerTicks;
                active.StopLossTicks = Trail3StopLossTicks;
                active.FrequencyTicks = Math.Max(1, Trail3FrequencyTicks);
            }

            return active;
        }
        
        private void ResetActiveBracketState()
        {
            activeEntryPrice = 0;
            activeStopPrice = 0;
            activeTargetPrice = 0;
            activeDirection = 0;
            autoBreakevenApplied = false;
        }

        private struct TrailStep
        {
            public int ProfitTriggerTicks;
            public int StopLossTicks;
            public int FrequencyTicks;
        }
    }
}