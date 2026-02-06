using System;

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV3 : Strategy
    {
        private bool HasMomentum(
            int bip, // which series to use (0 = primary 5m)
            int barsAgo, // evaluate bar (1 = last closed bar)
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

            const int erLookback = 10;
            const double minER = 0.38;
            const int overlapLookback = 10;
            const double maxAvgOverlap = 0.55;
            const int minBodyTicksReq = 10;
            const double maxWickBodyReq = 1.2;
            const double minClvAbsReq = 0.35;

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

            return true;
        }

        private bool ComputeChopOk(out double eff, out int lbUsed, out bool hasBars, out bool bypassAdx,
            out bool bypassSlope, out string reason)
        {
            eff = 1.0;
            lbUsed = 0;
            hasBars = false;
            bypassAdx = false;
            bypassSlope = false;
            reason = "chop=off";

            if (!EnableChopFilter)
                return true;

            var barsAgo = SigClosed(); // always closed bar
            var sigEntry = SigEntry();

            var now = Time[sigEntry];
            var minFromOpen = (int)Math.Floor(now.Subtract(sessionStart).TotalMinutes);

            // var lb = Math.Max(2, minFromOpen <= MinMinutesFromOpen ? 3 :  ChopLookbackBars);
            var lb = Math.Max(2, ChopLookbackBars);
            lbUsed = lb;

            if (CurrentBar < barsAgo + lb)
            {
                reason = $"chop=pass(not-enough-bars lb={lb})";
                return true;
            }

            hasBars = true;

            // -------------------------
            // 1) RANGE (magnitude) gate
            // -------------------------
            var hh = High[barsAgo];
            var ll = Low[barsAgo];
            for (var i = 1; i <= lb; i++)
            {
                hh = Math.Max(hh, High[barsAgo + i]);
                ll = Math.Min(ll, Low[barsAgo + i]);
            }

            var rangeTicks = (hh - ll) / TickSize;
            if (ChopMinRangeTicks > 0 && rangeTicks < ChopMinRangeTicks)
            {
                reason = $"chop=block(range {rangeTicks:0.0} < minRange {ChopMinRangeTicks} lb={lb})";
                return false;
            }

            // ==========================================================
            // NEW) CONSOLIDATION + NO-FOLLOW-THROUGH (open chop killer)
            // ==========================================================
            // Params you’ll need as NinjaScript properties (or consts):
            // - EnableChopConsolidationBlock (bool)
            // - ChopConsolidationRatioMax (double, e.g. 0.75)
            // - ChopNoFollowThroughBars (int, e.g. 4)
            // - ChopConsolidationMinRangeTicks (double, optional; e.g. 40)
            //
            // Logic: if range is contracting AND last N bars have no consecutive same-color bodies => block.

            var EnableChopConsolidationBlock = true;
            var ChopNoFollowThroughBars = 4;
            var ChopConsolidationRatioMax = 0.75;
            var ChopConsolidationMinRangeTicks = 0;
            

            if (EnableChopConsolidationBlock)
            {
                bool enoughForConsolidation = CurrentBar >= barsAgo + (2 * lb);

                // ----- 1) Range contraction ratio -----
                double rRecentTicks = rangeTicks;
                double rPriorTicks = 0.0;
                double ratio = 999.0;
                bool contracting = false;

                if (enoughForConsolidation)
                {
                    var hh2 = High[barsAgo + lb];
                    var ll2 = Low[barsAgo + lb];
                    for (var i = lb + 1; i <= 2 * lb; i++)
                    {
                        hh2 = Math.Max(hh2, High[barsAgo + i]);
                        ll2 = Math.Min(ll2, Low[barsAgo + i]);
                    }

                    rPriorTicks = (hh2 - ll2) / TickSize;
                    ratio = (rPriorTicks <= TickSize * 1e-9) ? 999.0 : (rRecentTicks / rPriorTicks);

                    contracting = (ChopConsolidationRatioMax > 0 && ratio <= ChopConsolidationRatioMax);

                    // optional: ignore tiny ranges (avoids blocking normal pullbacks)
                    if (ChopConsolidationMinRangeTicks > 0 && rRecentTicks < ChopConsolidationMinRangeTicks)
                        contracting = true; // treat very small recent range as consolidation
                }

                // ----- 2) No follow-through: no consecutive green or red bodies -----
                bool noFollowThrough = false;
                if (ChopNoFollowThroughBars > 1 && CurrentBar >= barsAgo + ChopNoFollowThroughBars)
                {
                    int prevDir = 0;
                    bool hasConsecutive = false;

                    // Examine last N closed bars: barsAgo..barsAgo+N-1
                    for (int i = 0; i < ChopNoFollowThroughBars; i++)
                    {
                        double o = Open[barsAgo + i];
                        double c = Close[barsAgo + i];

                        int dir = 0;
                        if (c > o) dir = 1;
                        else if (c < o) dir = -1;

                        // ignore dojis (dir=0)
                        if (dir != 0 && prevDir != 0 && dir == prevDir)
                        {
                            hasConsecutive = true;
                            break;
                        }

                        if (dir != 0)
                            prevDir = dir;
                    }

                    noFollowThrough = !hasConsecutive;
                }

                // Combined block:
                // - contracting range AND no follow-through => chop
                if (contracting && noFollowThrough)
                {
                    reason =
                        enoughForConsolidation
                            ? $"chop=block(consolidation+nofollow rRecent={rRecentTicks:0.0} rPrior={rPriorTicks:0.0} ratio={ratio:0.00}<= {ChopConsolidationRatioMax:0.00} nfLb={ChopNoFollowThroughBars} lb={lb})"
                            : $"chop=block(nofollow nfLb={ChopNoFollowThroughBars} lb={lb})";
                    return false;
                }
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
                        reason =
                            $"chop=block(flipPct {flipPct:0.00} >= max {ChopMaxFlipPct:0.00} lb={lb} rangeTicks={rangeTicks:0.0})";
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
                reason =
                    $"chop=bypass(adx {adx[barsAgo]:0.00} >= {ChopBypassAdx}) eff={eff:0.00} rangeTicks={rangeTicks:0.0} lb={lb}";
                return true;
            }

            // BYPASS: EMA slope strength
            if (ChopBypassEmaSlopeStrengthTicks > 0)
            {
                var m = GetEmaStruct(barsAgo);
                if (m.HasBars && m.SlopeStrengthTicks >= ChopBypassEmaSlopeStrengthTicks)
                {
                    bypassSlope = true;
                    reason =
                        $"chop=bypass(slope {m.SlopeStrengthTicks:0.0} >= {ChopBypassEmaSlopeStrengthTicks:0.0}) eff={eff:0.00} rangeTicks={rangeTicks:0.0} lb={lb}";
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
