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
            public void SubmitMasterMarketWithBracket(
                Account master,
                Instrument instr,
                OrderAction action,
                int qty,
                string atmTemplateName,
                string entryName)
            {
                if (master == null || instr == null)
                {
                    Log("SubmitMasterMarketWithBracket: missing master/instrument.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(entryName))
                    entryName = "STC:ENTRY:" + Guid.NewGuid().ToString("N");

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

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
                        IsBuy = action == OrderAction.Buy,
                        StopTicks = Math.Max(0, stopTicks),
                        TargetTicks = Math.Max(0, targetTicks)
                    };

                    SafeTradeSuiteRuntime.PrintLog(
                        $"[MASTER BRACKET PENDING ADD] entry={entryName} master={master.Name} instr={instr.FullName} qty={qty} atm={atmTemplateName} stopTicks={stopTicks} targetTicks={targetTicks}");
                }

                Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                master.Submit(new[] { entry });
            }

            private async Task CopyToFollowers(string execId, OrderAction action, int masterExecQty, CancellationToken token)
            {
                SeenCleanup();

                List<Account> followerSnap;
                Account masterSnap;
                Instrument instrSnap;
                FollowerGuard guard;

                lock (_gate)
                {
                    followerSnap = _followers.ToList();
                    masterSnap = _master;
                    instrSnap = _instrument;
                    guard = _followerGuard ?? new FollowerGuard();
                }

                foreach (var f in followerSnap)
                {
                    if (token.IsCancellationRequested)
                        return;

                    if (f == null)
                        continue;

                    if (masterSnap != null && ReferenceEquals(f, masterSnap))
                        continue;

                    if (instrSnap == null)
                        return;

                    if (f.ConnectionStatus != ConnectionStatus.Connected)
                    {
                        lock (_gate)
                        {
                            _isRequested = false;
                            DisarmUnsafe_NoLock($"Follower {f.Name} not Connected");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: $"Follower {f.Name} disconnected");
                        }
                        return;
                    }

                    var seenKey = $"{execId}|{f.Name}|{instrSnap.FullName}";
                    if (!_seen.TryAdd(seenKey, DateTime.UtcNow.Ticks))
                        continue;

                    if (!CanEnterForRisk(f, out _, out var longReason))
                    {
                        Log($"Copy skipped -> {f.Name}: {longReason}");
                        continue;
                    }

                    var qtyToCopy = ResolveFollowerQty(f, masterExecQty);
                    if (qtyToCopy < 1)
                    {
                        Log($"Copy skipped -> {f.Name}: invalid follower qty ({qtyToCopy}). Must be >= 1.");
                        continue;
                    }

                    if (qtyToCopy > _maxAbsQtyPerFollower)
                    {
                        Log($"Copy skipped -> {f.Name}: follower qty ({qtyToCopy}) exceeds max allowed ({_maxAbsQtyPerFollower}).");
                        continue;
                    }

                    if (IsFollowerGuardDisabled(f))
                    {
                        Log($"Copy skipped -> {f.Name}: follower disabled by guard.");
                        continue;
                    }

                    if (HasAnyOpenFollowerTradeState(f, instrSnap, out var openStateReason))
                    {
                        Log($"[FOLLOWER COPY GATE] skip acc={f.Name} instr={instrSnap.FullName} reason={openStateReason}");
                        continue;
                    }

                    Log($"[FOLLOWER COPY GATE] allow acc={f.Name} instr={instrSnap.FullName} qty={qtyToCopy}");

                    var bracketMode = ResolveFollowerBracket(f);
                    var followMasterExit = FollowerUsesMasterExit(f);
                    var hasOwnBracket =
                        !string.IsNullOrWhiteSpace(bracketMode) &&
                        !string.Equals(bracketMode, "None", StringComparison.OrdinalIgnoreCase) &&
                        !followMasterExit;

                    Log(
                        $"Copy -> {f.Name}: action={action}, qty={qtyToCopy}, instr={instrSnap.FullName}, " +
                        $"mode={(followMasterExit ? "FOLLOW_MASTER_EXIT" : hasOwnBracket ? $"OWN_BRACKET:{bracketMode}" : "ENTRY_ONLY")}");

                    try
                    {
                        var entryName = $"STC:ENTRY:{execId}:{f.Name}";
                        if (hasOwnBracket)
                        {
                            MarkFollowerEntrySubmitted(f, entryName);
                            SubmitFollowerMarketWithBracket(f, instrSnap, action, qtyToCopy, bracketMode, entryName);
                        }
                        else
                        {
                            MarkFollowerEntrySubmitted(f, entryName);

                            var ord = f.CreateOrder(
                                instrSnap,
                                action,
                                OrderType.Market,
                                OrderEntry.Manual,
                                TimeInForce.Day,
                                qtyToCopy,
                                0,
                                0,
                                string.Empty,
                                entryName,
                                DateTime.MaxValue,
                                null
                            );

                            f.Submit(new[] { ord });
                        }
                    }
                    catch (Exception ex)
                    {
                        MarkFollowerEntryResolved(f);

                        ApplyGuardAction(
                            f,
                            guard.OnEntryReject,
                            $"Follower entry submit failed: {ex.Message}");

                        continue;
                    }

                    RecordCopy();

                    if (StaggerMsPerFollower > 0)
                        await Task.Delay(StaggerMsPerFollower, token).ConfigureAwait(false);
                }
            }

            private void TrySubmitBracketOnFill(Account master, Execution execution)
            {
                try
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[TRY SUBMIT BRACKET ON FILL] acc={master?.Name} orderName={execution?.Order?.Name} instr={execution?.Order?.Instrument?.FullName} price={execution?.Price}");

                    if (master == null || execution == null)
                        return;

                    var ord = execution.Order;
                    if (ord == null)
                        return;

                    var name = ord.Name ?? "";
                    if (string.IsNullOrWhiteSpace(name))
                        return;

                    PendingBracket pb;
                    lock (_gate)
                    {
                        if (!_pendingBrackets.TryGetValue(name, out pb))
                        {
                            Log($"[BRACKET MISS] No pending bracket found for entry '{name}'");
                            return;
                        }

                        SafeTradeSuiteRuntime.PrintLog(
                            $"[BRACKET PENDING FOUND] entry={name} qty={pb.Qty} isBuy={pb.IsBuy} stopTicks={pb.StopTicks} targetTicks={pb.TargetTicks}");
                    }

                    if (pb.StopTicks <= 0 && pb.TargetTicks <= 0)
                    {
                        RemovePendingBracketForEntry(name);
                        return;
                    }

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

                    var currentStopPrice = 0.0;
                    var targetPrice = 0.0;
                    string stopOrderName = null;
                    string targetOrderName = null;

                    if (pb.TargetTicks > 0)
                    {
                        targetPrice = pb.IsBuy
                            ? fillPrice + pb.TargetTicks * tickSize
                            : fillPrice - pb.TargetTicks * tickSize;

                        var tgt = master.CreateOrder(
                            instr,
                            exitAction,
                            OrderType.Limit,
                            OrderEntry.Manual,
                            TimeInForce.Day,
                            pb.Qty,
                            targetPrice,
                            0,
                            oco,
                            "STC:TP",
                            DateTime.MaxValue,
                            null
                        );

                        targetOrderName = "STC:TP";
                        orders.Add(tgt);
                    }

                    if (pb.StopTicks > 0)
                    {
                        currentStopPrice = pb.IsBuy
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
                            currentStopPrice,
                            oco,
                            "STC:SL",
                            DateTime.MaxValue,
                            null
                        );

                        stopOrderName = "STC:SL";
                        orders.Add(stp);
                    }

                    if (orders.Count == 0)
                    {
                        RemovePendingBracketForEntry(name);
                        return;
                    }

                    lock (_gate)
                    {
                        _activeBracketByAccInstr[BracketKey(master, instr)] =
                            new ActiveBracketSpec
                            {
                                AutoBeSuppressedUntilFlat = false,
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

                    SafeTradeSuiteRuntime.PrintLog(
                        $"[BRACKET SUBMIT] acc={master.Name} instr={instr.FullName} fill={fillPrice:0.00} oco={oco} stopPrice={currentStopPrice:0.00} targetPrice={targetPrice:0.00} qty={pb.Qty}");

                    try
                    {
                        master.Submit(orders.ToArray());
                        RemovePendingBracketForEntry(name);
                        Log($"Bracket submitted -> {master.Name} {instr.FullName} OCO={oco} (SL={pb.StopTicks}t TP={pb.TargetTicks}t @ fill={fillPrice:0.00})");
                    }
                    catch
                    {
                        ClearActiveBracket(master, instr);
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    LogUnhandled("TrySubmitBracketOnFill", ex);
                    throw;
                }
            }

            private void SubmitFollowerMarketWithBracket(Account acc, Instrument instr, OrderAction action, int qty, string atmTemplateName, string entryName)
            {
                if (acc == null || instr == null)
                    return;

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"Follower ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

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
                        IsBuy = action == OrderAction.Buy,
                        StopTicks = Math.Max(0, stopTicks),
                        TargetTicks = Math.Max(0, targetTicks)
                    };
                }

                Log($"Follower submit -> {acc.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                acc.Submit(new[] { entry });
            }
        }
    }
}