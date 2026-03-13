using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void SubmitMasterMarket(SafeCopierEngine eng, bool isBuy)
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

            var qty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);
            var action = isBuy ? OrderAction.Buy : OrderAction.Sell;
            
            if (!eng.CanEnterForRisk(master, out var riskReason))
            {
                eng.Log($"Master blocked by risk -> {master.Name}: {riskReason}");
                return;
            }

            var atm = NormalizeAtm(_masterAtmBox?.SelectedItem as string);

            if (!string.Equals(atm, "None", StringComparison.OrdinalIgnoreCase))
            {
                eng.SubmitMasterMarketWithBracket(master, instr, action, qty, atm);
                return;
            }

            var entryName = "STC:ENTRY:" + Guid.NewGuid().ToString("N");

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
                entryName,
                DateTime.MaxValue,
                null
            );

            eng.Log($"Master submit -> {master.Name}: {action} MKT qty={qty} instr={instr.FullName}");
            master.Submit(new[] { ord });
        }
    }
}