using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            public void EnsureFlatInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                // fire-and-forget: cancel + flatten now, then re-check once (or twice) shortly after
                Task.Run(async () =>
                {
                    try
                    {
                        FlattenInstrument(acc, instr);

                        // Re-check after NT has processed cancels/fills
                        await Task.Delay(300).ConfigureAwait(false);
                        FlattenInstrument(acc, instr);

                        // Optional: one more pass for stubborn ATM state transitions
                        await Task.Delay(300).ConfigureAwait(false);
                        FlattenInstrument(acc, instr);
                    }
                    catch
                    {
                        // keep tool silent/safe; no exceptions to user from background
                    }
                });
            }

            private void FlattenInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                // 1) Cancel any working orders on this instrument (ATM targets/stops live here)
                var orders = new List<Order>();
                try
                {
                    orders.AddRange(acc.Orders);
                }
                catch
                {
                    // if Orders enumeration fails, we still try to flatten net position below
                }

                try
                {
                    foreach (var o in orders)
                    {
                        if (o?.Instrument == null) continue;
                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                        if (o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted)
                        {
                            acc.Cancel(new[] { o });
                        }
                    }
                }
                catch
                {
                    // non-fatal
                }

                // 2) Now flatten the net position
                var net = GetNetPosition(acc, instr);
                if (net == 0)
                {
                    ClearActiveBracket(acc, instr);
                    Log($"Flatten -> {acc.Name}: net=0 (nothing to do) instr={instr.FullName}");
                    return;
                }

                var action = net > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                var qty = Math.Abs(net);

                Log($"Flatten -> {acc.Name}: net={net}, action={action}, qty={qty}, instr={instr.FullName}");

                var ord = acc.CreateOrder(
                    instr,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    0,
                    string.Empty,
                    "STC:FLATTEN",
                    DateTime.MaxValue,
                    null
                );

                acc.Submit(new[] { ord });
            }
            
            private void TryFlattenFollowersOnMasterFlat()
            {
                if (_master == null || _instrument == null)
                    return;

                if (GetNetPosition(_master, _instrument) != 0)
                    return;

                FlattenFollowersThatUseMasterExit(_instrument);
            }
            
            private void FlattenFollowersThatUseMasterExit(Instrument instr)
            {
                if (instr == null)
                    return;

                List<Account> followersToFlatten;

                lock (_gate)
                {
                    followersToFlatten = (_configuredFollowers ?? new List<Account>())
                        .Where(f => f != null && f.ConnectionStatus == ConnectionStatus.Connected && FollowerUsesMasterExit(f))
                        .Distinct()
                        .ToList();
                }

                foreach (var f in followersToFlatten)
                {
                    try
                    {
                        if (GetNetPosition(f, instr) == 0)
                            continue;

                        EnsureFlatInstrument(f, instr);
                        Log($"Follow-master-exit flatten -> {f.Name} ({instr.FullName})");
                    }
                    catch (Exception ex)
                    {
                        Log($"Follow-master-exit flatten failed -> {f?.Name} ({instr?.FullName}) msg={ex.Message}");
                    }
                }
            }
        }
    }
}