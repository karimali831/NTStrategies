using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        #region Tick execution

        private void ProcessTickSeries()
        {
            if (CurrentBars[TickSeriesIndex] < 1)
                return;


            var time =
                Times[TickSeriesIndex][0];

            var price =
                Closes[TickSeriesIndex][0];


            EnsureTradingDate(
                time.Date,
                time);

            // Restart-safe daily risk state. Historical always returns immediately.
            // When enabled, genuine live and Playback State.Realtime both use
            // the same durable recovery path before a new entry can be submitted.
            EnsureLiveDailyStateReady(time);


            var timeValue =
                ToTimeValue(
                    time);


            //
            // RTH flatten and pending-entry cancellation.
            //
            if (timeValue >= FlattenTime)
            {
                ClearPendingEntry(
                    time,
                    "FlattenTime");

                CancelPendingEntryOrder(
                    time);

                FlattenPosition(
                    time);

                return;
            }


            if (TryExitForMaximumHold(
                    time))
            {
                return;
            }


            if (pendingDirection
                == PendingDirection.None)
            {
                return;
            }


            //
            // The 1-minute signal is only known once that candle has
            // completed. Never execute against an older tick cached by
            // Playback; wait for a tick at/after the new minute.
            //
            if (time
                < pendingEarliestExecutionTime)
            {
                return;
            }


            if (timeValue >= EntryEndTime)
            {
                ClearPendingEntry(
                    time,
                    "EntryWindowClosed");

                return;
            }


            if (!CanTakeNewTrade(
                    time,
                    true))
            {
                ClearPendingEntry(
                    time,
                    "DailyRiskLimit");

                return;
            }


            if (Position.MarketPosition
                != MarketPosition.Flat)
            {
                ClearPendingEntry(
                    time,
                    "PositionNotFlat");

                return;
            }


            if (entryOrderPending
                || manualExitPending)
            {
                return;
            }


            SubmitPendingEntry(
                time,
                price);
        }


        private void SubmitPendingEntry(
            DateTime time,
            double observedMarketPrice)
        {
            var direction =
                pendingDirection;

            if (direction
                == PendingDirection.None)
            {
                return;
            }


            var signalName =
                pendingEntrySignal;

            var modelName =
                pendingModelName;

            if (string.IsNullOrEmpty(signalName))
            {
                ClearPendingEntry(
                    time,
                    "MissingEntrySignal");

                return;
            }

            submittedSignalTime =
                pendingSignalTime;


            //
            // Fill-relative managed brackets.
            //
            // SetStopLoss / SetProfitTarget with CalculationMode.Ticks
            // are intentionally configured before the market entry so
            // NinjaTrader anchors the protective orders to the actual
            // execution fill rather than the pre-submission observed tick.
            //
            VerifiedLiveBracketOverrideEntry verifiedLiveBracket;
            var verifiedLiveBracketApplied =
                TryGetVerifiedLiveBracketOverride(pendingSignalTime, signalName, out verifiedLiveBracket);

            if (verifiedLiveBracketApplied)
            {
                SetStopLoss(signalName, CalculationMode.Price, verifiedLiveBracket.StopPrice, false);
                SetProfitTarget(signalName, CalculationMode.Price, verifiedLiveBracket.TargetPrice);
                Diagnostic(
                    time,
                    "VERIFIED LIVE BRACKET MIRROR Signal={0} SignalTime={1:yyyy-MM-dd HH:mm:ss} VerifiedEntry={2} Stop={3} Target={4} Source='{5}'",
                    signalName, pendingSignalTime, verifiedLiveBracket.VerifiedEntryPrice,
                    verifiedLiveBracket.StopPrice, verifiedLiveBracket.TargetPrice, verifiedLiveBracket.Source);
            }
            else
            {
                SetStopLoss(signalName, CalculationMode.Ticks, StopLossTicks, false);
                SetProfitTarget(signalName, CalculationMode.Ticks, ProfitTargetTicks);
            }


            entryOrderPending = true;


            Diagnostic(
                time,
                "ENTRY SUBMIT " +
                "Model={0} Signal={1} Direction={2} " +
                "ObservedMarket={3} " +
                "SignalTime={4:HH:mm:ss} " +
                "Stop={5}t Target={6}t " +
                "Qty={7}",
                modelName,
                signalName,
                direction,
                observedMarketPrice,
                pendingSignalTime,
                StopLossTicks,
                ProfitTargetTicks,
                OrderQuantity);


            if (direction
                == PendingDirection.Long)
            {
                EnterLong(
                    TickSeriesIndex,
                    OrderQuantity,
                    signalName);
            }
            else
            {
                EnterShort(
                    TickSeriesIndex,
                    OrderQuantity,
                    signalName);
            }


            pendingDirection =
                PendingDirection.None;
        }

        #endregion

        #region Order / execution callbacks

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
            string nativeError)
        {
            if (order == null)
                return;


            if (IsEntrySignalName(
                    order.Name))
            {
                activeEntryOrder =
                    order;


                if (orderState
                    == OrderState.Rejected
                    || orderState
                    == OrderState.Cancelled)
                {
                    entryOrderPending = false;

                    activeEntryOrder = null;

                    if (filled <= 0
                        && !activeTradeCounted)
                    {
                        ClearPendingResearchSignal();

                        submittedSignalTime =
                            Core.Globals.MinDate;

                        activeMaxHoldExitTime =
                            Core.Globals.MinDate;
                    }


                    Diagnostic(
                        time,
                        "ENTRY ORDER END " +
                        "Name={0} State={1} Error={2} Native={3}",
                        order.Name,
                        orderState,
                        error,
                        nativeError);
                }
            }


            if (order.Name == LongEodExitSignal
                || order.Name == ShortEodExitSignal
                || order.Name == LongTimeExitSignal
                || order.Name == ShortTimeExitSignal)
            {
                if (orderState == OrderState.Rejected
                    || orderState == OrderState.Cancelled
                    || orderState == OrderState.Filled)
                {
                    manualExitPending = false;
                }
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
            if (execution == null
                || execution.Order == null
                || quantity <= 0)
            {
                return;
            }


            var order =
                execution.Order;


            var isLongEntry =
                IsLongEntrySignalName(
                    order.Name);

            var isShortEntry =
                IsShortEntrySignalName(
                    order.Name);


            if (isLongEntry
                || isShortEntry)
            {
                entryOrderPending = false;

                var isNewResearchTrade =
                    !activeTradeCounted;


                if (!activeTradeCounted)
                {
                    activeTradeCounted = true;

                    activeTradeDirection =
                        isLongEntry
                            ? PendingDirection.Long
                            : PendingDirection.Short;

                    activeEntrySignal =
                        order.Name;

                    activeModelName =
                        GetModelNameForEntrySignal(
                            order.Name);

                    activeEntryFilledQuantity = 0;
                    activeEntryPriceQuantity = 0;

                    activeTradeGrossPnl = 0;

                    activeMaxHoldExitTime =
                        MaxHoldMinutes > 0
                        && submittedSignalTime
                            != Core.Globals.MinDate
                            ? submittedSignalTime.AddMinutes(
                                MaxHoldMinutes)
                            : Core.Globals.MinDate;

                    tradesToday++;

                    PersistLiveDailyState(
                        time,
                        true,
                        "EntryFill");
                }


                activeEntryFilledQuantity +=
                    quantity;

                activeEntryPriceQuantity +=
                    price * quantity;

                if (isNewResearchTrade)
                {
                    RecordResearchEntryFill(
                        time,
                        GetActiveAverageEntryPrice());
                }


                Diagnostic(
                    time,
                    "ENTRY FILL " +
                    "Model={0} Signal={1} Direction={2} " +
                    "Price={3} Qty={4} " +
                    "AvgEntry={5:0.########} " +
                    "TradesToday={6}",
                    activeModelName,
                    activeEntrySignal,
                    activeTradeDirection,
                    price,
                    quantity,
                    GetActiveAverageEntryPrice(),
                    tradesToday);


                return;
            }


            if (!activeTradeCounted)
                return;


            var fromEntrySignal =
                order.FromEntrySignal
                ?? string.Empty;

            var belongsToActiveTrade =
                !string.IsNullOrEmpty(activeEntrySignal)
                && fromEntrySignal
                    == activeEntrySignal;


            if (!belongsToActiveTrade)
                return;


            var averageEntry =
                GetActiveAverageEntryPrice();

            if (!IsFinite(averageEntry))
                return;


            var points =
                activeTradeDirection
                    == PendingDirection.Long
                    ? price - averageEntry
                    : averageEntry - price;


            var executionPnl =
                points
                * Instrument.MasterInstrument.PointValue
                * quantity;


            activeTradeGrossPnl +=
                executionPnl;


            Diagnostic(
                time,
                "EXIT FILL " +
                "Model={0} Signal={1} Direction={2} " +
                "Order={3} Price={4} Qty={5} " +
                "ExecutionPnl={6:0.00} " +
                "TradeGross={7:0.00}",
                activeModelName,
                activeEntrySignal,
                activeTradeDirection,
                order.Name,
                price,
                quantity,
                executionPnl,
                activeTradeGrossPnl);


            if (marketPosition == MarketPosition.Flat
                || Position.MarketPosition
                    == MarketPosition.Flat)
            {
                FinalizeActiveTrade(
                    time,
                    order.Name,
                    price);
            }
        }


        private void FinalizeActiveTrade(
            DateTime time,
            string exitName,
            double exitPrice)
        {
            if (!activeTradeCounted)
                return;


            grossPnlToday +=
                activeTradeGrossPnl;


            if (activeTradeGrossPnl > 0)
                winnersToday++;

            else if (activeTradeGrossPnl < 0)
                lossesToday++;

            PersistLiveDailyState(
                time,
                false,
                "TradeComplete");

            RecordResearchTradeExit(
                time,
                exitName,
                exitPrice);


            Diagnostic(
                time,
                "TRADE COMPLETE " +
                "Model={0} Signal={1} Direction={2} Exit={3} " +
                "GrossPnl={4:0.00} " +
                "DayTrades={5} Winners={6} Losses={7} " +
                "DayGross={8:0.00}",
                activeModelName,
                activeEntrySignal,
                activeTradeDirection,
                exitName,
                activeTradeGrossPnl,
                tradesToday,
                winnersToday,
                lossesToday,
                grossPnlToday);


            activeTradeCounted = false;

            activeTradeDirection =
                PendingDirection.None;

            activeModelName =
                string.Empty;

            activeEntrySignal =
                string.Empty;

            activeEntryFilledQuantity = 0;
            activeEntryPriceQuantity = 0;

            activeTradeGrossPnl = 0;

            activeEntryOrder = null;

            submittedSignalTime =
                Core.Globals.MinDate;

            activeMaxHoldExitTime =
                Core.Globals.MinDate;

            manualExitPending = false;
        }

        #endregion

        #region Flatten / pending cleanup

        private void CancelPendingEntryOrder(
            DateTime time)
        {
            if (activeEntryOrder == null)
                return;


            if (activeEntryOrder.OrderState
                    == OrderState.Accepted
                || activeEntryOrder.OrderState
                    == OrderState.Working
                || activeEntryOrder.OrderState
                    == OrderState.Submitted)
            {
                Diagnostic(
                    time,
                    "ENTRY CANCEL " +
                    "Reason=FlattenTime " +
                    "Name={0}",
                    activeEntryOrder.Name);

                CancelOrder(
                    activeEntryOrder);
            }
        }


        private void FlattenPosition(
            DateTime time)
        {
            if (manualExitPending)
                return;


            if (Position.MarketPosition
                == MarketPosition.Long)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "FLATTEN LONG " +
                    "Qty={0}",
                    Position.Quantity);

                ExitLong(
                    TickSeriesIndex,
                    Position.Quantity,
                    LongEodExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Long));

                return;
            }


            if (Position.MarketPosition
                == MarketPosition.Short)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "FLATTEN SHORT " +
                    "Qty={0}",
                    Position.Quantity);

                ExitShort(
                    TickSeriesIndex,
                    Position.Quantity,
                    ShortEodExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Short));
            }
        }


        private bool TryExitForMaximumHold(
            DateTime time)
        {
            if (MaxHoldMinutes <= 0
                || activeMaxHoldExitTime
                    == Core.Globals.MinDate
                || time < activeMaxHoldExitTime
                || manualExitPending)
            {
                return false;
            }


            if (Position.MarketPosition
                == MarketPosition.Long)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "TIME EXIT LONG " +
                    "Deadline={0:HH:mm:ss.fff} Qty={1}",
                    activeMaxHoldExitTime,
                    Position.Quantity);

                ExitLong(
                    TickSeriesIndex,
                    Position.Quantity,
                    LongTimeExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Long));

                return true;
            }


            if (Position.MarketPosition
                == MarketPosition.Short)
            {
                manualExitPending = true;

                Diagnostic(
                    time,
                    "TIME EXIT SHORT " +
                    "Deadline={0:HH:mm:ss.fff} Qty={1}",
                    activeMaxHoldExitTime,
                    Position.Quantity);

                ExitShort(
                    TickSeriesIndex,
                    Position.Quantity,
                    ShortTimeExitSignal,
                    GetActiveEntrySignalForExit(
                        PendingDirection.Short));

                return true;
            }


            return false;
        }


        private void ClearPendingEntry(
            DateTime time,
            string reason)
        {
            if (pendingDirection
                != PendingDirection.None)
            {
                Diagnostic(
                    time,
                    "PENDING CLEAR " +
                    "Model={0} Signal={1} " +
                    "Direction={2} Reason={3}",
                    pendingModelName,
                    pendingEntrySignal,
                    pendingDirection,
                    reason);
            }


            pendingDirection =
                PendingDirection.None;

            pendingModelName =
                string.Empty;

            pendingEntrySignal =
                string.Empty;

            pendingSignalTime =
                Core.Globals.MinDate;

            pendingEarliestExecutionTime =
                Core.Globals.MinDate;

            pendingSignalClose =
                double.NaN;

            pendingSignalHigh =
                double.NaN;

            pendingSignalLow =
                double.NaN;

            pendingAtr5mTicks =
                double.NaN;

            pendingEmaSlow5m =
                double.NaN;

            pendingEmaFast5m =
                double.NaN;

            pendingOvernightWidthTicks =
                double.NaN;

            pendingPremarketWidthTicks =
                double.NaN;

            pendingMinutesFromOpen = 0;

            ClearPendingResearchSignal();
        }

        #endregion

    }
}
