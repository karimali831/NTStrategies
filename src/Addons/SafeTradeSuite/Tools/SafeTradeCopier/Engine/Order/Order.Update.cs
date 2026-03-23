using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private void OnMasterOrderUpdate(object sender, OrderEventArgs e)
            {
                if (e?.Order == null)
                    return;

                if (_instrument == null)
                    return;

                if (e.Order.Instrument == null || e.Order.Instrument.FullName != _instrument.FullName)
                    return;

                var name = (e.Order.Name ?? "").Trim();

                HandlePendingEntryCleanup(e.Order);
                CompleteMasterManualSubmit(e.Order);

                if (name.StartsWith("STC:", StringComparison.OrdinalIgnoreCase))
                    SyncBracketFromOrderUpdate(e.Order);
            }
            
            private void OnFollowerOrderUpdate(object sender, OrderEventArgs e)
            {
                if (e?.Order == null)
                    return;

                var name = (e.Order.Name ?? "").Trim();
                if (!name.StartsWith("STC:", StringComparison.OrdinalIgnoreCase))
                    return;

                HandlePendingEntryCleanup(e.Order);
                SyncBracketFromOrderUpdate(e.Order);

                var acc = e.Order.Account;
                if (acc != null && name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                {
                    FollowerGuard settings;
                    lock (_gate)
                        settings = _followerGuard ?? new FollowerGuard();

                    if (e.Order.OrderState == OrderState.Rejected)
                    {
                        MarkFollowerEntryResolved(acc);
                        ApplyGuardAction(
                            acc,
                            settings.OnEntryReject,
                            $"Follower entry rejected: {name}");

                        return;
                    }

                    if (e.Order.OrderState == OrderState.Cancelled)
                        MarkFollowerEntryResolved(acc);
                }
            }
        }
    }
}