using System;
using NinjaTrader.Cbi;
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
        
        private double lastExecutionEquityMarketPrice =
            double.NaN;

        private decimal lastExecutionEquityRealizedPnl;

        private int lastExecutionEquityPositionQuantity;

        private MarketPosition lastExecutionEquityPosition =
            MarketPosition.Flat;
        
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
            // Entry and flat transitions are captured explicitly
            // from OnExecutionUpdate.
            //
            if (Position.MarketPosition
                == MarketPosition.Flat)
            {
                return;
            }

            //
            // NinjaTrader multi-series processing can expose
            // historical tick callbacks whose timestamps precede
            // the actual execution fill.
            //
            if (activeExecutionEntryTime
                    != Core.Globals.MinDate
                && time
                   < activeExecutionEntryTime)
            {
                return;
            }

            var unrealizedPnl =
                CalculateExecutionUnrealizedPnl(
                    marketPrice);

            var position =
                Position.MarketPosition;

            var quantity =
                Position.Quantity;

            //
            // Equity cannot have changed when price, realized P&L,
            // position and quantity are all unchanged.
            //
            var sameState =
                !double.IsNaN(
                    lastExecutionEquityMarketPrice)
                && Math.Abs(
                    marketPrice
                    - lastExecutionEquityMarketPrice)
                   < TickSize / 2.0
                && executionRealizedPnl
                   == lastExecutionEquityRealizedPnl
                && quantity
                   == lastExecutionEquityPositionQuantity
                && position
                   == lastExecutionEquityPosition;

            if (sameState)
                return;

            lastExecutionEquityMarketPrice =
                marketPrice;

            lastExecutionEquityRealizedPnl =
                executionRealizedPnl;

            lastExecutionEquityPositionQuantity =
                quantity;

            lastExecutionEquityPosition =
                position;

            var candidate =
                activeExecutionCandidate;

            var tradingDate =
                activeTradingDate
                    != Core.Globals.MinDate
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
                    quantity,
                    candidate?.Direction);

            ExportExecutionEquitySnapshot(
                snapshot);
        }
        
        private void ResetExecutionEquity()
        {
            executionRealizedPnl =
                0m;

            lastExecutionEquityMarketPrice =
                double.NaN;

            lastExecutionEquityRealizedPnl =
                0m;

            lastExecutionEquityPositionQuantity =
                0;

            lastExecutionEquityPosition =
                MarketPosition.Flat;

            executionEquityTracker.Reset();
        }
    }
}