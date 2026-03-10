using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private CopierUiConfig _lastAppliedConfig;
        
        private void ApplyConfigFromUi()
        {
            if (_engine == null)
                return;

            var config = BuildConfigFromUi();
            if (config == null)
                return;

            if (AreConfigsEqual(_lastAppliedConfig, config))
                return;

            _engine.ApplyConfig(
                masterAccount: config.MasterAccount,
                followerAccounts: config.Followers,
                instrName: config.InstrumentName,
                masterQty: config.MasterQty,
                masterAtm: config.MasterAtm,
                followerQtyOverridesByAccountName: config.FollowerQtyOverrides,
                followerAtmOverridesByAccountName: config.FollowerAtmOverrides
            );

            _lastAppliedConfig = config;
        }
        
        private CopierUiConfig BuildConfigFromUi()
        {
            var master = _masterBox?.SelectedItem as Account;
            var instr = (_instrumentSelector?.SelectedItem as string ?? "").Trim();

            var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);

            var masterAtm = _masterAtmBox?.SelectedItem as string ?? "None";
            if (string.IsNullOrWhiteSpace(masterAtm))
                masterAtm = "None";

            if (_simOnlyMode && master != null && !IsSimAccount(master))
            {
                var accounts = GetSelectableAccounts();
                var firstSim = accounts.FirstOrDefault(IsSimAccount);
                _masterBox.SelectedItem = firstSim;
                master = firstSim;
            }

            var followers = new List<Account>();
            var qtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            var atmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null)
                    continue;

                var enabled = r.EnabledCheck?.IsChecked == true;
                if (!enabled)
                    continue;

                if (_simOnlyMode && !IsSimAccount(r.Account))
                {
                    r.EnabledCheck.IsChecked = false;
                    continue;
                }

                followers.Add(r.Account);

                var qText = (r.QtyOverrideBox?.Text ?? "").Trim();
                if (int.TryParse(qText, out var qv) && qv > 0)
                    qtyOverrides[r.Account.Name] = qv;

                var aText = (r.AtmOverrideBox?.SelectedItem as string) ?? "(inherit master)";
                if (string.IsNullOrWhiteSpace(aText))
                    aText = "(inherit master)";

                atmOverrides[r.Account.Name] = aText;
            }

            return new CopierUiConfig
            {
                MasterAccount = master,
                InstrumentName = instr,
                MasterQty = masterQty,
                MasterAtm = masterAtm,
                Followers = followers,
                FollowerQtyOverrides = qtyOverrides,
                FollowerAtmOverrides = atmOverrides
            };
        }
        
        private static bool AreConfigsEqual(CopierUiConfig a, CopierUiConfig b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null)
                return false;

            if (!ReferenceEquals(a.MasterAccount, b.MasterAccount))
                return false;

            if (!string.Equals(a.InstrumentName ?? "", b.InstrumentName ?? "", StringComparison.Ordinal))
                return false;

            if (a.MasterQty != b.MasterQty)
                return false;

            if (!string.Equals(a.MasterAtm ?? "", b.MasterAtm ?? "", StringComparison.Ordinal))
                return false;

            if (a.Followers.Count != b.Followers.Count)
                return false;

            for (var i = 0; i < a.Followers.Count; i++)
            {
                if (!ReferenceEquals(a.Followers[i], b.Followers[i]))
                    return false;
            }

            if (a.FollowerQtyOverrides.Count != b.FollowerQtyOverrides.Count)
                return false;

            foreach (var kv in a.FollowerQtyOverrides)
            {
                if (!b.FollowerQtyOverrides.TryGetValue(kv.Key, out var val))
                    return false;

                if (val != kv.Value)
                    return false;
            }

            if (a.FollowerAtmOverrides.Count != b.FollowerAtmOverrides.Count)
                return false;

            foreach (var kv in a.FollowerAtmOverrides)
            {
                if (!b.FollowerAtmOverrides.TryGetValue(kv.Key, out var val))
                    return false;

                if (!string.Equals(val ?? "", kv.Value ?? "", StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}