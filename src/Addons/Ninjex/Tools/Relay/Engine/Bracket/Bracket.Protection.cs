using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public partial class RelayEngine
        {
            private void CheckMissingProtectiveBracket()
            {
                Instrument instr;
                Account master;
                List<Account> followers;
                AutoFlattenProtectionScope scope;

                lock (_gate)
                {
                    instr = _instrument;
                    master = _master;
                    followers = _followers?.ToList() ?? new List<Account>();
                    scope = _autoFlattenMissingBracket;
                }

                if (instr == null || scope == AutoFlattenProtectionScope.Disabled)
                    return;
                

                var accounts = new List<Account>();
                if (master != null)
                    accounts.Add(master);

                accounts.AddRange(followers.Where(a => a != null));
                accounts = accounts
                    .GroupBy(a => a.Name ?? "", StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();

                foreach (var acc in accounts)
                {
                    if (acc == null)
                        continue;

                    if (!IsProtectionEnabledForAccount(acc, scope))
                        continue;

                    if (!HasSelectedBracket(acc))
                        continue;

                    if (!TryGetLivePosition(acc, instr, out _, out _))
                        continue;

                    Log($"[PROTECT CHECK] acc={acc.Name} instr={instr.FullName} begin");

                    if (!IsProtectionEnabledForAccount(acc, scope))
                    {
                        Log($"[PROTECT SKIP] acc={acc.Name} reason=scope-disabled-for-account");
                        continue;
                    }

                    if (!HasSelectedBracket(acc))
                    {
                        Log($"[PROTECT SKIP] acc={acc.Name} reason=no-selected-bracket");
                        continue;
                    }

                    if (!TryGetLivePosition(acc, instr, out var mp, out var liveQty))
                    {
                        Log($"[PROTECT SKIP] acc={acc.Name} reason=no-live-position");
                        continue;
                    }

                    if (!ReferenceEquals(acc, _master))
                    {
                        var state = GetGuardState(acc);
                        if (state != null && state.EntryWorking)
                        {
                            Log($"[PROTECT SKIP] acc={acc.Name} reason=entry-working");
                            continue;
                        }
                    }

                    if (HasWorkingManagedExitOrder(acc, instr))
                    {
                        Log($"[PROTECT SKIP] acc={acc.Name} reason=managed-exit-in-progress");
                        continue;
                    }

                    if (HasWorkingManagedExitOrder(acc, instr))
                    {
                        Log($"[PROTECTION SKIP] acc={acc.Name} instr={instr.FullName} reason=managed-exit-in-progress");
                        continue;
                    }

                    if (HasProtectiveBracketQtyMismatch(acc, instr, out var mismatchDetail))
                    {
                        Log($"[PROTECTION VIOLATION] acc={acc.Name} instr={instr.FullName} detail={mismatchDetail}");

                        TriggerRiskProtectionFlatten(
                            acc,
                            instr,
                            $"Protective bracket invalid: {mismatchDetail}");

                        continue;
                    }

                    if (HasWorkingProtectiveStop(acc, instr))
                        continue;

                    Log($"[PROTECTION VIOLATION] acc={acc.Name} instr={instr.FullName} detail=missing working protective stop");

                    TriggerRiskProtectionFlatten(
                        acc,
                        instr,
                        "Live position detected with selected bracket but no working protective stop.");
                }
            }
            
            private bool HasProtectiveBracketQtyMismatch(Account acc, Instrument instr, out string detail)
            {
                detail = "";

                if (acc == null || instr == null)
                    return false;

                if (!TryGetLivePosition(acc, instr, out var mp, out var liveQty) || liveQty <= 0)
                    return false;

                var stop = FindAnyWorkingManagedStop(acc, instr);
                var target = FindAnyWorkingManagedTarget(acc, instr);

                var stopQty = stop?.Quantity ?? 0;
                var targetQty = target?.Quantity ?? 0;

                // Stop is the load-bearing protection. Target is also useful to validate.
                if (stop == null)
                {
                    detail = $"missing stop liveQty={liveQty}";
                    return true;
                }

                if (stopQty != liveQty)
                {
                    detail = $"stop qty mismatch liveQty={liveQty} stopQty={stopQty}";
                    return true;
                }

                if (target != null && targetQty != liveQty)
                {
                    detail = $"target qty mismatch liveQty={liveQty} targetQty={targetQty}";
                    return true;
                }

                return false;
            }
            
            private void RebuildProtectiveBracketForLivePosition(
                Account acc,
                Instrument instr,
                ActiveBracketSpec spec,
                int qty,
                double avgEntryPrice)
            {
                if (acc == null || instr == null || spec == null)
                    return;

                var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;
                if (tickSize <= 0)
                    return;

                try
                {
                    var liveManagedOrders = GetWorkingOrdersForInstrument(acc, instr)
                        .Where(o =>
                        {
                            var name = (o?.Name ?? "").Trim();
                            return name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                                   name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase);
                        })
                        .ToArray();

                    if (liveManagedOrders.Length > 0)
                    {
                        acc.Cancel(liveManagedOrders);

                        Log(
                            $"[BRACKET REBUILD CANCEL] acc={acc.Name} instr={instr.FullName} " +
                            $"count={liveManagedOrders.Length}");
                    }
                }
                catch (Exception ex)
                {
                    Log(
                        $"[BRACKET REBUILD CANCEL FAILED] acc={acc.Name} instr={instr.FullName} msg={ex.Message}");
                }

                var exitAction = spec.IsBuy ? OrderAction.Sell : OrderAction.BuyToCover;
                var oco = "STC:BRK:" + Guid.NewGuid().ToString("N");

                var stopPrice = 0.0;
                var targetPrice = 0.0;
                string stopOrderName = null;
                string targetOrderName = null;

                var orders = new List<Order>(2);

                if (spec.TargetTicks > 0)
                {
                    targetPrice = spec.IsBuy
                        ? avgEntryPrice + spec.TargetTicks * tickSize
                        : avgEntryPrice - spec.TargetTicks * tickSize;

                    targetPrice = RoundToTick(targetPrice, tickSize);

                    var tgt = acc.CreateOrder(
                        instr,
                        exitAction,
                        OrderType.Limit,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        qty,
                        targetPrice,
                        0,
                        oco,
                        "STC:TP",
                        DateTime.MaxValue,
                        null
                    );

                    targetOrderName = "STC:TP";
                    orders.Add(tgt);
                }

                if (spec.StopTicks > 0)
                {
                    stopPrice = spec.IsBuy
                        ? avgEntryPrice - spec.StopTicks * tickSize
                        : avgEntryPrice + spec.StopTicks * tickSize;

                    stopPrice = RoundToTick(stopPrice, tickSize);

                    var stp = acc.CreateOrder(
                        instr,
                        exitAction,
                        OrderType.StopMarket,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        qty,
                        0,
                        stopPrice,
                        oco,
                        "STC:SL",
                        DateTime.MaxValue,
                        null
                    );

                    stopOrderName = "STC:SL";
                    orders.Add(stp);
                }

                if (orders.Count == 0)
                    return;

                acc.Submit(orders.ToArray());

                lock (_gate)
                {
                    _activeBracketByAccInstr[BracketKey(acc, instr)] =
                        new ActiveBracketSpec
                        {
                            AutoBeSuppressedUntilFlat = false,
                            StopTicks = spec.StopTicks,
                            TargetTicks = spec.TargetTicks,
                            IsBuy = spec.IsBuy,
                            Qty = qty,
                            EntryFilledQty = qty,
                            EntryValueSum = avgEntryPrice * qty,
                            EntryPrice = avgEntryPrice,
                            OriginalStopPrice = stopPrice,
                            CurrentStopPrice = stopPrice,
                            TargetPrice = targetPrice,
                            IsFreeTradeApplied = spec.IsFreeTradeApplied,
                            StopOrderName = stopOrderName,
                            TargetOrderName = targetOrderName,
                            StopOco = oco
                        };
                }

                Log(
                    $"[BRACKET REBUILT] acc={acc.Name} instr={instr.FullName} qty={qty} " +
                    $"avgEntry={avgEntryPrice:0.00} stop={stopPrice:0.00} target={targetPrice:0.00} oco={oco}");
            }
        }
    }
}