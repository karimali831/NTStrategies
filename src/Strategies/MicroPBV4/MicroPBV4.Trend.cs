using System;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class MicroPBV4 : Strategy
    {
        private struct TrendQuality
        {
            public double Score;           // 0..100
            public double Displacement;    // 0..1
            public double Persistence;     // 0..1
            public double EmaVelocity;     // 0..1
            public double Efficiency;      // 0..1
            public bool DirectionOk;       // direction matches longSide?
        }
        
        private bool TrendConfirm(
            out string failReason,
            out bool trendUp,
            out bool trendDown)
        {
            failReason = "none";
            trendUp = false;
            trendDown = false;

            var m = GetEmaStruct(EmaSlopeMetric.DirectionalAbs);
            if (!m.HasBars)
            {
                failReason = "insufficient-bars";
                return false;
            }
            
            TrendTicks(30, out var lb, out var bars, out var ticks, out var upTicks,
                out var downTicks);

            trendUp = upTicks > downTicks;
            trendDown = downTicks > upTicks;

            return trendUp || trendDown;
        }
        

        // Convenience overload if you don't care about trendUp/trendDown flags
        private bool TrendConfirm(out string failReason)
        {
            return TrendConfirm(out failReason, out _, out _);
        }

        private bool TrendConfirm()
        {
            return TrendConfirm(out _);
        }
        

        private void TrendTicks(
            int lookbackBars,
            out int lb, 
            out bool hasBars,
            out double rangeTicks,
            out double upTicks,
            out double downTicks)
        {
            lb = 0;
            hasBars = false;
            upTicks = 0;
            downTicks = 0;
            
            var closedBarsSinceOpen = 0;
            if (_sessionStartBarIdx >= 0)
                closedBarsSinceOpen = Math.Max(0, CurrentBar - _sessionStartBarIdx);

            var minutesPerBar = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? BarsPeriod.Value : 1;
            var minFromOpen = closedBarsSinceOpen * minutesPerBar;

            lb = minFromOpen < MinMinutesFromOpen
                ? closedBarsSinceOpen
                : // 0,1,2,3...
                Math.Min(lookbackBars, closedBarsSinceOpen);
            
            hasBars = true;

            var hh = High[0];
            var ll = Low[0];

            for (var i = 1; i <= lb; i++)
            {
                hh = Math.Max(hh, High[i]);
                ll = Math.Min(ll, Low[i]);
            }

            rangeTicks = (hh - ll) / TickSize;
            upTicks = 0.0;
            downTicks = 0.0;

            for (var i = 0; i < lb; i++)
            {
                var d = Close[i] - Close[i + 1];
                var ticks = Math.Abs(d) / TickSize;

                if (d > 0)
                    upTicks += ticks;
                else if (d < 0)
                    downTicks += ticks;
            }
        }
        
        private TrendQuality ComputeTrendQuality(bool longSide, int idx)
        {
            var tq = new TrendQuality();
            
            var lb = Math.Max(3, TrendQualityLookbackBars);
            if (CurrentBar < idx + lb + 1)
            {
                tq.Score = 0;
                tq.DirectionOk = false;
                return tq;
            }

            // ---------- 1) Direction ----------
            // Use net move over lookback, aligned with side.
            var netTicksSigned = (Close[idx] - Close[idx + lb]) / TickSize;
            tq.DirectionOk = longSide ? netTicksSigned > 0 : netTicksSigned < 0;
            var netTicksAbs = Math.Abs(netTicksSigned);

            // ---------- 2) Displacement vs ATR ----------
            // Big net move relative to ATR implies real impulse / continuation potential.
            if (atr != null)
            {
                var atrTicks = Math.Max(1.0, (atr[idx] / TickSize));
                var disp = netTicksAbs / (atrTicks * lb);            // normalize by ATR * bars +     tq.Displacement = Clamp01(disp / 0.8);               // 0.8 ~= "strong"
            }

            // ---------- 3) Persistence ----------
            // Count closes in the intended direction.
            var good = 0;
            for (var i = idx; i < idx + lb; i++)
            {
                if (longSide && Close[i] > Close[i + 1]) good++;
                else if (!longSide && Close[i] < Close[i + 1]) good++;
            }
            tq.Persistence = Clamp01(good / (double)lb);

            // ---------- 4) EMA velocity ----------
            // Use fast EMA slope over lookback (directional).
            var emaNow = emaFast[idx];
            var emaPast = emaFast[idx + lb];
            var emaSlopeTicksSigned = (emaNow - emaPast) / TickSize;
            var emaSlopeAbs = Math.Abs(emaSlopeTicksSigned);

            // gate: slope should point the correct way; otherwise this component is weak
            var slopeDirOk = longSide ? emaSlopeTicksSigned > 0 : emaSlopeTicksSigned < 0;
            var vel01 = Scale01(emaSlopeAbs, 20, 140);          // tune for NQ 5m
            tq.EmaVelocity = slopeDirOk ? vel01 : vel01 * 0.25; // punish wrong-way slope

            // ---------- 5) Efficiency (directional ER) ----------
            // Your ER is fine, but make it directional (only reward net move in that direction).
            double path = 0;
            for (var i = idx; i < idx + lb; i++)
                path += Math.Abs(Close[i] - Close[i + 1]) / TickSize;
            
            var dirNet = tq.DirectionOk ? netTicksAbs : 0.0;
            tq.Efficiency = path <= 1e-9 ? 0 : Clamp01(dirNet / path);

            // ---------- Weighted score ----------
            // Displacement + EMA velocity do the heavy lifting; persistence/efficiency confirm.
            const double wDisp = 0.35, wVel = 0.30, wPers = 0.20, wEff = 0.15;
            var score01 =
                wDisp * tq.Displacement +
                wVel  * tq.EmaVelocity +
                wPers * tq.Persistence +
                wEff  * tq.Efficiency;

            // If direction is wrong, overall score should be low for that side.
            if (!tq.DirectionOk)
                score01 *= 0.25;

            tq.Score = Math.Round(Clamp01(score01) * 100.0, 1);
            return tq;
        }
    }
}
