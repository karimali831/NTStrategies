using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void OnUiPositionUpdate(object sender, PositionEventArgs e)
        {
            if (!(sender is Account acc)) return;
            if (e?.Position == null) return;
            
            SafeTradeSuiteRuntime.PrintLog(
                $"[UI POS EVT] acc={acc?.Name} instr={e.Position?.Instrument?.FullName} qty={e.Position?.Quantity} mp={e.Position?.MarketPosition}");

            var instrFull = e.Position.Instrument?.FullName ?? "";
            var key = $"{acc.Name}|{instrFull}";
            var qty = e.Position.Quantity;

            lock (_uiNet)
                _uiNet[key] = qty;

            InvalidatePositionsPanel();
        }
        
        
        private void RenderFlattenEnablementUi()
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
                var instr = GetInstrument();
                var instrFull = GetInstrumentFullName();

                foreach (var row in _followerRows)
                {
                    if (row?.Account == null || row.FlattenBtn == null)
                        continue;

                    if (instr == null)
                    {
                        RenderFlattenButtonState(row.FlattenBtn, false);
                        continue;
                    }

                    var canFlatten = CanFlatten(row.Account, instrFull) && row.EnabledCheck?.IsChecked == true;
                    RenderFlattenButtonState(row.FlattenBtn, canFlatten);
                }

                RenderFlattenAllButtonState();
            }, DispatcherPriority.Background);
        }
        
        private static void RenderFlattenButtonState(Button btn, bool enabled)
        {
            if (btn == null)
                return;

            btn.IsEnabled = enabled;
            btn.Background = enabled ? Brushes.Maroon : Brushes.Gray;
            btn.Foreground = Brushes.White;
            btn.Opacity = enabled ? 1.0 : 0.60;
            btn.ToolTip = enabled
                ? "Flatten this follower position"
                : "No open position for this instrument";
        }
        
        private void RenderFlattenAllButtonState()
        {
            if (_btnFlattenAll == null) return;

            var master = _masterBox?.SelectedItem as Account;
            var instrFull = GetInstrumentFullName();

            var canFlattenMaster = CanFlatten(master, instrFull);

            var canFlattenFollowers = _followerRows.Any(r =>
                r?.Account != null &&
                r.EnabledCheck?.IsChecked == true &&
                CanFlatten(r.Account, instrFull));

            var canFlattenAny = canFlattenMaster || canFlattenFollowers;

            _btnFlattenAll.IsEnabled = canFlattenAny;
            _btnFlattenAll.Background = canFlattenAny ? Brushes.DarkRed : Brushes.Gray;
            _btnFlattenAll.Foreground = Brushes.White;
            _btnFlattenAll.Opacity = canFlattenAny ? 1.0 : 0.65;
        }
        
        private void RenderBreakEvenEnablementUi()
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;
     
            display?.InvokeAsync(() =>
            {
                var instr = GetInstrument();
                if (instr == null)
                {
                    if (_btnMasterFreeTrade != null)
                        RenderFreeTradeButtonState(_btnMasterFreeTrade, false, false,"Break-even");

                    if (_btnFreeTradeAll != null)
                        RenderFreeTradeButtonState(_btnFreeTradeAll, false, false, "Break-even All");

                    foreach (var row in _followerRows)
                    {
                        if (row?.FreeTradeBtn != null)
                            RenderFreeTradeButtonState(row.FreeTradeBtn, false, false, "✓");
                    }

                    return;
                }

                var anyCanApplyOrUndo = false;

                if (_btnMasterFreeTrade != null && _masterBox?.SelectedItem is Account master)
                {
                    var canUndoMaster = _engine != null && _engine.CanUndoFreeTrade(master, instr, out _);
                    var canApplyMaster = _engine != null && _engine.CanApplyFreeTrade(master, instr, _freeTradeMinProfitPoints, out _);

                    RenderFreeTradeButtonState(_btnMasterFreeTrade, canUndoMaster || canApplyMaster, canUndoMaster,"Break-even");
                    anyCanApplyOrUndo |= canUndoMaster || canApplyMaster;
                }

                foreach (var row in _followerRows)
                {
                    if (row?.Account == null || row.FreeTradeBtn == null)
                        continue;

                    var enabledFollower = row.EnabledCheck?.IsChecked == true;
                    if (!enabledFollower || BreakEvenDisabled)
                    {
                        RenderFreeTradeButtonState(row.FreeTradeBtn, false, false, "✓");
                        continue;
                    }

                    var canUndo = _engine != null && _engine.CanUndoFreeTrade(row.Account, instr, out _);
                    var canApply = _engine != null && _engine.CanApplyFreeTrade(row.Account, instr, _freeTradeMinProfitPoints, out _);

                    RenderFreeTradeButtonState(row.FreeTradeBtn, canUndo || canApply, canUndo, "✓");
                    anyCanApplyOrUndo |= canUndo || canApply;
                }

                if (_btnFreeTradeAll != null)
                    RenderFreeTradeButtonState(_btnFreeTradeAll, anyCanApplyOrUndo && !BreakEvenDisabled, false, "Break-even All");
            }, DispatcherPriority.Background);
        }
        
        private void RenderMasterSubmitButtonsState()
        {
            var pending = _engine != null && _engine.IsMasterSubmitInFlight();

            if (_btnBuyMkt != null)
                _btnBuyMkt.IsEnabled = !pending;

            if (_btnSellMkt != null)
                _btnSellMkt.IsEnabled = !pending;
        }

        private static void RenderFreeTradeButtonState(Button btn, bool enabled, bool undoMode, string btnText)
        {
            if (btn == null)
                return;

            btn.IsEnabled = enabled;
            btn.Content = undoMode ? "Undo BE" : btnText;
            btn.Background = enabled
                ? undoMode ? Brushes.DarkOrange : Brushes.RoyalBlue
                : Brushes.Gray;
            btn.Foreground = Brushes.White;
            btn.Opacity = enabled ? 1.0 : 0.60;
        }
    }
}