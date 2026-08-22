using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private readonly ExecutionEquityTracker
            executionEquityTracker =
                new ExecutionEquityTracker();

        private decimal executionRealizedPnl;
        private int activeExecutionEntryQuantity;
        
        private decimal CalculateExecutionUnrealizedPnl(
            double marketPrice)
        {
            if (Position.MarketPosition
                == MarketPosition.Flat)
            {
                return 0m;
            }

            if (double.IsNaN(
                    activeExecutionEntryPrice)
                || TickSize <= 0)
            {
                return 0m;
            }

            var tickValue =
                Instrument.MasterInstrument.PointValue
                * TickSize;

            var ticks =
                Position.MarketPosition
                == MarketPosition.Long
                    ? (
                        marketPrice
                        - activeExecutionEntryPrice
                    ) / TickSize
                    : (
                        activeExecutionEntryPrice
                        - marketPrice
                    ) / TickSize;

            var pnl =
                ticks
                * tickValue
                * Math.Abs(
                    Position.Quantity);

            return Convert.ToDecimal(
                pnl);
        }
        
        private void CaptureExecutionEquity(
            DateTime time,
            double marketPrice)
        {
            if (!EnableTradeExecution)
                return;

            //
            // Intratrade snapshots only.
            // Flat transitions are captured explicitly from
            // OnExecutionUpdate when an exit fills.
            //
            if (Position.MarketPosition
                == MarketPosition.Flat)
            {
                return;
            }

            var unrealizedPnl =
                CalculateExecutionUnrealizedPnl(
                    marketPrice);

            var candidate =
                activeExecutionCandidate;

            var direction =
                candidate?.Direction;
            
            var tradingDate =
                activeTradingDate != Core.Globals.MinDate
                    ? activeTradingDate.Date
                    : time.Date;

            var snapshot =
                executionEquityTracker.Update(
                    time,
                    tradingDate,
                    executionRealizedPnl,
                    unrealizedPnl,
                    candidate?.CandidateId,
                    marketPrice,
                    Position.Quantity,
                    direction);

            ExportExecutionEquitySnapshot(
                snapshot);
        }
        
        private void ResetExecutionEquity()
        {
            executionRealizedPnl = 0m;
            executionEquityTracker.Reset();
        }
    }
}