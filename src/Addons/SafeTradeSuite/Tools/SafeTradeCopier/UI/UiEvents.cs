using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private readonly Dictionary<string, (double r, double u)> _uiPnl = new Dictionary<string, (double r, double u)>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _uiNet = new Dictionary<string, int>(StringComparer.Ordinal);

        
        private void ApplyConfigFromUi()
        {
            if (_engine == null) return;

            var master = _masterBox?.SelectedItem as Account;
            var instr = _instrBox?.Text.Trim() ?? "";

            // master qty
            var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);

            // master ATM
            var masterAtm = _masterAtmBox?.SelectedItem as string ?? "None";
            if (string.IsNullOrWhiteSpace(masterAtm)) masterAtm = "None";
            
            if (_simOnlyMode && master != null && !IsSimAccount(master))
            {
                // enforce safety: do not allow non-sim master
                var accounts = GetSelectableAccounts();
                var firstSim = accounts.FirstOrDefault(IsSimAccount);
                _masterBox.SelectedItem = firstSim;
                master = firstSim;
            }

            // followers enabled + overrides
            var followers = new List<Account>();
            var qtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            var atmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;

                var enabled = r.EnabledCheck?.IsChecked == true;
                if (!enabled) continue;

                if (_simOnlyMode && !IsSimAccount(r.Account))
                {
                    // safety: if something slips through, force it off
                    r.EnabledCheck.IsChecked = false;
                    continue;
                }

                followers.Add(r.Account);

                // qty override (blank = inherit)
                var qText = (r.QtyOverrideBox?.Text ?? "").Trim();
                if (int.TryParse(qText, out var qv) && qv > 0)
                    qtyOverrides[r.Account.Name] = qv;

                // atm override (inherit master / none / template)
                var aText = (r.AtmOverrideBox?.SelectedItem as string) ?? "(inherit master)";
                if (string.IsNullOrWhiteSpace(aText)) aText = "(inherit master)";
                atmOverrides[r.Account.Name] = aText;
            }

            _engine.ApplyConfig(
                masterAccount: master,
                followerAccounts: followers,
                instrName: instr,
                masterQty: masterQty,
                masterAtm: masterAtm,
                followerQtyOverridesByAccountName: qtyOverrides,
                followerAtmOverridesByAccountName: atmOverrides
            );
        }
        
        private void RebuildFollowersAndRewire(SafeCopierEngine eng, List<Account> accounts)
        {
            // preserve follower selections by account name (optional but nice)
            var selected = new HashSet<string>(
                _followerRows.Where(r => r?.EnabledCheck?.IsChecked == true && r.Account != null).Select(r => r.Account.Name),
                StringComparer.Ordinal);

            // rebuild rows (excludes current master)
            BuildFollowerRows(accounts);

            // restore selections
            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.EnabledCheck == null) continue;
                r.EnabledCheck.IsChecked = selected.Contains(r.Account.Name);
            }

            // sim-only enforcement after rebuild
            EnforceSimOnlyModeUi(accounts);

            // reload ATMs into NEW combo instances
            foreach (var r in _followerRows)
                LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

            // rewire follower flatten button handlers (NEW button instances)
            WireFollowerFlattenButtons(eng);

            // update engine config
            ApplyConfigFromUi();

            if (eng.CopyEnabled)
                eng.SetCopyEnabled(true);
        }
        
        private void RenderPnlUi()
            {
                var disp = _uiDispatcher ?? _window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    var totalR = 0.0;
                    var totalU = 0.0;

                    // master
                    var master = _masterBox?.SelectedItem as Account;
                    if (master != null)
                    {
                        var mr = 0.0; var mu = 0.0;
                        lock (_uiPnl)
                        {
                            if (_uiPnl.TryGetValue(master.Name, out var snap))
                            {
                                mr = snap.r;
                                mu = snap.u;
                            }
                        }

                        totalR += mr;
                        totalU += mu;

                        if (_masterPnlText != null)
                            _masterPnlText.Text = $"Master PnL  R: {FmtUsd(r)}  U: {FmtUsd(u)}";
                    }

                    // followers
                    foreach (var row in _followerRows)
                    {
                        var acc = row?.Account;
                        if (acc == null) continue;

                        var r = 0.0; var u = 0.0;
                        lock (_uiPnl)
                        {
                            if (_uiPnl.TryGetValue(acc.Name, out var snap))
                            {
                                r = snap.r;
                                u = snap.u;
                            }
                        }

                        totalR += r;
                        totalU += u;

                        if (row.PnlText != null)
                            row.PnlText.Text = $"R: {FmtUsd(r)}  U: {FmtUsd(u)}";
                    }

                    if (_totalPnlText != null)
                        _totalPnlText.Text = $"TOTAL PnL  R: {FmtUsd(totalR)}  U: {FmtUsd(totalU)}";
                }, DispatcherPriority.Background);
            }
        
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
                if (r.FlattenBtn != null) r.FlattenBtn.IsEnabled = false; // PnL timer will re-enable when allowed+net!=0
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

            public void UnsubscribeUiAccountEvents(IEnumerable<Account> accounts)
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
            

            private void RenderFlattenEnablementUi()
            {
                var disp = _uiDispatcher ?? _window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    var instrName = (_instrBox?.Text ?? "").Trim();
                    var instr = string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);
                    var instrFull = instr?.FullName ?? "";

                    foreach (var row in _followerRows)
                    {
                        if (row?.Account == null || row.FlattenBtn == null)
                            continue;

                        if (instr == null)
                        {
                            row.FlattenBtn.IsEnabled = false;
                            continue;
                        }

                        var net = 0;
                        var key = $"{row.Account.Name}|{instrFull}";

                        lock (_uiNet)
                            _uiNet.TryGetValue(key, out net);

                        if (net == 0)
                        {
                            foreach (var p in row.Account.Positions)
                            {
                                if (p?.Instrument == null) continue;
                                if (!string.Equals(p.Instrument.FullName, instrFull, StringComparison.Ordinal)) continue;
                                net = p.Quantity;
                                break;
                            }
                        }

                        row.FlattenBtn.IsEnabled = net != 0 && row.EnabledCheck?.IsChecked == true;
                    }
                }, DispatcherPriority.Background);
            }
    }
}