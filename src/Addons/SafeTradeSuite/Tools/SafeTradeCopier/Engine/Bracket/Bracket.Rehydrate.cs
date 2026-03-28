using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            internal void RehydrateActiveBracketsFromLiveOrders()
            {
                try
                {
                    var accounts = Account.All;

                    if (accounts == null || accounts.Count == 0)
                    {
                        Log("[BRACKET REHYDRATE] no accounts found");
                        return;
                    }

                    lock (_gate)
                        _activeBracketByAccInstr.Clear();

                    foreach (var acc in accounts)
                    {
                        if (acc == null)
                            continue;

                        try
                        {
                            // --- scan positions ---
                            foreach (var pos in acc.Positions)
                            {
                                if (pos?.Instrument == null)
                                    continue;

                                var instr = pos.Instrument;
                                var net = pos.Quantity;

                                if (net == 0)
                                    continue;

                                Order stop = null;
                                Order target = null;

                                // --- scan orders for this instrument ---
                                foreach (var o in acc.Orders)
                                {
                                    if (o?.Instrument == null)
                                        continue;

                                    if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                                        continue;

                                    var isLive =
                                        o.OrderState == OrderState.Working ||
                                        o.OrderState == OrderState.Accepted ||
                                        o.OrderState == OrderState.Submitted ||
                                        o.OrderState == OrderState.PartFilled;

                                    if (!isLive)
                                        continue;

                                    var name = (o.Name ?? "").Trim();

                                    if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                                        name.Equals("Stop1", StringComparison.OrdinalIgnoreCase))
                                    {
                                        stop = o;
                                    }
                                    else if (name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                                             name.Equals("Target1", StringComparison.OrdinalIgnoreCase))
                                    {
                                        target = o;
                                    }
                                }

                                if (stop == null && target == null)
                                    continue;

                                var entryPrice = pos.AveragePrice;
                                if (entryPrice <= 0)
                                    continue;

                                var stopPrice = stop?.StopPrice ?? 0.0;
                                var targetPrice = target?.LimitPrice ?? 0.0;
                                var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;

                                var stopTicks = 0;
                                var targetTicks = 0;

                                if (tickSize > 0 && stopPrice > 0)
                                    stopTicks = (int)Math.Round(Math.Abs(entryPrice - stopPrice) / tickSize, MidpointRounding.AwayFromZero);

                                if (tickSize > 0 && targetPrice > 0)
                                    targetTicks = (int)Math.Round(Math.Abs(targetPrice - entryPrice) / tickSize, MidpointRounding.AwayFromZero);

                                var isBuy = net > 0;
                                var qty = Math.Abs(net);

                                lock (_gate)
                                {
                                    _activeBracketByAccInstr[BracketKey(acc, instr)] = new ActiveBracketSpec
                                    {
                                        AutoBeSuppressedUntilFlat = false,
                                        StopTicks = stopTicks,
                                        TargetTicks = targetTicks,
                                        IsBuy = isBuy,
                                        Qty = qty,
                                        EntryPrice = entryPrice,
                                        OriginalStopPrice = stopPrice,
                                        CurrentStopPrice = stopPrice,
                                        TargetPrice = targetPrice,
                                        IsFreeTradeApplied = stopPrice > 0 &&
                                                             ((isBuy && stopPrice >= entryPrice) ||
                                                              (!isBuy && stopPrice <= entryPrice)),
                                        StopOrderName = stop?.Name,
                                        TargetOrderName = target?.Name,
                                        StopOco = stop?.Oco ?? target?.Oco
                                    };
                                }

                                Log($"[BRACKET REHYDRATE] acc={acc.Name} instr={instr.FullName} qty={qty} entry={entryPrice:0.00}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"[BRACKET REHYDRATE FAILED] acc={acc?.Name} msg={ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[BRACKET REHYDRATE FATAL] msg={ex.Message}");
                }
            }
        }
    }
}
