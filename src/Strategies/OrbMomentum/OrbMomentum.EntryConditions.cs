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

            var longBreak  = High[0] >= orHigh + dist;
            var shortBreak = Low[0]  <= orLow  - dist;

            var trendUp   = emaFast[0] > emaSlow[0];
            var trendDown = emaFast[0] < emaSlow[0];

            var adxOk = adx[0] >= ADXMin;

            var outLongTicks  = (Close[0] - orHigh) / TickSize;
            var outShortTicks = (orLow - Close[0]) / TickSize;
            
            if (longBreak && shortBreak)
            {
                if (outShortTicks > outLongTicks)
                    longBreak = false;
                else
                    shortBreak = false;
            }

            if (EnableDiagnostics && CurrentBar != lastLoggedSigBar)
            {
                LogDiag($"SIGCHK: close={Close[0]:F2} orH={orHigh:F2} orL={orLow:F2} outL={outLongTicks:F1}t outS={outShortTicks:F1}t emaF={emaFast[0]:F2} emaS={emaSlow[0]:F2} adx={adx[0]:F2}");
                lastLoggedSigBar = CurrentBar;
            }

            if (!adxOk)
            {
                failReason = $"adx-too-low ({adx[0]:F1} < {ADXMin})";
                return 0;
            }

            if (longBreak)
            {
                if (!trendUp)
                {
                    failReason = "long-block: emaFast-not-above-emaSlow";
                    return 0;
                }

                // must be at EMA zone (no chasing)
                if (!EntryAtEmaFast(+1, out var emaFail))
                {
                    failReason = $"long-block: {emaFail}";
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

                // must be at EMA zone (no chasing)
                if (!EntryAtEmaFast(-1, out var emaFail))
                {
                    failReason = $"short-block: {emaFail}";
                    return 0;
                }

                failReason = "ok";
                return -1;
            }

            // not outside OR
            failReason = $"not-outside-orb (outL={outLongTicks:F1}t outS={outShortTicks:F1}t need={MinTicksOutsideOrb}t)";
            return 0;
        }
        
        private bool EntryAtEmaFast(int dir, out string failReason)
        {
            failReason = "ok";

            var minTicks = Math.Max(0, EntryEmaMinProximityTicks);
            var maxTicks = Math.Max(0, EntryEmaMaxProximityTicks);

            // if both disabled, skip filter
            if (minTicks <= 0 && maxTicks <= 0)
                return true;

            // safety: if user accidentally flips them
            if (maxTicks > 0 && minTicks > maxTicks)
            {
                (minTicks, maxTicks) = (maxTicks, minTicks);
            }

            var ema = emaFast[0];

            // Wick-aware touch distance (you already compute this)
            double touchDistTicks;
            if (Low[0] <= ema && ema <= High[0])
                touchDistTicks = 0.0;
            else
                touchDistTicks = Math.Min(
                    Math.Abs(High[0] - ema),
                    Math.Abs(Low[0] - ema)
                ) / TickSize;

            // enforce minimum distance (avoid entries sitting right on EMA)
            if (minTicks > 0 && touchDistTicks < minTicks)
            {
                failReason = $"entry-too-close-to-emaFast (touchDist={touchDistTicks:F1}t < {minTicks}t)";
                return false;
            }

            // enforce maximum distance (avoid chasing)
            if (maxTicks > 0 && touchDistTicks > maxTicks)
            {
                failReason = $"entry-too-far-from-emaFast (touchDist={touchDistTicks:F1}t > {maxTicks}t)";
                return false;
            }

            if (EnableDiagnostics)
                LogDiag($"EMA CHECK: dir={(dir > 0 ? "LONG" : "SHORT")} close={Close[0]:F2} low={Low[0]:F2} high={High[0]:F2} emaFast={ema:F2} touchDist={touchDistTicks:F1}t min={minTicks}t max={maxTicks}t", oncePerBar: true);

            if (dir > 0 && High[0] < ema)
            {
                failReason = "entry-wrong-side-long (bar entirely below emaFast)";
                return false;
            }

            if (dir < 0 && Low[0] > ema)
            {
                failReason = "entry-wrong-side-short (bar entirely above emaFast)";
                return false;
            }
            
            return true;
        }
        
        private bool RunnerPullbackTouched(int dir, out string failReason)
        {
            failReason = "ok";

            var pbTicks = Math.Max(1, RunnerPullbackTicks);

            if (dir > 0)
            {
                if (Low[0] <= emaFast[0] - (pbTicks * TickSize))
                    return true;

                failReason = $"runner-pullback-not-deep-enough (low not <= emaFast-{pbTicks}t)";
                return false;
            }

            if (dir < 0)
            {
                if (High[0] >= emaFast[0] + (pbTicks * TickSize))
                    return true;

                failReason = $"runner-pullback-not-deep-enough (high not >= emaFast+{pbTicks}t)";
                return false;
            }

            failReason = "dir=0";
            return false;
        }
		
        private bool ConfirmBarsSatisfied(int dir, out string failReason)
        {
            failReason = "unknown";

            var bars = Math.Max(1, ConfirmBars);
            if (CurrentBar < bars - 1)
            {
                failReason = $"insufficient-bars (need {bars} confirm bars)";
                return false;
            }

            // EMA structure on most recent bar
            // Early-entry mode: use last CLOSED bar for structure
            if (dir > 0)
            {
                if (!(emaFast[1] > emaSlow[1]))
                {
                    failReason = "ema-structure-fail (need emaFast > emaSlow)";
                    return false;
                }
            }
            else if (dir < 0)
            {
                if (!(emaFast[1] < emaSlow[1]))
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

            for (var j = 0; j < bars; j++)
            {
                if (CurrentBar <= j)
                {
                    failReason = $"insufficient-bars (need barsAgo={j})";
                    return false;
                }

                // use i instead of j from here down
                if (IsIndecisionCandle(j, out var topW, out var botW))
                {
                    failReason = $"confirm-indecision (i={j} topW={topW:F1}t botW={botW:F1}t diff<={IndecisionWickDiffTicks}t)";
                    return false;
                }

                var bodyTicks = Math.Abs(Close[j] - Open[j]) / TickSize;
                if (bodyTicks < minBodyTicks)
                {
                    if (!IsRejectionCandle(dir, j, out var rejWick))
                    {
                        failReason = $"confirm-body-too-small (i={j} body={bodyTicks:F1}t < {minBodyTicks}t)";
                        return false;
                    }
                }
                
                var ej = Math.Max(1, j);

                if (dir > 0)
                {
                    if (!(Close[j] > Open[j]))
                    {
                        failReason = $"confirm-not-bullish (i={j})";
                        return false;
                    }
                    if (!(Close[j] > emaFast[ej] && Close[j] > emaSlow[ej]))
                    {
                        failReason = $"confirm-close-not-above-both-emas (i={j})";
                        return false;
                    }
                }
                else
                {
                    if (!(Close[j] < Open[j]))
                    {
                        failReason = $"confirm-not-bearish (i={j})";
                        return false;
                    }
                    if (!(Close[j] < emaFast[ej] && Close[j] < emaSlow[ej]))
                    {
                        failReason = $"confirm-close-not-below-both-emas (i={j})";
                        return false;
                    }
                }
            }

            failReason = "ok";
            return true;
        }
    }
}