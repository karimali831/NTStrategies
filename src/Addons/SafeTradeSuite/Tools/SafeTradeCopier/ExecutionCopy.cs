#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        public partial class SafeCopierEngine : IDisposable
        {
            private void OnMasterExecution(object sender, ExecutionEventArgs e)
            {
                if (!_armed || !_copyEnabled) return;
                if (e?.Execution == null) return;
                if (_master == null || e.Execution.Account != _master) return;
                if (_instrument == null) return;

                if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != _instrument.FullName)
                    return;

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

                // For copying, we mirror the master *order action* direction.
                // Sell = Sell (close long), BuyToCover = BuyToCover (close short), etc.
                var followerAction = masterAction;

                CancellationToken token;
                CancellationTokenSource localCts;
                lock (_gate)
                {
                    localCts = _cts;
                    token = localCts.Token;
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
                        $"STC:{execId}",
                        DateTime.MaxValue,
                        null
                    );

                    f.Submit(new[] { ord });
                    RecordCopy();

                    if (StaggerMsPerFollower > 0)
                        await Task.Delay(StaggerMsPerFollower, token).ConfigureAwait(false);
                }

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