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
                if (acc == null || instr == null)
                    return;

                Task.Run(async () =>
                {
                    for (var pass = 1; pass <= 4; pass++)
                    {
                        try
                        {
                            FlattenInstrument(acc, instr, pass);

                            await Task.Delay(400).ConfigureAwait(false);

                            var netAfter = GetNetPosition(acc, instr);
                            var workingAfter = GetWorkingOrdersForInstrument(acc, instr).Count;

                            Log(
                                $"Flatten verify -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                                $"netAfter={netAfter}, workingAfter={workingAfter}");

                            if (netAfter == 0 && workingAfter == 0)
                            {
                                ClearActiveBracket(acc, instr);
                                Log($"Flatten complete -> acc={acc.Name}, instr={instr.FullName}, pass={pass}");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            Log(
                                $"Flatten pass failed -> acc={acc?.Name}, instr={instr?.FullName}, pass={pass}, " +
                                $"msg={ex.Message}");
                        }
                    }

                    Log($"Flatten incomplete after retries -> acc={acc.Name}, instr={instr.FullName}");
                });
            }

            private List<Order> GetWorkingOrdersForInstrument(Account acc, Instrument instr)
            {
                var result = new List<Order>();

                if (acc == null || instr == null)
                    return result;

                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        if (o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted ||
                            o.OrderState == OrderState.PartFilled)
                        {
                            result.Add(o);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"GetWorkingOrdersForInstrument failed -> acc={acc?.Name}, instr={instr?.FullName}, msg={ex.Message}");
                }

                return result;
            }
            
            private void FlattenInstrument(Account acc, Instrument instr, int pass)
            {
                if (acc == null || instr == null)
                    return;

                List<Order> orders;
                try
                {
                    orders = acc.Orders?.ToList() ?? new List<Order>();
                }
                catch (Exception ex)
                {
                    Log($"Flatten orders snapshot failed -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, msg={ex.Message}");
                    orders = new List<Order>();
                }

                var instrumentOrders = orders
                    .Where(o => o?.Instrument != null &&
                                string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                    .ToList();

                foreach (var o in instrumentOrders)
                {
                    Log(
                        $"Flatten inspect order -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                        $"name={o.Name}, signal={o.Name}, state={o.OrderState}, qty={o.Quantity}");
                }

                try
                {
                    var cancellable = instrumentOrders
                        .Where(o =>
                            o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted ||
                            o.OrderState == OrderState.PartFilled)
                        .ToArray();

                    if (cancellable.Length > 0)
                    {
                        acc.Cancel(cancellable);
                        Log($"Flatten cancel submitted -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, count={cancellable.Length}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Flatten cancel failed -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, msg={ex.Message}");
                }

                var net = GetNetPosition(acc, instr);
                if (net == 0)
                {
                    Log($"Flatten -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, net=0");
                    return;
                }

                var action = net > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                var qty = Math.Abs(net);

                try
                {
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

                    Log(
                        $"Flatten market submitted -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                        $"net={net}, action={action}, qty={qty}");
                }
                catch (Exception ex)
                {
                    Log(
                        $"Flatten market submit failed -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                        $"net={net}, msg={ex.Message}");
                }
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