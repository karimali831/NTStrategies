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
        
        private int ParseQtyOrDefault(string s, int fallback)
        {
            if (int.TryParse(s, out var v) && v > 0) return v;
            return fallback;
        }

        private void WireOrderButtons(SafeCopierEngine eng, TextBox instrBox)
        {
            if (btnBuyMkt != null)
                btnBuyMkt.Click += (s, e) => SubmitMasterMarket(eng, instrBox?.Text, isBuy: true);

            if (btnSellMkt != null)
                btnSellMkt.Click += (s, e) => SubmitMasterMarket(eng, instrBox?.Text, isBuy: false);
        }

        private void SubmitMasterMarket(SafeCopierEngine eng, string instrumentName, bool isBuy)
        {
            if (eng == null)
                return;

            var master = masterBox?.SelectedItem as Account;
            if (master == null)
            {
                eng.Log("Select a master account first.");
                return;
            }

            var instrName = (instrumentName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(instrName))
            {
                eng.Log("Instrument is empty.");
                return;
            }

            var instr = Instrument.GetInstrument(instrName);
            if (instr == null)
            {
                eng.Log("Invalid instrument (must match NT instrument exactly).");
                return;
            }

            var qty = ParseQtyOrDefault(qtyBox?.Text, 1);
            var action = isBuy ? OrderAction.Buy : OrderAction.Sell;

            // ATM is currently UI-only for AddOn (logged for now)
            var atm = atmBox?.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(atm) && !string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase))
                eng.Log($"ATM selected: {atm} (note: ATM attach not supported from AddOn yet)");

            // This is the "master action" that should get copied by ExecutionUpdate
            var ord = master.CreateOrder(
                instr,
                action,
                OrderType.Market,
                OrderEntry.Manual,
                TimeInForce.Day,
                qty,
                0,
                0,
                string.Empty,
                "STC:MANUAL",
                DateTime.MaxValue,
                null
            );

            eng.Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName}");
            master.Submit(new[] { ord });
        }
    }
}