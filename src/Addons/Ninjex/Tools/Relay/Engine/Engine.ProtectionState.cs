using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
        {
            private readonly Dictionary<string, ProtectionRuntime> _protectionByAccInstr =
                new Dictionary<string, ProtectionRuntime>(StringComparer.Ordinal);

            private const int ProtectionBreachConfirmMs = 350;

            private ProtectionRuntime GetOrCreateProtectionRuntime(Account acc, Instrument instr)
            {
                var key = BracketKey(acc, instr);

                lock (_gate)
                {
                    if (_protectionByAccInstr.TryGetValue(key, out var rt) && rt != null)
                        return rt;

                    rt = new ProtectionRuntime
                    {
                        AccountName = acc?.Name ?? "",
                        InstrumentName = instr?.FullName ?? ""
                    };

                    _protectionByAccInstr[key] = rt;
                    return rt;
                }
            }

            private void SetProtectionState(ProtectionRuntime rt, ProtectionState newState, string reason)
            {
                if (rt == null)
                    return;

                var normalizedReason = reason ?? "";
                var changed = rt.State != newState ||
                    !string.Equals(rt.LastReason ?? "", normalizedReason, StringComparison.Ordinal);

                if (!changed)
                    return;

                rt.State = newState;
                rt.LastReason = normalizedReason;
                rt.LastStateChangeUtc = DateTime.UtcNow;

                NinjexRuntime.PrintLog(
                    $"[PROTECTION STATE] acc={rt.AccountName} instr={rt.InstrumentName} " +
                    $"state={rt.State} reason={rt.LastReason}");
            }

            private ProtectionRuntime EvaluateProtectionState(Account acc, Instrument instr)
            {
                var rt = GetOrCreateProtectionRuntime(acc, instr);

                var hasLivePosition = TryGetLivePosition(acc, instr, out _, out var absQty) && absQty > 0;
                var net = GetNetPosition(acc, instr);
                var hasWorkingEntry = HasWorkingEntryOrders(acc, instr);
                var hasWorkingBracket = HasWorkingBracketOrders(acc, instr);
                var hasPendingBracket = HasPendingBracketForInstrument(acc, instr);
                var flattenInFlight = IsFlattenInFlight(acc, instr);

                var exitSeenRecently =
                    rt.LastExitExecutionUtc.HasValue &&
                    (DateTime.UtcNow - rt.LastExitExecutionUtc.Value).TotalMilliseconds <= 1500;

                var entrySeenRecently =
                    rt.LastEntryExecutionUtc.HasValue &&
                    (DateTime.UtcNow - rt.LastEntryExecutionUtc.Value).TotalMilliseconds <= 1500;

                rt.HasLivePosition = hasLivePosition;
                rt.NetQuantity = net;
                rt.HasWorkingEntry = hasWorkingEntry;
                rt.HasWorkingBracket = hasWorkingBracket;
                rt.HasPendingBracket = hasPendingBracket;
                rt.FlattenInFlight = flattenInFlight;
                rt.ExitExecutionSeenRecently = exitSeenRecently;

                ProtectionState newState;
                string reason;

                if (!hasLivePosition && !hasWorkingEntry && !hasPendingBracket && !flattenInFlight)
                {
                    newState = ProtectionState.Flat;
                    reason = "No live position, no entry, no bracket, no flatten in flight.";
                }
                else if (flattenInFlight)
                {
                    newState = ProtectionState.FlattenPending;
                    reason = "Flatten in flight.";
                }
                else if (hasWorkingEntry && !hasLivePosition)
                {
                    newState = ProtectionState.EntryPending;
                    reason = "Entry working, no live position yet.";
                }
                else if (hasLivePosition && hasWorkingBracket)
                {
                    newState = ProtectionState.Protected;
                    reason = "Live position with working bracket.";
                }
                else if (hasLivePosition && (hasWorkingEntry || hasPendingBracket || entrySeenRecently))
                {
                    newState = ProtectionState.BracketPending;
                    reason = "Live position exists while bracket is pending/building.";
                }
                else if (hasLivePosition && exitSeenRecently)
                {
                    newState = ProtectionState.ExitPending;
                    reason = "Live position, no bracket visible, recent exit execution seen.";
                }
                else if (hasLivePosition)
                {
                    var grace =
                        rt.LastStateChangeUtc.HasValue &&
                        (DateTime.UtcNow - rt.LastStateChangeUtc.Value).TotalMilliseconds <= 500;

                    if (grace)
                    {
                        newState = ProtectionState.BracketPending;
                        reason = "Grace window after transition.";
                    }
                    else
                    {
                        newState = ProtectionState.Faulted;
                        reason = "Live position without bracket or valid transition.";
                    }
                }
                else
                {
                    newState = ProtectionState.Flat;
                    reason = "Flat";
                }

                SetProtectionState(rt, newState, reason);
                return rt;
            }
           
            private bool HasPendingBracketForInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;

                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!IsSameInstrument(o.Instrument, instr))
                        continue;

                    var name = (o.Name ?? "").Trim();
                    if (!name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                        continue;

                    lock (_gate)
                    {
                        if (_pendingBrackets.ContainsKey(name))
                            return true;
                    }
                }

                return false;
            }
           
            private void ConfirmProtectionBreachIfNeeded(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return;

                var rt = EvaluateProtectionState(acc, instr);

                if (rt.State != ProtectionState.Faulted)
                {
                    rt.BreachPending = false;
                    rt.BreachFirstDetectedUtc = null;
                    return;
                }

                var now = DateTime.UtcNow;

                if (!rt.BreachPending)
                {
                    rt.BreachPending = true;
                    rt.BreachFirstDetectedUtc = now;

                    Log(
                        $"[WARNING] Protection breach pending -> acc={acc.Name} instr={instr.FullName} " +
                        $"state={rt.State} reason={rt.LastReason}");

                    Log($"[PROTECTION BREACH PENDING] acc={acc.Name} instr={instr.FullName} reason={rt.LastReason}");
                    return;
                }

                var elapsedMs = rt.BreachFirstDetectedUtc.HasValue
                    ? (now - rt.BreachFirstDetectedUtc.Value).TotalMilliseconds
                    : 0;

                if (elapsedMs < ProtectionBreachConfirmMs)
                    return;

                Log($"[PROTECTION BREACH CONFIRMED] acc={acc.Name} instr={instr.FullName} reason={rt.LastReason}");

                TriggerRiskProtectionFlatten(
                    acc,
                    instr,
                    $"Protection breach confirmed. {rt.LastReason}");
            }
            
            private void AuditProtectionStates()
            {
                var targets = new List<(Account Acc, Instrument Instr)>();

                lock (_gate)
                {
                    if (_master != null)
                    {
                        foreach (var instr in CollectActiveInstruments(_master))
                        {
                            if (instr != null)
                                targets.Add((_master, instr));
                        }
                    }

                    foreach (var f in _followers)
                    {
                        if (f == null)
                            continue;

                        foreach (var instr in CollectActiveInstruments(f))
                        {
                            if (instr != null)
                                targets.Add((f, instr));
                        }
                    }
                }

                foreach (var t in targets
                             .GroupBy(x => $"{x.Acc?.Name}|{x.Instr?.FullName}", StringComparer.Ordinal)
                             .Select(g => g.First()))
                {
                    try
                    {
                        ConfirmProtectionBreachIfNeeded(t.Acc, t.Instr);
                    }
                    catch (Exception ex)
                    {
                        Log($"[PROTECTION AUDIT ERROR] acc={t.Acc?.Name} instr={t.Instr?.FullName} msg={ex.Message}");
                    }
                }
            }
        }
    }
}