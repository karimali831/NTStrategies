using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
        {
            private void OnMasterOrderUpdate(object sender, OrderEventArgs e)
            {
                if (e?.Order == null)
                    return;

                if (_instrument == null)
                    return;

                if (e.Order.Instrument == null || !IsSameInstrument(e.Order.Instrument, _instrument))
                    return;

                var name = (e.Order.Name ?? "").Trim();

                HandlePendingEntryCleanup(e.Order);
                CompleteMasterManualSubmit(e.Order);

                if (name.StartsWith("STC:", StringComparison.OrdinalIgnoreCase))
                {
                    SyncBracketFromOrderUpdate(e.Order);
                    HandleRejectedProtectedOrder(e.Order);
                }
            }
            
            private void OnFollowerOrderUpdate(object sender, OrderEventArgs e)
            {
                if (e?.Order == null)
                    return;

                var name = (e.Order.Name ?? "").Trim();
                if (!name.StartsWith("STC:", StringComparison.OrdinalIgnoreCase))
                    return;
                
                NinjexRuntime.PrintLog(
                    $"[FOLLOWER ORDER UPDATE] acc={e.Order?.Account?.Name} " +
                    $"name={(e.Order?.Name ?? "").Trim()} " +
                    $"state={e.Order?.OrderState} " +
                    $"instr={e.Order?.Instrument?.FullName} " +
                    $"oco={(e.Order?.Oco ?? "").Trim()} " +
                    $"qty={e.Order?.Quantity}");

                HandlePendingEntryCleanup(e.Order);
                SyncBracketFromOrderUpdate(e.Order);

                var acc = e.Order?.Account;
                if (acc != null && name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                {
                    FollowerGuard settings;
                    lock (_gate)
                        settings = _followerGuard ?? new FollowerGuard();

                    if (e.Order.OrderState == OrderState.Rejected)
                    {
                        NinjexRuntime.PrintLog(
                            $"[FOLLOWER ENTRY RESOLVE] acc={acc.Name} name={name} reason=rejected");

                        MarkFollowerEntryResolved(acc);
                        ApplyGuardAction(
                            acc,
                            settings.OnEntryReject,
                            $"Follower entry rejected: {name}");

                        return;
                    }

                    if (e.Order.OrderState == OrderState.Cancelled)
                    {
                        NinjexRuntime.PrintLog(
                            $"[FOLLOWER ENTRY CANCELLED] acc={acc.Name} name={name} " +
                            $"hasWorkingEntry={HasWorkingEntryOrders(acc, e.Order.Instrument)}");

                        if (!HasWorkingEntryOrders(acc, e.Order.Instrument))
                            MarkFollowerEntryResolved(acc);
                    }
                }
            }
        }
    }
}