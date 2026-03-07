using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void ApplyConfigFromUi()
        {
            if (_engine == null) return;

            var master = _masterBox?.SelectedItem as Account;
            var instr = _instrBox?.Text.Trim() ?? "";

            // master qty
            var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);

            // master ATM
            var masterAtm = _masterAtmBox?.SelectedItem as string ?? "None";
            if (string.IsNullOrWhiteSpace(masterAtm)) masterAtm = "None";
            
            if (_simOnlyMode && master != null && !IsSimAccount(master))
            {
                // enforce safety: do not allow non-sim master
                var accounts = GetSelectableAccounts();
                var firstSim = accounts.FirstOrDefault(IsSimAccount);
                _masterBox.SelectedItem = firstSim;
                master = firstSim;
            }

            // followers enabled + overrides
            var followers = new List<Account>();
            var qtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            var atmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;

                var enabled = r.EnabledCheck?.IsChecked == true;
                if (!enabled) continue;

                if (_simOnlyMode && !IsSimAccount(r.Account))
                {
                    // safety: if something slips through, force it off
                    r.EnabledCheck.IsChecked = false;
                    continue;
                }

                followers.Add(r.Account);

                // qty override (blank = inherit)
                var qText = (r.QtyOverrideBox?.Text ?? "").Trim();
                if (int.TryParse(qText, out var qv) && qv > 0)
                    qtyOverrides[r.Account.Name] = qv;

                // atm override (inherit master / none / template)
                var aText = (r.AtmOverrideBox?.SelectedItem as string) ?? "(inherit master)";
                if (string.IsNullOrWhiteSpace(aText)) aText = "(inherit master)";
                atmOverrides[r.Account.Name] = aText;
            }

            _engine.ApplyConfig(
                masterAccount: master,
                followerAccounts: followers,
                instrName: instr,
                masterQty: masterQty,
                masterAtm: masterAtm,
                followerQtyOverridesByAccountName: qtyOverrides,
                followerAtmOverridesByAccountName: atmOverrides
            );
        }
    }
}