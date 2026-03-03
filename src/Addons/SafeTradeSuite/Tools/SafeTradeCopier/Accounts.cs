#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        // private List<Account> GetSelectableAccounts()
        // {
        //     // “Connected in Accounts panel” practical filter:
        //     // must have a connection object and be Connected.
        //     return Account.All
        //         .Where(a => a != null && a.Connection != null && a.ConnectionStatus == ConnectionStatus.Connected)
        //         .OrderBy(a => a.Name)
        //         .ToList();
        // }
        //
        private static List<Account> GetSelectableAccounts()
        {
            return Account.All
                .Where(a => a != null && a.Connection != null && a.ConnectionStatus == ConnectionStatus.Connected)
                .OrderBy(a => a.Name)
                .ToList();
        }
        
        private void StartAccountsAutoRefresh()
        {
            if (_accountsTimer != null) return;

            var display = _uiDispatcher ?? Dispatcher.CurrentDispatcher;

            _accountsTimer = new DispatcherTimer(DispatcherPriority.Background, display)
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _accountsTimer.Tick += (s, e) => RefreshAccountsUi();
            _accountsTimer.Start();

            RefreshAccountsUi();
        }
        
        private void StopAccountsAutoRefresh()
        {
            if (_accountsTimer == null) return;
            _accountsTimer.Stop();
            _accountsTimer = null;
        }

        private void RefreshAccountsUi()
        {
            if (_window == null) return;
            if (_masterBox == null) return;
            if (_followersPanel == null) return;
            if (_engine == null) return;

            // Preserve selections
            var prevMasterName = (_masterBox.SelectedItem as Account)?.Name ?? "";
            var prevChecked = new HashSet<string>(
                _followerCheckboxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => (cb.Tag as Account)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
            );

            var accounts = GetSelectableAccounts();

            var snap = accounts.Select(a => new AccountSnap(a)).ToList();
            if (SameSnapshot(_lastAccountsSnapshot, snap))
                return;

            _lastAccountsSnapshot = snap;

            _masterBox.ItemsSource = accounts;
            _masterBox.DisplayMemberPath = "Name";

            var newMaster = !string.IsNullOrWhiteSpace(prevMasterName)
                ? accounts.FirstOrDefault(a => a.Name == prevMasterName)
                : null;

            _masterBox.SelectedItem = newMaster ?? accounts.FirstOrDefault();

            // Rebuild follower list
            var master = _masterBox.SelectedItem as Account;
            var masterName = master?.Name ?? "";

            _followersPanel.Children.Clear();
            _followerCheckboxes.Clear();

            foreach (var acc in accounts)
            {
                if (!string.IsNullOrWhiteSpace(masterName) && acc.Name == masterName)
                    continue;

                var cb = new CheckBox
                {
                    Content = acc.Name,
                    Tag = acc,
                    Margin = new Thickness(6, 3, 6, 3),
                    Foreground = SystemColors.ControlTextBrush,
                    IsChecked = prevChecked.Contains(acc.Name)
                };

                // Hook changes: auto re-apply config / rewire if COPY ON
                cb.Checked += (s, e) =>
                {
                    ApplyConfigFromUi();
                    if (_engine.CopyEnabled)
                        _engine.SetCopyEnabled(true);
                };

                cb.Unchecked += (s, e) =>
                {
                    ApplyConfigFromUi();
                    if (_engine.CopyEnabled)
                        _engine.SetCopyEnabled(true);
                };

                _followerCheckboxes.Add(cb);
                _followersPanel.Children.Add(cb);
            }

            ApplyConfigFromUi();

            // If COPY is ON, ensure engine re-wires to the refreshed account objects
            if (_engine.CopyEnabled)
                _engine.SetCopyEnabled(true);
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