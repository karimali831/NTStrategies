using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void TrackEntryExecution(Account acc, Execution execution, bool isMaster, string bracketUsed)
        {
            if (acc == null || execution?.Order == null || execution.Instrument == null)
                return;

            var instr = execution.Instrument;
            var key = TradeKey(acc, instr);

            if (_activeTrades.ContainsKey(key))
                return;

            var order = execution.Order;
            var isBuySide =
                order.OrderAction == OrderAction.Buy ||
                order.OrderAction == OrderAction.BuyToCover;

            var qty = Math.Abs((int)Math.Round((double)execution.Quantity, MidpointRounding.AwayFromZero));
            if (qty <= 0)
                qty = 1;

            _activeTrades[key] = new ActiveTradeRuntime
            {
                TradeNumber = _nextTradeNumber++,
                Key = key,
                InstrumentName = instr.FullName,
                MarketPosition = isBuySide ? "Long" : "Short",
                OrderQty = qty,
                AccountName = acc.Name,
                EntryTimeUtc = execution.Time.ToUniversalTime(),
                EntryPrice = execution.Price,
                BracketUsed = string.IsNullOrWhiteSpace(bracketUsed) ? "None" : bracketUsed,
                IsMaster = isMaster,
                EntryOrderName = (order.Name ?? "").Trim(),
                BreakEvenApplied = false,
                BreakEvenKind = BreakEvenTriggerKind.None,
                PendingFlattenReason = FlattenTriggerReason.None,
                PendingFlattenDetail = null
            };

            RefreshTradesPanel();
            SavePersistentUiState();
        }

        private void TryTrackTradeExitFromExecution(Account acc, Execution execution)
        {
            if (acc == null || execution?.Order == null)
                return;

            var name = (execution.Order.Name ?? "").Trim();
            var isExit =
                name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase);

            if (!isExit)
                return;

            TryCloseTradeFromExecution(acc, execution);
        }

        private void MarkTradeBreakEvenApplied(Account acc, Instrument instr, bool isAuto)
        {
            MarkTradeBreakEven(
                acc,
                instr,
                isAuto ? BreakEvenTriggerKind.Auto : BreakEvenTriggerKind.Manual);
        }

        private void MarkTradeBreakEven(Account acc, Instrument instr, BreakEvenTriggerKind kind)
        {
            if (acc == null || instr == null)
                return;

            var key = TradeKey(acc, instr);
            if (!_activeTrades.TryGetValue(key, out var trade) || trade == null)
                return;

            trade.BreakEvenApplied = kind != BreakEvenTriggerKind.None;
            trade.BreakEvenKind = kind;

            RefreshTradesPanel();
            SavePersistentUiState();
        }

        private void MarkTradeFlattenIntent(Account acc, Instrument instr, FlattenTriggerReason reason, string detail = null)
        {
            if (acc == null || instr == null)
                return;

            var key = TradeKey(acc, instr);
            if (!_activeTrades.TryGetValue(key, out var trade) || trade == null)
                return;

            trade.PendingFlattenReason = reason;
            trade.PendingFlattenDetail = string.IsNullOrWhiteSpace(detail) ? null : detail;

            RefreshTradesPanel();
            SavePersistentUiState();
        }

        private void MarkTradeManualFlatten(Account acc, Instrument instr)
        {
            MarkTradeFlattenIntent(acc, instr, FlattenTriggerReason.ManualFlatten);
        }

        private void TryCloseTradeFromExecution(Account acc, Execution execution)
        {
            if (acc == null || execution?.Order == null || execution.Instrument == null)
                return;

            var instr = execution.Instrument;
            var key = TradeKey(acc, instr);

            if (!_activeTrades.TryGetValue(key, out var active) || active == null)
                return;

            var orderName = (execution.Order.Name ?? "").Trim();
            var exitPrice = execution.Price;
            var exitTimeUtc = execution.Time.ToUniversalTime();

            var exitQty = Math.Abs((int)Math.Round((double)execution.Quantity, MidpointRounding.AwayFromZero));
            if (exitQty <= 0)
                exitQty = active.OrderQty;

            var pointValue = instr.MasterInstrument?.PointValue ?? 0.0;
            var pnl = 0.0;

            if (pointValue > 0)
            {
                pnl = string.Equals(active.MarketPosition, "Long", StringComparison.OrdinalIgnoreCase)
                    ? (exitPrice - active.EntryPrice) * pointValue * exitQty
                    : (active.EntryPrice - exitPrice) * pointValue * exitQty;
            }

            var outcome = BuildTradeOutcomeLabel(active, orderName);

            _tradeHistory.Insert(0, new TradeHistoryItemState
            {
                TradeNumber = active.TradeNumber,
                InstrumentName = active.InstrumentName,
                MarketPosition = active.MarketPosition,
                OrderQty = active.OrderQty,
                AccountName = active.AccountName,
                EntryTimeUtc = active.EntryTimeUtc,
                ExitTimeUtc = exitTimeUtc,
                EntryPrice = active.EntryPrice,
                ExitPrice = exitPrice,
                RealizedPnL = pnl,
                BracketUsed = active.BracketUsed,
                Outcome = outcome,
                IsMaster = active.IsMaster,
                BreakEvenApplied = active.BreakEvenApplied,
                BreakEvenKind = active.BreakEvenKind,
                PendingFlattenReason = active.PendingFlattenReason,
                PendingFlattenDetail = active.PendingFlattenDetail,
                EntryOrderName = active.EntryOrderName,
                ExitOrderName = orderName
            });

            _activeTrades.Remove(key);

            RefreshTradesPanel();
            SavePersistentUiState();
        }

        private string BuildTradeOutcomeLabel(ActiveTradeRuntime trade, string exitOrderName)
        {
            if (trade == null)
                return "Unknown";

            switch (trade.PendingFlattenReason)
            {
                case FlattenTriggerReason.ManualFlatten:
                case FlattenTriggerReason.ManualFlattenAll:
                    return "Manual";

                case FlattenTriggerReason.Panic:
                    return "Panic";

                case FlattenTriggerReason.FollowMasterExit:
                    return "Follow master exit";

                case FlattenTriggerReason.RiskProtection:
                    return "Risk protection";

                case FlattenTriggerReason.FollowerGuard:
                    return string.IsNullOrWhiteSpace(trade.PendingFlattenDetail)
                        ? "Follower guard"
                        : $"Follower guard ({trade.PendingFlattenDetail})";
            }

            var name = (exitOrderName ?? "").Trim();

            if (name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase))
                return "Profit target";

            if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase))
            {
                if (trade.BreakEvenKind == BreakEvenTriggerKind.Auto)
                    return "Auto BE";

                if (trade.BreakEvenKind == BreakEvenTriggerKind.Manual)
                    return "Manual BE";

                return "Stop loss";
            }

            if (name.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase))
                return "Manual";

            return "Unknown";
        }
    }
}