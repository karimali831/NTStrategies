#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        private readonly List<FlatTradeSummary> _flatTradeSummaries = new List<FlatTradeSummary>(256); 
        private string _activeEntryTag = "";
        
        private struct FlatTradeSummary
        {
            public DateTime ExitTime;
            public string   Tag;
            public string   Outcome;
            public double   PnlCur;
            public double   PnlTicks;
            public double   HoldSeconds;
            public double   MfeTicks;
            public double   MaeTicks;
            public string   RegimeJson;
        }
        
        private void DumpFlatTradesSummary()
        {
            if (!DebugMode)
                return;

            if (_flatTradeSummaries.Count == 0)
            {
                Print("[DIAG] End-of-run: no flat trades recorded.");
                return;
            }

            Print($"[DIAG] End-of-run: flat trades={_flatTradeSummaries.Count}");

            foreach (var t in _flatTradeSummaries)
            {
                Print(
                    $"[ENTRY FLAT] {t.ExitTime:yyyy-MM-dd HH:mm:ss.fff} " +
                    $"name={t.Tag} outcome={t.Outcome} " +
                    $"pnl={t.PnlCur:0.00} ticks={t.PnlTicks:0.0} " +
                    $"hold={t.HoldSeconds:0}s " +
                    $"mfeTicks={t.MfeTicks:0.0} maeTicks={t.MaeTicks:0.0} " +
                    $"regime={t.RegimeJson}"
                );
            }
        }
    }
}