using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static int GetNetPosition(Account acc, Instrument instr)
        {
            if (acc == null || instr == null) return 0;

            foreach (var p in acc.Positions)
            {
                if (p?.Instrument == null) continue;
                if (p.Instrument.FullName != instr.FullName) continue;

                var qty = (int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero);
                if (p.MarketPosition == MarketPosition.Short)
                    qty = -Math.Abs(qty);
                else if (p.MarketPosition == MarketPosition.Long)
                    qty = Math.Abs(qty);
                else
                    qty = 0;

                return qty;
            }

            return 0;
        }
        
        private void FreeTradeMasterSelected(SafeCopierEngine eng)
        {
            if (!(_masterBox?.SelectedItem is Account masterAcc))
                return;

            var instr = GetInstrument();
            if (instr == null)
            {
                eng.Log("Invalid instrument.");
                return;
            }

            if (!_breakEvenEnabled)
            {
                eng.Log("Break-even disabled in Settings.");
                return;
            }

            if (eng.CanUndoFreeTrade(masterAcc, instr, out _))
            {
                if (eng.UndoFreeTrade(masterAcc, instr))
                    eng.Log($"Free Trade undone -> {masterAcc.Name} ({instr.FullName})");
            }
            else
            {
                if (eng.ApplyFreeTrade(masterAcc, instr, _freeTradeMinProfitPoints, _freeTradePlusPoints))
                    eng.Log($"Free Trade applied -> {masterAcc.Name} ({instr.FullName})");
            }

            RenderBreakEvenEnablementUi();
        }

        private void FreeTradeAllSelected(SafeCopierEngine eng)
        {
            var instr = GetInstrument();
            if (instr == null)
            {
                eng.Log("Invalid instrument.");
                return;
            }

            if (!_breakEvenEnabled)
            {
                eng.Log("Break-even disabled in Settings.");
                return;
            }

            var accounts = new List<Account>();

            if (_masterBox?.SelectedItem is Account masterAcc)
                accounts.Add(masterAcc);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.EnabledCheck?.IsChecked != true) continue;
                accounts.Add(r.Account);
            }

            eng.ApplyFreeTradeAll(accounts, instr, _freeTradeMinProfitPoints, _freeTradePlusPoints);
        }
        
        private void FlattenAllSelected(SafeCopierEngine eng)
        {
            if (eng == null) return;

            if (!(_masterBox?.SelectedItem is Account master))
            {
                eng.Log("Select a master account first.");
                return;
            }

            var instr = GetInstrument();
            if (instr == null)
            {
                eng.Log("Invalid instrument.");
                return;
            }

            eng.Log($"Flatten All clicked. Instr={instr.FullName}");

            if (_masterPnlBar != null)
                _masterPnlBar.Tag = "ORDER_FILLED";

            eng.EnsureFlatInstrument(master, instr);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.IncludeCheck?.IsChecked != true) continue;

                if (r.PnlBar != null)
                    r.PnlBar.Tag = "ORDER_FILLED";

                eng.EnsureFlatInstrument(r.Account, instr);
            }

            eng.Log("Flatten All submitted (instrument-only).");
        }
        
        private bool CanFlatten(Account account, string instrFull)
        {
            if (account is null)
                return false;
                
            int net;
            var key = $"{account.Name}|{instrFull}";

            lock (_uiNet)
                _uiNet.TryGetValue(key, out net);

            if (net == 0)
            {
                foreach (var p in account.Positions)
                {
                    if (p?.Instrument == null) continue;
                    if (!string.Equals(p.Instrument.FullName, instrFull, StringComparison.Ordinal)) continue;
                    net = p.Quantity;
                    break;
                }
            }

            return net != 0;
        }
        
        private static bool TryGetInstrumentUnrealized(Account acc, Instrument instr, out double unrealized, out int absQty)
        {
            unrealized = 0;
            absQty = 0;
            if (acc == null || instr == null) return false;

            foreach (var pos in acc.Positions)
            {
                if (pos?.Instrument == null) continue;
                if (!string.Equals(pos.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                absQty = Math.Abs((int)Math.Round((double)pos.Quantity, MidpointRounding.AwayFromZero));
                unrealized = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                return absQty > 0;
            }

            return false;
        }
    }
}