using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void RebuildFollowersAndRewire(SafeCopierEngine eng, List<Account> accounts)
        {
            // preserve follower selections by account name (optional but nice)
            var selected = new HashSet<string>(
                _followerRows.Where(r => r?.EnabledCheck?.IsChecked == true && r.Account != null).Select(r => r.Account.Name),
                StringComparer.Ordinal);

            // rebuild rows (excludes current master)
            BuildFollowerRows(accounts);

            // restore selections
            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.EnabledCheck == null) continue;
                r.EnabledCheck.IsChecked = selected.Contains(r.Account.Name);
            }

            // sim-only enforcement after rebuild
            EnforceSimOnlyModeUi(accounts);

            // reload ATMs into NEW combo instances
            foreach (var r in _followerRows)
                LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

            // rewire follower flatten button handlers (NEW button instances)
            WireFollowerFlattenButtons(eng);

            // update engine config
            ApplyConfigFromUi();

            if (eng.CopyEnabled)
                eng.SetCopyEnabled(true);
        }
        
        private void WireFollowerFlattenButtons(SafeCopierEngine eng)
        {
            foreach (var r in _followerRows)
            {
                if (r?.FlattenBtn == null) continue;

                r.FlattenBtn.Click += (s, e) =>
                {
                    if (eng == null) return;
                    if (r.Account == null) return;

                    var instrName = (_instrBox?.Text ?? "").Trim();
                    var instr = string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);
                    if (instr == null)
                    {
                        eng.Log("Invalid instrument (must match NT instrument exactly).");
                        return;
                    }

                    if (r.PnlBar != null)
                        r.PnlBar.Tag = "ORDER_FILLED";

                    eng.EnsureFlatInstrument(r.Account, instr);
                    eng.Log($"Flatten submitted -> {r.Account.Name} ({instr.FullName})");
                };
            }
        }
    }
}