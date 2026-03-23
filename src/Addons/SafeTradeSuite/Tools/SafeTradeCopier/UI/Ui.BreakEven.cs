using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
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
                            RenderFreeTradeButtonState(row.FreeTradeBtn, false, false, CheckGlyph);
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
                        RenderFreeTradeButtonState(row.FreeTradeBtn, false, false, CheckGlyph);
                        continue;
                    }

                    var canUndo = _engine != null && _engine.CanUndoFreeTrade(row.Account, instr, out _);
                    var canApply = _engine != null && _engine.CanApplyFreeTrade(row.Account, instr, _freeTradeMinProfitPoints, out _);

                    RenderFreeTradeButtonState(row.FreeTradeBtn, canUndo || canApply, canUndo, CheckGlyph);
                    anyCanApplyOrUndo |= canUndo || canApply;
                }

                if (_btnFreeTradeAll != null)
                    RenderFreeTradeButtonState(_btnFreeTradeAll, anyCanApplyOrUndo && !BreakEvenDisabled, false, "Break-even All");
            }, DispatcherPriority.Background);
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