using System;
using NinjaTrader.Cbi;
namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexEsEvaluationScalper
    {
        private void Submit(EvaluationCandidate candidate, DateTime time, int stopTicks, int quantity)
        {
            activeCandidate = candidate; activeDirection = candidate.Direction;
            requestedEntryQuantity = quantity; entryFilledTotal = 0; workingEntry = null;
            activeSignal = "Eval_" + candidate.Id; pendingEntry = true; exiting = false;
            tradeGross = tradeFees = tradeMfe = tradeMae = 0; entryTime = time;
            // Managed Set methods require orders on the first Bars context for this instrument.
            // Signal timing still comes from the 1-tick series in Market Replay.
            // Set templates BEFORE entry. Managed NT brackets protect each partial execution.
            SetStopLoss(activeSignal, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(activeSignal, CalculationMode.Ticks, (int)Math.Ceiling(stopTicks * RewardRisk));
            dailyEntries++;
            if (activeDirection > 0) EnterLong(0, quantity, activeSignal);
            else EnterShort(0, quantity, activeSignal);
        }
        private void Flatten(string reason)
        {
            if (exiting || filledQuantity == 0 || string.IsNullOrEmpty(activeSignal)) return;
            exiting = true;
            if (pendingEntry && workingEntry != null) CancelOrder(workingEntry);
            if (activeDirection > 0) ExitLong(0, filledQuantity, reason, activeSignal);
            else ExitShort(0, filledQuantity, reason, activeSignal);
        }
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity,
            int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (research == null) return;
            research.Write("orders", time, State, order.OrderId, order.Name, orderState, quantity, filled,
                averageFillPrice, limitPrice, stopPrice, error, comment);
            if (order.Name == activeSignal) workingEntry = order;
            if (order.Name == activeSignal && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)) pendingEntry = false;
            if (error != ErrorCode.NoError || orderState == OrderState.Rejected)
            {
                faulted = true;
                research.Write("quality",time,State,"OrderFault",error + " " + comment);
                Print(Name + " | ORDER FAULT " + error + " " + comment);
                Flatten("OrderFault");
            }
        }
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
            int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (research == null || execution.Order == null || quantity <= 0) return;
            // Copy only name/action identity; fill accounting uses callback price/quantity/time.
            string name = execution.Order.Name;
            OrderAction action = execution.Order.OrderAction;
            bool isEntry = name == activeSignal && (action == OrderAction.Buy || action == OrderAction.SellShort);
            if (isEntry)
            {
                averageEntry = (averageEntry * filledQuantity + price * quantity) / (filledQuantity + quantity);
                filledQuantity += quantity; realized -= quantity * CommissionPerSide; tradeFees += quantity * CommissionPerSide;
                entryFilledTotal += quantity;
                if (entryFilledTotal >= requestedEntryQuantity) pendingEntry = false;
            }
            else if (filledQuantity > 0 && (action == OrderAction.Sell || action == OrderAction.BuyToCover))
            {
                int closed = Math.Min(quantity, filledQuantity);
                double gross = closed * activeDirection * (price - averageEntry) * Instrument.MasterInstrument.PointValue;
                realized += gross - closed * CommissionPerSide; tradeGross += gross; tradeFees += closed * CommissionPerSide;
                filledQuantity -= closed;
                if (filledQuantity == 0)
                {
                    research.Write("trades", entryTime, time, activeSignal, activeCandidate.Model, activeDirection,
                        averageEntry, price, tradeGross, tradeFees, tradeGross - tradeFees, tradeMfe, tradeMae);
                    lastExit = time; exiting = false;
                    // A bracket can fill before the market entry finishes. Keep the latch
                    // until its remaining quantity is cancelled or executed.
                    if (pendingEntry && workingEntry != null) CancelOrder(workingEntry);
                }
            }
            research.Write("fills", time, State, executionId, orderId, name, action, price, quantity, realized, filledQuantity,lastObserved);
            // Capture realized equity even if no next tick arrives after the final exit.
            WriteEquity(time, realized, filledQuantity * activeDirection * (price-averageEntry) * Instrument.MasterInstrument.PointValue,
                realized + filledQuantity * activeDirection * (price-averageEntry) * Instrument.MasterInstrument.PointValue - filledQuantity * CommissionPerSide);
        }
    }
}
