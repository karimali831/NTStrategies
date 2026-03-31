using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            public void SubmitMasterMarketWithBracket(
                Account master,
                Instrument instr,
                OrderAction action,
                int qty,
                string atmTemplateName,
                string entryName)
            {
                if (master == null || instr == null)
                {
                    Log("SubmitMasterMarketWithBracket: missing master/instrument.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(entryName))
                    entryName = "STC:ENTRY:" + Guid.NewGuid().ToString("N");

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

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
                        OriginalQty = Math.Max(1, qty),
                        IsBuy = action == OrderAction.Buy,
                        StopTicks = Math.Max(0, stopTicks),
                        TargetTicks = Math.Max(0, targetTicks),
                        FilledQty = 0,
                        EntryValueSum = 0.0,
                        BracketSubmitted = false,
                        BracketOco = null
                    };

                    SafeTradeSuiteRuntime.PrintLog(
                        $"[MASTER BRACKET PENDING ADD] entry={entryName} master={master.Name} instr={instr.FullName} qty={qty} atm={atmTemplateName} stopTicks={stopTicks} targetTicks={targetTicks}");
                }

                Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                master.Submit(new[] { entry });
            }

            private void TrySubmitBracketOnFill(Account account, Execution execution)
            {
                try
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[TRY SUBMIT BRACKET ON FILL] acc={account?.Name} orderName={execution?.Order?.Name} instr={execution?.Order?.Instrument?.FullName} price={execution?.Price}");

                    if (account == null || execution == null)
                        return;

                    var ord = execution.Order;
                    if (ord == null)
                        return;

                    var entryName = (ord.Name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(entryName))
                        return;

                    var instr = ord.Instrument;
                    if (instr == null)
                    {
                        Log($"Bracket skipped: missing instrument for {entryName}.");
                        return;
                    }

                    var fillQty = Math.Abs((int)Math.Round((double)execution.Quantity, MidpointRounding.AwayFromZero));
                    if (fillQty <= 0)
                    {
                        Log($"Bracket skipped: invalid fill qty for {entryName}.");
                        return;
                    }

                    var fillPrice = execution.Price;
                    if (fillPrice <= 0)
                    {
                        Log($"Bracket skipped: invalid fill price for {entryName}.");
                        return;
                    }

                    var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;
                    if (tickSize <= 0)
                    {
                        Log($"Bracket skipped: invalid TickSize for {instr.FullName}.");
                        return;
                    }

                    PendingBracket pb;
                    int totalFilledQty;
                    double avgEntryPrice;
                    bool shouldSubmitInitialBracket;
                    bool shouldResizeWorkingBracket;

                    lock (_gate)
                    {
                        if (!_pendingBrackets.TryGetValue(entryName, out pb) || pb == null)
                        {
                            Log($"[BRACKET MISS] No pending bracket found for entry '{entryName}'");
                            return;
                        }

                        pb.FilledQty += fillQty;
                        pb.EntryValueSum += fillPrice * fillQty;

                        totalFilledQty = pb.FilledQty;
                        avgEntryPrice = pb.EntryValueSum / Math.Max(1, pb.FilledQty);

                        shouldSubmitInitialBracket =
                            !pb.BracketSubmitted &&
                            (pb.StopTicks > 0 || pb.TargetTicks > 0) &&
                            totalFilledQty > 0;

                        if (shouldSubmitInitialBracket)
                            pb.BracketSubmitted = true;

                        shouldResizeWorkingBracket =
                            pb.BracketSubmitted &&
                            !shouldSubmitInitialBracket &&
                            totalFilledQty > 0;
                    }

                    SafeTradeSuiteRuntime.PrintLog(
                        $"[BRACKET FILL ACCUM] entry={entryName} fillQty={fillQty} totalFilledQty={totalFilledQty} avgEntry={avgEntryPrice:0.00}");

                    if (shouldSubmitInitialBracket)
                    {
                        var pbStopTicks = pb.StopTicks;
                        var pbTargetTicks = pb.TargetTicks;
                        var pbIsBuy = pb.IsBuy;

                        var oco = "STC:BRK:" + Guid.NewGuid().ToString("N");
                        var exitAction = pbIsBuy ? OrderAction.Sell : OrderAction.BuyToCover;

                        var orders = new List<Order>(2);

                        var stopPrice = 0.0;
                        var targetPrice = 0.0;
                        string stopOrderName = null;
                        string targetOrderName = null;

                        if (pbTargetTicks > 0)
                        {
                            targetPrice = pbIsBuy
                                ? avgEntryPrice + pbTargetTicks * tickSize
                                : avgEntryPrice - pbTargetTicks * tickSize;

                            targetPrice = RoundToTick(targetPrice, tickSize);

                            var tgt = account.CreateOrder(
                                instr,
                                exitAction,
                                OrderType.Limit,
                                OrderEntry.Manual,
                                TimeInForce.Day,
                                totalFilledQty,
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

                        if (pbStopTicks > 0)
                        {
                            stopPrice = pbIsBuy
                                ? avgEntryPrice - pbStopTicks * tickSize
                                : avgEntryPrice + pbStopTicks * tickSize;

                            stopPrice = RoundToTick(stopPrice, tickSize);

                            var stp = account.CreateOrder(
                                instr,
                                exitAction,
                                OrderType.StopMarket,
                                OrderEntry.Manual,
                                TimeInForce.Day,
                                totalFilledQty,
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

                        if (orders.Count > 0)
                        {
                            lock (_gate)
                            {
                                pb.BracketOco = oco;

                                _activeBracketByAccInstr[BracketKey(account, instr)] =
                                    new ActiveBracketSpec
                                    {
                                        AutoBeSuppressedUntilFlat = false,
                                        StopTicks = pbStopTicks,
                                        TargetTicks = pbTargetTicks,
                                        IsBuy = pbIsBuy,
                                        Qty = totalFilledQty,
                                        EntryFilledQty = totalFilledQty,
                                        EntryValueSum = avgEntryPrice * totalFilledQty,
                                        EntryPrice = avgEntryPrice,
                                        OriginalStopPrice = stopPrice,
                                        CurrentStopPrice = stopPrice,
                                        TargetPrice = targetPrice,
                                        IsFreeTradeApplied = false,
                                        StopOrderName = stopOrderName,
                                        TargetOrderName = targetOrderName,
                                        StopOco = oco
                                    };
                            }

                            SafeTradeSuiteRuntime.PrintLog(
                                $"[BRACKET SUBMIT] acc={account.Name} instr={instr.FullName} avgEntry={avgEntryPrice:0.00} qty={totalFilledQty} oco={oco} stopPrice={stopPrice:0.00} targetPrice={targetPrice:0.00}");

                            try
                            {
                                account.Submit(orders.ToArray());
                                Log($"Bracket submitted -> {account.Name} {instr.FullName} OCO={oco} qty={totalFilledQty} @ avgEntry={avgEntryPrice:0.00}");
                            }
                            catch
                            {
                                ClearActiveBracket(account, instr);
                                throw;
                            }
                        }
                    }
                    else if (shouldResizeWorkingBracket)
                    {
                        if (TryGetActiveBracketSpec(account, instr, out var spec) && spec != null)
                        {
                            SafeTradeSuiteRuntime.PrintLog(
                                $"[BRACKET RESIZE PATH] acc={account.Name} instr={instr.FullName} entry={entryName} " +
                                $"currentSpecQty={spec.Qty} newQty={totalFilledQty} currentEntry={spec.EntryPrice:0.00} newEntry={avgEntryPrice:0.00} oco={spec.StopOco}");

                            try
                            {
                                ResizeAndRepriceWorkingBracket(account, instr, spec, totalFilledQty, avgEntryPrice);
                            }
                            catch (Exception ex)
                            {
                                Log($"[BRACKET RESIZE FAILED] acc={account.Name} instr={instr.FullName} msg={ex.Message}");
                            }
                        }
                        else
                        {
                            Log(
                                $"[BRACKET RESIZE SKIPPED] acc={account.Name} instr={instr.FullName} entry={entryName} " +
                                $"reason=no-active-spec totalFilledQty={totalFilledQty} avgEntry={avgEntryPrice:0.00}");
                        }
                    }

                    lock (_gate)
                    {
                        if (_pendingBrackets.TryGetValue(entryName, out pb) &&
                            pb != null && pb.FilledQty >= pb.OriginalQty)
                        {
                            _pendingBrackets.Remove(entryName);

                            SafeTradeSuiteRuntime.PrintLog(
                                $"[BRACKET PENDING COMPLETE] entry={entryName} filledQty={pb.FilledQty} originalQty={pb.OriginalQty}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUnhandled("TrySubmitBracketOnFill", ex);
                }
            }

            private void SubmitFollowerMarketWithBracket(Account acc, Instrument instr, OrderAction action, int qty, string atmTemplateName, string entryName)
            {
                if (acc == null || instr == null)
                    return;

                if (!TryReadAtmTemplateBasic(atmTemplateName, out var stopTicks, out var targetTicks))
                {
                    Log($"Follower ATM template parse failed: '{atmTemplateName}'. Submitting entry only.");
                    stopTicks = 0;
                    targetTicks = 0;
                }

                var entry = acc.CreateOrder(
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
                    if (_pendingBrackets.TryGetValue(entryName, out var existing) && existing != null)
                    {
                        existing.OriginalQty += Math.Max(1, qty);
                        existing.StopTicks = Math.Max(0, stopTicks);
                        existing.TargetTicks = Math.Max(0, targetTicks);
                        existing.IsBuy = action == OrderAction.Buy;

                        SafeTradeSuiteRuntime.PrintLog(
                            $"[FOLLOWER PENDING UPSERT] entry={entryName} addQty={qty} originalQty={existing.OriginalQty} " +
                            $"filledQty={existing.FilledQty} stopTicks={existing.StopTicks} targetTicks={existing.TargetTicks}");
                    }
                    else
                    {
                        _pendingBrackets[entryName] = new PendingBracket
                        {
                            EntryName = entryName,
                            OriginalQty = Math.Max(1, qty),
                            IsBuy = action == OrderAction.Buy,
                            StopTicks = Math.Max(0, stopTicks),
                            TargetTicks = Math.Max(0, targetTicks),
                            FilledQty = 0,
                            EntryValueSum = 0.0,
                            BracketSubmitted = false,
                            BracketOco = null
                        };

                        SafeTradeSuiteRuntime.PrintLog(
                            $"[FOLLOWER PENDING NEW] entry={entryName} originalQty={Math.Max(1, qty)} " +
                            $"stopTicks={Math.Max(0, stopTicks)} targetTicks={Math.Max(0, targetTicks)}");
                    }
                }

                Log($"Follower submit -> {acc.Name}: {action} MKT qty={qty} instr={instr.FullName} ATM='{atmTemplateName}' (ST={stopTicks} TK={targetTicks})");
                acc.Submit(new[] { entry });
            }
        }
    }
}