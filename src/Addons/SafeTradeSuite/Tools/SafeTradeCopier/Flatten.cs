using System;
using System.Collections.Generic;
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
                var orders = new List<Order>();
                try
                {
                    orders.AddRange(acc.Orders);
                }
                catch
                {
                    // if Orders enumeration fails, we still try to flatten net position below
                }

                try
                {
                    foreach (var o in orders)
                    {
                        if (o?.Instrument == null) continue;
                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal)) continue;

                        if (o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted)
                        {
                            acc.Cancel(new[] { o });
                        }
                    }
                }
                catch
                {
                    // non-fatal
                }

                // 2) Now flatten the net position
                var net = GetNetPosition(acc, instr);
                if (net == 0)
                {
                    ClearActiveBracket(acc, instr);
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
            
            if (_masterPnlBar != null)
                _masterPnlBar.Tag = "ORDER_FILLED";

            // Master + included followers (instrument-only)
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
    }
}