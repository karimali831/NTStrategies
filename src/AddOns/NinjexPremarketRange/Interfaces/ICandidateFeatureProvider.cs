using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Interfaces
{
    public interface ICandidateFeatureProvider
    {
        void Reset(RangeSessionSnapshot session);
        void UpdateCompletedBar(
            NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis.CandleSnapshot bar,
            double atr1MinuteTicks,
            double atr5MinuteTicks,
            double adx5Minute);
        void OnBreakoutRegistered(BreakoutSignalSnapshot breakout);
        void OnBreakoutResolved(BreakoutResolutionSnapshot resolution);
        CandidateFeatureSnapshot Capture(CandidateFeatureContext context);
        CandidateFeatureSnapshot Capture(BreakoutSignalSnapshot breakout);
    }
}
