using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private ActualTradeState activeActualTrade;

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (!EnableDataCollection || !LogActualTrades)
                return;

            if (execution == null || execution.Order == null)
                return;

            var order = execution.Order;
            var orderName = order.Name ?? string.Empty;

            if (quantity <= 0)
                return;

            var isLongEntry = orderName == LongSignal;
            var isShortEntry = orderName == ShortSignal;

            if (isLongEntry || isShortEntry)
            {
                var direction = isLongEntry ? "LONG" : "SHORT";

                var actualEntryDistanceTicks = GetEntryDistanceTicks(direction, price);

                activeActualTrade = new ActualTradeState
                {
                    Direction = direction,
                    EntrySignal = orderName,
                    EntryTime = time,
                    EntryPrice = price,
                    EntryQuantity = quantity,

                    VAH = pendingActualTradePlan?.VAH ?? activeVAH,
                    VAL = pendingActualTradePlan?.VAL ?? activeVAL,
                    POC = pendingActualTradePlan?.POC ?? activePOC,

                    StopPrice = pendingActualTradePlan?.PlannedStopPrice ?? double.NaN,
                    TargetPrice = pendingActualTradePlan?.PlannedTargetPrice ?? double.NaN,

                    EntryDistanceTicks = actualEntryDistanceTicks,
                    EntryDistancePoints = actualEntryDistanceTicks * TickSize
                };

                pendingActualTradePlan = null;
                return;
            }

            if (activeActualTrade == null)
                return;

            var isExitAction =
                order.OrderAction == OrderAction.Sell ||
                order.OrderAction == OrderAction.BuyToCover;

            if (!isExitAction)
                return;

            var outcome = "Exit";

            if (orderName.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                orderName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                outcome = "TargetHit";
            }
            else if (orderName.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                outcome = "StopHit";
            }

            LogActualTrade(activeActualTrade, outcome, time, price, orderName);

            activeActualTrade = null;
        }

        private double CalculateActualTradePnlUsd(ActualTradeState trade, double exitPrice)
        {
            var pointValue = Instrument.MasterInstrument.PointValue;
            var safeQuantity = Math.Max(1, trade.EntryQuantity);

            if (trade.Direction == "LONG")
                return (exitPrice - trade.EntryPrice) * pointValue * safeQuantity;

            if (trade.Direction == "SHORT")
                return (trade.EntryPrice - exitPrice) * pointValue * safeQuantity;

            return 0;
        }

        private void PreparePendingActualTradePlan(
            string direction,
            string signalName,
            double expectedEntry,
            double stopPrice,
            double targetPrice)
        {
            var easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            var entryDistanceTicks = GetEntryDistanceTicks(direction, expectedEntry);

            pendingActualTradePlan = new PendingActualTradePlan
            {
                Direction = direction,
                SignalName = signalName,

                SignalTimeChart = Time[0],
                SignalTimeEt = easternNow,

                SignalEntryPrice = expectedEntry,
                PlannedStopPrice = stopPrice,
                PlannedTargetPrice = targetPrice,

                VAH = activeVAH,
                VAL = activeVAL,
                POC = activePOC,

                SignalEntryDistanceTicks = entryDistanceTicks,
                SignalEntryDistancePoints = entryDistanceTicks * TickSize
            };
        }

        private void LogSetupOutcome(
            string eventType,
            TrackedResearchSetup setup,
            string outcome,
            DateTime exitTimeChart,
            double exitPrice,
            string notes)
        {
            var c = setup.Candidate;

            AppendDataRow(
                eventType: eventType,
                decision: c.Decision,
                dateEt: c.SignalDateEt,
                timeChart: c.SignalTimeChart,
                timeEt: c.SignalTimeEt,
                direction: c.Direction,
                open: c.Open,
                high: c.High,
                low: c.Low,
                close: c.Close,
                bodyHigh: c.BodyHigh,
                bodyLow: c.BodyLow,
                entryPrice: c.EntryPrice,
                entryDistanceTicks: c.EntryDistanceTicks,
                entryDistancePoints: c.EntryDistancePoints,
                stopPrice: c.StopPrice,
                targetPrice: c.TargetPrice,
                barsTracked: setup.BarsTracked,
                mfeUsd: setup.MfeUsd,
                maeUsd: setup.MaeUsd,
                realizedPnlUsd: double.NaN,
                outcome: outcome,
                exitTimeChart: exitTimeChart,
                exitPrice: exitPrice,
                notes: notes);
        }

        private void LogActualTrade(
            ActualTradeState trade,
            string outcome,
            DateTime exitTimeChart,
            double exitPrice,
            string notes)
        {
            var realizedPnlUsd = CalculateActualTradePnlUsd(trade, exitPrice);

            AppendDataRow(
                eventType: "ACTUAL_TRADE",
                decision: "Executed",
                dateEt: trade.EntryTime.Date,
                timeChart: trade.EntryTime,
                timeEt: trade.EntryTime,
                direction: trade.Direction,
                open: double.NaN,
                high: double.NaN,
                low: double.NaN,
                close: double.NaN,
                bodyHigh: double.NaN,
                bodyLow: double.NaN,
                entryPrice: trade.EntryPrice,
                entryDistanceTicks: trade.EntryDistanceTicks,
                entryDistancePoints: trade.EntryDistancePoints,
                stopPrice: trade.StopPrice,
                targetPrice: trade.TargetPrice,
                barsTracked: 0,
                mfeUsd: 0,
                maeUsd: 0,
                realizedPnlUsd,
                outcome: outcome,
                exitTimeChart: exitTimeChart,
                exitPrice: exitPrice,
                notes: notes);
        }
    }
}