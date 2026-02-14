#region Using declarations

using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        private readonly Dictionary<string, string> _regimeJsonByTag = new Dictionary<string, string>();

        private struct RegimeSnapshot
        {
            public double Adx;
            public double AtrTicks;
            public double Er;

            public double EmaSlopeTicks;
            public double EmaSepTicks;
            public bool   EmaCrossover;   // keep for JSON (we’ll redefine as "recent cross")
            public double EmaEff;
            public bool CrossOverrideOk;
            public int    BarsSinceCross;
            public bool   CrossPenaltyActive;
            
            public bool Aligned;
            public bool StrongStructure;
            public double MinScoreUsed;
            
            public double AtrPct;     // 0..1 percentile rank vs lookback
            public double AtrMedian;
            public double AtrRatio;   // atrTicks / median

            public double RangeTicks;
            public double BodyPct;
            public double Clv;
            public bool   Displacement;


            public double StrongSlopeMinUsed;
            public double StrongSepMinUsed;
            public bool   StrongSlopeOk;
            public bool   StrongSepOk;
            public string StrongFail;
            
            public double Score;   // 0..100
            public bool   Ok;
            public string Label;
            public string Fail;
            public string Json;
        }
        
        // Regime (NOW) + Gate (NOW), while allowing disp at SIG
        // =====================================================

        // 1) Base regime snapshot: ALWAYS computed at bar 0 (now).
        //    This keeps score/ER/ATR percentile/cross-penalty consistent.
        private void ComputeMarketRegime(out RegimeSnapshot r)
        {
            r = new RegimeSnapshot
            {
                Ok = true,
                Label = "UNKNOWN",
                Fail = "none"
            };

            var warmupBars = Math.Max(20, Math.Max(RegimeErLookbackBars + 2, EmaSlopeLookbackBars + 2));
            if (CurrentBar < warmupBars)
            {
                r.Ok = false;
                r.Label = "WARMUP";
                r.Fail = "warmup";
                r.Json = BuildRegimeJson(Time[0], 0, 0, 0, 0, 0, 0, r.Label, false, 0);
                return;
            }

            // ---- Raw values (CLOSED bar 0) ----
            var adxNow   = adx[0];
            var atrTicks = atr[0] / TickSize;
            var er       = GetEfficiencyRatio();

            var es = GetEmaStruct(EmaSlopeMetric.Strength);

            r.Adx           = adxNow;
            r.AtrTicks      = atrTicks;
            r.Er            = er;
            r.EmaSlopeTicks = es.SlopeMetricTicks;
            r.EmaSepTicks   = es.SepTicks;
            r.EmaCrossover  = es.EmaCrossover;
            r.EmaEff        = es.Eff;

            // ---- Subscores (0..1) ----
            var sAdx = SweetSpot01(adxNow, RegimeAdxSweetLow, RegimeAdxSweetHigh);

            // ATR regime (adaptive): percentile + median ratio
            var atrPct = ComputeAtrPercentile(RegimeAtrPctLookbackBars, out var atrMed, out var atrRatio);

            r.AtrPct    = atrPct;
            r.AtrMedian = atrMed;
            r.AtrRatio  = atrRatio;

            // Score ATR based on percentile (not absolute ticks)
            var sAtr = Scale01(atrPct, RegimeAtrPctMin, RegimeAtrPctMax);

            var sEr = Scale01(er, RegimeErMin, 1.0);

            const double MIN_SLOPE_TICKS = 20;
            const double MAX_SLOPE_TICKS = 250;
            const double MIN_SEP_TICKS   = 20;
            const double MAX_SEP_TICKS   = 250;

            var sSlope  = Scale01(Math.Abs(r.EmaSlopeTicks), MIN_SLOPE_TICKS, MAX_SLOPE_TICKS);
            var sSep    = Scale01(Math.Abs(r.EmaSepTicks),   MIN_SEP_TICKS,   MAX_SEP_TICKS);
            var sStruct = 0.7 * sSlope + 0.3 * sSep;

            // ---- Weighted score ----
            const double wAdx = 0.25, wAtr = 0.20, wEr = 0.30, wStruct = 0.25;
            var score01 = wAdx * sAdx + wAtr * sAtr + wEr * sEr + wStruct * sStruct;

            // ---- crossover recency (time-limited penalty) ----
            const int crossPenaltyBars = 6;
            var barsSinceCross = BarsSinceEmaCross(50); // NOW

            r.BarsSinceCross      = barsSinceCross;
            r.CrossPenaltyActive  = barsSinceCross <= crossPenaltyBars;

            // transition penalty (only when cross is RECENT, and slope is not strong)
            if (r.CrossPenaltyActive && Math.Abs(r.EmaSlopeTicks) < 120)
                score01 = Clamp01(score01 - 0.15);

            // Pullback-friendly regime lift
            if (Math.Abs(r.EmaSlopeTicks) >= 150 && r.Er >= 0.45 && r.Adx >= 25)
                score01 = Math.Max(score01, 0.60); // floor at 60

            r.Score = Math.Round(score01 * 100.0, 1);

            // ---- Regime label (facts only) ----
            if (r.Score >= 70)      r.Label = "TREND_TRADEABLE";
            else if (r.Score >= 55) r.Label = "OK";
            else if (r.Score >= 40) r.Label = "TRANSITION";
            else                    r.Label = "CHOP_RISK";

            // Base regime is not a gate
            r.Ok = true;
            r.Fail = "none";

            r.Json = BuildRegimeJson(
                Time[0],
                r.Adx,
                r.AtrTicks,
                r.Er,
                r.EmaSlopeTicks,
                r.EmaSepTicks,
                r.Score,
                r.Label,
                r.CrossPenaltyActive,
                r.EmaEff
            );
        }


        // 2) Gate: regime is NOW, alignment/structure are NOW.
        //    Displacement is evaluated at sigBarsAgo and can bypass SOME fails.
        private bool PassesMarketRegimeGate(bool longSide, int sigBarsAgo, out RegimeSnapshot r)
        {
            // Build base regime snapshot (always NOW)
            ComputeMarketRegime(out r);

            // Warmup -> hard block
            if (r.Label == "WARMUP" || !r.Ok)
            {
                r.Ok = false;
                r.Fail = string.IsNullOrEmpty(r.Fail) ? "warmup" : r.Fail;
                return false;
            }

            var fails = new List<string>();

            // --- strong structure override (pullback-friendly) ---
            const double STRONG_SLOPE_TICKS = 120;
            const double STRONG_SEP_TICKS   = 80;

            r.StrongSlopeMinUsed = STRONG_SLOPE_TICKS;
            r.StrongSepMinUsed   = STRONG_SEP_TICKS;

            // IMPORTANT: Alignment is NOW (matches slope/sep/cross penalty/score)
            r.Aligned = longSide ? emaFast[0] > emaSlow[0] : emaFast[0] < emaSlow[0];

            r.StrongSlopeOk   = Math.Abs(r.EmaSlopeTicks) >= STRONG_SLOPE_TICKS;
            r.StrongSepOk     = Math.Abs(r.EmaSepTicks)   >= STRONG_SEP_TICKS;
            r.StrongStructure = r.Aligned && r.StrongSlopeOk && r.StrongSepOk;

            if (r.StrongStructure)
            {
                r.StrongFail = "none";
            }
            else
            {
                var parts = new List<string>(3);
                if (!r.Aligned)       parts.Add("not-aligned");
                if (!r.StrongSlopeOk) parts.Add($"slope<{STRONG_SLOPE_TICKS:0}");
                if (!r.StrongSepOk)   parts.Add($"sep<{STRONG_SEP_TICKS:0}");
                r.StrongFail = string.Join("|", parts);
            }

            // Directional displacement check (AS-OF sigBarsAgo)
            // Use atrMedian from NOW regime snapshot (stable baseline)
            var disp = IsDisplacementBar(longSide, sigBarsAgo, r.AtrMedian, out _, out _, out _);

            var minScore = RegimeScoreMin;

            // strong structure => relax score gate
            if (r.StrongStructure)
                minScore = Math.Max(0, RegimeScoreMin - 20);

            // very strong slope + ER => relax even more
            if (Math.Abs(r.EmaSlopeTicks) >= 150 && r.Er >= 0.45)
                minScore = Math.Min(minScore, 10);   // or 0 / 5 depending how aggressive you want

            // Displacement override: allow some marginal regimes if signal bar shows real impulse
            var dispOverrideOk =
                disp &&
                r.Er >= DispOverrideErMin &&
                r.Aligned;

            if (r.Score < minScore && !dispOverrideOk)
                fails.Add($"regime-score-low(<{minScore})");

            // ER hard fail only when structure is NOT strong (and no disp override)
            if (r.Er < RegimeErMin && !r.StrongStructure && !dispOverrideOk)
                fails.Add("er-too-low");

            // --- SOFT crossover gate ---
            const double CROSS_OVERRIDE_ER = 0.65;
            const double CROSS_OVERRIDE_SLOPE_FRAC = 0.75;
            const double CROSS_OVERRIDE_SEP_FRAC   = 0.75;

            var absSlope = Math.Abs(r.EmaSlopeTicks);
            var absSep   = Math.Abs(r.EmaSepTicks);

            var crossOverrideOk =
                r.Er >= CROSS_OVERRIDE_ER &&
                (absSlope >= STRONG_SLOPE_TICKS * CROSS_OVERRIDE_SLOPE_FRAC ||
                 absSep   >= STRONG_SEP_TICKS   * CROSS_OVERRIDE_SEP_FRAC);

            var crossBypassOk = crossOverrideOk || dispOverrideOk;

            if (r.CrossPenaltyActive && !crossBypassOk)
                fails.Add($"ema-crossover-soft(barsSince={r.BarsSinceCross})");

            r.CrossOverrideOk = crossOverrideOk;
            r.MinScoreUsed    = minScore;

            // Refresh JSON (still "now")
            r.Json = BuildRegimeJson(
                Time[0],
                r.Adx,
                r.AtrTicks,
                r.Er,
                r.EmaSlopeTicks,
                r.EmaSepTicks,
                r.Score,
                r.Label,
                r.CrossPenaltyActive,
                r.EmaEff
            );

            r.Ok   = fails.Count == 0;
            r.Fail = r.Ok ? "none" : string.Join("|", fails);

            return r.Ok;
        }
                        
        private double GetEfficiencyRatio()
        {
            var lb = Math.Max(2, RegimeErLookbackBars);

            // we read Close[lb] and Close[i+1] up to lb
            if (CurrentBar < lb + 1)
                return 0;

            var net = Math.Abs(Close[0] - Close[lb]) / TickSize;

            double path = 0;
            for (var i = 0; i < lb; i++)
                path += Math.Abs(Close[i] - Close[i + 1]) / TickSize;

            if (path <= 1e-9)
                return 0;

            return Math.Max(0, Math.Min(1, net / path));
        }
        
        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static double Scale01(double v, double min, double max)
        {
            if (max <= min) return 0;
            return Clamp01((v - min) / (max - min));
        }

        private static double SweetSpot01(double v, double lo, double hi)
        {
            if (hi <= lo) return 0;
            var mid = (lo + hi) * 0.5;

            if (v <= lo) return 0;
            if (v >= hi) return 0;

            if (v <= mid) return Clamp01((v - lo) / (mid - lo));
            return Clamp01((hi - v) / (hi - mid));
        }

        // =========================
        // JSON builder
        // =========================
        private string BuildRegimeJson(
            DateTime barTime,
            double adx,
            double atrTicks,
            double er,
            double emaSlopeTicks,
            double emaSepTicks,
            double score,
            string label,
            bool emaCrossover,
            double emaEff)
        {
            var ic = CultureInfo.InvariantCulture;

            return "{"
                   + "\"t\":\"" + barTime.ToString("yyyy-MM-dd HH:mm:ss", ic) + "\","
                   + "\"ADX\":" + adx.ToString("0.##", ic) + ","
                   + "\"ATRTicks\":" + atrTicks.ToString("0.##", ic) + ","
                   + "\"ER\":" + er.ToString("0.###", ic) + ","
                   + "\"EmaSlopeTicks\":" + emaSlopeTicks.ToString("0.##", ic) + ","
                   + "\"EmaSepTicks\":" + emaSepTicks.ToString("0.##", ic) + ","
                   + "\"EmaCrossover\":" + (emaCrossover ? "true" : "false") + ","
                   + "\"EmaEff\":" + emaEff.ToString("0.###", ic) + ","
                   + "\"RegimeScore\":" + score.ToString("0.#", ic) + ","
                   + "\"Regime\":\"" + label + "\""
                   + "}";
        }
        
        private void RememberRegimeForTag(string tag, string regimeJson)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            _regimeJsonByTag[tag] = regimeJson ?? "";
        }

        private string GetRegimeForTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "";

            return _regimeJsonByTag.TryGetValue(tag, out var json) ? json : "";
        }

        private void ForgetRegimeForTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            _regimeJsonByTag.Remove(tag);
        }
        
        private double GetAtrTicks(int barsAgo)
        {
            return atr[barsAgo] / TickSize;
        }

        private double ComputeMedian(double[] tmp, int n)
        {
            Array.Sort(tmp, 0, n);
            if (n <= 0) return 0;
            if ((n & 1) == 1) return tmp[n / 2];
            return 0.5 * (tmp[(n / 2) - 1] + tmp[n / 2]);
        }

        // Percentile rank of current vs past N bars (0..1)
        // Uses "strictly less than" count; ties sit around middle naturally on average.
        private double ComputeAtrPercentile(int lookbackBars, out double median, out double ratio)
        {
            median = 0;
            ratio = 1;

            var lb = Math.Max(10, lookbackBars);
            if (CurrentBar < lb + 1)
                return 0.5;

            var cur = GetAtrTicks(0);

            // Collect history (closed bars)
            var n = lb;
            var arr = new double[n];
            for (int i = 0; i < n; i++)
                arr[i] = GetAtrTicks(i);

            median = ComputeMedian(arr, n);
            if (median > 1e-9)
                ratio = cur / median;

            int less = 0;
            for (int i = 0; i < n; i++)
                if (arr[i] < cur) less++;

            return Clamp01(less / (double)Math.Max(1, n - 1));
        }

        private void ComputeBarShape(int barsAgo, out double rangeTicks, out double bodyPct, out double clv)
        {
            var h = High[barsAgo];
            var l = Low[barsAgo];
            var o = Open[barsAgo];
            var c = Close[barsAgo];

            var range = Math.Max(TickSize, h - l);
            rangeTicks = range / TickSize;

            var body = Math.Abs(c - o);
            bodyPct = Clamp01(body / range);

            // CLV: 0..1 where close sits in the bar (1=at high, 0=at low)
            clv = Clamp01((c - l) / range);
        }

        private bool IsDisplacementBar(bool longSide, int barsAgo, double atrMedianTicks,
            out double rangeTicks, out double bodyPct, out double clv)
        {
            ComputeBarShape(barsAgo, out rangeTicks, out bodyPct, out clv);

            // must have usable median
            if (atrMedianTicks <= 1e-9)
                return false;

            var rangeOk = rangeTicks >= (DispRangeAtrMult * atrMedianTicks);
            var bodyOk  = bodyPct   >= DispBodyPctMin;

            // close location
            var clvOk = longSide ? (clv >= DispClvMinBull) : (clv <= DispClvMaxBear);

            // simple breakout confirmation (optional)
            var breakoutOk = true;
            if (DispBreakoutTicks > 0)
            {
                var bt = DispBreakoutTicks * TickSize;
                breakoutOk = longSide
                    ? Close[barsAgo] >= High[barsAgo + 1] + bt
                    : Close[barsAgo] <= Low[barsAgo + 1]  - bt;
            }

            return rangeOk && bodyOk && clvOk && breakoutOk;
        }
    }
}