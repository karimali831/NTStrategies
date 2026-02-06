using System;

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV3 : Strategy
    {
       private bool HasMomentum(
            int bip,                 // which series to use (0 = primary 5m)
            int barsAgo,             // evaluate bar (1 = last closed bar)
            bool longSide,
            out string failReason,
            out double er,
            out double avgOverlap,
            out double bodyTicks,
            out double wickBody,
            out double clv)
        {
            failReason = "ok";
            er = 0; avgOverlap = 0; bodyTicks = 0; wickBody = 0; clv = 0;

            if (!EnableMomentumFilter)
                return true;

            const int    erLookback       = 10;
            const double minER            = 0.38;
            const int    overlapLookback  = 10;
            const double maxAvgOverlap    = 0.55;
            const int    minBodyTicksReq  = 10;
            const double maxWickBodyReq   = 1.2;
            const double minClvAbsReq     = 0.35;

            // Make sure requested series has enough bars
            if (CurrentBars.Length <= bip)
            {
                failReason = "invalid-bip";
                return false;
            }

            int cb = CurrentBars[bip];
            int needed = barsAgo + Math.Max(erLookback, overlapLookback) + 2;
            if (cb < needed)
            {
                failReason = "insufficient-bars";
                return false;
            }

            // Helpers for series access
            double O(int ba) => Opens[bip][ba];
            double H(int ba) => Highs[bip][ba];
            double L(int ba) => Lows[bip][ba];
            double C(int ba) => Closes[bip][ba];

            // =========================================================
            // 1) ER (efficiency) on the intended bar window
            //    Use barsAgo offset so you're not accidentally measuring the forming bar
            // =========================================================
            double net = Math.Abs(C(barsAgo) - C(barsAgo + erLookback));
            double sum = 0;

            for (int i = 0; i < erLookback; i++)
                sum += Math.Abs(C(barsAgo + i) - C(barsAgo + i + 1));

            if (sum <= TickSize)
            {
                failReason = "er-sum-too-small";
                return false;
            }

            er = net / sum;
            if (er < minER)
            {
                failReason = $"er-too-low(<{minER:0.00})";
                return false;
            }

            // =========================================================
            // 2) Overlap / compression
            // =========================================================
            double overlapSum = 0;

            for (int i = 0; i < overlapLookback; i++)
            {
                double h0 = H(barsAgo + i);
                double l0 = L(barsAgo + i);
                double h1 = H(barsAgo + i + 1);
                double l1 = L(barsAgo + i + 1);

                double overlap = Math.Min(h0, h1) - Math.Max(l0, l1);
                if (overlap < 0) overlap = 0;

                double range = Math.Max(TickSize, Math.Max(h0 - l0, h1 - l1));
                overlapSum += overlap / range;
            }

            avgOverlap = overlapSum / overlapLookback;
            if (avgOverlap > maxAvgOverlap)
            {
                failReason = $"overlap-too-high(>{maxAvgOverlap:0.00})";
                return false;
            }

            // =========================================================
            // 3) Impulse on the signal bar
            // =========================================================
            double o = O(barsAgo);
            double h = H(barsAgo);
            double l = L(barsAgo);
            double c = C(barsAgo);

            double body = Math.Abs(c - o);
            bodyTicks = body / TickSize;

            if (bodyTicks < minBodyTicksReq)
            {
                failReason = $"body-too-small(<{minBodyTicksReq})";
                return false;
            }

            double upperWick = h - Math.Max(o, c);
            double lowerWick = Math.Min(o, c) - l;
            double wicks = Math.Max(0, upperWick) + Math.Max(0, lowerWick);

            wickBody = wicks / Math.Max(TickSize, body);
            if (wickBody > maxWickBodyReq)
            {
                failReason = $"wickbody-too-high(>{maxWickBodyReq:0.00})";
                return false;
            }

            double rangeBar = Math.Max(TickSize, h - l);
            clv = ((c - l) - (h - c)) / rangeBar; // -1..+1

            if (Math.Abs(clv) < minClvAbsReq)
            {
                failReason = $"clv-too-centered(|clv|<{minClvAbsReq:0.00})";
                return false;
            }

            if (longSide && clv < 0) { failReason = "clv-wrong-side-long"; return false; }
            if (!longSide && clv > 0) { failReason = "clv-wrong-side-short"; return false; }

            return true;
        }
       
        private bool ComputeChopOk(
            out double eff, 
            out int lbUsed, 
            out bool hasBars, 
            out bool bypassAdx, 
            out bool bypassSlope, 
            out double upTicks,
            out double downTicks,
            out string reason)
        {
            eff = 1.0;
            lbUsed = 0;
            hasBars = false;
            bypassAdx = false;
            bypassSlope = false;
            reason = "chop=off";
            upTicks = 0;
            downTicks = 0;

            if (!EnableChopFilter)
                return true;

            var barsAgo = SigClosed();   // 0 or 1
            var sigEntry = SigEntry();

            var now = Time[sigEntry];
            var minFromOpen = (int)Math.Floor(now.Subtract(sessionStart).TotalMinutes);
            
            var closedBarsSinceOpen = 0;
            if (entryBarIdx >= 0)
                closedBarsSinceOpen = Math.Max(0, (CurrentBar - entryBarIdx) - barsAgo);

            var lb = (minFromOpen < MinMinutesFromOpen)
                ? Math.Min(closedBarsSinceOpen, ChopLookbackBars)
                : ChopLookbackBars;

            lbUsed = lb;

            if (lb < 2 || entryBarIdx < 0 || CurrentBar < barsAgo + lb)
            {
                reason = $"chop=pass(not-enough-bars lb={lb} closedSinceOpen={closedBarsSinceOpen} minFromOpen={minFromOpen})";
                return true;
            }


            hasBars = true;

            var hh = High[barsAgo];
            var ll = Low[barsAgo];

            for (var i = 1; i <= lb; i++)
            {
                hh = Math.Max(hh, High[barsAgo + i]);
                ll = Math.Min(ll, Low[barsAgo + i]);
            }

            var rangeTicks = (hh - ll) / TickSize;
            
            upTicks = 0.0;
            downTicks = 0.0;

            for (int i = 0; i < lb; i++)
            {
                double d = Close[barsAgo + i] - Close[barsAgo + i + 1];
                double ticks = Math.Abs(d) / TickSize;

                if (d > 0)
                    upTicks += ticks;
                else if (d < 0)
                    downTicks += ticks;
            }

            if (ChopMinRangeTicks > 0 && rangeTicks < ChopMinRangeTicks)
            {
                reason = $"chop=block(range {rangeTicks:0.0} < minRange {ChopMinRangeTicks} lb={lb})";
                return false;
            }

            // -------------------------
            // 2) FLIP-RATE (alternation)
            // -------------------------
            if (ChopMaxFlipPct > 0)
            {
                var flips = 0;
                var comps = 0;

                // compare direction of consecutive closes (you can swap to Close-Open if you prefer)
                for (var i = 0; i < lb; i++)
                {
                    var d0 = Close[barsAgo + i] - Close[barsAgo + i + 1];
                    var d1 = Close[barsAgo + i + 1] - Close[barsAgo + i + 2];

                    // ignore tiny/no-change to reduce noise
                    if (Math.Abs(d0) < TickSize || Math.Abs(d1) < TickSize)
                        continue;

                    comps++;
                    if ((d0 > 0 && d1 < 0) || (d0 < 0 && d1 > 0))
                        flips++;
                }

                if (comps >= 3) // only judge if we had enough meaningful comparisons
                {
                    var flipPct = (double)flips / comps;
                    if (flipPct >= ChopMaxFlipPct)
                    {
                        reason = $"chop=block(flipPct {flipPct:0.00} >= max {ChopMaxFlipPct:0.00} lb={lb} rangeTicks={rangeTicks:0.0})";
                        return false;
                    }
                }
            }

            // -------------------------
            // 3) EFFICIENCY (optional)
            // -------------------------
            var net = Math.Abs(Close[barsAgo] - Close[barsAgo + lb]);
            var path = 0.0;
            for (var i = 0; i < lb; i++)
                path += Math.Abs(Close[barsAgo + i] - Close[barsAgo + i + 1]);

            eff = path <= TickSize * 1e-9 ? 0.0 : Math.Min(1.0, net / path);
            var okByEff = MinChopEfficiency <= 0 || eff >= MinChopEfficiency;

            // BYPASS: ADX
            if (ChopBypassAdx > 0 && adx[barsAgo] >= ChopBypassAdx)
            {
                bypassAdx = true;
                reason = $"chop=bypass(adx {adx[barsAgo]:0.00} >= {ChopBypassAdx}) eff={eff:0.00} rangeTicks={rangeTicks:0.0} lb={lb}";
                return true;
            }
            
            // BYPASS: EMA slope strength
            if (ChopBypassEmaSlopeStrengthTicks > 0)
            {
                var m = GetEmaStruct(barsAgo);
                if (m.HasBars && m.SlopeStrengthTicks >= ChopBypassEmaSlopeStrengthTicks)
                {
                    bypassSlope = true;
                    reason = $"chop=bypass(slope {m.SlopeStrengthTicks:0.0} >= {ChopBypassEmaSlopeStrengthTicks:0.0}) eff={eff:0.00} rangeTicks={rangeTicks:0.0} lb={lb}";
                    return true;
                }
            }

            if (okByEff)
            {
                reason = $"chop=ok(eff {eff:0.00} >= {MinChopEfficiency:0.00} lb={lb} rangeTicks={rangeTicks:0.0})";
                return true;
            }

            reason = $"chop=block(eff {eff:0.00} < {MinChopEfficiency:0.00} lb={lb} rangeTicks={rangeTicks:0.0})";
            return false;
        }
    }
}
