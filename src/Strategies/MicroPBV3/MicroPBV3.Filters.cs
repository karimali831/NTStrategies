using System;

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV3 : Strategy
    {
       private bool HasMomentum(
            bool longSide,
            out string failReason,
            out double er,
            out double avgOverlap,
            out double bodyTicks,
            out double wickBody,
            out double clv)
        {
            failReason = "ok";
            er = 0;
            avgOverlap = 0;
            bodyTicks = 0;
            wickBody = 0;
            clv = 0;

            if (!EnableMomentumFilter)
                return true;

            const int    structureBars   = 6;     // bars used for HH/HL check (current logic)
            const int    erLookback      = 10;
            const double minER           = 0.38;
            const int    overlapLookback = 10;
            const double maxAvgOverlap   = 0.55;
            const int    minBodyTicksReq = 10;
            const double maxWickBodyReq  = 1.2;
            const double minClvAbsReq    = 0.35;

            if (CurrentBar < Math.Max(erLookback, overlapLookback) + structureBars + 2)
            {
                failReason = "insufficient-bars";
                return false;
            }

            // =========================================================
            // 1) STRUCTURE: (your existing bar-to-bar check)
            // =========================================================
            var structureOk = true;

            for (var i = 0; i < structureBars - 1; i++)
            {
                if (longSide)
                {
                    if (!(High[i] > High[i + 1] || Low[i] > Low[i + 1]))
                    {
                        structureOk = false;
                        break;
                    }
                }
                else
                {
                    if (!(High[i] < High[i + 1] || Low[i] < Low[i + 1]))
                    {
                        structureOk = false;
                        break;
                    }
                }
            }

            if (!structureOk)
            {
                failReason = longSide ? "structure-fail-long" : "structure-fail-short";
                return false;
            }

            // =========================================================
            // 2) EFFICIENCY RATIO (kills chop)
            // =========================================================
            var net = Math.Abs(Close[0] - Close[erLookback]);
            double sum = 0;

            for (var i = 0; i < erLookback; i++)
                sum += Math.Abs(Close[i] - Close[i + 1]);

            if (sum <= TickSize)
            {
                failReason = "er-sum-too-small";
                er = 0;
                return false;
            }

            er = net / sum;
            if (er < minER)
            {
                failReason = $"er-too-low(<{minER:0.00})";
                return false;
            }

            // =========================================================
            // 3) BAR OVERLAP (compression / grind)
            // =========================================================
            double overlapSum = 0;

            for (int i = 0; i < overlapLookback; i++)
            {
                var h0 = High[i];
                var l0 = Low[i];
                var h1 = High[i + 1];
                var l1 = Low[i + 1];

                var overlap = Math.Min(h0, h1) - Math.Max(l0, l1);
                if (overlap < 0) overlap = 0;

                var range = Math.Max(TickSize, Math.Max(h0 - l0, h1 - l1));
                overlapSum += overlap / range;
            }

            avgOverlap = overlapSum / overlapLookback;
            if (avgOverlap > maxAvgOverlap)
            {
                failReason = $"overlap-too-high(>{maxAvgOverlap:0.00})";
                return false;
            }

            // =========================================================
            // 4) IMPULSE BAR (kills tiny wicks / dojis)
            // =========================================================
            var o = Open[0];
            var h = High[0];
            var l = Low[0];
            var c = Close[0];

            var body = Math.Abs(c - o);
            bodyTicks = body / TickSize;

            if (bodyTicks < minBodyTicksReq)
            {
                failReason = $"body-too-small(<{minBodyTicksReq})";
                return false;
            }

            var upperWick = h - Math.Max(o, c);
            var lowerWick = Math.Min(o, c) - l;
            var wicks = Math.Max(0, upperWick) + Math.Max(0, lowerWick);

            wickBody = wicks / Math.Max(TickSize, body);
            if (wickBody > maxWickBodyReq)
            {
                failReason = $"wickbody-too-high(>{maxWickBodyReq:0.00})";
                return false;
            }

            // Close location value (-1..+1)
            var rangeBar = Math.Max(TickSize, h - l);
            clv = ((c - l) - (h - c)) / rangeBar;

            if (Math.Abs(clv) < minClvAbsReq)
            {
                failReason = $"clv-too-centered(|clv|<{minClvAbsReq:0.00})";
                return false;
            }

            if (longSide && clv < 0)
            {
                failReason = "clv-wrong-side-long";
                return false;
            }

            if (!longSide && clv > 0)
            {
                failReason = "clv-wrong-side-short";
                return false;
            }

            failReason = "ok";
            return true;
        }
    }
}
