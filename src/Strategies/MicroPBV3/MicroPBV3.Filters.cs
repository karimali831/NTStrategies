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

    }
}
