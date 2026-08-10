using System;
using System.Globalization;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk
{
    public sealed class RiskScenario
    {
        public RiskScenario(
            int maximumInitialStopTicks,
            double riskRewardRatio)
        {
            if (maximumInitialStopTicks <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumInitialStopTicks));

            if (riskRewardRatio <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(riskRewardRatio));

            MaximumInitialStopTicks =
                maximumInitialStopTicks;

            RiskRewardRatio =
                riskRewardRatio;
        }

        public int MaximumInitialStopTicks
        {
            get;
        }

        public double RiskRewardRatio
        {
            get;
        }

        public string ScenarioId =>
            string.Format(
                CultureInfo.InvariantCulture,
                "S{0:000}_R{1:0.0}",
                MaximumInitialStopTicks,
                RiskRewardRatio);

        public override string ToString()
        {
            return ScenarioId;
        }
    }
}