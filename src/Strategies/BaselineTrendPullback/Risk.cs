#region Using declarations
using System;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class BaselineTrendPullback : Strategy
    {
        private bool TryComputeStopAndTargetTicks(bool longSide, out int stopTicks, out int targetTicks, out string failReason)
        {
            stopTicks = 0;
            targetTicks = 0;
            failReason = "none";

            int lb = Math.Max(2, SwingLookbackBars);

            // Compute swing extreme over last lb bars (excluding current bar to avoid lookahead)
            double swing = longSide ? High[1] : Low[1];
            for (int i = 1; i <= lb; i++)
            {
                if (longSide)
                    swing = Math.Min(swing, Low[i]);
                else
                    swing = Math.Max(swing, High[i]);
            }

            double entryPrice = Close[0];
            int buffer = Math.Max(0, StopBufferTicks);

            double stopPrice = longSide
                ? (swing - buffer * TickSize)
                : (swing + buffer * TickSize);

            double rawStopTicks = longSide
                ? (entryPrice - stopPrice) / TickSize
                : (stopPrice - entryPrice) / TickSize;

            if (rawStopTicks <= 1)
            {
                failReason = "stop-too-small";
                return false;
            }

            if (rawStopTicks > MaxStopTicks)
            {
                failReason = $"stop-too-big(>{MaxStopTicks})";
                return false;
            }

            stopTicks = (int)Math.Ceiling(rawStopTicks);

            double rawTargetTicks = stopTicks * Math.Max(0.5, ProfitTargetR);
            targetTicks = (int)Math.Round(rawTargetTicks);

            if (targetTicks <= stopTicks) // keep RR meaningful
            {
                targetTicks = stopTicks + 1;
            }

            return true;
        }
    }
}