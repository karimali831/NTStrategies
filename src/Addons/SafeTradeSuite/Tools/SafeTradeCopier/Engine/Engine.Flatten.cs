using System;
using System.Collections.Concurrent;
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
            private readonly ConcurrentDictionary<string, byte> _flattenInFlight =
                new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            
            private static string FlattenKey(Account acc, Instrument instr)
            {
                return (acc?.Name ?? "") + "|" + (instr?.FullName ?? "");
            }
            
            public void EnsureFlatInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return;

                var key = FlattenKey(acc, instr);
                if (!_flattenInFlight.TryAdd(key, 0))
                {
                    Log($"Flatten skipped -> acc={acc.Name}, instr={instr.FullName}, reason=already-in-flight");
                    return;
                }

                Task.Run(async () =>
                {
                    try
                    {
                        for (var pass = 1; pass <= 4; pass++)
                        {
                            try
                            {
                                FlattenInstrument(acc, instr, pass);
                                await Task.Delay(400).ConfigureAwait(false);

                                var hasLivePositionAfter = TryGetLivePosition(acc, instr, out var mpAfter, out var qtyAfter);
                                var workingAfter = GetWorkingOrdersForInstrument(acc, instr).Count;

                                Log(
                                    $"Flatten verify -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                                    $"hasLivePos={hasLivePositionAfter}, mpAfter={mpAfter}, qtyAfter={qtyAfter}, workingAfter={workingAfter}");

                                if (!hasLivePositionAfter && workingAfter == 0)
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
                    }
                    finally
                    {
                        _flattenInFlight.TryRemove(key, out _);
                    }
                });
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
                    Log(
                        $"Flatten orders snapshot failed -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, msg={ex.Message}");
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
                        Log(
                            $"Flatten cancel submitted -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, count={cancellable.Length}");
                    }
                }
                catch (Exception ex)
                {
                    Log(
                        $"Flatten cancel failed -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, msg={ex.Message}");
                }

                if (!TryGetLivePosition(acc, instr, out var marketPosition, out var qty))
                {
                    Log($"Flatten -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, no live position");
                    return;
                }

                var action = marketPosition == MarketPosition.Long
                    ? OrderAction.Sell
                    : OrderAction.BuyToCover;

                Log(
                    $"Flatten decision -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                    $"marketPosition={marketPosition}, qty={qty}");

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
                        $"marketPosition={marketPosition}, action={action}, qty={qty}");
                }
                catch (Exception ex)
                {
                    Log(
                        $"Flatten market submit failed -> acc={acc.Name}, instr={instr.FullName}, pass={pass}, " +
                        $"msg={ex.Message}");
                }
            }

            private void FlattenFollowersThatUseMasterExit(Instrument instr)
            {
                if (instr == null)
                    return;

                List<Account> followersToFlatten;

                lock (_gate)
                {
                    followersToFlatten = (_configuredFollowers ?? new List<Account>())
                        .Where(f => f != null && f.ConnectionStatus == ConnectionStatus.Connected &&
                                    FollowerUsesMasterExit(f))
                        .Distinct()
                        .ToList();
                }

                foreach (var f in followersToFlatten)
                {
                    try
                    {
                        if (!TryGetLivePosition(f, instr, out _, out _))
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