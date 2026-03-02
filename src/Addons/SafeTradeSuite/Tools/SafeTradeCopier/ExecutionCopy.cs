#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        internal partial class SafeCopierEngine : IDisposable
        {
            private void OnMasterExecution(object sender, ExecutionEventArgs e)
            {
                if (!armed || !copyEnabled) return;
                if (e?.Execution == null) return;
                if (master == null || e.Execution.Account != master) return;
                if (instrument == null) return;

                if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != instrument.FullName)
                    return;

                var execId = e.Execution.ExecutionId ?? "";
                if (string.IsNullOrWhiteSpace(execId))
                    execId = $"{e.Execution.Time.Ticks}_{e.Execution.Price}_{e.Execution.Quantity}_{e.Execution.MarketPosition}";

                if (!AllowCopyNow())
                {
                    lock (gate)
                    {
                        copyEnabled = false;
                        DisarmUnsafe_NoLock("Circuit breaker: too many copies in short window");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: "Circuit breaker tripped");
                    }
                    return;
                }

                if (!masterNetShadowInit)
                {
                    masterNetShadow = GetNetPosition(master, instrument);
                    masterNetShadowInit = true;
                }

                // Update shadow net using *this execution*
                masterNetShadow += SignedQtyFromExecution(e.Execution);
                var masterTargetNet = masterNetShadow;

                // Capture stable token
                CancellationToken token;
                CancellationTokenSource localCts;
                lock (gate)
                {
                    localCts = cts;
                    token = localCts.Token;
                }

                Task.Run(async () =>
                {
                    await submitLock.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await CopyToFollowers(execId, masterTargetNet, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        submitLock.Release();
                    }
                }, token);
            }
 
            private async Task CopyToFollowers(string execId, int masterTargetNet, CancellationToken token)
            {
                if (seen.Count > 5000)
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-30).Ticks;
                    foreach (var kv in seen.ToArray())
                    {
                        if (kv.Value < cutoff)
                            seen.TryRemove(kv.Key, out _);
                    }
                }

                // Snapshot followers to avoid mid-iteration edits (safe even without try/catch)
                List<Account> followerSnap;
                Account masterSnap;
                Instrument instrSnap;

                lock (gate)
                {
                    followerSnap = followers.ToList();
                    masterSnap = master;
                    instrSnap = instrument;
                }

                foreach (var f in followerSnap)
                {
                    if (token.IsCancellationRequested) return;
                    if (f == null) continue;
                    if (masterSnap != null && ReferenceEquals(f, masterSnap)) continue;
                    if (instrSnap == null) return;

                    if (f.ConnectionStatus != ConnectionStatus.Connected)
                    {
                        lock (gate)
                        {
                            copyEnabled = false;
                            DisarmUnsafe_NoLock($"Follower {f.Name} not Connected");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: $"Follower {f.Name} disconnected");
                        }
                        return;
                    }

                    var followerNet = GetNetPosition(f, instrSnap);
                    var delta = masterTargetNet - followerNet;

                    if (delta == 0) continue;

                    if (Math.Abs(delta) > MaxAbsQtyPerFollower)
                        delta = Math.Sign(delta) * MaxAbsQtyPerFollower;

                    var key = $"{execId}|{f.Name}|{instrSnap.FullName}";
                    if (!seen.TryAdd(key, DateTime.UtcNow.Ticks))
                        continue;

                    var action = delta > 0 ? OrderAction.Buy : OrderAction.SellShort;
                    var qty = Math.Abs(delta);

                    if (qty <= 0 || qty > MaxAbsQtyPerFollower)
                    {
                        lock (gate)
                        {
                            copyEnabled = false;
                            DisarmUnsafe_NoLock($"Safety stop: qty={qty} follower={f.Name}");
                            RaiseModeChanged_NoLock();
                            RaiseReady_NoLock(reasonOverride: "Safety stop");
                        }
                        return;
                    }

                    Log($"Copy -> {f.Name}: target={masterTargetNet}, followerNet={followerNet}, delta={delta}, action={action}, qty={qty}");

                    var ord = f.CreateOrder(instrSnap, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Day,
                        qty, 0, 0, string.Empty, $"STC:{execId}", DateTime.MaxValue, null);

                    f.Submit(new[] { ord });
                    RecordCopy();

                    if (StaggerMsPerFollower > 0)
                        await Task.Delay(StaggerMsPerFollower, token).ConfigureAwait(false);
                }
            }

            private void OnFollowerOrderUpdate(object sender, OrderEventArgs e)
            {
                if (!armed) return;
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

                    lock (gate)
                    {
                        copyEnabled = false;
                        DisarmUnsafe_NoLock($"Circuit breaker: copied order REJECTED on {e.Order.Account?.Name}. Msg={msg}");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: "Order rejected");
                    }
                }
            }
        }
    }
}