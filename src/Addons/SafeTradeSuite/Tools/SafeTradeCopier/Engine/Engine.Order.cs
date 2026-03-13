using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            public void SubmitMasterMarketWithBracket(Account master, Instrument instr, OrderAction action, int qty, string atmTemplateName)
            {
                if (master == null || instr == null)
                {
                    Log("SubmitMasterMarketWithBracket: missing master/instrument.");
                    return;
                }

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

                var entryName = "STC:ENTRY:" + Guid.NewGuid().ToString("N");
                var entry = master.CreateOrder(
                    instr,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    0,
                    string.Empty,
                    entryName,
                    DateTime.MaxValue,
                    null
                );

                lock (_gate)
                {
                    _pendingBrackets[entryName] = new PendingBracket
                    {
                        EntryName = entryName,
                        Qty = qty,
                        IsBuy = (action == OrderAction.Buy),
                        StopTicks = Math.Max(0, stopTicks),
                        TargetTicks = Math.Max(0, targetTicks)
                    };
                }

                Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                master.Submit(new[] { entry });
            }
            
            private async Task CopyToFollowers(string execId, OrderAction action, int masterExecQty, CancellationToken token)
            {
                if (_seen.Count > 5000)
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-30).Ticks;
                    foreach (var kv in _seen.ToArray())
                    {
                        if (kv.Value < cutoff)
                            _seen.TryRemove(kv.Key, out _);
                    }
                }

                List<Account> followerSnap;
                Account masterSnap;
                Instrument instrSnap;

                lock (_gate)
                {
                    followerSnap = _followers.ToList();
                    masterSnap = _master;
                    instrSnap = _instrument;
                }

                foreach (var f in followerSnap)
                {
                    if (token.IsCancellationRequested) return;
                    if (f == null) continue;
                    if (masterSnap != null && ReferenceEquals(f, masterSnap)) continue;
                    if (instrSnap == null) return;

                    if (f.ConnectionStatus != ConnectionStatus.Connected)
                    {
                        lock (_gate)
                        {
                            _copyEnabled = false;
                            DisarmUnsafe_NoLock($"Follower {f.Name} not Connected");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: $"Follower {f.Name} disconnected");
                        }
                        return;
                    }

                    var key = $"{execId}|{f.Name}|{instrSnap.FullName}";
                    if (!_seen.TryAdd(key, DateTime.UtcNow.Ticks))
                        continue;

                    var qty = ResolveFollowerQty(f, masterExecQty);
                    if (qty <= 0 || qty > MaxAbsQtyPerFollower)
                    {
                        // Safety: cap follower qty by max-per-follower
                        qty = Math.Min(Math.Max(qty, 1), MaxAbsQtyPerFollower);
                    }

                    var bracketMode = ResolveFollowerAtm(f);
                    var followMasterExit = FollowerUsesMasterExit(f);
                    var hasOwnBracket =
                        !string.IsNullOrWhiteSpace(bracketMode) &&
                        !string.Equals(bracketMode, "None", StringComparison.OrdinalIgnoreCase) &&
                        !followMasterExit;

                    Log(
                        $"Copy -> {f.Name}: action={action}, qty={qty}, instr={instrSnap.FullName}, " +
                        $"mode={(followMasterExit ? "FOLLOW_MASTER_EXIT" : hasOwnBracket ? $"OWN_BRACKET:{bracketMode}" : "ENTRY_ONLY")}");

                    // follower has its own bracket
                    if (hasOwnBracket)
                    {
                        SubmitFollowerMarketWithBracket(f, instrSnap, action, qty, bracketMode, execId);
                    }
                    // follower entry only, exits when master exits
                    else if (followMasterExit)
                    {
                        var ord = f.CreateOrder(
                            instrSnap,
                            action,
                            OrderType.Market,
                            OrderEntry.Manual,
                            TimeInForce.Day,
                            qty,
                            0,
                            0,
                            string.Empty,
                            $"STC:ENTRY:{execId}",
                            DateTime.MaxValue,
                            null
                        );

                        f.Submit(new[] { ord });
                    }
                    // entry only, no follower bracket
                    else
                    {
                        var ord = f.CreateOrder(
                            instrSnap,
                            action,
                            OrderType.Market,
                            OrderEntry.Manual,
                            TimeInForce.Day,
                            qty,
                            0,
                            0,
                            string.Empty,
                            $"STC:ENTRY:{execId}",
                            DateTime.MaxValue,
                            null
                        );

                        f.Submit(new[] { ord });
                    }

                    RecordCopy();

                    if (StaggerMsPerFollower > 0)
                        await Task.Delay(StaggerMsPerFollower, token).ConfigureAwait(false);
                }
            }
            
            private bool FollowerUsesMasterExit(Account follower)
            {
                if (follower == null)
                    return false;

                if (_configuredFollowerAtmOverrides != null &&
                    _configuredFollowerAtmOverrides.TryGetValue(follower.Name, out var a))
                {
                    return string.Equals(
                        (a ?? "").Trim(),
                        "(follow master exit)",
                        StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }
            
            private void TrySubmitBracketOnFill(Account master, Execution execution)
            {
                if (master == null || execution == null) return;

                var ord = execution.Order;
                if (ord == null) return;

                var name = ord.Name ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                PendingBracket pb;
                lock (_gate)
                {
                    if (!_pendingBrackets.TryGetValue(name, out pb))
                        return;

                    // only submit once (first fill)
                    _pendingBrackets.Remove(name);
                }

                if (pb.StopTicks <= 0 && pb.TargetTicks <= 0)
                    return;

                var fillPrice = execution.Price;
                if (fillPrice <= 0)
                {
                    Log($"Bracket skipped: invalid fill price for {name}.");
                    return;
                }

                var instr = ord.Instrument;
                if (instr == null)
                {
                    Log($"Bracket skipped: missing instrument for {name}.");
                    return;
                }

                var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;
                if (tickSize <= 0)
                {
                    Log($"Bracket skipped: invalid TickSize for {instr.FullName}.");
                    return;
                }

                var oco = "STC:BRK:" + Guid.NewGuid().ToString("N");
                var exitAction = pb.IsBuy ? OrderAction.Sell : OrderAction.BuyToCover;
                
                var orders = new List<Order>(2);

                if (pb.TargetTicks > 0)
                {
                    var tgtPrice = pb.IsBuy
                        ? fillPrice + pb.TargetTicks * tickSize
                        : fillPrice - pb.TargetTicks * tickSize;

                    var tgt = master.CreateOrder(
                        instr,
                        exitAction,
                        OrderType.Limit,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        pb.Qty,
                        tgtPrice,
                        0,
                        oco,
                        "STC:TP",
                        DateTime.MaxValue,
                        null
                    );

                    orders.Add(tgt);
                }

                if (pb.StopTicks > 0)
                {
                    var stpPrice = pb.IsBuy
                        ? fillPrice - pb.StopTicks * tickSize
                        : fillPrice + pb.StopTicks * tickSize;

                    var stp = master.CreateOrder(
                        instr,
                        exitAction,
                        OrderType.StopMarket,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        pb.Qty,
                        0,
                        stpPrice,
                        oco,
                        "STC:SL",
                        DateTime.MaxValue,
                        null
                    );

                    orders.Add(stp);
                }

                if (orders.Count > 0)
                {
                    var currentStopPrice = 0.0;
                    var targetPrice = 0.0;
                    string stopOrderName = null;
                    string targetOrderName = null;

                    if (pb.TargetTicks > 0)
                    {
                        targetPrice = pb.IsBuy
                            ? fillPrice + pb.TargetTicks * tickSize
                            : fillPrice - pb.TargetTicks * tickSize;

                        targetOrderName = "STC:TP";
                    }

                    if (pb.StopTicks > 0)
                    {
                        currentStopPrice = pb.IsBuy
                            ? fillPrice - pb.StopTicks * tickSize
                            : fillPrice + pb.StopTicks * tickSize;

                        stopOrderName = "STC:SL";
                    }

                    lock (_gate)
                    {
                        _activeBracketByAccInstr[BracketKey(master, instr)] =
                            new ActiveBracketSpec
                            {
                                StopTicks = pb.StopTicks,
                                TargetTicks = pb.TargetTicks,
                                IsBuy = pb.IsBuy,
                                Qty = pb.Qty,
                                EntryPrice = fillPrice,
                                OriginalStopPrice = currentStopPrice,
                                CurrentStopPrice = currentStopPrice,
                                TargetPrice = targetPrice,
                                IsFreeTradeApplied = false,
                                StopOrderName = stopOrderName,
                                TargetOrderName = targetOrderName,
                                StopOco = oco
                            };
                    }
                    
                    master.Submit(orders.ToArray());
                    Log($"Bracket submitted -> {master.Name} {instr.FullName} OCO={oco} (SL={pb.StopTicks}t TP={pb.TargetTicks}t @ fill={fillPrice:0.00})");
                }
            }
            
            private void SubmitFollowerMarketWithBracket(Account acc, Instrument instr, OrderAction action, int qty, string atmTemplateName, string execId)
            {
                if (acc == null || instr == null) return;

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"Follower ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

                // Unique entry name that we can match on ExecutionUpdate
                var entryName = $"STC:ENTRY:{execId}:{acc.Name}";

                var entry = acc.CreateOrder(
                    instr,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    0,
                    string.Empty,
                    entryName,
                    DateTime.MaxValue,
                    null
                );

                lock (_gate)
                {
                    _pendingBrackets[entryName] = new PendingBracket
                    {
                        EntryName = entryName,
                        Qty = qty,
                        IsBuy = (action == OrderAction.Buy),
                        StopTicks = Math.Max(0, stopTicks),
                        TargetTicks = Math.Max(0, targetTicks)
                    };
                }

                Log($"Follower submit -> {acc.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                acc.Submit(new[] { entry });
            }
            
            private void OnFollowerOrderUpdate(object sender, OrderEventArgs e)
            {
                if (e?.Order == null) return;

                if (string.IsNullOrWhiteSpace(e.Order.Name) || !e.Order.Name.StartsWith("STC:", StringComparison.Ordinal))
                    return;

                SyncBracketFromOrderUpdate(e.Order);

                if (!Armed) return;

                if (e.Order.OrderState == OrderState.Rejected)
                {
                    var msg =
                        $"Error={e.Error} " +
                        $"State={e.Order.OrderState} " +
                        $"Action={e.Order.OrderAction} " +
                        $"Qty={e.Order.Quantity} " +
                        $"Name={e.Order.Name}";

                    lock (_gate)
                    {
                        _copyEnabled = false;
                        DisarmUnsafe_NoLock($"Circuit breaker: copied order REJECTED on {e.Order.Account?.Name}. Msg={msg}");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: "Order rejected");
                    }
                }
            }
            
            
            private int ResolveFollowerQty(Account follower, int masterExecQty)
            {
                if (follower == null) return masterExecQty;

                if (_configuredFollowerQtyOverrides != null &&
                    _configuredFollowerQtyOverrides.TryGetValue(follower.Name, out var q) &&
                    q > 0)
                    return q;

                // inherit master execution qty (most consistent for strategy/manual)
                return masterExecQty;
            }
        }
    }
}