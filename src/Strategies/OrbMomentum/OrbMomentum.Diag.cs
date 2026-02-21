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
        private void LogDiag(string msg)
        {
            if (!EnableDiagnostics)
                return;

            Print($"[ORB] {Time[0]:yyyy-MM-dd HH:mm:ss} | {Instrument.FullName} | {msg}");
        }
    }
}