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
        private int GetEntrySignal(out string failReason)
        {
            failReason = "no-signal";

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

            if (!adxOk)
            {
                failReason = $"adx-too-low ({adx[0]:F1} < 18)";
                return 0;
            }

            if (longBreak)
            {
                if (!trendUp)
                {
                    failReason = "long-block: emaFast-not-above-emaSlow";
                    return 0;
                }
                failReason = "ok";
                return +1;
            }

            if (shortBreak)
            {
                if (!trendDown)
                {
                    failReason = "short-block: emaFast-not-below-emaSlow";
                    return 0;
                }
                failReason = "ok";
                return -1;
            }

            // not outside OR
            failReason = $"not-outside-orb (outL={outLongTicks:F1}t outS={outShortTicks:F1}t need={MinTicksOutsideOrb}t)";
            return 0;
        }
		
        private bool PullbackSatisfied(int dir, out string failReason)
        {
            failReason = "unknown";

            var bars = Math.Max(1, ConfirmBars);
            if (CurrentBar < bars)
            {
                failReason = $"insufficient-bars (need {bars})";
                return false;
            }

            // EMA structure on most recent bar
            if (dir > 0)
            {
                if (!(emaFast[0] > emaSlow[0]))
                {
                    failReason = "ema-structure-fail (need emaFast > emaSlow)";
                    return false;
                }
            }
            else if (dir < 0)
            {
                if (!(emaFast[0] < emaSlow[0]))
                {
                    failReason = "ema-structure-fail (need emaFast < emaSlow)";
                    return false;
                }
            }
            else
            {
                failReason = "dir=0";
                return true;
            }

            var minBodyTicks = Math.Max(0, ConfirmBodyTicks);

            for (var i = 0; i < bars; i++)
            {
                var bodyTicks = Math.Abs(Close[i] - Open[i]) / TickSize;
                if (bodyTicks < minBodyTicks)
                {
                    failReason = $"confirm-body-too-small (i={i} body={bodyTicks:F1}t < {minBodyTicks}t)";
                    return false;
                }

                if (dir > 0)
                {
                    if (!(Close[i] > Open[i]))
                    {
                        failReason = $"confirm-not-bullish (i={i})";
                        return false;
                    }
                    if (!(Close[i] > emaFast[i] && Close[i] > emaSlow[i]))
                    {
                        failReason = $"confirm-close-not-above-both-emas (i={i})";
                        return false;
                    }
                }
                else
                {
                    if (!(Close[i] < Open[i]))
                    {
                        failReason = $"confirm-not-bearish (i={i})";
                        return false;
                    }
                    if (!(Close[i] < emaFast[i] && Close[i] < emaSlow[i]))
                    {
                        failReason = $"confirm-close-not-below-both-emas (i={i})";
                        return false;
                    }
                }
            }

            failReason = "ok";
            return true;
        }
    }
}