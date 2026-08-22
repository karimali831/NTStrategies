using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk
{
    public sealed class ExecutionEquitySnapshot
    {
        public DateTime ObservedAt { get; set; }

        public DateTime TradingDate { get; set; }

        public decimal RealizedPnl { get; set; }

        public decimal UnrealizedPnl { get; set; }

        public decimal EquityDelta { get; set; }

        public decimal PeakEquityDelta { get; set; }

        public decimal DrawdownFromPeak { get; set; }

        public string CandidateId { get; set; }

        public double MarketPrice { get; set; }

        public int PositionQuantity { get; set; }

        public bool PositionOpen { get; set; }

        public TradeDirection? Direction { get; set; }
    }
}