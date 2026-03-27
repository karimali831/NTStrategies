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
            return _masterBox?.SelectedItem as Account;
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
            if (_window == null) return;
            if (_masterBox == null) return;
            if (_followersPanel == null) return;
            if (_engine == null) return;

            var masterAcc = GetMasterAccount();
            var prevMasterName = masterAcc?.Name ?? "";

            var prevFollowers = new Dictionary<string, PrevFollowerState>(StringComparer.Ordinal);
            foreach (var r in _followerRows)
            {
                var name = r?.AccountName;
                if (string.IsNullOrWhiteSpace(name)) continue;

                prevFollowers[name] = new PrevFollowerState
                {
                    Included = r.EnabledCheck?.IsChecked == true,
                    QtyText = r.QtyOverrideBox?.Text ?? "",
                    BracketName = r.BracketOverrideBox?.SelectedItem as string
                };
            }

            var accounts = GetSelectableAccounts();
            var snap = accounts.Select(a => new AccountSnap(a)).ToList();

            if (!SameSnapshot(_lastAccountsSnapshot, snap))
            {
                _lastAccountsSnapshot = snap;

                _masterBox.ItemsSource = accounts;
                _masterBox.DisplayMemberPath = "Name";

                var newMaster = !string.IsNullOrWhiteSpace(prevMasterName)
                    ? accounts.FirstOrDefault(a => a.Name == prevMasterName)
                    : null;

                _masterBox.SelectedItem = newMaster ?? accounts.FirstOrDefault();
            }

            foreach (var r in _followerRows)
            {
                if (r == null) continue;
                if (!prevFollowers.TryGetValue(r.AccountName, out var ps))
                    continue;

                if (r.EnabledCheck != null)
                    r.EnabledCheck.IsChecked = ps.Included;

                if (r.QtyOverrideBox != null)
                    r.QtyOverrideBox.Text = ps.QtyText ?? "";

                if (r.BracketOverrideBox != null && ps.BracketName != null)
                    r.BracketOverrideBox.SelectedItem = ps.BracketName;
            }

            EnforceSimOnlyModeUi(accounts);
            RenderFollowerRowsState();
            ApplyConfigFromUi();
            RenderMasterSubmitButtonsState();
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
        
        private bool HasAnyCheckedSimFollowersHealthy()
        {
            var query = _followerRows
                .Where(r => r.EnabledCheck?.IsChecked == true && IsSimAccount(r.Account))
                .ToList();

            return query.Any() &&
                   query.All(r => GetUiConnectionState(r.Account) == UiConnectionState.Connected);
        }
        
        private bool HasAnyCheckedLiveFollowersHealthy()
        {
            var query = _followerRows
                .Where(r => r.EnabledCheck?.IsChecked == true && !IsSimAccount(r.Account))
                .ToList();

            return query.Any() &&
                   query.All(r => GetUiConnectionState(r.Account) == UiConnectionState.Connected);
        }
        
        private bool HasAnyCheckedFollowersHealthy()
        {
            var query = _followerRows
                .Where(r => r.EnabledCheck?.IsChecked == true)
                .ToList();

            return query.Any() &&
                   query.All(r => GetUiConnectionState(r.Account) == UiConnectionState.Connected);
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
        
        private int CountCheckedFollowersHealthy()
        {
            return _followerRows.Count(r => r?.Account != null && r.EnabledCheck?.IsChecked == true && 
                GetUiConnectionState(r.Account) == UiConnectionState.Connected);
        }
        
        private static bool SameSnapshot(List<AccountSnap> a, List<AccountSnap> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            return !a.Where((t, i) => !t.Equals(b[i])).Any();
        }
        
        private sealed class PrevFollowerState
        {
            public bool Included;
            public string QtyText;
            public string BracketName;
        }

        private sealed class AccountSnap
        {
            private readonly string _name;
            private readonly ConnectionStatus _status;
            private readonly string _connName;

            public AccountSnap(Account a)
            {
                _name = a?.Name ?? "";
                _status = a?.ConnectionStatus ?? ConnectionStatus.Disconnected;
                _connName = a?.Connection != null ? a.Connection.Options?.Name ?? a.Connection.ToString() : "";
            }

            public override bool Equals(object obj)
            {
                if (!(obj is AccountSnap o)) return false;
                return string.Equals(_name, o._name, StringComparison.Ordinal)
                       && _status == o._status
                       && string.Equals(_connName, o._connName, StringComparison.Ordinal);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var h = 17;
                    h = h * 31 + (_name?.GetHashCode() ?? 0);
                    h = h * 31 + _status.GetHashCode();
                    h = h * 31 + (_connName?.GetHashCode() ?? 0);
                    return h;
                }
            }
        }
    }
}