using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
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