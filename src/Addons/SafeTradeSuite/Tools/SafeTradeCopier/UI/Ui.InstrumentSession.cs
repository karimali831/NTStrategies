using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void AddInstrumentSession(string instrumentName)
        {
            SaveUiToActiveSession();

            var session = new InstrumentSession
            {
                InstrumentName = instrumentName,
                MasterAccount = _masterBox?.SelectedItem as Account,
                MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1),
                MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None"
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;

            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
        }
        
        private void RefreshInstrumentTabs()
        {
            if (_instrumentTabs == null) return;

            _instrumentTabs.SelectionChanged -= OnInstrumentTabsSelectionChanged;
            _instrumentTabs.Items.Clear();

            foreach (var session in _instrumentSessions)
            {
                var tab = new TabItem
                {
                    Header = session.InstrumentName ?? "(instrument)",
                    Tag = session
                };

                _instrumentTabs.Items.Add(tab);

                if (ReferenceEquals(session, _activeInstrumentSession))
                    _instrumentTabs.SelectedItem = tab;
            }

            _instrumentTabs.SelectionChanged += OnInstrumentTabsSelectionChanged;
        }
        
        private void OnInstrumentTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_instrumentTabs?.SelectedItem is TabItem tab && tab.Tag is InstrumentSession session)
                SwitchToSession(session);
        }
        
        private void SwitchToSession(InstrumentSession session)
        {
            if (session == null) return;
            if (ReferenceEquals(_activeInstrumentSession, session)) return;

            SaveUiToActiveSession();
            _activeInstrumentSession = session;
            LoadActiveSessionToUi();
        }
        
        private void SaveUiToActiveSession()
        {
            if (_activeInstrumentSession == null) return;

            _activeInstrumentSession.InstrumentName = (_instrBox?.Text ?? "").Trim();
            _activeInstrumentSession.MasterAccount = _masterBox?.SelectedItem as Account;
            _activeInstrumentSession.MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);
            _activeInstrumentSession.MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None";

            _activeInstrumentSession.FollowersEnabled.Clear();
            _activeInstrumentSession.FollowerQtyOverrides.Clear();
            _activeInstrumentSession.FollowerAtmOverrides.Clear();

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;

                var accName = r.Account.Name;

                _activeInstrumentSession.FollowersEnabled[accName] = r.EnabledCheck?.IsChecked == true;

                var qtyText = (r.QtyOverrideBox?.Text ?? "").Trim();
                if (int.TryParse(qtyText, out var qv) && qv > 0)
                    _activeInstrumentSession.FollowerQtyOverrides[accName] = qv;

                var atm = (r.AtmOverrideBox?.SelectedItem as string) ?? "(inherit master)";
                _activeInstrumentSession.FollowerAtmOverrides[accName] = atm;
            }
        }
        
        private void LoadActiveSessionToUi()
        {
            if (_activeInstrumentSession == null) return;

            _suppressSessionUiEvents = true;
            try
            {
                _instrBox.Text = _activeInstrumentSession.InstrumentName ?? "";

                _masterBox.SelectedItem = _activeInstrumentSession.MasterAccount;
                _masterQtyBox.Text = (_activeInstrumentSession.MasterQty > 0 ? _activeInstrumentSession.MasterQty : 1).ToString();
                _masterAtmBox.SelectedItem = _activeInstrumentSession.MasterAtm ?? "None";

                foreach (var r in _followerRows)
                {
                    if (r?.Account == null) continue;

                    var accName = r.Account.Name;

                    r.EnabledCheck.IsChecked =
                        _activeInstrumentSession.FollowersEnabled.TryGetValue(accName, out var included) && included;

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
    }
}