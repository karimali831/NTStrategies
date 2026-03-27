using System;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private void HandlePendingEntryCleanup(Order order)
            {
                if (order == null)
                    return;

                var name = (order.Name ?? "").Trim();
                if (!name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                    return;

                if (order.OrderState != OrderState.Rejected &&
                    order.OrderState != OrderState.Cancelled)
                    return;

                RemovePendingBracketForEntry(name);
                Log($"[SYNC] ENTRY CLEARED acc={order.Account?.Name} instr={order.Instrument?.FullName} state={order.OrderState}");
            }

            private bool HasPendingBracketForEntry(string entryName)
            {
                if (string.IsNullOrWhiteSpace(entryName))
                    return false;

                lock (_gate)
                {
                    return _pendingBrackets.ContainsKey(entryName);
                }
            }

            private bool HasPendingFollowerEntry(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;

                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        var name = (o.Name ?? "").Trim();
                        if (!name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var isLive =
                            o.OrderState == OrderState.Initialized ||
                            o.OrderState == OrderState.Submitted ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.PartFilled;

                        if (isLive)
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[FOLLOWER COPY GATE] pending-entry scan failed acc={acc?.Name} instr={instr?.FullName} msg={ex.Message}");
                }

                return false;
            }

            private bool HasAnyOpenFollowerTradeState(Account acc, Instrument instr, out string reason)
            {
                reason = "";

                if (acc == null || instr == null)
                {
                    reason = "invalid account/instrument";
                    return true;
                }

                if (TryGetLivePosition(acc, instr, out var mp, out var absQty) && absQty > 0)
                {
                    reason = $"live position exists mp={mp} qty={absQty}";
                    return true;
                }

                if (HasPendingFollowerEntry(acc, instr))
                {
                    reason = "pending entry exists";
                    return true;
                }

                if (TryGetActiveBracketSpec(acc, instr, out var spec) && spec != null)
                {
                    reason =
                        $"active bracket exists qty={spec.Qty} entry={spec.EntryPrice:0.00} " +
                        $"stop={spec.CurrentStopPrice:0.00} target={spec.TargetPrice:0.00}";
                    return true;
                }
                
                if (HasWorkingEntryOrders(acc, instr))
                {
                    reason = "working entry order exists";
                    return true;
                }

                if (HasWorkingBracketOrders(acc, instr))
                {
                    reason = "working bracket orders exist";
                    return true;
                }

                return false;
            }

            private void SeenCleanup()
            {
                if (_seen.Count <= 5000)
                    return;

                var cutoff = DateTime.UtcNow.AddMinutes(-30).Ticks;
                foreach (var kv in _seen.ToArray())
                {
                    if (kv.Value < cutoff)
                        _seen.TryRemove(kv.Key, out _);
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
                        "Follow Master Exit",
                        StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }

            private int ResolveFollowerQty(Account follower, int masterExecQty)
            {
                if (follower == null)
                    return masterExecQty;

                if (_configuredFollowerQtyOverrides != null &&
                    _configuredFollowerQtyOverrides.TryGetValue(follower.Name, out var q))
                {
                    return q;
                }

                return masterExecQty;
            }
        }
    }
}