using System;
using System.Globalization;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeDisplacementV1
    {
        private void SubmitQualifiedEntry(BreakoutAttempt attempt, double gapTicks)
        {
            int direction = attempt.Direction;
            if (!InEntryWindow())
            {
                WriteDiagnostic("SKIP_TIME", direction, attempt, "Signal confirmed outside entry window.");
                return;
            }

            if (entryPending || Position.MarketPosition != MarketPosition.Flat)
            {
                WriteDiagnostic("SKIP_OCCUPIED", direction, attempt, "Entry pending or existing strategy position.");
                return;
            }

            if (MaxTradesPerDay > 0 && submittedToday >= MaxTradesPerDay)
            {
                WriteDiagnostic("SKIP_DAILY_CAP", direction, attempt, "MaxTradesPerDay reached.");
                return;
            }

            double close = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double stop = Instrument.MasterInstrument.RoundToTickSize(attempt.Stop);
            double risk = direction > 0 ? close - stop : stop - close;
            if (risk < TickSize * 0.999999)
            {
                WriteDiagnostic("SKIP_BAD_STOP", direction, attempt,
                    "Breakout candle stop is not behind signal close; stop=" + stop.ToString(CultureInfo.InvariantCulture));
                return;
            }

            pendingDirection = direction;
            pendingStop = stop;
            pendingReferenceClose = close;
            pendingTarget = Instrument.MasterInstrument.RoundToTickSize(close + direction * risk * RiskRewardRatio);
            string name = direction > 0 ? LongSignal : ShortSignal;

            // Protective orders are configured BEFORE entry, not after a fill callback.
            // Historical market orders execute on the next bar; the target is re-priced
            // using the actual average fill in OnExecutionUpdate to preserve true R:R.
            SetStopLoss(name, CalculationMode.Price, pendingStop, false);
            SetProfitTarget(name, CalculationMode.Price, pendingTarget);
            entryPending = true;
            submittedToday++;

            WriteDiagnostic("ORDER_SUBMITTED", direction, attempt,
                "ReferenceClose=" + close.ToString(CultureInfo.InvariantCulture)
                + "; Stop=" + pendingStop.ToString(CultureInfo.InvariantCulture)
                + "; ProvisionalTarget=" + pendingTarget.ToString(CultureInfo.InvariantCulture)
                + "; FvgTicks=" + gapTicks.ToString("0.##", CultureInfo.InvariantCulture)
                + "; R=" + RiskRewardRatio.ToString(CultureInfo.InvariantCulture));

            if (direction > 0)
                EnterLong(TradingQuantity, LongSignal);
            else
                EnterShort(TradingQuantity, ShortSignal);
        }

        protected override void OnExecutionUpdate(
            Execution execution, string executionId, double price,
            int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            string name = execution.Order.Name ?? "";
            bool isLong = name == LongSignal;
            bool isShort = name == ShortSignal;
            if (!isLong && !isShort)
            {
                WriteDiagnostic("EXIT_EXECUTION", 0, null,
                    "Order=" + name + "; Fill=" + price.ToString(CultureInfo.InvariantCulture)
                    + "; Quantity=" + quantity + "; Position=" + marketPosition);
                return;
            }

            int direction = isLong ? 1 : -1;
            double fill = execution.Order.AverageFillPrice > 0
                ? execution.Order.AverageFillPrice : price;
            double actualRisk = direction * (fill - pendingStop);

            WriteDiagnostic("ENTRY_EXECUTION", direction, null,
                "Order=" + name
                + "; Fill=" + fill.ToString(CultureInfo.InvariantCulture)
                + "; Quantity=" + quantity
                + "; OrderState=" + execution.Order.OrderState
                + "; InitialStop=" + pendingStop.ToString(CultureInfo.InvariantCulture)
                + "; RiskTicks=" + (actualRisk / TickSize).ToString("0.##", CultureInfo.InvariantCulture));

            // A large next-bar gap can invalidate an otherwise valid candle-based stop.
            // Close immediately; do not replace the stop with an arbitrary tick offset.
            if (actualRisk < TickSize * 0.999999)
            {
                WriteDiagnostic("INVALID_GAP_FILL", direction, null, "Fill reached/passed candle-anchored stop: flattening.");
                if (isLong) ExitLong("InvalidGapStop", LongSignal);
                else ExitShort("InvalidGapStop", ShortSignal);
            }
            else
            {
                pendingTarget = Instrument.MasterInstrument.RoundToTickSize(
                    fill + direction * actualRisk * RiskRewardRatio);
                SetProfitTarget(name, CalculationMode.Price, pendingTarget);
                WriteDiagnostic("BRACKET_REPRICED", direction, null,
                    "ActualFill=" + fill.ToString(CultureInfo.InvariantCulture)
                    + "; Stop=" + pendingStop.ToString(CultureInfo.InvariantCulture)
                    + "; Target=" + pendingTarget.ToString(CultureInfo.InvariantCulture)
                    + "; R=" + RiskRewardRatio.ToString(CultureInfo.InvariantCulture));
            }

            if (execution.Order.OrderState == OrderState.Filled)
                entryPending = false;
        }

        protected override void OnOrderUpdate(
            Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            string name = order.Name ?? "";
            if (orderState == OrderState.Rejected)
                WriteDiagnostic("ORDER_REJECTED", name == LongSignal ? 1 : name == ShortSignal ? -1 : 0, null,
                    "Order=" + name + "; Error=" + error + "; Comment=" + comment);
            else if (orderState == OrderState.Cancelled)
                WriteDiagnostic("ORDER_CANCELLED", name == LongSignal ? 1 : name == ShortSignal ? -1 : 0, null,
                    "Order=" + name + "; Filled=" + filled + "; Comment=" + comment);

            if ((name == LongSignal || name == ShortSignal)
                && (orderState == OrderState.Rejected || orderState == OrderState.Cancelled))
                entryPending = false;
        }
    }
}