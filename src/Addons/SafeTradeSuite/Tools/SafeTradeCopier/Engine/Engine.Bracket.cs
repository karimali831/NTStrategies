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
            
            
            internal void RehydrateActiveBracketsFromLiveOrders()
            {
                try
                {
                    var accounts = Account.All;

                    if (accounts == null || accounts.Count == 0)
                    {
                        Log("[BRACKET REHYDRATE] no accounts found");
                        return;
                    }

                    lock (_gate)
                        _activeBracketByAccInstr.Clear();

                    foreach (var acc in accounts)
                    {
                        if (acc == null)
                            continue;

                        try
                        {
                            // --- scan positions ---
                            foreach (var pos in acc.Positions)
                            {
                                if (pos?.Instrument == null)
                                    continue;

                                var instr = pos.Instrument;
                                var net = pos.Quantity;

                                if (net == 0)
                                    continue;

                                Order stop = null;
                                Order target = null;

                                // --- scan orders for this instrument ---
                                foreach (var o in acc.Orders)
                                {
                                    if (o?.Instrument == null)
                                        continue;

                                    if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                                        continue;

                                    var isLive =
                                        o.OrderState == OrderState.Working ||
                                        o.OrderState == OrderState.Accepted ||
                                        o.OrderState == OrderState.Submitted ||
                                        o.OrderState == OrderState.PartFilled;

                                    if (!isLive)
                                        continue;

                                    var name = (o.Name ?? "").Trim();

                                    if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                                        name.Equals("Stop1", StringComparison.OrdinalIgnoreCase))
                                    {
                                        stop = o;
                                    }
                                    else if (name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                                             name.Equals("Target1", StringComparison.OrdinalIgnoreCase))
                                    {
                                        target = o;
                                    }
                                }

                                if (stop == null && target == null)
                                    continue;

                                var entryPrice = pos.AveragePrice;
                                if (entryPrice <= 0)
                                    continue;

                                var stopPrice = stop?.StopPrice ?? 0.0;
                                var targetPrice = target?.LimitPrice ?? 0.0;
                                var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;

                                var stopTicks = 0;
                                var targetTicks = 0;

                                if (tickSize > 0 && stopPrice > 0)
                                    stopTicks = (int)Math.Round(Math.Abs(entryPrice - stopPrice) / tickSize, MidpointRounding.AwayFromZero);

                                if (tickSize > 0 && targetPrice > 0)
                                    targetTicks = (int)Math.Round(Math.Abs(targetPrice - entryPrice) / tickSize, MidpointRounding.AwayFromZero);

                                var isBuy = net > 0;
                                var qty = Math.Abs(net);

                                lock (_gate)
                                {
                                    _activeBracketByAccInstr[BracketKey(acc, instr)] = new ActiveBracketSpec
                                    {
                                        StopTicks = stopTicks,
                                        TargetTicks = targetTicks,
                                        IsBuy = isBuy,
                                        Qty = qty,
                                        EntryPrice = entryPrice,
                                        OriginalStopPrice = stopPrice,
                                        CurrentStopPrice = stopPrice,
                                        TargetPrice = targetPrice,
                                        IsFreeTradeApplied = stopPrice > 0 &&
                                                             ((isBuy && stopPrice >= entryPrice) ||
                                                              (!isBuy && stopPrice <= entryPrice)),
                                        StopOrderName = stop?.Name,
                                        TargetOrderName = target?.Name,
                                        StopOco = stop?.Oco ?? target?.Oco
                                    };
                                }

                                Log($"[BRACKET REHYDRATE] acc={acc.Name} instr={instr.FullName} qty={qty} entry={entryPrice:0.00}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"[BRACKET REHYDRATE FAILED] acc={acc?.Name} msg={ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[BRACKET REHYDRATE FATAL] msg={ex.Message}");
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

                    if (string.Equals(a, "Inherit Master", StringComparison.OrdinalIgnoreCase))
                        return _configuredMasterAtm ?? "None";

                    if (string.Equals(a, "Follow Master Exit", StringComparison.OrdinalIgnoreCase))
                        return "Follow Master Exit";

                    return a;
                }

                return _configuredMasterAtm ?? "None";
            }
        }
    }
}