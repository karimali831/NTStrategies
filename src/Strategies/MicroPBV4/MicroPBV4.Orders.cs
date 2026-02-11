#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
    {
        protected override void OnOrderUpdate(
            Order order,
            double limitPrice,
            double stopPrice,
            int quantity,
            int filled,
            double averageFillPrice,
            OrderState orderState,
            DateTime time,
            ErrorCode error,
            string comment)
        {
            if (order == null)
                return;

            if (IsEntryOrderName(order.Name))
            {
                var key = GetEntryOrderKey(order);

                switch (orderState)
                {
                    case OrderState.Accepted:
                    case OrderState.Working:
                    case OrderState.PartFilled:
                    {
                        if (!_entryOrderBirth.ContainsKey(key))
                            _entryOrderBirth[key] = (time != DateTime.MinValue ? time : Time[0]);
                        break;
                    }
                    case OrderState.Filled:
                    case OrderState.Cancelled:
                    case OrderState.Rejected:
                        _entryOrderBirth.Remove(key);
                        break;
                }
            }

            if (_duplicateBlocked)
            {
                TryDuplicateSafetyCleanup("OrderUpdate while duplicate-blocked");
                return;
            }

            var looksProtective =
                order.Name != null &&
                (order.Name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0
                 || order.Name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
                 || order.Name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0);

            if (looksProtective)
            {
                switch (orderState)
                {
                    case OrderState.Filled:
                        protectiveSeenSinceEntry = true;
                        break;
                    case OrderState.Rejected when Position.MarketPosition != MarketPosition.Flat:
                    {
                        if (DebugMode)
                        {
                            Print(string.Format(
                                "[PROTECTIVE REJECT -> FLATTEN] {0:yyyy-MM-dd HH:mm:ss.fff} Name={1} State={2} Err={3} Msg={4}",
                                time, order.Name, orderState, error, comment));
                        }

                        CancelWorkingOrders();
                        ForceFlatten("PROTECTIVE_REJECT");
                        return;
                    }
                }
            }

            if (orderState == OrderState.Rejected && DebugMode)
            {
                var bid = GetCurrentBid();
                var ask = GetCurrentAsk();

                Print($"[ORDER REJECTED] {time:yyyy-MM-dd HH:mm:ss.fff} Name={order.Name}, Action={order.OrderAction}, Type={order.OrderType}, Qty={quantity}, Stop={stopPrice}, Limit={limitPrice}, Bid={bid}, Ask={ask}, ErrorCode={error}, Msg={comment}");
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (_duplicateBlocked)
            {
                TryDuplicateSafetyCleanup("ExecutionUpdate while duplicate-blocked");
                return;
            }

            var name = execution.Order.Name ?? string.Empty;

            var isLongEntry =
                name.StartsWith("MPB_LONG_", StringComparison.OrdinalIgnoreCase)
                && execution.Order.OrderAction == OrderAction.Buy;

            var isShortEntry =
                name.StartsWith("MPB_SHORT_", StringComparison.OrdinalIgnoreCase)
                && execution.Order.OrderAction == OrderAction.SellShort;

            if (isLongEntry || isShortEntry)
            {
                _activeEntryTag = name; 
                
                entryBarIdx = CurrentBar;
                entryPriceHard = execution.Price;
                entryFillTime = time;
                hardStopTriggered = false;

                protectiveSeenSinceEntry = false;

                _wasInPosition = true;
                _entrySide = isLongEntry ? MarketPosition.Long : MarketPosition.Short;
                _entryQty = execution.Quantity;

                _mfeTicks = 0.0;
                _maeTicks = 0.0;

                if (DebugMode)
                {
                    Print(
                        $"[ENTRY FILL] {time:yyyy-MM-dd HH:mm:ss.fff} name={name} price={execution.Price} qty={execution.Quantity} emergencyTicks={EmergencyStopTicks}");
                }
            }

            if (_wasInPosition && Position.MarketPosition == MarketPosition.Flat &&
                execution.Order.OrderState == OrderState.Filled)
            {
                lastFlatExecutionTime = time;

                var pnlCur = 0.0;
                var pnlTicks = 0.0;

                var trades = SystemPerformance?.AllTrades;
                if (trades != null && trades.Count > 0)
                {
                    var t = trades[trades.Count - 1];
                    pnlCur = t.ProfitCurrency * Contracts;

                    double profitPoints;
                    try
                    {
                        profitPoints = t.ProfitPoints;
                    }
                    catch
                    {
                        profitPoints = 0.0;
                    }

                    if (profitPoints != 0.0)
                        pnlTicks = profitPoints / TickSize;
                    else
                    {
                        var dir = _entrySide == MarketPosition.Long ? 1.0 : -1.0;
                        pnlTicks = dir * (execution.Price - entryPriceHard) / TickSize;
                    }
                }

                var hold = entryFillTime != DateTime.MinValue ? (time - entryFillTime) : TimeSpan.Zero;
                var outcome = pnlCur >= 0 ? "WIN" : "LOSS";
                
                if (DebugMode)
                {
                    Print(
                        $"[ENTRY FLAT] {time:yyyy-MM-dd HH:mm:ss.fff} outcome={outcome} " +
                        $"pnl={pnlCur:0.00} ticks={pnlTicks:0.0} " +
                        $"hold={hold.TotalSeconds:0}s " +
                        $"mfeTicks={_mfeTicks:0.0} maeTicks={_maeTicks:0.0}"
                    );
                }

                var rj = GetRegimeForTag(_activeEntryTag);

                _flatTradeSummaries.Add(new FlatTradeSummary
                {
                    ExitTime     = time,
                    Tag          = _activeEntryTag ?? "",
                    Outcome      = outcome,
                    PnlCur       = pnlCur,
                    PnlTicks     = pnlTicks,
                    HoldSeconds  = hold.TotalSeconds,
                    MfeTicks     = _mfeTicks,
                    MaeTicks     = _maeTicks,
                    RegimeJson   = rj ?? ""
                });

                // cleanup once trade is complete
                ForgetRegimeForTag(_activeEntryTag);
                _activeEntryTag = "";

                _wasInPosition = false;
                _entrySide = MarketPosition.Flat;
                _entryQty = 0;
                _mfeTicks = 0.0;
                _maeTicks = 0.0;

                entryPriceHard = 0.0;
                entryFillTime = DateTime.MinValue;
                protectiveSeenSinceEntry = false;
                entryBarIdx = -1;
            }
        }

        private void TryDuplicateSafetyCleanup(string reason)
        {
            if (_duplicateCleanupDone)
                return;

            _duplicateCleanupDone = true;

            var acc = Account != null ? Account.Name : "N/A";
            var inst = Instrument != null ? Instrument.FullName : "N/A";

            if (DebugMode)
                Print($"[DUPLICATE CLEANUP] {_instanceGuid} reason={reason} acc={acc} inst={inst}");
            
            CancelWorkingOrders();
            
            var flattened = false;

            if (Account != null && Instrument != null)
            {
                Account.Flatten(new[] { Instrument });
                flattened = true;
            }

            if (!flattened)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    ForceFlatten("DUPLICATE_INSTANCE");
            }
        }

        // ----- working entry order timeout -----
        private static bool IsEntryOrderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return name.StartsWith("MPB_LONG_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("MPB_SHORT_", StringComparison.OrdinalIgnoreCase);
        }

        private string GetEntryOrderKey(Order o)
        {
            var id = o?.OrderId;

            if (!string.IsNullOrEmpty(id))
                return id;

            var inst = o?.Instrument != null ? o.Instrument.FullName : "N/A";
            var name = o?.Name ?? "N/A";
            return inst + "|" + name;
        }

        private void CancelStaleEntryOrders(DateTime now)
        {
            if (EntryOrderTimeoutMinutes <= 0)
                return;

            if (IsInStrategyAnalyzer)
                return;

            if (Account == null || Instrument == null)
                return;

            try
            {
                foreach (var o in Account.Orders)
                {
                    if (o?.Instrument == null) continue;
                    if (o.Instrument.FullName != Instrument.FullName) continue;

                    if (!IsEntryOrderName(o.Name)) continue;

                    if (o.OrderState != OrderState.Working && o.OrderState != OrderState.Accepted && o.OrderState != OrderState.PartFilled)
                        continue;

                    var key = GetEntryOrderKey(o);

                    if (!_entryOrderBirth.TryGetValue(key, out var born) || born == DateTime.MinValue)
                    {
                        var seed = now;
                        if (o.Time != DateTime.MinValue)
                            seed = o.Time;

                        _entryOrderBirth[key] = seed;
                        born = seed;
                    }

                    if ((now - born).TotalMinutes >= EntryOrderTimeoutMinutes)
                    {
                        if (DebugMode)
                            Print($"[ENTRY TIMEOUT] {now:yyyy-MM-dd HH:mm:ss.fff} canceling {o.Name} state={o.OrderState} ageMin={(now - born).TotalMinutes:0.0} >= {EntryOrderTimeoutMinutes}");

                        Account.Cancel(new[] { o });
                        _entryOrderBirth.Remove(key);
                    }
                }
            }
            catch (Exception ex)
            {
                if (DebugMode)
                    Print("[WARN] CancelStaleEntryOrders failed: " + ex.Message);
            }
        }
    }
}
