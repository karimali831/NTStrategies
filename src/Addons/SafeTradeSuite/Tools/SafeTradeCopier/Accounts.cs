#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        private List<Account> GetSelectableAccounts()
        {
            // “Connected in Accounts panel” practical filter:
            // must have a connection object and be Connected.
            return Account.All
                .Where(a => a != null && a.Connection != null && a.ConnectionStatus == ConnectionStatus.Connected)
                .OrderBy(a => a.Name)
                .ToList();
        }
        
        private void StartAccountsAutoRefresh()
        {
            if (accountsTimer != null) return;

            var disp = uiDispatcher ?? Dispatcher.CurrentDispatcher;

            accountsTimer = new DispatcherTimer(DispatcherPriority.Background, disp)
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            accountsTimer.Tick += (s, e) => RefreshAccountsUi();
            accountsTimer.Start();

            RefreshAccountsUi();
        }
        
                private void StopAccountsAutoRefresh()
        {
            if (accountsTimer == null) return;
            accountsTimer.Stop();
            accountsTimer = null;
        }

        private void RefreshAccountsUi()
        {
            if (window == null) return;
            if (masterBox == null) return;
            if (followersPanel == null) return;
            if (engine == null) return;

            // Preserve selections
            var prevMasterName = (masterBox.SelectedItem as Account)?.Name ?? "";
            var prevChecked = new HashSet<string>(
                followerCheckboxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => (cb.Tag as Account)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
            );

            var accounts = GetSelectableAccounts();

            var snap = accounts.Select(a => new AccountSnap(a)).ToList();
            if (SameSnapshot(lastAccountsSnapshot, snap))
                return;

            lastAccountsSnapshot = snap;

            masterBox.ItemsSource = accounts;
            masterBox.DisplayMemberPath = "Name";

            var newMaster = !string.IsNullOrWhiteSpace(prevMasterName)
                ? accounts.FirstOrDefault(a => a.Name == prevMasterName)
                : null;

            masterBox.SelectedItem = newMaster ?? accounts.FirstOrDefault();

            // Rebuild follower list
            var master = masterBox.SelectedItem as Account;
            var masterName = master?.Name ?? "";

            followersPanel.Children.Clear();
            followerCheckboxes.Clear();

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
                    if (engine.CopyEnabled)
                        engine.SetCopyEnabled(true);
                };

                cb.Unchecked += (s, e) =>
                {
                    ApplyConfigFromUi();
                    if (engine.CopyEnabled)
                        engine.SetCopyEnabled(true);
                };

                followerCheckboxes.Add(cb);
                followersPanel.Children.Add(cb);
            }

            ApplyConfigFromUi();

            // If COPY is ON, ensure engine re-wires to the refreshed account objects
            if (engine.CopyEnabled)
                engine.SetCopyEnabled(true);
        }
        
        private void LoadAtmTemplatesInto(ComboBox combo)
        {
            if (combo == null) return;

            var items = new List<string> { "None" };

            try
            {
                // Typical NT8 templates path:
                // Documents\NinjaTrader 8\templates\AtmStrategy\
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folder = System.IO.Path.Combine(docs, "NinjaTrader 8", "templates", "AtmStrategy");

                if (System.IO.Directory.Exists(folder))
                {
                    foreach (var f in System.IO.Directory.GetFiles(folder, "*.xml"))
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(f);
                        if (!string.IsNullOrWhiteSpace(name))
                            items.Add(name);
                    }
                }
            }
            catch
            {
                // No try/catch preference noted for “unhandled” —
                // but template loading is non-critical UI. If you want *zero* try/catch,
                // remove this and we’ll just let it throw (not recommended for UX).
            }

            items = items.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
            combo.ItemsSource = items;
            combo.SelectedItem = items.Contains("None") ? "None" : items.FirstOrDefault();
        }
    }
}