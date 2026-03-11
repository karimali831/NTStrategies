using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TabControl _instrumentTabs;
        private Button _btnAddInstrumentTab;
        
        private void RefreshInstrumentTabs()
        {
            if (_instrumentTabs == null)
                return;

            NormalizeInstrumentSessions();

            _instrumentTabs.SelectionChanged -= OnInstrumentTabsSelectionChanged;

            try
            {
                _instrumentTabs.Items.Clear();

                foreach (var session in _instrumentSessions)
                    _instrumentTabs.Items.Add(BuildInstrumentTabItem(session));

                EnsureActiveInstrumentSession();
                SelectActiveInstrumentTab();
            }
            finally
            {
                _instrumentTabs.SelectionChanged += OnInstrumentTabsSelectionChanged;
            }
        }
        
        private void EnsureActiveInstrumentSession()
        {
            if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                _activeInstrumentSession = _instrumentSessions[0];
        }

        private void SelectActiveInstrumentTab()
        {
            if (_instrumentTabs == null || _activeInstrumentSession == null)
                return;

            foreach (var obj in _instrumentTabs.Items)
            {
                if (obj is TabItem tab && ReferenceEquals(tab.Tag, _activeInstrumentSession))
                {
                    _instrumentTabs.SelectedItem = tab;
                    break;
                }
            }
        }
        
        private void OnInstrumentTabCloseClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button btn && btn.Tag is InstrumentSession session)
                RemoveInstrumentSession(session);
        }

        private void OnInstrumentTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_instrumentTabs?.SelectedItem is TabItem tab && tab.Tag is InstrumentSession session)
                SwitchToSession(session);
        }

        private void SwitchToSession(InstrumentSession session)
        {
            if (session == null)
                return;

            if (ReferenceEquals(_activeInstrumentSession, session))
                return;

            SaveUiToActiveSession();
            _activeInstrumentSession = session;

            RefreshInstrumentSelectorItems();
            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
        }

        private void ActivateOrCreateInstrumentSession(string instrumentName, bool refreshSelector = true)
        {
            var normalized = NormalizeInstrumentName(instrumentName);
            if (!IsValidInstrumentName(normalized))
                return;

            RememberInstrument(normalized);

            // remove placeholder empty sessions once a real instrument is chosen
            _instrumentSessions.RemoveAll(x =>
                x != null &&
                string.IsNullOrWhiteSpace(NormalizeInstrumentName(x.InstrumentName)));

            var existing = _instrumentSessions.FirstOrDefault(x =>
                string.Equals(
                    NormalizeInstrumentName(x?.InstrumentName),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!ReferenceEquals(_activeInstrumentSession, existing))
                {
                    SaveUiToActiveSession(saveInstrumentName: false);
                    _activeInstrumentSession = existing;
                }

                if (refreshSelector)
                    RefreshInstrumentSelectorItems();

                RefreshInstrumentTabs();
                LoadActiveSessionToUi();
                return;
            }

            SaveUiToActiveSession(saveInstrumentName: false);

            var session = new InstrumentSession
            {
                InstrumentName = normalized,
                MasterAccount = _masterBox?.SelectedItem as Account,
                MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1),
                MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None"
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;

            NormalizeInstrumentSessions();

            if (refreshSelector)
                RefreshInstrumentSelectorItems();

            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
        }

        private void RemoveInstrumentSession(InstrumentSession sessionToRemove)
        {
            if (sessionToRemove == null)
                return;

            if (_instrumentSessions.Count <= 1)
            {
                ShowFriendlyError("Cannot remove tab", "At least one instrument tab must remain open.");
                return;
            }

            SaveUiToActiveSession();

            var instrumentToMaybeForget = NormalizeInstrumentName(sessionToRemove.InstrumentName);
            var idx = _instrumentSessions.IndexOf(sessionToRemove);
            if (idx < 0)
                return;

            _instrumentSessions.RemoveAt(idx);

            if (!IsInstrumentUsedByAnySession(instrumentToMaybeForget, sessionToRemove))
                ForgetInstrument(instrumentToMaybeForget);

            if (_instrumentSessions.Count > 0)
            {
                var nextIdx = Math.Min(idx, _instrumentSessions.Count - 1);
                _activeInstrumentSession = _instrumentSessions[nextIdx];
            }
            else
            {
                _activeInstrumentSession = null;
            }

            RefreshInstrumentSelectorItems();
            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
            RenderFlattenAllButtonState();
        }

        private void SaveUiToActiveSession()
        {
            SaveUiToActiveSession(saveInstrumentName: true);
        }

        private void SaveUiToActiveSession(bool saveInstrumentName)
        {
            try
            {
                if (_activeInstrumentSession == null)
                    return;

                if (saveInstrumentName)
                {
                    var selectedInstrument = NormalizeInstrumentName(GetSelectedInstrumentName());
                    if (IsValidInstrumentName(selectedInstrument))
                        _activeInstrumentSession.InstrumentName = selectedInstrument;
                }

                _activeInstrumentSession.MasterAccount = _masterBox?.SelectedItem as Account;
                _activeInstrumentSession.MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);
                _activeInstrumentSession.MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None";

                _activeInstrumentSession.FollowersEnabled.Clear();
                _activeInstrumentSession.FollowerQtyOverrides.Clear();
                _activeInstrumentSession.FollowerAtmOverrides.Clear();

                foreach (var r in _followerRows)
                {
                    if (r?.Account == null)
                        continue;

                    var accName = r.Account.Name;

                    _activeInstrumentSession.FollowersEnabled[accName] = r.EnabledCheck?.IsChecked == true;

                    var qtyText = (r.QtyOverrideBox?.Text ?? "").Trim();
                    if (int.TryParse(qtyText, out var qv) && qv > 0)
                        _activeInstrumentSession.FollowerQtyOverrides[accName] = qv;

                    var atm = (r.AtmOverrideBox?.SelectedItem as string) ?? "(inherit master)";
                    _activeInstrumentSession.FollowerAtmOverrides[accName] = atm;
                }
            }
            catch (Exception ex)
            {
                LogUnhandled("SafeTradeCopierTool.SaveUiToActiveSession()", ex);
                throw;
            } 
        }

        private void LoadActiveSessionToUi()
        {
            try
            {
                if (_activeInstrumentSession == null)
                    return;

                _suppressSessionUiEvents = true;
                try
                {
                    RefreshInstrumentSelectorItems();

                    var instrumentName = NormalizeInstrumentName(_activeInstrumentSession.InstrumentName);

                    if (!string.IsNullOrWhiteSpace(instrumentName) && !_instrumentSelector.Items.Contains(instrumentName))
                        _instrumentSelector.Items.Add(instrumentName);

                    _instrumentSelector.Text = instrumentName;
                    _instrumentSelector.SelectedItem = instrumentName;

                    var targetMaster = _activeInstrumentSession.MasterAccount;
                    if (targetMaster == null)
                        targetMaster = _masterBox.SelectedItem as Account ?? GetSelectableAccounts().FirstOrDefault(IsSimAccount) ?? GetSelectableAccounts().FirstOrDefault();

                    _masterBox.SelectedItem = targetMaster;

                    if (_activeInstrumentSession.MasterAccount == null)
                        _activeInstrumentSession.MasterAccount = targetMaster;
                    _masterQtyBox.Text =
                        (_activeInstrumentSession.MasterQty > 0 ? _activeInstrumentSession.MasterQty : 1).ToString();
                    _masterAtmBox.SelectedItem = _activeInstrumentSession.MasterAtm ?? "None";

                    foreach (var r in _followerRows)
                    {
                        if (r?.Account == null)
                            continue;

                        var accName = r.Account.Name;

                        r.EnabledCheck.IsChecked =
                            _activeInstrumentSession.FollowersEnabled.TryGetValue(accName, out var included) &&
                            included;

                        r.QtyOverrideBox.Text =
                            _activeInstrumentSession.FollowerQtyOverrides.TryGetValue(accName, out var qv)
                                ? qv.ToString()
                                : "";

                        var atm =
                            _activeInstrumentSession.FollowerAtmOverrides.TryGetValue(accName, out var av)
                                ? av
                                : "(inherit master)";

                        r.AtmOverrideBox.SelectedItem = atm;
                        RenderFollowerRowState(r);
                    }
                }
                finally
                {
                    _suppressSessionUiEvents = false;
                }

                ApplyConfigFromUi();
                RenderFollowerRowsState();
            }
            catch (Exception ex)
            {
                LogUnhandled("SafeTradeCopierTool.LoadActiveSessionToUi()", ex);
                throw;
            }
        }
    }
}