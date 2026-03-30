using System;
using System.Collections.Generic;
using System.Linq;
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
            
            private bool HasSelectedBracket(Account acc)
            {
                if (acc == null)
                    return false;

                if (_master != null && ReferenceEquals(acc, _master))
                {
                    var atm = (_configuredMasterBracket ?? "").Trim();
                    return !string.IsNullOrWhiteSpace(atm) &&
                           !string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase);
                }

                var followerAtm = ResolveFollowerBracket(acc);
                return !string.IsNullOrWhiteSpace(followerAtm) &&
                       !string.Equals(followerAtm, "None", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(followerAtm, "Follow Master Exit", StringComparison.OrdinalIgnoreCase);
            }
            
            private void HandleBracketExitOutcome(Account acc, Execution execution)
            {
                if (acc == null || execution?.Order == null)
                    return;

                var ord = execution.Order;
                var instr = ord.Instrument;
                if (instr == null)
                    return;

                var name = (ord.Name ?? "").Trim();

                var isKnownExit =
                    name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase);

                if (!isKnownExit)
                    return;

                var isFlat = GetNetPosition(acc, instr) == 0;
                var hasWorkingBracket = HasWorkingBracketOrders(acc, instr);

                if (isFlat || !hasWorkingBracket)
                {
                    ClearActiveBracket(acc, instr);
                    Log($"[BRACKET CLEAR] acc={acc.Name} instr={instr.FullName} isFlat={isFlat} hasWorkingBracket={hasWorkingBracket}");
                    return;
                }

                Log($"[BRACKET KEEP] acc={acc.Name} instr={instr.FullName} isFlat={isFlat} hasWorkingBracket={hasWorkingBracket}");
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
            
            private string ResolveFollowerBracket(Account follower)
            {
                if (follower == null)
                    return _configuredMasterBracket ?? "None";

                if (_configuredFollowerAtmOverrides != null &&
                    _configuredFollowerAtmOverrides.TryGetValue(follower.Name, out var a) &&
                    !string.IsNullOrWhiteSpace(a))
                {
                    a = a.Trim();

                    if (string.Equals(a, "Inherit Master", StringComparison.OrdinalIgnoreCase))
                        return _configuredMasterBracket ?? "None";

                    if (string.Equals(a, "Follow Master Exit", StringComparison.OrdinalIgnoreCase))
                        return "Follow Master Exit";

                    return a;
                }

                return _configuredMasterBracket ?? "None";
            }
            
            private Order FindWorkingManagedTarget(Account acc, Instrument instr, ActiveBracketSpec spec)
            {
                if (acc == null || instr == null || spec == null)
                    return null;

                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        var isWorking =
                            o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted;

                        if (!isWorking)
                            continue;

                        var name = (o.Name ?? "").Trim();
                        if (!name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!string.IsNullOrWhiteSpace(spec.StopOco))
                        {
                            var oco = (o.Oco ?? "").Trim();
                            if (string.Equals(oco, spec.StopOco, StringComparison.Ordinal))
                                return o;
                        }
                    }

                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        var isWorking =
                            o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted;

                        if (!isWorking)
                            continue;

                        var name = (o.Name ?? "").Trim();
                        if (name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase))
                            return o;
                    }
                }
                catch
                {
                    return null;
                }

                return null;
            }

            private void ResizeAndRepriceWorkingBracket(
                Account acc,
                Instrument instr,
                ActiveBracketSpec spec,
                int newQty,
                double avgEntryPrice)
            {
                if (acc == null || instr == null || spec == null)
                    return;

                var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;
                if (tickSize <= 0)
                    return;

                var stop = FindWorkingManagedStop(acc, instr, spec);
                var target = FindWorkingManagedTarget(acc, instr, spec);

                var changes = new System.Collections.Generic.List<Order>();

                if (stop != null && spec.StopTicks > 0)
                {
                    var newStopPrice = spec.IsBuy
                        ? avgEntryPrice - spec.StopTicks * tickSize
                        : avgEntryPrice + spec.StopTicks * tickSize;

                    newStopPrice = RoundToTick(newStopPrice, tickSize);

                    stop.QuantityChanged = newQty;
                    stop.StopPriceChanged = newStopPrice;
                    changes.Add(stop);
                }

                if (target != null && spec.TargetTicks > 0)
                {
                    var newTargetPrice = spec.IsBuy
                        ? avgEntryPrice + spec.TargetTicks * tickSize
                        : avgEntryPrice - spec.TargetTicks * tickSize;

                    newTargetPrice = RoundToTick(newTargetPrice, tickSize);

                    target.QuantityChanged = newQty;
                    target.LimitPriceChanged = newTargetPrice;
                    changes.Add(target);
                }

                if (changes.Count == 0)
                    return;

                acc.Change(changes.ToArray());

                UpdateActiveBracketSpec(acc, instr, x =>
                {
                    x.Qty = newQty;
                    x.EntryFilledQty = newQty;
                    x.EntryValueSum = avgEntryPrice * newQty;
                    x.EntryPrice = avgEntryPrice;

                    if (spec.StopTicks > 0)
                    {
                        x.OriginalStopPrice = spec.IsBuy
                            ? RoundToTick(avgEntryPrice - spec.StopTicks * tickSize, tickSize)
                            : RoundToTick(avgEntryPrice + spec.StopTicks * tickSize, tickSize);

                        x.CurrentStopPrice = x.OriginalStopPrice;
                    }

                    if (spec.TargetTicks > 0)
                    {
                        x.TargetPrice = spec.IsBuy
                            ? RoundToTick(avgEntryPrice + spec.TargetTicks * tickSize, tickSize)
                            : RoundToTick(avgEntryPrice - spec.TargetTicks * tickSize, tickSize);
                    }
                });

                Log(
                    $"[BRACKET RESIZE] acc={acc.Name} instr={instr.FullName} qty={newQty} avgEntry={avgEntryPrice:0.00}");
            }
            
            private static double RoundToTick(double price, double tickSize)
            {
                if (tickSize <= 0)
                    return price;

                return Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
            }
        }
    }
}