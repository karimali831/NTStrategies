using System;
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