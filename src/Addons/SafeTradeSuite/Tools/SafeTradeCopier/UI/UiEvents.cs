using System;
using System.Collections.Generic;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void ApplyConfigFromUi()
        {
            if (_engine == null) return;

            var master = _masterBox?.SelectedItem as Account;
            var instr = _instrBox?.Text?.Trim() ?? "";

            // master qty
            var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);

            // master ATM
            var masterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None";
            if (string.IsNullOrWhiteSpace(masterAtm)) masterAtm = "None";

            // followers enabled + overrides
            var followers = new List<Account>();
            var qtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            var atmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;

                var enabled = r.EnabledCheck?.IsChecked == true;
                if (!enabled) continue;

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

        private static bool SameSnapshot(List<AccountSnap> a, List<AccountSnap> b)
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
        
        private static int ParseQtyOrDefault(string s, int fallback)
        {
            if (int.TryParse((s ?? "").Trim(), out var v) && v > 0) return v;
            return fallback;
        }
        
        private void StartPnLTimer()
        {
            if (_pnlTimer != null) return;

            var disp = _uiDispatcher ?? _window?.Dispatcher;
            if (disp == null) return;

            _pnlTimer = new DispatcherTimer(DispatcherPriority.Background, disp)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            _pnlTimer.Tick += (s, e) => UpdatePnLUi();
            _pnlTimer.Start();

            UpdatePnLUi();
        }

        private void StopPnLTimer()
        {
            if (_pnlTimer == null) return;
            _pnlTimer.Stop();
            _pnlTimer = null;
        }

        private void UpdatePnLUi()
        {
            if (_engine == null) return;

            var master = _masterBox?.SelectedItem as Account;
            var instrName = (_instrBox?.Text ?? "").Trim();
            var instr = string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);

            var totalR = 0.0;
            var totalU = 0.0;

            // --- master ---
            if (master != null)
            {
                _engine.TryGetPnlForUi(master, out var r, out var u);
                
                totalR += r;
                totalU += u;

                if (_masterPnlText != null)
                    _masterPnlText.Text = $"Master PnL  R: {r:0.00}  U: {u:0.00}";
            }
            else
            {
                if (_masterPnlText != null)
                    _masterPnlText.Text = $"Master PnL  R: 0.00  U: 0.00";
            }

            // --- followers rows ---
            // --- followers rows ---
            foreach (var row in _followerRows)
            {
                var acc = row?.Account;
                if (acc == null) continue;

                _engine.TryGetPnlForUi(acc, out var r, out var u);

                totalR += r;
                totalU += u;

                if (row.PnlText != null)
                    row.PnlText.Text = $"R: {r:0.00}  U: {u:0.00}";

                // enable/disable per-account flatten based on instrument net position (from Positions)
                if (row.FlattenBtn != null)
                {
                    if (instr == null)
                    {
                        row.FlattenBtn.IsEnabled = false;
                    }
                    else
                    {
                        var net = GetNetPositionForUi(acc, instr);
                        row.FlattenBtn.IsEnabled = (net != 0);
                    }
                }
            }

            if (_totalPnlText != null)
                _totalPnlText.Text = $"TOTAL PnL  R: {totalR:0.00}  U: {totalU:0.00}";

            // enable/disable FlattenAll if ANY selected account has open position on instrument
            if (_btnFlattenAll != null)
                _btnFlattenAll.IsEnabled = instr != null && master != null;
            
        }
    }
}