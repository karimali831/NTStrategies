#region Using declarations
using System;
using System.Threading.Tasks;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private bool AllowCopyNow()
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-2).Ticks;
                while (_copiedTicks.TryPeek(out var t) && t < cutoff)
                    _copiedTicks.TryDequeue(out _);

                return _copiedTicks.Count <= MaxCopiesPer2Sec;
            }
            
            private void RecordCopy()
            {
                _copiedTicks.Enqueue(DateTime.UtcNow.Ticks);
            }
            
            public int GetNetPositionForUi(Account acc, Instrument instr)
            {
                return GetNetPosition(acc, instr);
            }

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
            
            public static double GetAccountValue(Account a, AccountItem item)
            {
                if (a == null) return 0.0;

                try
                {
                    // Most NT8 installs support this signature
                    return a.Get(item, Currency.UsDollar);
                }
                catch
                {
                    return 0.0;
                }
            }

            public static string FmtMoney(double v)
            {
                return v.ToString("+#,0.00;-#,0.00;0.00");
            }
            
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

            public void FlattenInstrument(Account acc, Instrument instr)
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
    }
}