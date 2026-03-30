using System;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private readonly object _tradeGate = new object();
        
        private void RequestTradesUiRefresh()
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;
            display?.InvokeAsync(RefreshTradesPanel, DispatcherPriority.Background);
        }
        
        private void TrackEntryExecution(Account acc, Execution execution, bool isMaster, string bracketUsed)
        {
            if (acc == null || execution?.Order == null || execution.Instrument == null)
                return;

            var instr = execution.Instrument;
            var key = TradeKey(acc, instr);

            lock (_tradeGate)
            {
                if (!_activeTrades.TryGetValue(key, out var trade) || trade == null)
                {
                    var order = execution.Order;
                    var isBuySide =
                        order.OrderAction == OrderAction.Buy ||
                        order.OrderAction == OrderAction.BuyToCover;

                    trade = new ActiveTradeRuntime
                    {
                        TradeNumber = _nextTradeNumber++,
                        Key = key,
                        InstrumentName = instr.FullName,
                        MarketPosition = isBuySide ? "Long" : "Short",
                        AccountName = acc.Name,
                        EntryTimeUtc = execution.Time.ToUniversalTime(),
                        BracketUsed = string.IsNullOrWhiteSpace(bracketUsed) ? "None" : bracketUsed,
                        IsMaster = isMaster,
                        EntryOrderName = (order.Name ?? "").Trim()
                    };

                    _activeTrades[key] = trade;
                }

                var fillQty = Math.Abs((int)Math.Round((double)execution.Quantity, MidpointRounding.AwayFromZero));
                if (fillQty <= 0)
                    return;

                trade.EntryFilledQty += fillQty;
                trade.EntryValueSum += execution.Price * fillQty;
            }

            RequestTradesUiRefresh();
            SavePersistentUiState();
        }

        private void TryTrackTradeExitFromExecution(Account acc, Execution execution)
        {
            if (acc == null || execution?.Order == null || execution.Instrument == null)
                return;

            var name = (execution.Order.Name ?? "").Trim();
            var isExit =
                name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase);

            if (!isExit)
                return;

            AccumulateAndMaybeCloseTradeFromExecution(acc, execution);
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

            lock (_tradeGate)
            {
                var key = TradeKey(acc, instr);
                if (!_activeTrades.TryGetValue(key, out var trade) || trade == null)
                    return;

                trade.BreakEvenApplied = true;
                trade.BreakEvenKind = kind;
            }

            RequestTradesUiRefresh();
            SavePersistentUiState();
        }

        private void MarkTradeFlattenIntent(Account acc, Instrument instr, FlattenTriggerReason reason, string detail = null)
        {
            if (acc == null || instr == null)
                return;

            lock (_tradeGate)
            {
                var key = TradeKey(acc, instr);
                if (!_activeTrades.TryGetValue(key, out var trade) || trade == null)
                    return;

                trade.PendingFlattenReason = reason;
                trade.PendingFlattenDetail = detail;
            }

            RequestTradesUiRefresh();
            SavePersistentUiState();
        }

        private void MarkTradeManualFlatten(Account acc, Instrument instr)
        {
            MarkTradeFlattenIntent(acc, instr, FlattenTriggerReason.ManualFlatten);
        }

        private void AccumulateAndMaybeCloseTradeFromExecution(Account acc, Execution execution)
        {
            if (acc == null || execution?.Order == null || execution.Instrument == null)
                return;

            var instr = execution.Instrument;
            var key = TradeKey(acc, instr);

            ActiveTradeRuntime active;
            lock (_tradeGate)
            {
                if (!_activeTrades.TryGetValue(key, out active) || active == null)
                    return;

                var exitQty = Math.Abs((int)Math.Round((double)execution.Quantity, MidpointRounding.AwayFromZero));
                if (exitQty <= 0)
                    return;

                active.ClosedQty += exitQty;
                active.ExitValueSum += execution.Price * exitQty;
                active.LastExitTimeUtc = execution.Time.ToUniversalTime();
                active.LastExitOrderName = (execution.Order.Name ?? "").Trim();
            }

            FinalizeTrackedTrade(acc, instr);
        }
        
        private void FinalizeTrackedTrade(Account acc, Instrument instr)
        {
            if (acc == null || instr == null)
                return;

            var key = TradeKey(acc, instr);

            ActiveTradeRuntime active;
            lock (_tradeGate)
            {
                if (!_activeTrades.TryGetValue(key, out active) || active == null)
                    return;

                _activeTrades.Remove(key);
            }

            var totalEntryQty = Math.Max(1, active.EntryFilledQty);
            var avgEntryPrice = active.EntryFilledQty > 0
                ? active.EntryValueSum / active.EntryFilledQty
                : 0.0;

            var totalExitQty = Math.Max(1, active.ClosedQty);
            var avgExitPrice = active.ClosedQty > 0
                ? active.ExitValueSum / active.ClosedQty
                : avgEntryPrice;

            var exitTimeUtc = active.LastExitTimeUtc ?? DateTime.UtcNow;
            var exitOrderName = active.LastExitOrderName ?? "";

            var pointValue = instr.MasterInstrument?.PointValue ?? 0.0;
            var pnl = 0.0;

            if (pointValue > 0)
            {
                pnl = string.Equals(active.MarketPosition, "Long", StringComparison.OrdinalIgnoreCase)
                    ? (avgExitPrice - avgEntryPrice) * pointValue * totalEntryQty
                    : (avgEntryPrice - avgExitPrice) * pointValue * totalEntryQty;
            }

            var outcome = BuildTradeOutcomeLabel(active, exitOrderName);

            lock (_tradeGate)
            {
                _tradeHistory.Insert(0, new TradeHistoryItemState
                {
                    TradeNumber = active.TradeNumber,
                    InstrumentName = active.InstrumentName,
                    MarketPosition = active.MarketPosition,
                    OrderQty = totalEntryQty,
                    AccountName = active.AccountName,
                    EntryTimeUtc = active.EntryTimeUtc,
                    ExitTimeUtc = exitTimeUtc,
                    EntryPrice = avgEntryPrice,
                    ExitPrice = avgExitPrice,
                    RealizedPnL = pnl,
                    BracketUsed = active.BracketUsed,
                    Outcome = outcome,
                    IsMaster = active.IsMaster,
                    BreakEvenApplied = active.BreakEvenApplied,
                    BreakEvenKind = active.BreakEvenKind,
                    WasFlattenedManually = active.WasFlattenedManually,
                    EntryOrderName = active.EntryOrderName,
                    ExitOrderName = exitOrderName
                });
            }

            RequestTradesUiRefresh();
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