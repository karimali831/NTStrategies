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

            public int    BarsSinceCross;
            public bool   CrossPenaltyActive;
            
            public bool Aligned;
            public bool StrongStructure;
            public double MinScoreUsed;

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

        
        // 1) Keep ONE base builder (replaces your current ComputeMarketRegime body)
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

            // ---- Raw values (closed bar) ----
            var adxNow   = adx[0];
            var atrTicks = atr[0] / TickSize;
            var er       = GetEfficiencyRatio();

            var es = GetEmaStruct(EmaSlopeMetric.Strength);

            r.Adx          = adxNow;
            r.AtrTicks     = atrTicks;
            r.Er           = er;
            r.EmaSlopeTicks = es.SlopeMetricTicks;
            r.EmaSepTicks   = es.SepTicks;
            r.EmaCrossover  = es.EmaCrossover;
            r.EmaEff        = es.Eff;
            

            // ---- Subscores (0..1) ----
            var sAdx = SweetSpot01(adxNow, RegimeAdxSweetLow, RegimeAdxSweetHigh);
            var sAtr = Scale01(atrTicks, RegimeAtrMinTicks, RegimeAtrMaxTicks);
            var sEr  = Scale01(er, RegimeErMin, 1.0);

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

            // transition penalty
            // ---- crossover recency (time-limited penalty) ----
            const int CROSS_PENALTY_BARS = 6;     // start here
            var barsSinceCross = BarsSinceEmaCross(50);

            r.BarsSinceCross = barsSinceCross;
            r.CrossPenaltyActive = barsSinceCross <= CROSS_PENALTY_BARS;
            
            // transition penalty (only when cross is RECENT, and slope is not strong)
            if (r.CrossPenaltyActive && r.EmaSlopeTicks < 120)
                score01 = Clamp01(score01 - 0.15);
            
            // Pullback-friendly regime lift
            if (r.EmaSlopeTicks >= 150 && r.Er >= 0.45 && r.Adx >= 25)
            {
                score01 = Math.Max(score01, 0.60); // floor at 60
            }

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
                r.CrossPenaltyActive, // <-- instead of r.EmaCrossover
                r.EmaEff
            );
        }

        // 2) Refactor PassesMarketRegimeGate to reuse ComputeMarketRegime (no duplication)
        private bool PassesMarketRegimeGate(bool longSide, out RegimeSnapshot r)
        {
            // build base regime snapshot (always)
            ComputeMarketRegime(out r);

            // Warmup (or other base invalid) -> hard block
            if (r.Label == "WARMUP" || !r.Ok)
            {
                r.Ok = false;
                r.Fail = string.IsNullOrEmpty(r.Fail) ? "warmup" : r.Fail;
                return false;
            }

            // Directional adjustments can be added here later using longSide
            // (for now, score is direction-agnostic)

            var fails = new List<string>();

            // --- strong structure override (pullback-friendly) ---
            const double STRONG_SLOPE_TICKS = 120;
            const double STRONG_SEP_TICKS   = 80;

            r.StrongSlopeMinUsed = STRONG_SLOPE_TICKS;
            r.StrongSepMinUsed   = STRONG_SEP_TICKS;

            // must set aligned first (directional)
            r.Aligned = longSide ? emaFast[0] > emaSlow[0]
                : emaFast[0] < emaSlow[0];

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
            
            r.Aligned = longSide ? emaFast[0] > emaSlow[0] : emaFast[0] < emaSlow[0];

            var strongStructure =
                r.Aligned &&
                Math.Abs(r.EmaSlopeTicks) >= STRONG_SLOPE_TICKS &&
                Math.Abs(r.EmaSepTicks)   >= STRONG_SEP_TICKS;

            var minScore = RegimeScoreMin;

            if (r.StrongStructure)
                minScore = Math.Max(35, RegimeScoreMin - 20);

            if (r.EmaSlopeTicks >= 150 && r.Er >= 0.45)
                minScore = Math.Min(minScore, 45);

            if (r.Score < minScore)
                fails.Add($"regime-score-low(<{minScore})");
            
            // ER hard fail only when structure is NOT strong
            if (r.Er < RegimeErMin && !r.StrongStructure)
                fails.Add("er-too-low");

            if (r.CrossPenaltyActive)
                fails.Add($"ema-crossover-penalty(barsSince={r.BarsSinceCross})");

            r.MinScoreUsed = minScore;
            
            r.Json = BuildRegimeJson(
                Time[0],
                r.Adx,
                r.AtrTicks,
                r.Er,
                r.EmaSlopeTicks,
                r.EmaSepTicks,
                r.Score,
                r.Label,
                r.CrossPenaltyActive, // <-- instead of r.EmaCrossover
                r.EmaEff
            );
            
            r.Ok = fails.Count == 0;
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
    }
}