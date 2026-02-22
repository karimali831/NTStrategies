#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class OrbMomentum : Strategy
    {
        private void UpdateOpeningRange()
        {
            var now = GetEtTime();
            var t   = now.TimeOfDay;

            var open  = new TimeSpan(9, 30, 0);
            var buildEnd = open.Add(TimeSpan.FromMinutes(Math.Max(1, MinsFromStart)));

            if (t < open)
                return;

            // Build OR during 9:30 -> 9:30+MinsFromStart
            if (t >= open && t < buildEnd)
            {
                if (!orBuilt)
                {
                    if (orHigh == 0 && orLow == 0)
                    {
                        orHigh = High[0];
                        orLow  = Low[0];
                    }
                    else
                    {
                        orHigh = Math.Max(orHigh, High[0]);
                        orLow  = Math.Min(orLow,  Low[0]);
                    }

                    if (EnableDiagnostics)
                    {
                        // Only print when OR actually changes, and only once per bar
                        if (CurrentBar != lastLoggedOrbBar &&
                            (Math.Abs(orHigh - lastLoggedOrHigh) > TickSize * 0.1 ||
                             Math.Abs(orLow  - lastLoggedOrLow)  > TickSize * 0.1))
                        {
                            LogDiag($"ORB build: orHigh={orHigh:F2} orLow={orLow:F2}");
                            lastLoggedOrHigh = orHigh;
                            lastLoggedOrLow  = orLow;
                            lastLoggedOrbBar = CurrentBar;
                        }
                    }
                }

                return;
            }

            // Lock OR once build window ends
            if (!orBuilt && t >= buildEnd)
            {
                orBuilt = true;
                LogDiag($"ORB locked: orHigh={orHigh:F2} orLow={orLow:F2} buildMins={MinsFromStart}");
            }
        }
        
        // Returns: +1 long breakout, -1 short breakout, 0 none
        private int GetEntrySignal()
        {
            // Basic confirmation set (simple + effective):
            // - Break OR with 1 tick buffer
            // - EMA fast vs slow trend alignment
            // - ADX threshold (avoid dead chop)
            var dist = Math.Max(1, MinTicksOutsideOrb) * TickSize;

            var longBreak  = Close[0] >= orHigh + dist;
            var shortBreak = Close[0] <= orLow  - dist;

            var trendUp   = emaFast[0] > emaSlow[0];
            var trendDown = emaFast[0] < emaSlow[0];

            var adxOk = adx[0] >= 18;
            var outLongTicks  = (Close[0] - orHigh) / TickSize;
            var outShortTicks = (orLow - Close[0]) / TickSize;
	
            if (EnableDiagnostics && CurrentBar != lastLoggedSigBar)
            {
                LogDiag($"SIGCHK: close={Close[0]:F2} orH={orHigh:F2} orL={orLow:F2} outL={outLongTicks:F1}t outS={outShortTicks:F1}t emaF={emaFast[0]:F2} emaS={emaSlow[0]:F2} adx={adx[0]:F2}");
                lastLoggedSigBar = CurrentBar;
            }

            if (longBreak && trendUp && adxOk)
                return +1;

            if (shortBreak && trendDown && adxOk)
                return -1;

            return 0;
        }
		
        private bool PullbackSatisfied(int dir)
        {
            // This is used as your "re-entry pullback satisfied" gate.
            // Now it means: we only allow a new primary if trend confirm is met.

            var bars = Math.Max(1, ConfirmBars);

            if (CurrentBar < bars)
                return false;

            // EMA structure must be aligned on the most recent closed bar
            if (dir > 0)
            {
                if (!(emaFast[0] > emaSlow[0]))
                    return false;
            }
            else if (dir < 0)
            {
                if (!(emaFast[0] < emaSlow[0]))
                    return false;
            }
            else
                return true;

            var minBodyTicks = Math.Max(0, ConfirmBodyTicks);

            for (var i = 0; i < bars; i++)
            {
                var bodyTicks = Math.Abs(Close[i] - Open[i]) / TickSize;

                if (bodyTicks < minBodyTicks)
                    return false;

                if (dir > 0)
                {
                    // bullish + close above both EMAs
                    if (!(Close[i] > Open[i] && Close[i] > emaFast[i] && Close[i] > emaSlow[i]))
                        return false;
                }
                else
                {
                    // bearish + close below both EMAs
                    if (!(Close[i] < Open[i] && Close[i] < emaFast[i] && Close[i] < emaSlow[i]))
                        return false;
                }
            }

            return true;
        }
    }
}