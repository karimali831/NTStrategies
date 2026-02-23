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
        private void LogDiag(string msg, bool oncePerBar = true)
        {
            if (!EnableDiagnostics)
                return;

            if (oncePerBar && !ShouldLogThisBar())
                return;

            Print($"[ORB] {Time[0]:yyyy-MM-dd HH:mm:ss} | {Instrument?.FullName} | {msg}");
        }
        
        private void LogBlockOnce(string reason)
        {
            if (!EnableDiagnostics)
                return;

            // reset each new bar
            if (CurrentBar != lastLoggedBlockBar)
                lastLoggedBlockReason = null;

            // Dedupe by a stable "key" (strip dynamic values in parentheses)
            var key = reason;
            var idx = reason.IndexOf(" (", StringComparison.Ordinal);
            if (idx > 0)
                key = reason.Substring(0, idx);

            if (string.Equals(key, lastLoggedBlockReason, StringComparison.Ordinal))
                return;

            lastLoggedBlockBar = CurrentBar;
            lastLoggedBlockReason = key;

            // Print full reason once (with details)
            LogDiag($"BLOCK: {reason}", oncePerBar: false);
        }
        
        private bool ShouldLogThisBar()
        {
            if (CurrentBar == lastDiagBar)
                return false;

            lastDiagBar = CurrentBar;
            return true;
        }
        
        private void LogEmaDistanceEveryBar()
        {
            if (!EnableDiagnostics)
                return;

            if (CurrentBar == lastLoggedEmaDistBar)
                return;

            lastLoggedEmaDistBar = CurrentBar;

            if (emaFast == null)
                return;

            var ema = emaFast[0];

            // close distance
            var closeDistTicks = Math.Abs(Close[0] - ema) / TickSize;

            // wick-aware "touch distance": 0 if EMA is inside the bar range, else min distance to range
            double touchDistTicks;
            if (Low[0] <= ema && ema <= High[0])
                touchDistTicks = 0.0;
            else
                touchDistTicks = Math.Min(Math.Abs(High[0] - ema), Math.Abs(Low[0] - ema)) / TickSize;

            LogDiag($"EMA DIST: emaF={ema:F2} closeDist={closeDistTicks:F1}t touchDist={touchDistTicks:F1}t proxMin={EntryEmaMinProximityTicks}t proxMax={EntryEmaMaxProximityTicks}t earlyRange={EarlyEntryRangeTicks}t H={High[0]:F2} L={Low[0]:F2} C={Close[0]:F2}");
        }
    }
}