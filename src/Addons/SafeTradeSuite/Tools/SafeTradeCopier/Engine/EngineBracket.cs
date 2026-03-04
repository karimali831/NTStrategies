using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private sealed class PendingBracket
            {
                public string EntryName;
                public int Qty;
                public bool IsBuy;
                public int StopTicks;
                public int TargetTicks;
            }

            private readonly Dictionary<string, PendingBracket> _pendingBrackets =
                new Dictionary<string, PendingBracket>(StringComparer.Ordinal);

            public void SubmitMasterMarketWithBracket(Account master, Instrument instr, OrderAction action, int qty, string atmTemplateName)
            {
                if (master == null || instr == null)
                {
                    Log("SubmitMasterMarketWithBracket: missing master/instrument.");
                    return;
                }

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

                var entryName = "STC:ENTRY:" + Guid.NewGuid().ToString("N");
                var entry = master.CreateOrder(
                    instr,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    0,
                    string.Empty,
                    entryName,
                    DateTime.MaxValue,
                    null
                );

                lock (_gate)
                {
                    _pendingBrackets[entryName] = new PendingBracket
                    {
                        EntryName = entryName,
                        Qty = qty,
                        IsBuy = (action == OrderAction.Buy),
                        StopTicks = Math.Max(0, stopTicks),
                        TargetTicks = Math.Max(0, targetTicks)
                    };
                }

                Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                master.Submit(new[] { entry });
            }

            // Call this from your existing OnMasterExecution handler (see section 3)
            private void TrySubmitBracketOnFill(Account master, Execution execution)
            {
                if (master == null || execution == null) return;

                var ord = execution.Order;
                if (ord == null) return;

                var name = ord.Name ?? "";
                if (string.IsNullOrWhiteSpace(name)) return;

                PendingBracket pb;
                lock (_gate)
                {
                    if (!_pendingBrackets.TryGetValue(name, out pb))
                        return;

                    // only submit once (first fill)
                    _pendingBrackets.Remove(name);
                }

                if (pb.StopTicks <= 0 && pb.TargetTicks <= 0)
                    return;

                var fillPrice = execution.Price;
                if (fillPrice <= 0)
                {
                    Log($"Bracket skipped: invalid fill price for {name}.");
                    return;
                }

                var instr = ord.Instrument;
                if (instr == null)
                {
                    Log($"Bracket skipped: missing instrument for {name}.");
                    return;
                }

                var tickSize = instr.MasterInstrument != null ? instr.MasterInstrument.TickSize : 0.0;
                if (tickSize <= 0)
                {
                    Log($"Bracket skipped: invalid TickSize for {instr.FullName}.");
                    return;
                }

                var oco = "STC:BRK:" + Guid.NewGuid().ToString("N");

                var exitAction = pb.IsBuy ? OrderAction.Sell : OrderAction.Buy;

                var orders = new List<Order>(2);

                if (pb.TargetTicks > 0)
                {
                    var tgtPrice = pb.IsBuy
                        ? fillPrice + pb.TargetTicks * tickSize
                        : fillPrice - pb.TargetTicks * tickSize;

                    var tgt = master.CreateOrder(
                        instr,
                        exitAction,
                        OrderType.Limit,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        pb.Qty,
                        tgtPrice,
                        0,
                        oco,
                        "STC:TP",
                        DateTime.MaxValue,
                        null
                    );

                    orders.Add(tgt);
                }

                if (pb.StopTicks > 0)
                {
                    var stpPrice = pb.IsBuy
                        ? fillPrice - pb.StopTicks * tickSize
                        : fillPrice + pb.StopTicks * tickSize;

                    var stp = master.CreateOrder(
                        instr,
                        exitAction,
                        OrderType.StopMarket,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        pb.Qty,
                        0,
                        stpPrice,
                        oco,
                        "STC:SL",
                        DateTime.MaxValue,
                        null
                    );

                    orders.Add(stp);
                }

                if (orders.Count > 0)
                {
                    master.Submit(orders.ToArray());
                    Log($"Bracket submitted -> {master.Name} {instr.FullName} OCO={oco} (SL={pb.StopTicks}t TP={pb.TargetTicks}t @ fill={fillPrice:0.00})");
                }
            }
        }
    }
}