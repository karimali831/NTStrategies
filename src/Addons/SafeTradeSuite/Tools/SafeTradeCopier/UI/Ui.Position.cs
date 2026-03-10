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

            var instrFull = e.Position.Instrument?.FullName ?? "";
            var key = $"{acc.Name}|{instrFull}";
            var qty = e.Position.Quantity;

            lock (_uiNet)
                _uiNet[key] = qty;

            RenderFlattenEnablementUi();
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
    }
}