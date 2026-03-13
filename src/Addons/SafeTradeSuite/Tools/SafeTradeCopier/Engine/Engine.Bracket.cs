using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private readonly Dictionary<string, ActiveBracketSpec> _activeBracketByAccInstr =
                new Dictionary<string, ActiveBracketSpec>(StringComparer.Ordinal);
            
            private readonly Dictionary<string, PendingBracket> _pendingBrackets =
                new Dictionary<string, PendingBracket>(StringComparer.Ordinal);


            private static string BracketKey(Account acc, Instrument instr)
            {
                return (acc?.Name ?? "") + "|" + (instr?.FullName ?? "");
            }

            private void ClearActiveBracket(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                lock (_gate)
                {
                    _activeBracketByAccInstr.Remove(BracketKey(acc, instr));
                }
            }

            public bool TryGetActiveBracketSpec(Account acc, Instrument instr, out ActiveBracketSpec spec)
            {
                spec = null;
                if (acc == null || instr == null)
                    return false;

                lock (_gate)
                {
                    return _activeBracketByAccInstr.TryGetValue(BracketKey(acc, instr), out spec);
                }
            }

            private void UpdateActiveBracketSpec(Account acc, Instrument instr, Action<ActiveBracketSpec> update)
            {
                if (acc == null || instr == null || update == null)
                    return;

                lock (_gate)
                {
                    if (_activeBracketByAccInstr.TryGetValue(BracketKey(acc, instr), out var spec))
                        update(spec);
                }
            }
            
            internal bool HasActiveBracket(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;

                lock (_gate)
                    return _activeBracketByAccInstr.ContainsKey(BracketKey(acc, instr));
            }
            
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
            
            private void RemovePendingBracketForEntry(string entryName)
            {
                if (string.IsNullOrWhiteSpace(entryName))
                    return;

                lock (_gate)
                {
                    _pendingBrackets.Remove(entryName);
                }
            }
            
            private string ResolveFollowerAtm(Account follower)
            {
                if (follower == null)
                    return _configuredMasterAtm ?? "None";

                if (_configuredFollowerAtmOverrides != null &&
                    _configuredFollowerAtmOverrides.TryGetValue(follower.Name, out var a) &&
                    !string.IsNullOrWhiteSpace(a))
                {
                    a = a.Trim();

                    if (string.Equals(a, "(inherit master)", StringComparison.OrdinalIgnoreCase))
                        return _configuredMasterAtm ?? "None";

                    if (string.Equals(a, "(follow master exit)", StringComparison.OrdinalIgnoreCase))
                        return "(follow master exit)";

                    return a;
                }

                return _configuredMasterAtm ?? "None";
            }
        }
    }
}