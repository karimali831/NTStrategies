using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class BreakoutSignalSnapshot
    {
        public string EventId { get; }
        public DateTime TradingDate { get; }
        public string Contract { get; }
        public TradeDirection Direction { get; }
        public int AttemptNumber { get; }

        public DateTime BreakoutTime { get; }
        public int BreakoutBarIndex { get; }

        public double RangeLevel { get; }
        public double BreakoutClose { get; }
        public double DistanceOutsideTicks { get; }

        public CandleSnapshot Candle { get; }
        public CandleMetrics Metrics { get; }

        public BreakoutSignalSnapshot(
            string eventId,
            DateTime tradingDate,
            string contract,
            TradeDirection direction,
            int attemptNumber,
            DateTime breakoutTime,
            int breakoutBarIndex,
            double rangeLevel,
            double breakoutClose,
            double distanceOutsideTicks,
            CandleSnapshot candle,
            CandleMetrics metrics)
        {
            EventId = eventId ?? string.Empty;
            TradingDate = tradingDate;
            Contract = contract ?? string.Empty;
            Direction = direction;
            AttemptNumber = attemptNumber;
            BreakoutTime = breakoutTime;
            BreakoutBarIndex = breakoutBarIndex;
            RangeLevel = rangeLevel;
            BreakoutClose = breakoutClose;
            DistanceOutsideTicks = distanceOutsideTicks;
            Candle = candle;
            Metrics = metrics;
        }

        public static BreakoutSignalSnapshot From(
            BreakoutEvent breakout)
        {
            if (breakout == null)
                return null;

            return new BreakoutSignalSnapshot(
                breakout.EventId,
                breakout.TradingDate,
                breakout.Contract,
                breakout.Direction,
                breakout.AttemptNumber,
                breakout.BreakoutTime,
                breakout.BreakoutBarIndex,
                breakout.RangeLevel,
                breakout.BreakoutClose,
                breakout.DistanceOutsideTicks,
                breakout.Candle,
                breakout.Metrics);
        }
    }
}