using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk
{
    public sealed class ExecutionEquityTracker
    {
        public decimal CurrentEquityDelta { get; private set; }

        public decimal PeakEquityDelta { get; private set; }

        public decimal MaximumDrawdownFromPeak { get; private set; }

        public DateTime? MaximumDrawdownAt { get; private set; }

        public void Reset()
        {
            CurrentEquityDelta = 0m;
            PeakEquityDelta = 0m;
            MaximumDrawdownFromPeak = 0m;
            MaximumDrawdownAt = null;
        }

        public ExecutionEquitySnapshot Update(
            DateTime observedAt,
            DateTime tradingDate,
            decimal realizedPnl,
            decimal unrealizedPnl,
            string candidateId,
            double marketPrice,
            int positionQuantity,
            TradeDirection? direction)
        {
            CurrentEquityDelta =
                realizedPnl
                + unrealizedPnl;

            if (CurrentEquityDelta
                > PeakEquityDelta)
            {
                PeakEquityDelta =
                    CurrentEquityDelta;
            }

            var drawdownFromPeak =
                PeakEquityDelta
                - CurrentEquityDelta;

            if (drawdownFromPeak
                > MaximumDrawdownFromPeak)
            {
                MaximumDrawdownFromPeak =
                    drawdownFromPeak;

                MaximumDrawdownAt =
                    observedAt;
            }

            return new ExecutionEquitySnapshot
            {
                ObservedAt =
                    observedAt,

                TradingDate =
                    tradingDate.Date,

                RealizedPnl =
                    realizedPnl,

                UnrealizedPnl =
                    unrealizedPnl,

                EquityDelta =
                    CurrentEquityDelta,

                PeakEquityDelta =
                    PeakEquityDelta,

                DrawdownFromPeak =
                    drawdownFromPeak,

                CandidateId =
                    candidateId,

                MarketPrice =
                    marketPrice,

                PositionQuantity =
                    positionQuantity,

                PositionOpen =
                    positionQuantity != 0,

                Direction =
                    direction
            };
        }
    }
}