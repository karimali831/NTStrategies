#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private List<Account> GetSelectableAccounts()
        {
            return Account.All
                .Where(a => a?.Connection != null)
                .OrderBy(a => a.Name)
                .ToList();
        }
        
        private void RefreshAccountsUi()
        {
            if (_window == null) return;
            if (_masterBox == null) return;
            if (_followersPanel == null) return;
            if (_engine == null) return;

            var prevMasterName = (_masterBox.SelectedItem as Account)?.Name ?? "";

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

            // Only do a real master list refresh if account membership/status snapshot changed
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

            // Do NOT rebuild follower rows here.
            // Do NOT re-wire flatten buttons here.

            foreach (var r in _followerRows)
                LoadAtmTemplatesInto(r?.AtmBox, includeInherit: true);

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