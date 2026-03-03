using System.Windows;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void WireOrderButtons(SafeCopierEngine eng)
        {
            _btnBuyMkt.Click += (s, e) => SubmitMasterMarket(eng, isBuy: true);
            _btnSellMkt.Click += (s, e) => SubmitMasterMarket(eng, isBuy: false);
            
            _btnFlattenAll.Click += (s, e) =>
            {
                MessageBox.Show("Flatten button physically clicked.");
            };
            
            // _btnFlattenAll.Click += (s, e) => FlattenAllSelected(eng);
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

                    eng.FlattenInstrument(r.Account, instr);
                    eng.Log($"Flatten submitted -> {r.Account.Name} ({instr.FullName})");
                };
            }
        }
    }
}