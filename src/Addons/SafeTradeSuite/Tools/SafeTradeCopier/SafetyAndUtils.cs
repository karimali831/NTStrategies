#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        internal partial class SafeCopierEngine : IDisposable
        {
            private bool AllowCopyNow()
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-2).Ticks;
                while (copiedTicks.TryPeek(out var t) && t < cutoff)
                    copiedTicks.TryDequeue(out _);

                return copiedTicks.Count <= MaxCopiesPer2Sec;
            }
            
            private void RecordCopy()
            {
                copiedTicks.Enqueue(DateTime.UtcNow.Ticks);
            }
            
            private int SignedQtyFromExecution(Execution exec)
            {
                if (exec == null) return 0;

                var qty = (int)Math.Round((double)exec.Quantity, MidpointRounding.AwayFromZero);
                if (qty == 0) return 0;

                var action = exec.Order?.OrderAction ?? OrderAction.Buy;

                if (action == OrderAction.Buy || action == OrderAction.BuyToCover)
                    return Math.Abs(qty);

                if (action == OrderAction.Sell || action == OrderAction.SellShort)
                    return -Math.Abs(qty);

                return 0;
            }
            
            private int GetNetPosition(Account acc, Instrument instr)
            {
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
        }
    }
}