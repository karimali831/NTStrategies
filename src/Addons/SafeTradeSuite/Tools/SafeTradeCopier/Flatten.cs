using System;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            public void EnsureFlatInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                // fire-and-forget: cancel + flatten now, then re-check once (or twice) shortly after
                Task.Run(async () =>
                {
                    try
                    {
                        FlattenInstrument(acc, instr);

                        // Re-check after NT has processed cancels/fills
                        await Task.Delay(300).ConfigureAwait(false);
                        FlattenInstrument(acc, instr);

                        // Optional: one more pass for stubborn ATM state transitions
                        await Task.Delay(300).ConfigureAwait(false);
                        FlattenInstrument(acc, instr);
                    }
                    catch
                    {
                        // keep tool silent/safe; no exceptions to user from background
                    }
                });
            }

            private void FlattenInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                // 1) Cancel any working orders on this instrument (ATM targets/stops live here)
                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null) continue;
                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                        // cancel anything still working-ish
                        if (o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted)
                        {
                            // NT supports cancelling orders via Account.Cancel
                            acc.Cancel(new[] { o });
                        }
                    }
                }
                catch
                {
                    // non-fatal: flatten should still try to submit market
                }

                // 2) Now flatten the net position
                var net = GetNetPosition(acc, instr);
                if (net == 0)
                {
                    Log($"Flatten -> {acc.Name}: net=0 (nothing to do) instr={instr.FullName}");
                    return;
                }

                var action = net > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                var qty = Math.Abs(net);

                Log($"Flatten -> {acc.Name}: net={net}, action={action}, qty={qty}, instr={instr.FullName}");

                var ord = acc.CreateOrder(
                    instr,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    qty,
                    0,
                    0,
                    string.Empty,
                    "STC:FLATTEN",
                    DateTime.MaxValue,
                    null
                );

                acc.Submit(new[] { ord });
            }
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
    }
}