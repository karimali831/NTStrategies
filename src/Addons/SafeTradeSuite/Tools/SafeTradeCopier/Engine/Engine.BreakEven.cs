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

                if (minProfitPoints < 0)
                {
                    reason = "Invalid min profit points";
                    return false;
                }

                if (!TryGetActiveBracketSpec(acc, instr, out var spec) || spec == null)
                {
                    reason = "No active bracket";
                    return false;
                }

                if (spec.IsFreeTradeApplied)
                {
                    reason = "Already Break-even";
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

            
            internal bool CanUndoFreeTradeAll(IEnumerable<Account> accounts, Instrument instr)
            {
                if (accounts == null || instr == null)
                    return false;

                return accounts
                    .Where(a => a != null)
                    .Distinct()
                    .Any(acc => CanUndoFreeTrade(acc, instr, out _));
            }
            
            internal void UndoFreeTradeAll(IEnumerable<Account> accounts, Instrument instr)
            {
                if (accounts == null || instr == null) return;

                var applied = accounts.Where(a => a != null).Distinct()
                    .Count(acc => UndoFreeTrade(acc, instr));

                Log($"[BE] UNDO ALL instr={instr.FullName} applied={applied}");
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
                    reason = "Break-even not applied";
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

            internal bool ApplyFreeTrade(Account acc, Instrument instr, double minProfitPoints, double plusPoints)
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

                var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;
                if (tickSize <= 0)
                {
                    Log($"[BE] SKIP acc={acc?.Name} instr={instr?.FullName} reason=invalid-ticksize");
                    return false;
                }

                var offsetPoints = Math.Max(0.0, plusPoints);

                var oldStopPrice = spec.CurrentStopPrice;

                var newStopPrice = spec.IsBuy
                    ? spec.EntryPrice + offsetPoints
                    : spec.EntryPrice - offsetPoints;

                newStopPrice = Math.Round(newStopPrice / tickSize, MidpointRounding.AwayFromZero) * tickSize;
                var qty = Math.Max(1, spec.Qty);

                try
                {
                    stop.QuantityChanged = qty;
                    stop.StopPriceChanged = newStopPrice;
                    acc.Change(new[] { stop });
                }
                catch (Exception ex)
                {
                    Log($"[BE] APPLY change failed acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                    return false;
                }

                UpdateActiveBracketSpec(acc, instr, x =>
                {
                    x.CurrentStopPrice = newStopPrice;
                    x.IsFreeTradeApplied = true;
                });

                Log(
                    $"[BE] APPLY acc={acc.Name} instr={instr.FullName} " +
                    $"side={(spec.IsBuy ? "Long" : "Short")} " +
                    $"entry={spec.EntryPrice:0.00} plusPts={plusPoints:0.##} oldStop={oldStopPrice:0.00} newStop={newStopPrice:0.00}");

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
                var qty = Math.Max(1, spec.Qty);

                try
                {
                    stop.QuantityChanged = qty;
                    stop.StopPriceChanged = restoreStopPrice;
                    acc.Change(new[] { stop });
                }
                catch (Exception ex)
                {
                    Log($"[BE] UNDO change failed acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                    return false;
                }

                UpdateActiveBracketSpec(acc, instr, x =>
                {
                    x.CurrentStopPrice = restoreStopPrice;
                    x.IsFreeTradeApplied = false;
                });

                Log(
                    $"[BE] UNDO acc={acc.Name} instr={instr.FullName} " +
                    $"side={(spec.IsBuy ? "Long" : "Short")} " +
                    $"entry={spec.EntryPrice:0.00} restoreStop={restoreStopPrice:0.00}");

                return true;
            }

            internal void ApplyFreeTradeAll(IEnumerable<Account> accounts, Instrument instr, double minProfitPoints, double plusPoints)
            {
                if (accounts == null || instr == null) return;

                var applied = accounts.Where(a => a != null).Distinct()
                    .Count(acc => ApplyFreeTrade(acc, instr, minProfitPoints, plusPoints));

                Log($"[BE] APPLY ALL instr={instr.FullName} applied={applied}");
            }

            private Order FindWorkingManagedStop(Account acc, Instrument instr, ActiveBracketSpec spec)
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