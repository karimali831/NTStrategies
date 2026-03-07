using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static int ParseQtyOrDefault(string s, int fallback)
        {
            if (int.TryParse((s ?? "").Trim(), out var v) && v > 0) return v;
            return fallback;
        }
        
        private void EnforceSimOnlyModeUi(List<Account> accounts)
        {
            if (accounts == null) return;

            // 1) Master: if sim-only enabled and current selection is not sim -> move to first sim
            if (_simOnlyMode && _masterBox != null)
            {
                if (_masterBox.SelectedItem is Account selected && !IsSimAccount(selected))
                {
                    var firstSim = accounts.FirstOrDefault(IsSimAccount);
                    _masterBox.SelectedItem = firstSim; // may be null if none
                }
            }

            // 2) Followers: disable + uncheck non-sim rows (and disable their overrides/flatten)
            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                var allow = !_simOnlyMode || IsSimAccount(r.Account);

                if (r.EnabledCheck != null)
                {
                    r.EnabledCheck.IsEnabled = allow;
                    if (!allow) r.EnabledCheck.IsChecked = false;
                }

                if (r.QtyOverrideBox != null) r.QtyOverrideBox.IsEnabled = allow;
                if (r.AtmOverrideBox != null) r.AtmOverrideBox.IsEnabled = allow;
                if (r.FlattenBtn != null) RenderFlattenButtonState(r.FlattenBtn, enabled: false);
            }

            UpdateMasterComboItemEnablement();
        }
        
        private void UpdateMasterComboItemEnablement()
        {
            // containers may not exist yet
            _masterBox?.Dispatcher?.InvokeAsync(() =>
            {
                foreach (var item in _masterBox.Items)
                {
                    var acc = item as Account;
                    var c = _masterBox.ItemContainerGenerator.ContainerFromItem(item) as ComboBoxItem;
                    if (c == null) continue;

                    var allow = !_simOnlyMode || IsSimAccount(acc);
                    c.IsEnabled = allow;
                    c.Opacity = allow ? 1.0 : 0.45;
                }
            }, DispatcherPriority.Loaded);
        }
        
        private void SubscribeUiAccountEvents(IEnumerable<Account> accounts)
            {
                if (accounts == null) return;

                foreach (var a in accounts)
                {
                    if (a == null) continue;

                    a.AccountItemUpdate -= OnUiAccountItemUpdate;
                    a.AccountItemUpdate += OnUiAccountItemUpdate;

                    a.PositionUpdate -= OnUiPositionUpdate;
                    a.PositionUpdate += OnUiPositionUpdate;
                }
            }

        private void UnsubscribeUiAccountEvents(IEnumerable<Account> accounts)
        {
            if (accounts == null) return;

            foreach (var a in accounts)
            {
                if (a == null) continue;
                a.AccountItemUpdate -= OnUiAccountItemUpdate;
                a.PositionUpdate -= OnUiPositionUpdate;
            }
        }
        
        private void OnUiAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            if (e?.Account == null) return;
            if (e.Currency != Currency.UsDollar) return;

            var name = e.Account.Name ?? "";
            if (string.IsNullOrWhiteSpace(name)) return;

            lock (_uiPnl)
            {
                _uiPnl.TryGetValue(name, out var snap);

                if (e.AccountItem == AccountItem.RealizedProfitLoss)
                    snap.r = e.Value;
                else if (e.AccountItem == AccountItem.UnrealizedProfitLoss)
                    snap.u = e.Value;

                _uiPnl[name] = snap;
            }

            RenderPnlUi();
        }

        private void OnUiPositionUpdate(object sender, PositionEventArgs e)
        {
            var acc = sender as Account;
            if (acc == null) return;
            if (e?.Position == null) return;

            var instrFull = e.Position.Instrument?.FullName ?? "";
            var key = $"{acc.Name}|{instrFull}";
            var qty = e.Position.Quantity;

            lock (_uiNet)
                _uiNet[key] = qty;

            RenderFlattenEnablementUi();
        }

        private Instrument GetInstrument()
        {
            var instrName = (_instrBox?.Text ?? "").Trim();
            return string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);
        }
        
        private string GetInstrumentFullName()
        {
            var instr = GetInstrument();
            return instr?.FullName ?? "";
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
    }
}