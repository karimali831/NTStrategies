using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    /// <summary>
    /// Immutable session information supplied to candidate models.
    /// Models must not mutate strategy-owned session state.
    /// </summary>
    public sealed class RangeSessionSnapshot
    {
        public DateTime TradingDate { get; }

        public string Instrument { get; }

        public string Contract { get; }

        public double PremarketHigh { get; }

        public double PremarketLow { get; }

        public DateTime HighFormationTime { get; }

        public DateTime LowFormationTime { get; }

        public double TickSize { get; }

        public double PointValue { get; }

        public double RangeTicks
        {
            get
            {
                if (TickSize <= 0)
                    return 0;

                return Math.Max(
                    0,
                    (PremarketHigh - PremarketLow) / TickSize);
            }
        }

        public RangeSessionSnapshot(
            DateTime tradingDate,
            string instrument,
            string contract,
            double premarketHigh,
            double premarketLow,
            DateTime highFormationTime,
            DateTime lowFormationTime,
            double tickSize,
            double pointValue)
        {
            TradingDate = tradingDate;
            Instrument = instrument ?? string.Empty;
            Contract = contract ?? string.Empty;
            PremarketHigh = premarketHigh;
            PremarketLow = premarketLow;
            HighFormationTime = highFormationTime;
            LowFormationTime = lowFormationTime;
            TickSize = tickSize;
            PointValue = pointValue;
        }

        public static RangeSessionSnapshot From(
            RangeSessionContext session)
        {
            if (session == null)
                return null;

            return new RangeSessionSnapshot(
                session.TradingDate,
                session.Instrument,
                session.Contract,
                session.PremarketHigh,
                session.PremarketLow,
                session.HighFormationTime,
                session.LowFormationTime,
                session.TickSize,
                session.PointValue);
        }
    }
}