using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private bool HasWorkingProtectiveStop(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;

                if (TryGetActiveBracketSpec(acc, instr, out var spec) && spec != null)
                    return FindWorkingManagedStop(acc, instr, spec) != null;

                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                        continue;

                    var isWorking =
                        o.OrderState == OrderState.Working ||
                        o.OrderState == OrderState.Accepted ||
                        o.OrderState == OrderState.Submitted ||
                        o.OrderState == OrderState.PartFilled;

                    if (!isWorking)
                        continue;

                    var name = (o.Name ?? "").Trim();
                    if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Stop1", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            
            private void HandleRejectedProtectedOrder(Order order)
            {
                if (order == null)
                    return;

                if (order.OrderState != OrderState.Rejected)
                    return;

                var acc = order.Account;
                var instr = order.Instrument;
                if (acc == null || instr == null)
                    return;

                var name = (order.Name ?? "").Trim();
                var isProtectedOrder =
                    name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Stop1", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Target1", StringComparison.OrdinalIgnoreCase);

                if (!isProtectedOrder)
                    return;

                AutoFlattenProtectionScope scope;
                lock (_gate)
                    scope = _autoFlattenOnOrderReject;

                if (!IsProtectionEnabledForAccount(acc, scope))
                    return;

                if (!HasSelectedBracket(acc))
                    return;

                if (!TryGetLivePosition(acc, instr, out _, out _))
                    return;

                TriggerRiskProtectionFlatten(
                    acc,
                    instr,
                    $"Protected order rejected: {name}");
            }
        }
    }
}