using System;
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
            eng.EnsureFlatInstrument(master, instr);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.IncludeCheck?.IsChecked != true) continue;
                
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