#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV3 : Strategy
    {
        private void TryReportNewClosedTrades()
        {
            try
            {
                if (SystemPerformance == null || SystemPerformance.AllTrades == null)
                    return;
        
                var count = SystemPerformance.AllTrades.Count;
                if (count <= _lastReportedTradeCount)
                    return;
        
                var acc = Account?.Name ?? string.Empty;
        
                for (var i = _lastReportedTradeCount; i < count; i++)
                {
                    var t = SystemPerformance.AllTrades[i];
                    if (t == null)
                        continue;
        
                    // Only send closed trades (Exit exists)
                    // Reporter also checks this, but we keep it tight here too.
                    var exitObj = t.GetType().GetProperty("Exit")?.GetValue(t, null);
                    if (exitObj == null)
                        continue;
        
                    _tradeApi.TryReportClosedTrade(this, t, acc);
                }
        
                _lastReportedTradeCount = count;
            }
            catch (Exception ex)
            {
                if (DebugMode)
                    Print("[API Reporter] TryReportNewClosedTrades error: " + ex.Message);
            }
        }

                
        private string BuildStrategyParamsJson()
        {
            try
            {
                var dict = new Dictionary<string, object>();
        
                var props = GetType().GetProperties();
        
                foreach (var p in props)
                {
                    if (!Attribute.IsDefined(p, typeof(NinjaScriptPropertyAttribute)))
                        continue;
        
                    object value;
        
                    try
                    {
                        value = p.GetValue(this, null);
                    }
                    catch
                    {
                        continue;
                    }
        
                    dict[p.Name] = value;
                }
        
                var serializer = new JavaScriptSerializer();
                return serializer.Serialize(dict);
            }
            catch (Exception ex)
            {
                if (DebugMode)
                    Print("[PARAM JSON ERROR] " + ex.Message);
        
                return "{}";
            }
        }
    }
}
