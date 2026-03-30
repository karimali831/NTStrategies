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

                var newStopPrice = 0.0;
                var newTargetPrice = 0.0;

                if (spec.StopTicks > 0)
                {
                    newStopPrice = spec.IsBuy
                        ? avgEntryPrice - spec.StopTicks * tickSize
                        : avgEntryPrice + spec.StopTicks * tickSize;

                    newStopPrice = RoundToTick(newStopPrice, tickSize);
                }

                if (spec.TargetTicks > 0)
                {
                    newTargetPrice = spec.IsBuy
                        ? avgEntryPrice + spec.TargetTicks * tickSize
                        : avgEntryPrice - spec.TargetTicks * tickSize;

                    newTargetPrice = RoundToTick(newTargetPrice, tickSize);
                }

                Log(
                    $"[BRACKET RESIZE PATH] acc={acc.Name} instr={instr.FullName} " +
                    $"currentSpecQty={spec.Qty} newQty={newQty} currentEntry={spec.EntryPrice:0.00} " +
                    $"newEntry={avgEntryPrice:0.00} oco={spec.StopOco}");

                var stop = FindWorkingManagedStop(acc, instr, spec);
                var target = FindWorkingManagedTarget(acc, instr, spec);

                Log(
                    $"[BRACKET RESIZE PATH] acc={acc.Name} instr={instr.FullName} " +
                    $"stopFound={(stop != null)} targetFound={(target != null)} " +
                    $"newStop={newStopPrice:0.00} newTarget={newTargetPrice:0.00}");

                // If we cannot find the live managed orders, do NOT leave a stale partial bracket in place.
                // Rebuild a fresh protective bracket for the full live quantity.
                if ((spec.StopTicks > 0 && stop == null) || (spec.TargetTicks > 0 && target == null))
                {
                    Log(
                        $"[BRACKET RESIZE FAILURE] acc={acc.Name} instr={instr.FullName} " +
                        $"reason=missing-live-orders qty={newQty} avgEntry={avgEntryPrice:0.00}");

                    DumpLiveOrders(acc, instr, spec, newQty);

                    // DO NOT rebuild here — let watchdog handle safety
                    return;
                }

                var changes = new List<Order>();

                if (stop != null && spec.StopTicks > 0)
                {
                    stop.QuantityChanged = newQty;
                    stop.StopPriceChanged = newStopPrice;
                    changes.Add(stop);
                }

                if (target != null && spec.TargetTicks > 0)
                {
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
                        x.OriginalStopPrice = newStopPrice;
                        x.CurrentStopPrice = newStopPrice;
                    }

                    if (spec.TargetTicks > 0)
                        x.TargetPrice = newTargetPrice;
                });

                Log(
                    $"[BRACKET RESIZE] acc={acc.Name} instr={instr.FullName} qty={newQty} " +
                    $"avgEntry={avgEntryPrice:0.00} stop={newStopPrice:0.00} target={newTargetPrice:0.00}");
            }
        
            private void DumpLiveOrders(
                Account acc,
                Instrument instr,
                ActiveBracketSpec spec,
                int expectedQty)
            {
                try
                {
                    var orders = acc.Orders
                        .Where(o => o?.Instrument != null &&
                                    IsSameInstrument(o.Instrument, instr))
                        .ToList();

                    Log($"[ORDER DUMP] acc={acc.Name} instr={instr.FullName} expectedQty={expectedQty} specOco={spec?.StopOco}");

                    foreach (var o in orders)
                    {
                        Log(
                            $"[ORDER] name={o.Name} state={o.OrderState} qty={o.Quantity} " +
                            $"oco={o.Oco} limit={o.LimitPrice:0.00} stop={o.StopPrice:0.00}");
                    }

                    if (TryGetLivePosition(acc, instr, out var mp, out var liveQty))
                    {
                        Log($"[POSITION] mp={mp} qty={liveQty}");
                    }
                    else
                    {
                        Log("[POSITION] none");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ORDER DUMP FAILED] {ex.Message}");
                }
            }

            private void ClearActiveBracket(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return;

                var key = BracketKey(acc, instr);

                lock (_gate)
                {
                    _activeBracketByAccInstr.Remove(key);
                    _lastHasWorkingState.Remove(key);
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

                var hasLivePosition = TryGetLivePosition(acc, instr, out var mp, out var absQty);
                var net = GetNetPosition(acc, instr);
                var hasWorkingBracket = HasWorkingBracketOrders(acc, instr);

                SafeTradeSuiteRuntime.PrintLog(
                    $"[BRACKET EXIT CHECK] acc={acc.Name} instr={instr.FullName} " +
                    $"orderName={name} mp={mp} absQty={absQty} net={net} hasLivePosition={hasLivePosition} " +
                    $"hasWorkingBracket={hasWorkingBracket}");

                if (!hasLivePosition || absQty <= 0 || net == 0)
                {
                    ClearActiveBracket(acc, instr);
                    Log($"[BRACKET CLEAR] acc={acc.Name} instr={instr.FullName} orderName={name} net={net} hasWorkingBracket={hasWorkingBracket}");
                    return;
                }

                if (hasWorkingBracket)
                {
                    Log($"[BRACKET KEEP] acc={acc.Name} instr={instr.FullName} orderName={name} net={net} hasWorkingBracket={hasWorkingBracket}");
                    return;
                }

                Log($"[BRACKET EXIT IN PROGRESS] acc={acc.Name} instr={instr.FullName} orderName={name} net={net} hasWorkingBracket={hasWorkingBracket}");
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
            
            private static double RoundToTick(double price, double tickSize)
            {
                if (tickSize <= 0)
                    return price;

                return Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
            }
        }
    }
}