using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            internal bool CanApplyFreeTrade(Account acc, Instrument instr, double minProfitPoints, out string reason)
            {
                reason = "";

                if (acc == null)
                {
                    reason = "No account";
                    return false;
                }

                if (instr == null)
                {
                    reason = "No instrument";
                    return false;
                }

                if (minProfitPoints <= 0)
                {
                    reason = "Feature disabled";
                    return false;
                }

                if (!TryGetActiveBracketSpec(acc, instr, out var spec) || spec == null)
                {
                    reason = "No active bracket";
                    return false;
                }

                if (spec.IsFreeTradeApplied)
                {
                    reason = "Already free trade";
                    return false;
                }

                if (spec.EntryPrice <= 0)
                {
                    reason = "No entry price";
                    return false;
                }

                if (spec.CurrentStopPrice <= 0 || spec.OriginalStopPrice <= 0)
                {
                    reason = "No tracked stop";
                    return false;
                }

                var net = GetNetPosition(acc, instr);
                if (net == 0)
                {
                    reason = "No open position";
                    return false;
                }

                if (!TryGetInstrumentUnrealized(acc, instr, out var unrealized, out var absQty) || absQty <= 0)
                {
                    reason = "No unrealized pnl";
                    return false;
                }

                if (unrealized <= 0)
                {
                    reason = "Trade not in profit";
                    return false;
                }

                var pointValue = instr.MasterInstrument?.PointValue ?? 0.0;
                if (pointValue <= 0)
                {
                    reason = "Invalid point value";
                    return false;
                }

                var profitPoints = unrealized / (pointValue * absQty);
                if (profitPoints + 1e-9 < minProfitPoints)
                {
                    reason = $"Min profit not reached ({profitPoints:0.##} < {minProfitPoints:0.##} pts)";
                    return false;
                }

                if (spec.IsBuy)
                {
                    if (spec.CurrentStopPrice >= spec.EntryPrice - 1e-9)
                    {
                        reason = "Stop already at or above entry";
                        return false;
                    }
                }
                else
                {
                    if (spec.CurrentStopPrice <= spec.EntryPrice + 1e-9)
                    {
                        reason = "Stop already at or below entry";
                        return false;
                    }
                }

                var stop = FindWorkingManagedStop(acc, instr, spec);
                if (stop == null)
                {
                    reason = "No working stop found";
                    return false;
                }

                return true;
            }

            internal bool CanUndoFreeTrade(Account acc, Instrument instr, out string reason)
            {
                reason = "";

                if (acc == null)
                {
                    reason = "No account";
                    return false;
                }

                if (instr == null)
                {
                    reason = "No instrument";
                    return false;
                }

                if (!TryGetActiveBracketSpec(acc, instr, out var spec) || spec == null)
                {
                    reason = "No active bracket";
                    return false;
                }

                if (!spec.IsFreeTradeApplied)
                {
                    reason = "Free trade not applied";
                    return false;
                }

                if (spec.OriginalStopPrice <= 0)
                {
                    reason = "No original stop";
                    return false;
                }

                var net = GetNetPosition(acc, instr);
                if (net == 0)
                {
                    reason = "No open position";
                    return false;
                }

                var stop = FindWorkingManagedStop(acc, instr, spec);
                if (stop == null)
                {
                    reason = "No working stop found";
                    return false;
                }

                return true;
            }

            internal bool ApplyFreeTrade(Account acc, Instrument instr, double minProfitPoints)
            {
                if (!CanApplyFreeTrade(acc, instr, minProfitPoints, out var reason))
                {
                    Log($"[BE] SKIP acc={acc?.Name} instr={instr?.FullName} reason={reason}");
                    return false;
                }

                if (!TryGetActiveBracketSpec(acc, instr, out var spec) || spec == null)
                    return false;

                var stop = FindWorkingManagedStop(acc, instr, spec);
                if (stop == null)
                {
                    Log($"[BE] SKIP acc={acc?.Name} instr={instr?.FullName} reason=working-stop-not-found");
                    return false;
                }

                var newStopPrice = spec.EntryPrice;
                var exitAction = spec.IsBuy ? OrderAction.Sell : OrderAction.BuyToCover;
                var qty = Math.Max(1, spec.Qty);
                var oco = string.IsNullOrWhiteSpace(spec.StopOco)
                    ? "STC:BRK:" + Guid.NewGuid().ToString("N")
                    : spec.StopOco;

                try
                {
                    acc.Cancel(new[] { stop });
                }
                catch (Exception ex)
                {
                    Log($"[BE] APPLY cancel failed acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                    return false;
                }

                var replacement = acc.CreateOrder(
                    instr,
                    exitAction,
                    OrderType.StopMarket,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    newStopPrice,
                    oco,
                    "STC:SL",
                    DateTime.MaxValue,
                    null
                );

                try
                {
                    acc.Submit(new[] { replacement });
                }
                catch (Exception ex)
                {
                    Log($"[BE] APPLY submit failed acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                    return false;
                }

                UpdateActiveBracketSpec(acc, instr, x =>
                {
                    x.CurrentStopPrice = newStopPrice;
                    x.IsFreeTradeApplied = true;
                    x.StopOrderName = "STC:SL";
                    x.StopOco = oco;
                });

                Log(
                    $"[BE] APPLY acc={acc.Name} instr={instr.FullName} " +
                    $"side={(spec.IsBuy ? "Long" : "Short")} " +
                    $"entry={spec.EntryPrice:0.00} oldStop={spec.CurrentStopPrice:0.00} newStop={newStopPrice:0.00}");

                return true;
            }

            internal bool UndoFreeTrade(Account acc, Instrument instr)
            {
                if (!CanUndoFreeTrade(acc, instr, out var reason))
                {
                    Log($"[BE] SKIP acc={acc?.Name} instr={instr?.FullName} reason={reason}");
                    return false;
                }

                if (!TryGetActiveBracketSpec(acc, instr, out var spec) || spec == null)
                    return false;

                var stop = FindWorkingManagedStop(acc, instr, spec);
                if (stop == null)
                {
                    Log($"[BE] SKIP acc={acc?.Name} instr={instr?.FullName} reason=working-stop-not-found");
                    return false;
                }

                var restoreStopPrice = spec.OriginalStopPrice;
                var exitAction = spec.IsBuy ? OrderAction.Sell : OrderAction.BuyToCover;
                var qty = Math.Max(1, spec.Qty);
                var oco = string.IsNullOrWhiteSpace(spec.StopOco)
                    ? "STC:BRK:" + Guid.NewGuid().ToString("N")
                    : spec.StopOco;

                try
                {
                    acc.Cancel(new[] { stop });
                }
                catch (Exception ex)
                {
                    Log($"[BE] UNDO cancel failed acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                    return false;
                }

                var replacement = acc.CreateOrder(
                    instr,
                    exitAction,
                    OrderType.StopMarket,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    restoreStopPrice,
                    oco,
                    "STC:SL",
                    DateTime.MaxValue,
                    null
                );

                try
                {
                    acc.Submit(new[] { replacement });
                }
                catch (Exception ex)
                {
                    Log($"[BE] UNDO submit failed acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                    return false;
                }

                UpdateActiveBracketSpec(acc, instr, x =>
                {
                    x.CurrentStopPrice = restoreStopPrice;
                    x.IsFreeTradeApplied = false;
                    x.StopOrderName = "STC:SL";
                    x.StopOco = oco;
                });

                Log(
                    $"[BE] UNDO acc={acc.Name} instr={instr.FullName} " +
                    $"side={(spec.IsBuy ? "Long" : "Short")} " +
                    $"entry={spec.EntryPrice:0.00} restoreStop={restoreStopPrice:0.00}");

                return true;
            }

            internal int ApplyFreeTradeAll(IEnumerable<Account> accounts, Instrument instr, double minProfitPoints)
            {
                if (accounts == null || instr == null || minProfitPoints <= 0)
                    return 0;

                var applied = 0;

                foreach (var acc in accounts.Where(a => a != null).Distinct())
                {
                    if (ApplyFreeTrade(acc, instr, minProfitPoints))
                        applied++;
                }

                Log($"[BE] APPLY ALL instr={instr.FullName} applied={applied}");
                return applied;
            }

            private static Order FindWorkingManagedStop(Account acc, Instrument instr, ActiveBracketSpec spec)
            {
                if (acc == null || instr == null || spec == null)
                    return null;

                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        var isWorking =
                            o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted;

                        if (!isWorking)
                            continue;

                        var name = (o.Name ?? "").Trim();
                        if (!name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!string.IsNullOrWhiteSpace(spec.StopOco))
                        {
                            var oco = (o.Oco ?? "").Trim();
                            if (string.Equals(oco, spec.StopOco, StringComparison.Ordinal))
                                return o;
                        }
                    }

                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        var isWorking =
                            o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted;

                        if (!isWorking)
                            continue;

                        var name = (o.Name ?? "").Trim();
                        if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase))
                            return o;
                    }
                }
                catch
                {
                    return null;
                }

                return null;
            }
        }
    }
}