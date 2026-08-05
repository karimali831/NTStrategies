namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts
{
    public sealed class CandidateModelDetails
    {
        public static readonly CandidateModelDetails Empty =
            new CandidateModelDetails(0, 0, 0);

        public int BarsAfterBreakout { get; }
        public double RetestInsideDepthTicks { get; }
        public double RetestOutsideDistanceTicks { get; }

        public CandidateModelDetails(
            int barsAfterBreakout,
            double retestInsideDepthTicks,
            double retestOutsideDistanceTicks)
        {
            BarsAfterBreakout = barsAfterBreakout;
            RetestInsideDepthTicks = retestInsideDepthTicks;
            RetestOutsideDistanceTicks = retestOutsideDistanceTicks;
        }
    }
}