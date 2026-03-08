using System;
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

            var textBlock = new TextBlock
            {
                Text = headerText,
                VerticalAlignment = VerticalAlignment.Center
            };

            var closeButton = new Button
            {
                Content = "×",
                Width = 16,
                Height = 16,
                Padding = new Thickness(0),
                Margin = new Thickness(6, 0, 0, 0),
                Opacity = 0,
                Focusable = false,
                Tag = session,
                ToolTip = "Remove instrument",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            closeButton.Click -= OnInstrumentTabCloseClick;
            closeButton.Click += OnInstrumentTabCloseClick;

            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = Brushes.Transparent
            };

            headerPanel.Children.Add(textBlock);
            headerPanel.Children.Add(closeButton);

            headerPanel.MouseEnter += (s, e) => closeButton.Opacity = 1.0;
            headerPanel.MouseLeave += (s, e) => closeButton.Opacity = 0.0;

            var tab = new TabItem
            {
                Header = headerPanel,
                Tag = session
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
            LoadActiveSessionToUi();
        }

        private void ActivateOrCreateInstrumentSession(string instrumentName)
        {
            var normalized = NormalizeInstrumentName(instrumentName);
            if (!IsValidInstrumentName(normalized))
                return;

            RememberInstrument(normalized);

            var existing = _instrumentSessions.FirstOrDefault(x =>
                string.Equals(
                    NormalizeInstrumentName(x?.InstrumentName),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                _activeInstrumentSession = existing;
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

            _instrumentSessions.Remove(sessionToRemove);

            if (!IsInstrumentUsedByAnySession(instrumentToMaybeForget))
                ForgetInstrument(instrumentToMaybeForget);

            var nextIdx = Math.Min(idx, _instrumentSessions.Count - 1);
            _activeInstrumentSession = nextIdx >= 0 ? _instrumentSessions[nextIdx] : null;

            if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                _activeInstrumentSession = _instrumentSessions[0];

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

                _activeInstrumentSession.InstrumentName = NormalizeInstrumentName(GetSelectedInstrumentName());
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

                    _masterBox.SelectedItem = _activeInstrumentSession.MasterAccount;
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