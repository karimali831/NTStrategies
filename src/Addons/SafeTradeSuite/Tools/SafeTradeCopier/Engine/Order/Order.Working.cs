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

                try
                {
                    var specFound = TryGetActiveBracketSpec(acc, instr, out var spec) && spec != null;
                    var stop = specFound ? FindWorkingManagedStop(acc, instr, spec) : FindAnyWorkingManagedStop(acc, instr);
                    var target = specFound ? FindWorkingManagedTarget(acc, instr, spec) : FindAnyWorkingManagedTarget(acc, instr);

                    var hasStop = stop != null;
                    var hasTarget = target != null;

                    // Keep permissive during exchange/order transition windows.
                    var result = hasStop || hasTarget;
                    return result;
                }
                catch (Exception ex)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[HAS WORKING ERROR] acc={acc?.Name} instr={instr?.FullName} msg={ex.Message}");
                    return false;
                }
            }

            private static bool IsWorking(Order o)
            {
                return
                    o.OrderState == OrderState.Working ||
                    o.OrderState == OrderState.Accepted ||
                    o.OrderState == OrderState.Submitted ||
                    o.OrderState == OrderState.PartFilled ||
                    o.OrderState == OrderState.ChangePending ||
                    o.OrderState == OrderState.ChangeSubmitted;
            }
            
            private bool HasWorkingEntryOrders(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;

                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!IsSameInstrument(o.Instrument, instr))
                        continue;

                    var isWorkingEntry = IsWorking(o) || o.OrderState == OrderState.Initialized;

                    if (!isWorkingEntry)
                        continue;

                    var name = (o.Name ?? "").Trim();
                    if (name.StartsWith("STC:ENTRY:", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
            
            private bool HasWorkingManagedExitOrder(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return false;

                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!IsSameInstrument(o.Instrument, instr))
                        continue;

                    if (!IsWorking(o))
                        continue;

                    var name = (o.Name ?? "").Trim();

                    if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("STC:FLATTEN", StringComparison.OrdinalIgnoreCase))
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

                        if (!IsSameInstrument(o.Instrument, instr))
                            continue;

                        if (IsWorking(o))
                            result.Add(o);
                    }
                }
                catch (Exception ex)
                {
                    Log(
                        $"GetWorkingOrdersForInstrument failed -> acc={acc?.Name}, instr={instr?.FullName}, msg={ex.Message}");
                }

                return result;
            }
            
            private List<Instrument> CollectActiveInstruments(Account acc)
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

                    if (!IsWorking(o))
                        continue;

                    var key = o.Instrument.FullName ?? "";
                    if (!result.ContainsKey(key))
                        result[key] = o.Instrument;
                }
                
                return result.Values.ToList();
            }
            
            private Order FindWorkingManagedStop(Account acc, Instrument instr, ActiveBracketSpec spec)
            {
                if (acc == null || instr == null || spec == null)
                    return null;
                
                var requireOco = !string.IsNullOrWhiteSpace(spec.StopOco);

                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!IsSameInstrument(o.Instrument, instr))
                        continue;

                    if (!IsWorking(o))
                        continue;

                    var name = (o.Name ?? "").Trim();
                    if (!name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var oco = (o.Oco ?? "").Trim();

                    if (requireOco)
                    {
                        if (string.Equals(oco, spec.StopOco, StringComparison.Ordinal))
                            return o;

                        continue;
                    }

                    return o;
                }
                
                return null;
            }
            
            private Order FindAnyWorkingManagedStop(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return null;

                try
                {
                    foreach (var o in acc.Orders)
                    {
                        if (o?.Instrument == null)
                            continue;

                        if (!IsSameInstrument(o.Instrument, instr))
                            continue;
                        
                        if (!IsWorking(o))
                            continue;

                        var name = (o.Name ?? "").Trim();
                        if (name.StartsWith("STC:SL", StringComparison.OrdinalIgnoreCase))
                            return o;
                    }
                }
                catch
                {
                }

                return null;
            }

            private Order FindAnyWorkingManagedTarget(Account acc, Instrument instr)
            {
                if (acc == null || instr == null)
                    return null;
                
                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!IsSameInstrument(o.Instrument, instr))
                        continue;

                    if (!IsWorking(o))
                        continue;

                    var name = (o.Name ?? "").Trim();
                    if (name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase))
                        return o;
                }

                return null;
            }
            
            private Order FindWorkingManagedTarget(Account acc, Instrument instr, ActiveBracketSpec spec)
            {
                if (acc == null || instr == null || spec == null)
                    return null;
                
                var requireOco = !string.IsNullOrWhiteSpace(spec.StopOco);

                foreach (var o in acc.Orders)
                {
                    if (o?.Instrument == null)
                        continue;

                    if (!IsSameInstrument(o.Instrument, instr))
                        continue;

                    if (!IsWorking(o))
                        continue;

                    var name = (o.Name ?? "").Trim();
                    if (!name.StartsWith("STC:TP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var oco = (o.Oco ?? "").Trim();

                    if (requireOco)
                    {
                        if (string.Equals(oco, spec.StopOco, StringComparison.Ordinal))
                            return o;

                        continue;
                    }

                    return o;
                }
                
                return null;
            }
        }
    }
}