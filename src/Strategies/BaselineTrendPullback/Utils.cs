#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class BaselineTrendPullback : Strategy
    {
        private bool IsWithinTimeWindow(DateTime t, int startHHmm, int endHHmm)
        {
            int hhmm = t.Hour * 100 + t.Minute;

            // Simple window (assumes start < end within same day)
            return hhmm >= startHHmm && hhmm <= endHHmm;
        }

        private void MaybeLogBar(string code, string reason)
        {
            if (!DiagEnabled || _log == null)
                return;

            if (!LogOnlyOnSignalOrBlock)
                _log.Info(code, new Dictionary<string, object>
                {
                    ["t"] = Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["reason"] = reason
                });
        }

        private void MaybeLogSignalOrBlock(string code, string reason, BarFeatures f,
            Dictionary<string, object> extra = null)
        {
            if (!DiagEnabled || _log == null)
                return;

            // If user wants strict minimal logs, only log on signal/block (not every bar)
            var map = f.ToLogMap(extraReason: reason);

            if (extra != null)
            {
                foreach (var kv in extra)
                    map[kv.Key] = kv.Value;
            }

            _log.Bar(code, map);
        }
    }
}

// ===========================
// Supporting types
// ===========================
public enum BaselineRiskMode
{
    SwingWithCap = 0
}

public sealed class BarFeatures
{
    public DateTime Time;
    public int Bar;
    public double Close;

    public bool LongBias;
    public bool ShortBias;

    public double EmaFast;
    public double EmaSlow;
    public double FastSlopeTicks;
    public double SlowSlopeTicks;
    public double SepTicks;

    public double Adx;
    public double Atr;
    public double AtrMedian;

    public double WickPct;
    public double RangeTicks;

    public double DistToFastTicks;

    public bool StructureOk;
    public string StructureFailReason;

    public bool RegimeOk;
    public string RegimeFailReason;

    public bool CandleOk;
    public string CandleFailReason;

    public Dictionary<string, object> ToLogMap(string extraReason)
    {
        return new Dictionary<string, object>
        {
            ["t"] = Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["bar"] = Bar,

            ["bias"] = LongBias ? "L" : (ShortBias ? "S" : "N"),
            ["close"] = Close.ToString("0.00", CultureInfo.InvariantCulture),

            ["ef"] = EmaFast.ToString("0.00", CultureInfo.InvariantCulture),
            ["es"] = EmaSlow.ToString("0.00", CultureInfo.InvariantCulture),
            ["slopeF"] = FastSlopeTicks.ToString("0.0", CultureInfo.InvariantCulture),
            ["slopeS"] = SlowSlopeTicks.ToString("0.0", CultureInfo.InvariantCulture),
            ["sep"] = SepTicks.ToString("0.0", CultureInfo.InvariantCulture),
            ["distEF"] = DistToFastTicks.ToString("0.0", CultureInfo.InvariantCulture),

            ["adx"] = Adx.ToString("0.00", CultureInfo.InvariantCulture),
            ["atr"] = Atr.ToString("0.00", CultureInfo.InvariantCulture),
            ["atrMed"] = AtrMedian.ToString("0.00", CultureInfo.InvariantCulture),

            ["wickPct"] = WickPct.ToString("0.00", CultureInfo.InvariantCulture),
            ["rngT"] = RangeTicks.ToString("0.0", CultureInfo.InvariantCulture),

            ["structOk"] = StructureOk,
            ["regimeOk"] = RegimeOk,
            ["candleOk"] = CandleOk,

            ["reason"] = extraReason ?? "none"
        };
    }
}

