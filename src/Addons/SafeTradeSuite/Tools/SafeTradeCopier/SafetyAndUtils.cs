#region Using declarations
using System;
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
            
            public int GetNetForUi(Account acc, Instrument instr)
            {
                return GetNetPosition(acc, instr);
            }

            private int GetNetPosition(Account acc, Instrument instr)
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

            public void FlattenInstrument(Account acc, Instrument instr)
            {
                if (acc == null || instr == null) return;

                var net = GetNetPosition(acc, instr);
                if (net == 0) return;

                var action = net > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
                var qty = Math.Abs(net);

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