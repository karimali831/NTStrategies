using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private sealed class ActiveBracketSpec
            {
                public int StopTicks;
                public int TargetTicks;
            }

            private readonly Dictionary<string, ActiveBracketSpec> _activeBracketByAccInstr =
                new Dictionary<string, ActiveBracketSpec>(StringComparer.Ordinal);

            private static string BracketKey(Account acc, Instrument instr)
            {
                return (acc?.Name ?? "") + "|" + (instr?.FullName ?? "");
            }
            
            public void ClearActiveBracket(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                lock (_gate)
                {
                    _activeBracketByAccInstr.Remove(BracketKey(acc, instr));
                }
            }

            internal bool TryGetActiveBracketSpecForUi(Account acc, Instrument instr, out int stopTicks, out int targetTicks)
            {
                stopTicks = 0;
                targetTicks = 0;
                if (acc == null || instr == null) return false;

                lock (_gate)
                {
                    if (!_activeBracketByAccInstr.TryGetValue(BracketKey(acc, instr), out var spec))
                        return false;

                    stopTicks = spec.StopTicks;
                    targetTicks = spec.TargetTicks;
                    return true;
                }
            }
            
            private sealed class PendingBracket
            {
                public string EntryName;
                public int Qty;
                public bool IsBuy;
                public int StopTicks;
                public int TargetTicks;
            }

            private readonly Dictionary<string, PendingBracket> _pendingBrackets =
                new Dictionary<string, PendingBracket>(StringComparer.Ordinal);
            
            
            private void HandleBracketExitOutcome(Account acc, Execution execution)
            {
                if (acc == null || execution?.Order == null) return;

                var ord = execution.Order;
                var instr = ord.Instrument;
                if (instr == null) return;

                var name = (ord.Name ?? "").Trim();

                // Only care about our own exit orders
                var isKnownExit =
                    name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase);

                if (!isKnownExit)
                    return;

                // If account is now flat on this instrument, bracket is complete
                if (GetNetPosition(acc, instr) == 0)
                    ClearActiveBracket(acc, instr);
            }
        }
    }
}