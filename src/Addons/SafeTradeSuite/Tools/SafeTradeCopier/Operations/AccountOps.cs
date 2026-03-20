using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static List<Account> GetSelectableAccounts()
        {
            return Account.All
                .Where(a => a?.Connection != null)
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
        
        private bool IsMasterConnected()
        {
            var master = GetMasterAccount();

            if (master == null)
                return false;

            return GetUiConnectionState(master) == UiConnectionState.Connected;
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
                    Included = r.IncludeCheck?.IsChecked == true,
                    OverrideEnabled = false,
                    QtyText = r.QtyBox?.Text ?? "",
                    AtmName = r.AtmBox?.SelectedItem as string
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

                if (r.IncludeCheck != null)
                    r.IncludeCheck.IsChecked = ps.Included;

                if (r.QtyBox != null)
                    r.QtyBox.Text = ps.QtyText ?? "";

                if (r.AtmBox != null && ps.AtmName != null)
                    r.AtmBox.SelectedItem = ps.AtmName;
            }

            EnforceSimOnlyModeUi(accounts);
            RenderFollowerRowsState();
            ApplyConfigFromUi();
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
        
        private static bool HasAnyCheckedFollowers()
        {
            return _followerRows.Any(r => r?.Account != null && r.EnabledCheck?.IsChecked == true);
        }
        
        private static int CountCheckedFollowers()
        {
            return _followerRows.Count(r => r?.Account != null && r.EnabledCheck?.IsChecked == true);
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
            public bool OverrideEnabled;
            public string QtyText;
            public string AtmName;
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
                _connName = a?.Connection != null ? (a.Connection.Options?.Name ?? a.Connection.ToString()) : "";
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