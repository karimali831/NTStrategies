using System;
using System.Collections.Generic;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeDisplacementV1
    {
        private int orderSequence;
        private string activeEntrySignal = "";
        private int activeDirection;
        private int entryFilledQuantity;
        private int exitFilledQuantity;
        private double entryFillNotional;
        private double tradeGrossPnl;
        private double tradeStopPrice;
        private bool pausedToday;
        private bool fatalOrderError;
        private readonly HashSet<string> outstandingExitSignals = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> exitOco = new Dictionary<string, string>(StringComparer.Ordinal);

        private static bool IsTerminal(OrderState state)
        {
            return state == OrderState.Filled || state == OrderState.Cancelled
                || state == OrderState.Rejected;
        }

        private void SubmitQualifiedEntry(BreakoutAttempt attempt, double gapTicks)
        {
            int direction = attempt.Direction;
            if (!InEntryWindow() || pausedToday || fatalOrderError)
            {
                WriteDiagnostic("SKIP_ENTRY_GATE", direction, attempt, "Time gate, daily pause or order-safety halt.");
                return;
            }
            if (entryPending || Position.MarketPosition != MarketPosition.Flat
                || outstandingExitSignals.Count > 0)
            {
                WriteDiagnostic("SKIP_OCCUPIED", direction, attempt,
                    "Entry pending, position open or prior OCO cancellation outstanding.");
                return;
            }
            if (MaxTradesPerDay > 0 && submittedToday >= MaxTradesPerDay)
            {
                WriteDiagnostic("SKIP_DAILY_CAP", direction, attempt, "MaxTradesPerDay reached.");
                return;
            }

            double signalClose = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double stop = Instrument.MasterInstrument.RoundToTickSize(attempt.Stop);
            double preliminaryRisk = direction * (signalClose - stop);
            if (preliminaryRisk < TickSize * 0.999999)
            {
                WriteDiagnostic("SKIP_BAD_STOP", direction, attempt,
                    "Breakout candle stop is not behind confirmed signal close.");
                return;
            }

            orderSequence++;
            activeEntrySignal = (direction > 0 ? "NXOR_L_" : "NXOR_S_") + orderSequence;
            activeDirection = direction;
            pendingDirection = direction;
            pendingStop = tradeStopPrice = stop;
            pendingReferenceClose = signalClose;
            pendingTarget = double.NaN;
            entryFilledQuantity = 0;
            exitFilledQuantity = 0;
            entryFillNotional = 0;
            tradeGrossPnl = 0;
            entryPending = true;
            submittedToday++;

            WriteDiagnostic("ORDER_SUBMITTED", direction, attempt,
                "EntrySignal=" + activeEntrySignal
                + "; SignalClose=" + F(signalClose)
                + "; CandleStop=" + F(stop)
                + "; SignalRiskTicks=" + F(preliminaryRisk / TickSize)
                + "; FvgTicks=" + F(gapTicks));

            // Unlike SetStopLoss/SetProfitTarget, no attached stop is sent blindly
            // on an adverse next-bar fill. A new OCO pair is created only AFTER
            // the fill has passed the candle-stop/market validity check.
            SubmitOrderUnmanaged(0,
                direction > 0 ? OrderAction.Buy : OrderAction.SellShort,
                OrderType.Market, TradingQuantity, 0, 0, "", activeEntrySignal);
        }

        private double ConservativeMarketReference(int direction, double fill)
        {
            // Availability of real bid/ask data is NOT required for OHLC-only
            // backtesting; historical/playback without quotes falls back to fill.
            // Live quotes, when present, can only make validation stricter.
            double quote = direction > 0 ? GetCurrentBid() : GetCurrentAsk();
            if (double.IsNaN(quote) || double.IsInfinity(quote) || quote <= 0)
                return fill;
            return direction > 0 ? Math.Min(fill, quote) : Math.Max(fill, quote);
        }

        private static string F(double price)
        {
            return price.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private void SendMarketSafetyExit(int direction, int quantity, string reason, DateTime time)
        {
            pausedToday = true;
            WriteDiagnostic("EMERGENCY_EXIT_SUBMITTED", direction, null,
                "Reason=" + reason + "; Quantity=" + quantity, time);
            SubmitOrderUnmanaged(0,
                direction > 0 ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.Market, quantity, 0, 0, "",
                "NXOR_EM_" + (++orderSequence));
        }

        private void PlaceProtectionForFill(int direction, int quantity, double fill, DateTime time)
        {
            if (quantity <= 0)
                return;

            double currentReference = ConservativeMarketReference(direction, fill);
            double target;
            string reason;
            if (!NinjexOpeningBracketMath.TryBracket(direction, fill, tradeStopPrice,
                    currentReference, TickSize, RiskRewardRatio, out target, out reason))
            {
                WriteDiagnostic("INVALID_GAP_FILL", direction, null,
                    "Reason=" + reason + "; Fill=" + F(fill)
                    + "; MarketReference=" + F(currentReference)
                    + "; CandleStop=" + F(tradeStopPrice)
                    + "; Quantity=" + quantity, time);
                SendMarketSafetyExit(direction, quantity, reason, time);
                return;
            }

            target = Instrument.MasterInstrument.RoundToTickSize(target);
            string suffix = (++orderSequence).ToString(CultureInfo.InvariantCulture);
            string stopName = "NXOR_ST_" + suffix;
            string targetName = "NXOR_PT_" + suffix;
            string uniqueOco = "NXOR_" + Guid.NewGuid().ToString("N");

            pendingTarget = target;
            outstandingExitSignals.Add(stopName);
            exitOco[stopName] = uniqueOco;
            WriteDiagnostic("BRACKET_SUBMITTED", direction, null,
                "OCO=" + uniqueOco + "; StopSignal=" + stopName
                + "; TargetSignal=" + targetName
                + "; EntrySignal=" + activeEntrySignal
                + "; Quantity=" + quantity
                + "; ActualFill=" + F(fill)
                + "; Stop=" + F(tradeStopPrice)
                + "; Target=" + F(target)
                + "; RiskTicks=" + F(Math.Abs(fill - tradeStopPrice) / TickSize)
                + "; RiskUSD=" + F(Math.Abs(fill - tradeStopPrice)
                    * Instrument.MasterInstrument.PointValue * quantity), time);

            // The stop is attempted FIRST. If Playback fills/rejects it inline,
            // do not submit a second order with an OCO group already completed.
            SubmitOrderUnmanaged(0,
                direction > 0 ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket, quantity, 0, tradeStopPrice, uniqueOco, stopName);

            if (fatalOrderError || !outstandingExitSignals.Contains(stopName)
                || Position.MarketPosition == MarketPosition.Flat)
            {
                WriteDiagnostic("BRACKET_INTERRUPTED", direction, null,
                    "Stop completed/rejected or position flat before target submission; OCO=" + uniqueOco, time);
                return;
            }

            outstandingExitSignals.Add(targetName);
            exitOco[targetName] = uniqueOco;
            SubmitOrderUnmanaged(0,
                direction > 0 ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.Limit, quantity, target, 0, uniqueOco, targetName);
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price,
            int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null)
                return;
            string name = execution.Name ?? "";
            bool isEntry = string.Equals(name, activeEntrySignal, StringComparison.Ordinal);

            // Never infer fill time from Time[0]; use callback execution timestamp.
            WriteDiagnostic(isEntry ? "ENTRY_EXECUTION" : "EXIT_EXECUTION",
                isEntry ? activeDirection : 0, null,
                "Order=" + name + "; OrderId=" + orderId
                + "; ExecutionId=" + executionId
                + "; Quantity=" + quantity
                + "; Price=" + F(price)
                + "; PositionAfter=" + marketPosition
                + "; CandleStop=" + F(tradeStopPrice)
                + "; Target=" + F(pendingTarget)
                + "; OCO=" + (exitOco.ContainsKey(name) ? exitOco[name] : ""), time);

            if (isEntry)
            {
                entryFilledQuantity += quantity;
                entryFillNotional += price * quantity;
                PlaceProtectionForFill(activeDirection, quantity, price, time);
                return;
            }

            if (name.StartsWith("NXOR_ST_", StringComparison.Ordinal)
                || name.StartsWith("NXOR_PT_", StringComparison.Ordinal)
                || name.StartsWith("NXOR_EM_", StringComparison.Ordinal))
            {
                exitFilledQuantity += quantity;
                double averageEntry = entryFilledQuantity > 0
                    ? entryFillNotional / entryFilledQuantity : price;
                double realized = activeDirection * (price - averageEntry)
                    * quantity * Instrument.MasterInstrument.PointValue;
                tradeGrossPnl += realized;
                WriteDiagnostic("EXIT_PNL", activeDirection, null,
                    "Order=" + name + "; FillQty=" + quantity
                    + "; GrossUSD=" + F(realized)
                    + "; GrossAccumulatedUSD=" + F(tradeGrossPnl)
                    + "; EntryAverage=" + F(averageEntry)
                    + "; ExitPrice=" + F(price), time);
                if (exitFilledQuantity >= entryFilledQuantity
                    && !entryPending && entryFilledQuantity > 0)
                    WriteDiagnostic("TRADE_COMPLETE", activeDirection, null,
                        "Entry=" + activeEntrySignal
                        + "; Quantity=" + entryFilledQuantity
                        + "; AvgEntry=" + F(averageEntry)
                        + "; GrossUSD=" + F(tradeGrossPnl)
                        + "; CommissionExcluded=True", time);
            }
        }

        protected override void OnOrderUpdate(
            Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            string name = order.Name ?? "";
            // Full order-event audit: catches IDs, OCO reuse, rejections,
            // partial fills, working/cancel states.
            WriteDiagnostic("ORDER_STATUS", 0, null,
                "Order=" + name + "; OrderId=" + order.OrderId
                + "; OCO=" + order.Oco
                + "; State=" + orderState
                + "; Action=" + order.OrderAction
                + "; Type=" + order.OrderType
                + "; Quantity=" + quantity + "; Filled=" + filled
                + "; AvgFill=" + F(averageFillPrice)
                + "; StopPrice=" + F(stopPrice)
                + "; LimitPrice=" + F(limitPrice)
                + "; Error=" + error + "; Comment=" + comment, time);

            if (string.Equals(name, activeEntrySignal, StringComparison.Ordinal)
                && (IsTerminal(orderState)))
                entryPending = false;

            if (name.StartsWith("NXOR_ST_", StringComparison.Ordinal)
                || name.StartsWith("NXOR_PT_", StringComparison.Ordinal))
            {
                if (IsTerminal(orderState))
                    outstandingExitSignals.Remove(name);
            }

            if (orderState == OrderState.Rejected || error != ErrorCode.NoError)
            {
                // Preserve StopCancelClose default: NT must stop and flatten on
                // genuine protective-order rejection. Never ignore order errors.
                fatalOrderError = true;
                WriteDiagnostic("ORDER_REJECTED", 0, null,
                    "Order=" + name + "; OCO=" + order.Oco
                    + "; Error=" + error + "; Comment=" + comment
                    + "; Strategy default StopCancelClose remains enabled.", time);
            }
        }
    }
}