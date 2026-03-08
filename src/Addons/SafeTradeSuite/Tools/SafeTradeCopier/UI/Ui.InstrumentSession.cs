using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

            _instrumentTabs.SelectionChanged -= OnInstrumentTabsSelectionChanged;
            _instrumentTabs.Items.Clear();

            foreach (var session in _instrumentSessions)
            {
                var headerText = NormalizeInstrumentName(session?.InstrumentName);
                if (string.IsNullOrWhiteSpace(headerText))
                    headerText = "(instrument)";

                var closeButton = new Button
                {
                    Content = "×",
                    Tag = session,
                    Width = 18,
                    Height = 18,
                    MinWidth = 18,
                    MinHeight = 18,
                    Padding = new Thickness(0),
                    Margin = new Thickness(2, 0, 0, 0),
                    Visibility = Visibility.Visible,
                    Focusable = false,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = Brushes.DimGray,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Remove instrument"
                };

                closeButton.MouseEnter += (s,e) => closeButton.Foreground = Brushes.Red;
                closeButton.MouseLeave += (s,e) => closeButton.Foreground = Brushes.DimGray;
                closeButton.Click += OnInstrumentTabCloseClick;

                var textBlock = new TextBlock
                {
                    Text = headerText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var headerPanel = new DockPanel
                {
                    LastChildFill = false,
                    MinWidth = 92,
                    Margin = new Thickness(0)
                };

                DockPanel.SetDock(closeButton, Dock.Right);
                DockPanel.SetDock(textBlock, Dock.Left);

                headerPanel.Children.Add(closeButton);
                headerPanel.Children.Add(textBlock);

                var tab = new TabItem
                {
                    Header = headerPanel,
                    Tag = session,
                    MinWidth = 92,
                    Padding = new Thickness(6, 2, 6, 2)
                };

                _instrumentTabs.Items.Add(tab);

                if (ReferenceEquals(session, _activeInstrumentSession))
                    _instrumentTabs.SelectedItem = tab;
            }

            if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                _activeInstrumentSession = _instrumentSessions[0];

            if (_instrumentTabs.SelectedItem == null && _activeInstrumentSession != null)
            {
                foreach (var obj in _instrumentTabs.Items)
                {
                    if (obj is TabItem tab && ReferenceEquals(tab.Tag, _activeInstrumentSession))
                    {
                        _instrumentTabs.SelectedItem = tab;
                        break;
                    }
                }
            }

            _instrumentTabs.SelectionChanged += OnInstrumentTabsSelectionChanged;
        }
                
        private void SyncSessionsToAvailableInstruments()
        {
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var instrument in GetAvailableInstruments())
            {
                var normalized = NormalizeInstrumentName(instrument);
                if (IsValidInstrumentName(normalized))
                    available.Add(normalized);
            }

            if (_instrumentSelector != null)
            {
                foreach (var obj in _instrumentSelector.Items)
                {
                    var normalized = NormalizeInstrumentName(obj as string);
                    if (IsValidInstrumentName(normalized))
                        available.Add(normalized);
                }
            }

            // remove blank placeholders once real instruments exist
            if (available.Count > 0)
            {
                _instrumentSessions.RemoveAll(x =>
                    x != null &&
                    string.IsNullOrWhiteSpace(NormalizeInstrumentName(x.InstrumentName)));
            }

            foreach (var instrument in available)
            {
                var exists = _instrumentSessions.Any(x =>
                    string.Equals(
                        NormalizeInstrumentName(x?.InstrumentName),
                        instrument,
                        StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    _instrumentSessions.Add(new InstrumentSession
                    {
                        InstrumentName = instrument
                    });
                }
            }

            // de-dupe sessions by instrument name
            var deduped = _instrumentSessions
                .Where(x => x != null)
                .GroupBy(x => NormalizeInstrumentName(x.InstrumentName), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            _instrumentSessions.Clear();
            _instrumentSessions.AddRange(deduped);

            if (_instrumentSessions.Count == 0)
            {
                _instrumentSessions.Add(new InstrumentSession
                {
                    InstrumentName = ""
                });
            }

            if (_activeInstrumentSession != null)
            {
                var activeName = NormalizeInstrumentName(_activeInstrumentSession.InstrumentName);
                var match = _instrumentSessions.FirstOrDefault(x =>
                    string.Equals(
                        NormalizeInstrumentName(x?.InstrumentName),
                        activeName,
                        StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    _activeInstrumentSession = match;
            }

            if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                _activeInstrumentSession = _instrumentSessions[0];
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
                    SaveUiToActiveSession();
                    _activeInstrumentSession = existing;
                }

                if (refreshSelector)
                    RefreshInstrumentSelectorItems();

                RefreshInstrumentTabs();
                LoadActiveSessionToUi();
                return;
            }

            SaveUiToActiveSession();

            var session = new InstrumentSession
            {
                InstrumentName = normalized,
                MasterAccount = _masterBox?.SelectedItem as Account,
                MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1),
                MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None"
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;

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
            try
            {
                if (_activeInstrumentSession == null)
                    return;

                var selectedInstrument = NormalizeInstrumentName(GetSelectedInstrumentName());
                if (IsValidInstrumentName(selectedInstrument))
                    _activeInstrumentSession.InstrumentName = selectedInstrument;

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
                RenderFlattenEnablementUi();
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