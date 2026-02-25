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
            
            var outLongTicks  = (High[0] - orHigh) / TickSize;   // how far the bar's HIGH exceeded orHigh
            var outShortTicks = (orLow - Low[0]) / TickSize;     // how far the bar's LOW broke below orLow
            
            if (longBreak && shortBreak)
            {
                if (outShortTicks > outLongTicks)
                    longBreak = false;
                else
                    shortBreak = false;
            }

            if (EnableDiagnostics && CurrentBar != lastLoggedSigBar)
            {
                LogDiag($"SIGCHK: H={High[0]:F2} L={Low[0]:F2} C={Close[0]:F2} orH={orHigh:F2} orL={orLow:F2} outL={outLongTicks:F1}t outS={outShortTicks:F1}t need={MinTicksOutsideOrb}t emaF={emaFast[0]:F2} emaS={emaSlow[0]:F2} adx={adx[0]:F2}");
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

            if (EnableDoubleTopBottomFilter)
            {
                var passes = PassesDoubleTopBottomFilter(dir, out _);

                if (passes)
                {
                    if (EnableDiagnostics)
                    {
                        LogDiag("PASSED double top/bottom filter", oncePerBar: true);
                    }
                    return true;
                }
            }
            
            var minTicks = Math.Max(0, EntryEmaMinProximityTicks);
            var maxTicks = Math.Max(0, EntryEmaMaxProximityTicks);

            // if both disabled, skip filter
            if (minTicks <= 0 && maxTicks <= 0)
                return true;

            // safety: if user accidentally flips them
            if (maxTicks > 0 && minTicks > maxTicks)
                (minTicks, maxTicks) = (maxTicks, minTicks);

            var ema = emaFast[0];

            // 1) HARD REQUIREMENT: bar must actually TOUCH/RECLAIM the EMA (wick touch)
            // (i.e., EMA is inside the bar range)
            var touchedEma = (Low[0] <= ema && High[0] >= ema);
            if (!touchedEma)
            {
                failReason = "entry-no-ema-touch (bar did not reclaim/touch emaFast)";
                return false;
            }

            // 2) Proximity is based on CLOSE distance (your intent: don't enter sitting on EMA)
            var closeDistTicks = Math.Abs(Close[0] - ema) / TickSize;

            // enforce minimum distance from EMA using CLOSE, direction-aware
            if (minTicks > 0)
            {
                if (dir > 0)
                {
                    if (Close[0] < ema + (minTicks * TickSize))
                    {
                        failReason = $"entry-close-too-near-emaFast-long (closeDist={closeDistTicks:F1}t < {minTicks}t)";
                        return false;
                    }
                }
                else if (dir < 0)
                {
                    if (Close[0] > ema - (minTicks * TickSize))
                    {
                        failReason = $"entry-close-too-near-emaFast-short (closeDist={closeDistTicks:F1}t < {minTicks}t)";
                        return false;
                    }
                }
            }

            // enforce maximum distance from EMA using CLOSE (avoid chasing)
            if (maxTicks > 0 && closeDistTicks > maxTicks)
            {
                failReason = $"entry-close-too-far-from-emaFast (closeDist={closeDistTicks:F1}t > {maxTicks}t)";
                return false;
            }

            if (EnableDiagnostics)
                LogDiag(
                    $"EMA CHECK: dir={(dir > 0 ? "LONG" : "SHORT")} close={Close[0]:F2} low={Low[0]:F2} high={High[0]:F2} " +
                    $"emaFast={ema:F2} touched=Y closeDist={closeDistTicks:F1}t min={minTicks}t max={maxTicks}t",
                    oncePerBar: true
                );

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
        
        private bool TryGetDoubleTopBottomClosedBars(out bool isDoubleTop, out bool isDoubleBottom, out double levelPrice, out string debug)
        {
            isDoubleTop = false;
            isDoubleBottom = false;
            levelPrice = 0;
            debug = "none";

            var lookback = Math.Max(2, DoubleTopBottomLookbackBars);
            var maxDiffTicks = Math.Max(1, DoubleTopBottomMaxDiffTicks);
            var maxDiff = maxDiffTicks * TickSize;

            // Use CLOSED bars only: barsAgo = 1..lookback
            if (CurrentBar < lookback + 1)
            {
                debug = "insufficient-bars";
                return false;
            }

            // --- Double Top (two highs within maxDiff)
            int top1Idx = -1, top2Idx = -1;
            double top1 = double.MinValue, top2 = double.MinValue;

            for (int i = 1; i <= lookback; i++)
            {
                var h = High[i];
                if (h > top1)
                {
                    top2 = top1; top2Idx = top1Idx;
                    top1 = h;    top1Idx = i;
                }
                else if (h > top2)
                {
                    top2 = h; top2Idx = i;
                }
            }

            if (top1Idx > 0 && top2Idx > 0 && Math.Abs(top1 - top2) <= maxDiff)
            {
                isDoubleTop = true;
                levelPrice = (top1 + top2) * 0.5;
                debug = $"double-top top1={top1:F2}@{top1Idx} top2={top2:F2}@{top2Idx} diffTicks={Math.Abs(top1-top2)/TickSize:F1}";
                return true;
            }

            // --- Double Bottom (two lows within maxDiff)
            int bot1Idx = -1, bot2Idx = -1;
            double bot1 = double.MaxValue, bot2 = double.MaxValue;

            for (int i = 1; i <= lookback; i++)
            {
                var l = Low[i];
                if (l < bot1)
                {
                    bot2 = bot1; bot2Idx = bot1Idx;
                    bot1 = l;    bot1Idx = i;
                }
                else if (l < bot2)
                {
                    bot2 = l; bot2Idx = i;
                }
            }

            if (bot1Idx > 0 && bot2Idx > 0 && Math.Abs(bot1 - bot2) <= maxDiff)
            {
                isDoubleBottom = true;
                levelPrice = (bot1 + bot2) * 0.5;
                debug = $"double-bottom bot1={bot1:F2}@{bot1Idx} bot2={bot2:F2}@{bot2Idx} diffTicks={Math.Abs(bot1-bot2)/TickSize:F1}";
                return true;
            }

            debug = "none";
            return false;
        }

        

        private bool PassesDoubleTopBottomFilter(int dir, out string failReason)
        {
            failReason = "ok";

            if (!EnableDoubleTopBottomFilter)
                return true;

            if (!TryGetDoubleTopBottomClosedBars(out var isTop, out var isBot, out var lvl, out var dbg))
                return true;

            // Block LONGs into a double top (resistance)
            if (dir > 0 && isTop)
            {
                failReason = $"double-top-block (lvl={lvl:F2})";
                return false;
            }

            // Block SHORTs into a double bottom (support)
            if (dir < 0 && isBot)
            {
                failReason = $"double-bottom-block (lvl={lvl:F2})";
                return false;
            }

            return true;
        }
    }
}