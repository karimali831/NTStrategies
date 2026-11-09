using System;

namespace NinjaTrader.NinjaScript.Ninjex
{
    // Pure price-validation rules, independent of NinjaTrader order events.
    public static class NinjexOpeningBracketMath
    {
        public static bool TryBracket(int direction, double fill, double breakoutStop,
            double marketReference, double tickSize, double rewardRisk,
            out double target, out string rejection)
        {
            target = double.NaN;
            rejection = "";
            if (direction != 1 && direction != -1 || tickSize <= 0 || rewardRisk <= 0
                || double.IsNaN(fill) || double.IsNaN(breakoutStop)
                || double.IsInfinity(fill) || double.IsInfinity(breakoutStop))
            {
                rejection = "INVALID_INPUT";
                return false;
            }
            double risk = direction * (fill - breakoutStop);
            if (risk < tickSize * 0.999999)
            {
                rejection = "FILL_AT_OR_BEYOND_CANDLE_STOP";
                return false;
            }

            // A short buy-to-cover stop must be ABOVE current market, and
            // a long sell stop must be BELOW current market. Do not silently
            // displace the user's candle-anchored stop to make an order valid.
            double reference = marketReference > 0 ? marketReference : fill;
            if (direction > 0
                ? breakoutStop >= reference - tickSize * 0.5
                : breakoutStop <= reference + tickSize * 0.5)
            {
                rejection = "MARKET_AT_OR_BEYOND_CANDLE_STOP";
                return false;
            }

            double rawTarget = fill + direction * risk * rewardRisk;
            target = Math.Round(rawTarget / tickSize, 0, MidpointRounding.AwayFromZero) * tickSize;
            if (direction * (target - fill) < tickSize * 0.999999)
            {
                rejection = "INVALID_TARGET";
                return false;
            }
            return true;
        }
    }
}