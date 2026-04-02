using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private void EnforceSimOnlyModeUi(List<Account> accounts)
        {
            if (accounts == null)
                return;

            if (_simOnlyMode && _masterBox != null)
            {
                var selectedMaster = GetMasterAccount();
                if (selectedMaster != null && !IsSimAccount(selectedMaster))
                {
                    var firstSim = accounts.FirstOrDefault(IsSimAccount);

                    using (BeginSessionUiSuppression())
                    {
                        _masterBox.SelectedItem = firstSim;
                    }

                    if (_activeInstrumentSession != null)
                        _activeInstrumentSession.MasterAccount = firstSim;
                }
            }

            foreach (var r in _followerRows)
            {
                if (r?.Account == null)
                    continue;

                var allow = !_simOnlyMode || IsSimAccount(r.Account);

                if (r.EnabledCheck != null)
                {
                    r.EnabledCheck.IsEnabled = allow;

                    if (!allow)
                    {
                        using (BeginSessionUiSuppression())
                        {
                            r.EnabledCheck.IsChecked = false;
                        }
                    }
                }

                if (r.QtyOverrideBox != null)
                    r.QtyOverrideBox.IsEnabled = allow;

                if (r.BracketOverrideBox != null)
                    r.BracketOverrideBox.IsEnabled = allow;

                if (r.FlattenBtn != null)
                    RenderFlattenFollowerButtonState(r.FlattenBtn, enabled: false);
            }
        }

        private void SubscribeUiAccountEvents(IEnumerable<Account> accounts)
        {
            if (accounts == null)
                return;

            foreach (var a in accounts)
            {
                if (a == null)
                    continue;

                a.AccountItemUpdate -= OnUiAccountItemUpdate;
                a.AccountItemUpdate += OnUiAccountItemUpdate;

                a.PositionUpdate -= OnUiPositionUpdate;
                a.PositionUpdate += OnUiPositionUpdate;
            }
        }

        private void RebindMasterAccounts(List<Account> accounts)
        {
            if (_masterBox == null || accounts == null)
                return;

            var prevMasterName = _activeInstrumentSession?.MasterAccount?.Name ?? "";

            var restoredMaster = !string.IsNullOrWhiteSpace(prevMasterName)
                ? accounts.FirstOrDefault(a => string.Equals(a.Name, prevMasterName, StringComparison.Ordinal))
                : null;

            var finalMaster = restoredMaster ?? accounts.FirstOrDefault();

            using (BeginSessionUiSuppression())
            {
                _masterBox.ItemsSource = accounts;
                _masterBox.DisplayMemberPath = "Name";
                _masterBox.SelectedItem = finalMaster;
            }

            if (_activeInstrumentSession != null)
                _activeInstrumentSession.MasterAccount = finalMaster;

            EnforceSimOnlyModeUi(accounts);
        }

        private void RefreshUiAfterAccountScopeChanged()
        {
            ApplyConfigFromUi();
            RenderFollowerRowsState();
            RefreshRelayStatusPanel();
            RefreshFollowerBulkActionButtons();
            RefreshRiskFieldset();
        }

        private void UnsubscribeUiAccountEvents(IEnumerable<Account> accounts)
        {
            if (accounts == null)
                return;

            foreach (var a in accounts)
            {
                if (a == null)
                    continue;

                a.AccountItemUpdate -= OnUiAccountItemUpdate;
                a.PositionUpdate -= OnUiPositionUpdate;
            }
        }

        private void OnUiAccountItemUpdate(object sender, AccountItemEventArgs e)
        {
            if (e?.Account == null)
                return;

            if (e.Currency != Currency.UsDollar)
                return;

            if (e.AccountItem != AccountItem.RealizedProfitLoss &&
                e.AccountItem != AccountItem.UnrealizedProfitLoss)
                return;

            RenderPnlUi();
        }
    }
}