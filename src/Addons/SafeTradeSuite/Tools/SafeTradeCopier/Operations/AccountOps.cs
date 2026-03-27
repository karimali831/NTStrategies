using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private List<Account> GetSelectableAccounts()
        {
            var accounts = Account.All
                .Where(a => a?.Connection != null);

            if (_simOnlyMode)
                accounts = accounts.Where(IsSimAccount);
            else
                accounts = accounts.Where(a => !IsSimAccount(a));

            return accounts
                .OrderBy(a => a.Name)
                .ToList();
        }
        
        private static bool IsSimAccount(Account acc)
        {
            var n = acc?.Name ?? "";
            return n.StartsWith("Sim", StringComparison.OrdinalIgnoreCase)
                   || n.StartsWith("Playback", StringComparison.OrdinalIgnoreCase);
        }

        private Account GetMasterAccount()
        {
            return _activeInstrumentSession?.MasterAccount 
                ?? _masterBox?.SelectedItem as Account;
        }
        
        private bool IsMasterConnected(out Account acc)
        {
            acc = GetMasterAccount();

            if (acc == null)
                return false;

            return GetUiConnectionState(acc) == UiConnectionState.Connected;
        }
        
        private void RefreshAccountsUi()
        {
            if (_window == null || _masterBox == null || _followersPanel == null || _engine == null)
                return;

            var accounts = GetSelectableAccounts();
            var snap = accounts.Select(a => new AccountSnap(a)).ToList();
            var snapshotChanged = !SameSnapshot(_lastAccountsSnapshot, snap);

            if (snapshotChanged)
            {
                _lastAccountsSnapshot = snap;

                if (!_isLoadingSessionUi && !SuppressSessionUiEvents)
                {
                    RebindMasterAccounts(accounts);
                    RebuildFollowersAndRewire(_engine, accounts);
                    RefreshRiskFieldset();
                }

                return;
            }

            RenderFollowerRowsState();
            RefreshCopierStatusPanel();
            RenderMasterSubmitButtonsState();
            RenderPnlUi();
        }
        
        private string DiagCheckedFollowers()
        {
            return string.Join(",",
                _followerRows
                    .Where(r => r?.Account != null)
                    .Select(r => $"{r.Account.Name}:{(r.EnabledCheck?.IsChecked == true ? "1" : "0")}")
            );
        }

        private int DiagCheckedFollowerCount()
        {
            return _followerRows.Count(r => r?.Account != null && r.EnabledCheck?.IsChecked == true);
        }
        
        private bool AreAllCheckedFollowersHealthy()
        {
            var query = _followerRows
                .Where(r => r?.Account != null && r.EnabledCheck?.IsChecked == true)
                .ToList();

            return query.Any() &&
                   query.All(r => GetUiConnectionState(r.Account) == UiConnectionState.Connected);
        }
        
        private int CountCheckedFollowersHealthy()
        {
            return _followerRows.Count(r => r?.Account != null && r.EnabledCheck?.IsChecked == true && 
                GetUiConnectionState(r.Account) == UiConnectionState.Connected);
        }
        
        private IList<FollowerRow> GetCheckedFollowers()
        {
            return _followerRows
                .Where(r => r?.Account != null && r.EnabledCheck?.IsChecked == true)
                .ToList();
        }
        
        private IList<FollowerRow> GetCheckedFollowersHealthy()
        {
            return _followerRows
                .Where(r => r?.Account != null && r.EnabledCheck?.IsChecked == true
                    && GetUiConnectionState(r.Account) == UiConnectionState.Connected)
                .ToList();
        }
        
        private bool HasAnyCheckedFollowers()
        {
            return _followerRows.Any(r => r?.Account != null && r.EnabledCheck?.IsChecked == true);
        }
        
        private static bool SameSnapshot(List<AccountSnap> a, List<AccountSnap> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            return !a.Where((t, i) => !t.Equals(b[i])).Any();
        }

        private sealed class AccountSnap
        {
            private readonly string _name;
            private readonly string _connName;

            public AccountSnap(Account a)
            {
                _name = a?.Name ?? "";
                _connName = a?.Connection?.Options?.Name ?? "";
            }

            public override bool Equals(object obj)
            {
                if (!(obj is AccountSnap o)) return false;

                return string.Equals(_name, o._name, StringComparison.Ordinal)
                       && string.Equals(_connName, o._connName, StringComparison.Ordinal);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var h = 17;
                    h = h * 31 + (_name?.GetHashCode() ?? 0);
                    h = h * 31 + (_connName?.GetHashCode() ?? 0);
                    return h;
                }
            }
        }
    }
}