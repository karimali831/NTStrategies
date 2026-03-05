using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        public partial class SafeCopierEngine : IDisposable
        {
            private void OnMasterExecution(object sender, ExecutionEventArgs e)
            {
                if (e?.Execution == null) return;
                if (_master == null || e.Execution.Account != _master) return;
                if (_instrument == null) return;

                if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != _instrument.FullName)
                    return;

                // ✅ Always try to submit bracket (even if not armed / no followers)
                TrySubmitBracketOnFill(_master, e.Execution);

                // ✅ Only COPY on *entry* executions created by STC
                if (!IsStcEntryExecution(e.Execution.Order))
                    return;

                // Copy requires normal arming/safety
                if (!_armed || !_copyEnabled) return;

                var execId = e.Execution.ExecutionId ?? "";
                if (string.IsNullOrWhiteSpace(execId))
                    execId = $"{e.Execution.Time.Ticks}_{e.Execution.Price}_{e.Execution.Quantity}_{e.Execution.MarketPosition}";

                if (!AllowCopyNow())
                {
                    lock (_gate)
                    {
                        _copyEnabled = false;
                        DisarmUnsafe_NoLock("Circuit breaker: too many copies in short window");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: "Circuit breaker tripped");
                    }
                    return;
                }

                var masterExecQty = (int)Math.Round((double)e.Execution.Quantity, MidpointRounding.AwayFromZero);
                masterExecQty = Math.Abs(masterExecQty);
                if (masterExecQty <= 0) return;

                var masterAction = e.Execution.Order?.OrderAction ?? OrderAction.Buy;
                var followerAction = masterAction;

                CancellationToken token;
                lock (_gate)
                {
                    token = _cts.Token;
                }

                Task.Run(async () =>
                {
                    await _submitLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await CopyToFollowers(execId, followerAction, masterExecQty, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _submitLock.Release();
                    }
                }, token);
            }

            private static bool IsStcEntryExecution(Order ord)
            {
                if (ord == null) return false;

                var name = (ord.Name ?? "").Trim();
                var fromSignal = (ord.FromEntrySignal ?? "").Trim();

                // ✅ Only treat STC entries as copy-eligible
                if (name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase)) return true;
                if (fromSignal.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            
            private void OnFollowerExecution(object sender, ExecutionEventArgs e)
            {
                if (!_armed) return;
                if (e?.Execution == null) return;

                // Only manage brackets for the instrument we’re operating on
                if (_instrument == null) return;
                if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != _instrument.FullName)
                    return;

                var acc = e.Execution.Account;
                if (acc == null) return;

                // Brackets only submit if there's a pending entry name for this fill.
                TrySubmitBracketOnFill(acc, e.Execution);
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

                    var atm = ResolveFollowerAtm(f);
                    if (!string.IsNullOrWhiteSpace(atm) && !string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase))
                        Log($"Follower {f.Name} ATM override: {atm} (stored; attach not enabled from AddOn yet)");

                    Log($"Copy -> {f.Name}: action={action}, qty={qty}, instr={instrSnap.FullName}");

                    // If we have a template, submit with bracket tracking
                    if (!string.IsNullOrWhiteSpace(atm) && !string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        SubmitFollowerMarketWithBracket(f, instrSnap, action, qty, atm, execId);
                    }
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
                if (!_armed) return;
                if (e?.Order == null) return;

                if (string.IsNullOrWhiteSpace(e.Order.Name) || !e.Order.Name.StartsWith("STC:", StringComparison.Ordinal))
                    return;

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

            private string ResolveFollowerAtm(Account follower)
            {
                if (follower == null) return _configuredMasterAtm ?? "None";

                if (_configuredFollowerAtmOverrides != null &&
                    _configuredFollowerAtmOverrides.TryGetValue(follower.Name, out var a) &&
                    !string.IsNullOrWhiteSpace(a))
                {
                    a = a.Trim();
                    if (string.Equals(a, "(inherit master)", StringComparison.OrdinalIgnoreCase))
                        return _configuredMasterAtm ?? "None";
                    return a;
                }

                return _configuredMasterAtm ?? "None";
            }
        }
    }
}