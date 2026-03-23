using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            internal bool HasWorkingBracketOrders(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;
                
                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                        continue;

                    var isWorking =
                        o.OrderState == OrderState.Working ||
                        o.OrderState == OrderState.Accepted ||
                        o.OrderState == OrderState.Submitted ||
                        o.OrderState == OrderState.PartFilled;

                    if (!isWorking)
                        continue;

                    var name = (o.Name ?? "").Trim();

                    if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Stop1", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Target1", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            
            private List<Order> GetWorkingOrdersForInstrument(Account acc, Instrument instr)
            {
                var result = new List<Order>();

                if (acc == null || instr == null)
                    return result;

                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!string.Equals(o.Instrument.FullName, instr.FullName, StringComparison.Ordinal))
                            continue;

                        if (o.OrderState == OrderState.Working ||
                            o.OrderState == OrderState.Accepted ||
                            o.OrderState == OrderState.Submitted ||
                            o.OrderState == OrderState.PartFilled)
                        {
                            result.Add(o);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(
                        $"GetWorkingOrdersForInstrument failed -> acc={acc?.Name}, instr={instr?.FullName}, msg={ex.Message}");
                }

                return result;
            }
            
            private static List<Instrument> CollectActiveInstruments(Account acc)
            {
                var result = new Dictionary<string, Instrument>(StringComparer.Ordinal);

                if (acc == null)
                    return new List<Instrument>();

                foreach (var pos in acc.Positions)
                {
                    if (pos?.Instrument == null)
                        continue;

                    var qty = Math.Abs(pos.Quantity);
                    if (qty <= 0)
                        continue;

                    var key = pos.Instrument.FullName ?? "";
                    if (!result.ContainsKey(key))
                        result[key] = pos.Instrument;
                }
       
                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    var isWorking =
                        o.OrderState == OrderState.Working ||
                        o.OrderState == OrderState.Accepted ||
                        o.OrderState == OrderState.Submitted ||
                        o.OrderState == OrderState.PartFilled;

                    if (!isWorking)
                        continue;

                    var key = o.Instrument.FullName ?? "";
                    if (!result.ContainsKey(key))
                        result[key] = o.Instrument;
                }
                
                return result.Values.ToList();
            }
        }
    }
}