using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private readonly List<TradeHistoryItemState> _tradeHistory = new List<TradeHistoryItemState>();
        private readonly Dictionary<string, ActiveTradeRuntime> _activeTrades =
            new Dictionary<string, ActiveTradeRuntime>(StringComparer.Ordinal);
        private int _nextTradeNumber = 1;
        
        private void SaveTradeHistoryToState()
        {
            EnsurePersistedStateDefaults();

            _persistedState.TradeHistory = _tradeHistory
                .Select(x => new TradeHistoryItemState
                {
                    TradeNumber = x.TradeNumber,
                    InstrumentName = x.InstrumentName,
                    MarketPosition = x.MarketPosition,
                    OrderQty = x.OrderQty,
                    AccountName = x.AccountName,
                    EntryTimeUtc = x.EntryTimeUtc,
                    ExitTimeUtc = x.ExitTimeUtc,
                    EntryPrice = x.EntryPrice,
                    ExitPrice = x.ExitPrice,
                    RealizedPnL = x.RealizedPnL,
                    BracketUsed = x.BracketUsed,
                    Outcome = x.Outcome,
                    IsMaster = x.IsMaster,
                    BreakEvenApplied = x.BreakEvenApplied,
                    BreakEvenKind = x.BreakEvenKind,
                    WasFlattenedManually = x.WasFlattenedManually,
                    EntryOrderName = x.EntryOrderName,
                    ExitOrderName = x.ExitOrderName
                })
                .ToList();
        }

        private void LoadTradeHistoryFromState()
        {
            _tradeHistory.Clear();

            EnsurePersistedStateDefaults();

            foreach (var item in _persistedState.TradeHistory)
            {
                if (item == null)
                    continue;

                _tradeHistory.Add(new TradeHistoryItemState
                {
                    TradeNumber = item.TradeNumber,
                    InstrumentName = item.InstrumentName,
                    MarketPosition = item.MarketPosition,
                    OrderQty = item.OrderQty,
                    AccountName = item.AccountName,
                    EntryTimeUtc = item.EntryTimeUtc,
                    ExitTimeUtc = item.ExitTimeUtc,
                    EntryPrice = item.EntryPrice,
                    ExitPrice = item.ExitPrice,
                    RealizedPnL = item.RealizedPnL,
                    BracketUsed = item.BracketUsed,
                    Outcome = item.Outcome,
                    IsMaster = item.IsMaster,
                    BreakEvenApplied = item.BreakEvenApplied,
                    BreakEvenKind = item.BreakEvenKind,
                    WasFlattenedManually = item.WasFlattenedManually,
                    EntryOrderName = item.EntryOrderName,
                    ExitOrderName = item.ExitOrderName
                });
            }

            _nextTradeNumber = _tradeHistory.Count == 0
                ? 1
                : _tradeHistory.Max(x => x.TradeNumber) + 1;
        }
    }
}