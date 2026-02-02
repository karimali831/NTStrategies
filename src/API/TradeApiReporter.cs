#region Using declarations
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Posts CLOSED trades (Trade objects) to an API endpoint.
    /// Designed to be called from a Strategy instance (e.g., ApexMicroPBLiveV3).
    /// </summary>
    public sealed class TradeApiReporter
    {
        private readonly string _baseUrl;
        private readonly int _timeoutMs;
        private readonly bool _debug;
		private readonly bool _isHistorical;
		
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public TradeApiReporter(string apiBaseUrl, int timeoutMs, bool debug, bool isHistorical)
        {
            _baseUrl = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
            _timeoutMs = Math.Max(1000, timeoutMs);
            _debug = debug;
			_isHistorical = isHistorical;
        }

        public void TryReportClosedTrade(Strategy strategy, Trade trade, string accountNumber)
        {
            if (strategy == null || trade == null) return;

            // Only report CLOSED trades
            // (Trade.Exit can be null until the trade is closed.)
            var exitObj = SafeGet(trade, "Exit");
            if (exitObj == null) return;

            var entryTime = GetDateTime(SafeGet(trade, "Entry"), "Time");
            var exitTime  = GetDateTime(exitObj, "Time");

            // Defensive: if ExitTime is missing, don't send
            if (exitTime == DateTime.MinValue) return;

            var dto = new TradeReportDto
            {
				IsHistorical = _isHistorical,
                TradeId       = GetTradeId(trade, entryTime, exitTime),
                MarketPos  = GetMarketPos(trade),
                Qty        = GetInt(trade, "Quantity", fallback: 0),
                EntryPrice = GetDouble(SafeGet(trade, "Entry"), "Price", fallback: 0),
                ExitPrice  = GetDouble(exitObj, "Price", fallback: 0),

                EntryTime = FormatNtTime(entryTime),
                ExitTime  = FormatNtTime(exitTime),

                EntryName = GetString(SafeGet(trade, "Entry"), "Name"),
                ExitName  = GetString(exitObj, "Name"),

                Profit     = GetDouble(trade, "ProfitCurrency", fallback: GetDouble(trade, "Profit", fallback: 0)),
                Commission = GetDouble(trade, "Commission", fallback: 0),

                MAE  = GetDouble(trade, "MAE", fallback: 0),
                MFE  = GetDouble(trade, "MFE", fallback: 0),
                ETD  = GetDouble(trade, "ETD", fallback: 0),
                Bars = GetInt(trade, "Bars", fallback: GetInt(trade, "BarsInTrade", fallback: 0)),

                CreatedUtc = DateTime.UtcNow.ToString("o"),
				ExternalAccountNumber = accountNumber ?? string.Empty,
				StrategyParamsJson = BuildParamsSnapshotJson(strategy),
				StrategyName = strategy.Name,
				Instrument = strategy.Instrument != null ? strategy.Instrument.FullName : string.Empty
            };

            var body = _json.Serialize(dto);
			
			if (_debug)
			{
			    strategy.Print("========== TRADE API PAYLOAD ==========");
			    strategy.Print(body);
			    strategy.Print("=======================================");
			}

            try
            {
                var url = _baseUrl.TrimEnd('/') + "/api/trading/nt/trades";

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = _timeoutMs;
                req.ReadWriteTimeout = _timeoutMs;

                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;

                using (var rs = req.GetRequestStream())
                    rs.Write(bytes, 0, bytes.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var respText = sr.ReadToEnd();

                    if (_debug)
                        strategy.Print($"[TRADE API] OK {(int)resp.StatusCode} {resp.StatusDescription} tradeId={dto.TradeId} resp={respText}");
                }
            }
            catch (Exception ex)
            {
	            if (_debug)
                    strategy.Print($"[TRADE API] FAIL tradeId={dto.TradeId} err={ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------- DTO ----------
        private sealed class TradeReportDto
        {
			public bool IsHistorical {get; set;}
            public string TradeId { get; set; }
            public string ExternalAccountNumber { get; set; }

            public string MarketPos { get; set; }
            public int Qty { get; set; }

            public double EntryPrice { get; set; }
            public double ExitPrice { get; set; }

            public string EntryTime { get; set; }
            public string ExitTime { get; set; }

            public string EntryName { get; set; }
            public string ExitName { get; set; }

            public double Profit { get; set; }
            public double Commission { get; set; }

            public double MAE { get; set; }
            public double MFE { get; set; }
            public double ETD { get; set; }

            public int Bars { get; set; }

            public string CreatedUtc { get; set; }
			
			public string StrategyName { get; set; }
			public string Instrument { get; set; }
			public string StrategyParamsJson { get; set; }
        }

        // ---------- Helpers (reflection-based so it compiles across NT8 property differences) ----------
        private static object SafeGet(object obj, string prop)
        {
            if (obj == null) return null;
            var p = obj.GetType().GetProperty(prop);
            if (p == null) return null;
            try { return p.GetValue(obj, null); } catch { return null; }
        }

        private static string GetString(object obj, string prop)
        {
            var v = SafeGet(obj, prop);
            return v == null ? string.Empty : (v.ToString() ?? string.Empty);
        }

        private static int GetInt(object obj, string prop, int fallback)
        {
            var v = SafeGet(obj, prop);
            if (v == null) return fallback;
            try
            {
                if (v is int i) return i;
                return Convert.ToInt32(v);
            }
            catch { return fallback; }
        }

        private static double GetDouble(object obj, string prop, double fallback)
        {
            var v = SafeGet(obj, prop);
            if (v == null) return fallback;
            try
            {
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is decimal m) return (double)m;
                return Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        private static DateTime GetDateTime(object obj, string prop)
        {
            var v = SafeGet(obj, prop);
            if (v == null) return DateTime.MinValue;
            try
            {
                if (v is DateTime dt) return dt;
                return Convert.ToDateTime(v);
            }
            catch { return DateTime.MinValue; }
        }

        private static string GetMarketPos(Trade trade)
        {
            // Try common fields: MarketPosition / Entry.MarketPosition / Direction
            var mp = SafeGet(trade, "MarketPosition");
            if (mp != null) return mp.ToString();

            var entry = SafeGet(trade, "Entry");
            var emp = SafeGet(entry, "MarketPosition");
            if (emp != null) return emp.ToString();

            var dir = SafeGet(trade, "Direction");
            if (dir != null) return dir.ToString();

            return string.Empty;
        }

        private static string GetTradeId(Trade trade, DateTime entryTime, DateTime exitTime)
        {
            // Prefer actual TradeId/TradeNumber if available, else deterministic fallback
            var tid = SafeGet(trade, "TradeId");
            if (tid != null && !string.IsNullOrWhiteSpace(tid.ToString()))
                return tid.ToString();

            var tn = SafeGet(trade, "TradeNumber");
            if (tn != null && !string.IsNullOrWhiteSpace(tn.ToString()))
                return tn.ToString();

            // fallback: entry/exit ticks (stable enough per strategy instance)
            return "T_" + entryTime.Ticks.ToString() + "_" + exitTime.Ticks.ToString();
        }

        private static string FormatNtTime(DateTime t)
        {
            // NT times are often Kind=Unspecified; keep as ISO-like without offset to avoid wrong TZ conversion.
            // Your API can interpret these as "chart/session time" (often ET for CME instruments).
            if (t == DateTime.MinValue) return null;
            return t.ToString("yyyy-MM-ddTHH:mm:ss.fff");
        }
		
		private string BuildParamsSnapshotJson(Strategy strategy)
		{
		    if (strategy == null)
		        return "{}";
		
		    try
		    {
		        var dict = new Dictionary<string, object>();
		
		        var props = strategy.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
		
		        foreach (var p in props)
		        {
		            // Only NinjaScriptProperty props
		            if (!Attribute.IsDefined(p, typeof(NinjaScriptPropertyAttribute)))
		                continue;
		
		            // Skip indexers / write-only
		            if (p.GetIndexParameters().Length != 0 || !p.CanRead)
		                continue;
		
		            object val = null;
		            try { val = p.GetValue(strategy, null); }
		            catch { continue; }
		
		            dict[p.Name] = SanitizeForJson(val);
		        }
		
		        // JavaScriptSerializer can choke on large graphs; keep it primitive.
		        return _json.Serialize(dict);
		    }
		    catch (Exception ex)
		    {
		        if (_debug)
		            strategy.Print("[PARAM SNAPSHOT] Failed: " + ex.Message);
		
		        return "{}";
		    }
		}
		
		private object SanitizeForJson(object val)
		{
		    if (val == null)
		        return null;
		
		    // Unwrap nullable
		    var t = val.GetType();
		
		    // Enums as names (e.g., ConsistencyRuleMode.ThirtyPercent -> "ThirtyPercent")
		    if (t.IsEnum)
		        return val.ToString();
		
		    // DateTimes as ISO (no offset)
		    if (val is DateTime dt)
		        return dt.ToString("yyyy-MM-ddTHH:mm:ss.fff");
		
		    // Numeric safety: no NaN/Infinity
		    if (val is double d)
		        return (double.IsNaN(d) || double.IsInfinity(d)) ? 0.0 : d;
		
		    if (val is float f)
		        return (float.IsNaN(f) || float.IsInfinity(f)) ? 0.0f : f;
		
		    if (val is decimal)
		        return val; // fine
		
		    if (val is int || val is long || val is short || val is byte ||
		        val is uint || val is ulong || val is ushort ||
		        val is bool || val is string)
		        return val;
		
		    // TimeSpan not used in your strategies (you avoid it), but just in case:
		    if (val is TimeSpan ts)
		        return ts.ToString();
		
		    // Collections: keep shallow, sanitize elements
		    if (val is IEnumerable enumerable)
		    {
		        var list = new List<object>();
		        foreach (var item in enumerable)
		            list.Add(SanitizeForJson(item));
		        return list;
		    }
		
		    // Fallback: string representation
		    return val.ToString();
		}
    }
}
