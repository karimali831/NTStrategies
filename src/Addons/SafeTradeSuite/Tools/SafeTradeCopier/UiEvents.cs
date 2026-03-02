#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        private void BuildFollowerCheckboxes(List<Account> accounts)
        {
            if (followersPanel == null) return;

            followersPanel.Children.Clear();
            followerCheckboxes.Clear();

            foreach (var acc in accounts)
            {
                var cb = new CheckBox
                {
                    Content = acc.Name,
                    Tag = acc,
                    Margin = new Thickness(6, 3, 6, 3),
                    Foreground = SystemColors.ControlTextBrush
                };

                followerCheckboxes.Add(cb);
                followersPanel.Children.Add(cb);
            }
        }
        
        private void ApplyConfigFromUi()
        {
            if (engine == null) return;
            if (masterBox == null) return;

            var master = masterBox.SelectedItem as Account;
            if (master == null)
            {
                engine.ApplyConfig(null, new List<Account>(), instrBox?.Text?.Trim());
                return;
            }

            var followers = followerCheckboxes
                .Where(cb => cb.IsChecked == true)
                .Select(cb => cb.Tag as Account)
                .Where(a => a != null && !ReferenceEquals(a, master))
                .ToList();

            engine.ApplyConfig(master, followers, instrBox?.Text?.Trim());
        }

        private bool SameSnapshot(List<AccountSnap> a, List<AccountSnap> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }

            return true;
        }
    }
}