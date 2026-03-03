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

            if (!(_masterBox?.SelectedItem is Account master))
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

            eng.Log($"Flatten All clicked. Instr={instr.FullName}");

            // Master + included followers (instrument-only)
            eng.EnsureFlatInstrument(master, instr);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.IncludeCheck?.IsChecked != true) continue;

                eng.EnsureFlatInstrument(r.Account, instr);
            }

            eng.Log("Flatten All submitted (instrument-only).");
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
                var r = master.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                var u = master.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);

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
            foreach (var row in _followerRows)
            {
                var acc = row?.Account;
                if (acc == null) continue;

                var r = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                var u = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);

                // Fallback: if account Unrealized is 0 but we have a position on this instrument,
                // compute Unrealized directly from the matching Position.
                if (instr != null && Math.Abs(u) < 0.0001)
                {
                    foreach (var pos in acc.Positions)
                    {
                        if (pos?.Instrument == null) continue;
                        if (!string.Equals(pos.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                        _engine.Log($"UI sees position: {acc.Name} {pos.Instrument.FullName} qty={pos.Quantity}");

                        u = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                        break;
                    }
                }

                totalR += r;
                totalU += u;

                if (row.PnlText != null)
                    row.PnlText.Text = $"R: {r:0.00}  U: {u:0.00}";

                if (row.FlattenBtn != null)
                {
                    if (instr == null)
                    {
                        row.FlattenBtn.IsEnabled = false;
                    }
                    else
                    {
                        var net = _engine.GetNetPositionForUi(acc, instr);
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