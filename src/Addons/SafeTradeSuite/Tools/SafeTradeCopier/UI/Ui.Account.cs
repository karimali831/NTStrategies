using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void EnforceSimOnlyModeUi(List<Account> accounts)
        {
            if (accounts == null) return;

            if (_simOnlyMode && _masterBox != null)
            {
                if (_masterBox.SelectedItem is Account selected && !IsSimAccount(selected))
                {
                    var firstSim = accounts.FirstOrDefault(IsSimAccount);
                    _masterBox.SelectedItem = firstSim;
                }
            }

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
                if (r.BracketOverrideBox != null) r.BracketOverrideBox.IsEnabled = allow;
                if (r.FlattenBtn != null) RenderFlattenFollowerButtonState(r.FlattenBtn, enabled: false);
            }
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
        
        private void RebindMasterAccounts(List<Account> accounts)
        {
            if (_masterBox == null)
                return;

            var prevMasterName = (_masterBox.SelectedItem as Account)?.Name ?? "";

            _masterBox.ItemsSource = accounts;
            _masterBox.DisplayMemberPath = "Name";

            var restoredMaster = !string.IsNullOrWhiteSpace(prevMasterName)
                ? accounts.FirstOrDefault(a => string.Equals(a.Name, prevMasterName, StringComparison.Ordinal))
                : null;

            _masterBox.SelectedItem = restoredMaster ?? accounts.FirstOrDefault();

            EnforceSimOnlyModeUi(accounts);
        }
        
        private void RefreshUiAfterAccountScopeChanged()
        {
            ApplyConfigFromUi();
            RenderFollowerRowsState();
            RefreshCopierStatusPanel();
            RefreshFollowerBulkActionButtons();
            RefreshRiskFieldset();
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