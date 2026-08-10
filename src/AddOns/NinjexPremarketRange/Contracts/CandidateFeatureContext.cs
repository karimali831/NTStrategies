using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class CandidateFeatureContext
    {
        public RangeSessionSnapshot Session { get; }
        public BreakoutSignalSnapshot Breakout { get; }
        public CandleSnapshot Bar { get; }
        public double Atr1MinuteTicks { get; }
        public double Atr5MinuteTicks { get; }
        public double Adx5Minute { get; }

        public CandidateFeatureContext(
            RangeSessionSnapshot session,
            BreakoutSignalSnapshot breakout,
            CandleSnapshot bar,
            double atr1MinuteTicks,
            double atr5MinuteTicks,
            double adx5Minute)
        {
            Session = session;
            Breakout = breakout;
            Bar = bar;
            Atr1MinuteTicks = atr1MinuteTicks;
            Atr5MinuteTicks = atr5MinuteTicks;
            Adx5Minute = adx5Minute;
        }
    }
}
