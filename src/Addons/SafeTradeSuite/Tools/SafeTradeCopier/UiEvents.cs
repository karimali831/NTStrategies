#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

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
        
        
        private void FlattenAllSelected(SafeCopierEngine eng)
        {
            if (eng == null) return;

            var master = _masterBox?.SelectedItem as Account;
            if (master == null)
            {
                eng.Log("Select a master account first.");
                return;
            }

            var instrName = (_instrBox?.Text ?? "").Trim();
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

            // Flatten master + enabled followers
            eng.FlattenInstrument(master, instr);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.EnabledCheck?.IsChecked != true) continue;
                eng.FlattenInstrument(r.Account, instr);
            }

            eng.Log("Flatten All submitted (instrument-only).");
        }
        
      
        
        private void StartPnLTimer()
        {
            if (_pnlTimer != null) return;
            var display = _uiDispatcher ?? Dispatcher.CurrentDispatcher;

            _pnlTimer = new DispatcherTimer(DispatcherPriority.Background, display)
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _pnlTimer.Tick += (s, e) => RefreshPnLUi();
            _pnlTimer.Start();

            RefreshPnLUi();
        }

        private void StopPnLTimer()
        {
            if (_pnlTimer == null) return;
            _pnlTimer.Stop();
            _pnlTimer = null;
        }

        private void RefreshPnLUi()
        {
            if (_engine == null) return;

            var master = _masterBox?.SelectedItem as Account;
            var instrName = (_instrBox?.Text ?? "").Trim();
            var instr = string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);

            double totalR = 0, totalU = 0;

            if (master != null)
            {
                var r = SafeCopierEngine.GetAccountValue(master, AccountItem.RealizedProfitLoss);
                var u = SafeCopierEngine.GetAccountValue(master, AccountItem.UnrealizedProfitLoss);
                totalR += r; totalU += u;

                if (_masterPnlText != null)
                    _masterPnlText.Text = $"Master PnL  R: {SafeCopierEngine.FmtMoney(r)}  U: {SafeCopierEngine.FmtMoney(u)}";
            }

            foreach (var row in _followerRows)
            {
                if (row?.Account == null) continue;

                var r = SafeCopierEngine.GetAccountValue(row.Account, AccountItem.RealizedProfitLoss);
                var u = SafeCopierEngine.GetAccountValue(row.Account, AccountItem.UnrealizedProfitLoss);

                if (row.PnlText != null)
                    row.PnlText.Text = $"R: {SafeCopierEngine.FmtMoney(r)}  U: {SafeCopierEngine.FmtMoney(u)}";

                // totals include enabled followers only (as requested “master + follower accounts” – I’m assuming enabled)
                if (row.EnabledCheck?.IsChecked == true)
                {
                    totalR += r;
                    totalU += u;
                }

                // enable/disable per-follower flatten button based on open position (instrument-only)
                if (instr != null && row.FlattenBtn != null)
                {
                    var net = _engine.GetNetForUi(row.Account, instr);
                    row.FlattenBtn.IsEnabled = net != 0;
                }
            }

            if (_totalPnlText != null)
                _totalPnlText.Text = $"TOTAL PnL  R: {SafeCopierEngine.FmtMoney(totalR)}  U: {SafeCopierEngine.FmtMoney(totalU)}";

            // flatten all button enabled if any enabled account has open pos
            if (_btnFlattenAll != null && instr != null)
            {
                var anyOpen = false;

                if (master != null && _engine.GetNetForUi(master, instr) != 0)
                    anyOpen = true;

                if (!anyOpen)
                {
                    foreach (var r in _followerRows)
                    {
                        if (r?.Account == null) continue;
                        if (r.EnabledCheck?.IsChecked != true) continue;
                        if (_engine.GetNetForUi(r.Account, instr) != 0) { anyOpen = true; break; }
                    }
                }

                _btnFlattenAll.IsEnabled = anyOpen;
            }
        }
        
        private void SubmitMasterMarket(SafeCopierEngine eng, bool isBuy)
        {
            if (eng == null) return;

            var master = _masterBox?.SelectedItem as Account;
            if (master == null)
            {
                eng.Log("Select a master account first.");
                return;
            }

            var instrName = (_instrBox?.Text ?? "").Trim();
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

            var qty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);
            var action = isBuy ? OrderAction.Buy : OrderAction.Sell;

            var atm = NormalizeAtm(_masterAtmBox?.SelectedItem as string);
            if (!string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase))
                eng.Log($"Master ATM selected: {atm} (selection stored; attach not enabled from AddOn yet)");

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