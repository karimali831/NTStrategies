#region Using declarations
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// TrailingDdApiReporter
    /// - Encapsulates config fetch + point posting for intraday trailing DD simulation.
    /// - Designed to be called by a Strategy (heartbeat-style).
    /// - Minimizes endpoint calls: will only work when allowSend == true AND throttled by interval.
    /// </summary>
    internal sealed class TrailingDdApiReporter
    {
        private readonly JavaScriptSerializer _js = new JavaScriptSerializer();

        private readonly string _apiBaseUrl;
		private readonly int _timeoutMs;
        private readonly int _planContextId;
        private readonly int _sendIntervalSeconds;
        private readonly bool _debug;

        private DateTime _lastSendUtc = DateTime.MinValue;

        private bool _hasConfig = false;
        private decimal _ddLimit = 0m;
        private decimal _bufferExtra = 0m;
        private string _currency = "USD";
        private int _propFirmAccountId = 0; // may be 0 in plan-only mode

        private decimal _peakEquity = 0m;
        private bool _hasPeak = false;
		private readonly decimal _overrideDdLimit;
		private readonly Guid _tddRunId = Guid.NewGuid();
		private MarketPosition _lastMp = MarketPosition.Flat;

       public TrailingDdApiReporter(
		    string apiBaseUrl,
			int timeoutMs,
		    int planContextId,
		    int sendIntervalSeconds,
		    bool enableDebug,
		    double overrideTrailingDrawdown)
		{
		    _apiBaseUrl = (apiBaseUrl ?? "").Trim();
			_timeoutMs = Math.Max(1000, timeoutMs);
		    _planContextId = planContextId;
		    _sendIntervalSeconds = Math.Max(1, sendIntervalSeconds);
		    _debug = enableDebug;
		
		    _overrideDdLimit = overrideTrailingDrawdown > 0
		        ? (decimal)overrideTrailingDrawdown
		        : 0m;
		}


        /// <summary>Reset peak tracking (intraday peak).</summary>
        public void ResetPeak()
        {
            _peakEquity = 0m;
            _hasPeak = false;
        }

        /// <summary>
        /// Call this frequently (e.g. OnBarUpdate). It will throttle and only do work if allowSend==true.
        /// </summary>
        public void OnHeartbeat(Strategy s, bool allowSend)
        {
            if (!allowSend) return;
            if (s?.Account == null) return;
			
			var mp = s.Position.MarketPosition;

			// send during open positions, plus one final snapshot when the trade closes
			var shouldSendNow = mp != MarketPosition.Flat || _lastMp != MarketPosition.Flat;
			
			_lastMp = mp;
			if (!shouldSendNow)
			    return;

            // Market Replay is State.Realtime, so this still works there.
            if (s.State != State.Realtime)
                return;

            if (string.IsNullOrWhiteSpace(_apiBaseUrl))
            {
                Log(s, "[TDD] ApiBaseUrl empty; skipping.");
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastSendUtc).TotalSeconds < _sendIntervalSeconds)
                return;

            // config first
            if (!_hasConfig)
            {
                TryLoadConfig(s);
                _lastSendUtc = nowUtc;
                return;
            }

            // equity
            var eqD = s.Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
            if (double.IsNaN(eqD) || double.IsInfinity(eqD))
            {
                Log(s, $"[TDD] NetLiq invalid: {eqD}");
                _lastSendUtc = nowUtc;
                return;
            }

            var equity = (decimal)eqD;

            // peak
            if (!_hasPeak)
            {
                _peakEquity = equity;
                _hasPeak = true;
            }
            else if (equity > _peakEquity)
            {
                _peakEquity = equity;
            }
			
			// Safety: enforce invariant for payload
			var payloadPeak = _peakEquity;
			if (payloadPeak < equity) payloadPeak = equity;

            var ddAllowance = _ddLimit + _bufferExtra;
            var ddFromPeak = equity - _peakEquity; // <= 0
            var trailingThreshold = _peakEquity - ddAllowance;

            var remainingBuffer = ddAllowance - (_peakEquity - equity);
            if (remainingBuffer < 0m) remainingBuffer = 0m;
			
			// remainingBuffer = ddAllowance - (_peakEquity - equity)
			if (remainingBuffer < 0m) remainingBuffer = 0m;
			
			// IMPORTANT: remaining can never exceed the drawdown allowance.
			// For your intraday model bufferExtra should be 0, but clamp anyway.
			if (remainingBuffer > ddAllowance) remainingBuffer = ddAllowance;
			
			// And if you're treating "BufferExtra" as 0 for intraday,
			// you can clamp to _ddLimit specifically:
			if (remainingBuffer > _ddLimit) remainingBuffer = _ddLimit;

            TryPostPoint(s, nowUtc, equity, _peakEquity, ddFromPeak, trailingThreshold, remainingBuffer);

            _lastSendUtc = nowUtc;
        }

        private void TryLoadConfig(Strategy s)
        {
            var externalAccountNumber = (s.Account?.Name ?? "").Trim();
            if (string.IsNullOrEmpty(externalAccountNumber))
            {
                Log(s, "[TDD][CFG] Account.Name empty; cannot load config.");
                return;
            }

            var url = _apiBaseUrl.TrimEnd('/')
                      + "/api/trading/nt/trailing-dd/config?externalAccountNumber="
                      + Uri.EscapeDataString(externalAccountNumber);

            // planContextId enables Playback/Sim simulation
            if (_planContextId > 0)
                url += "&planContextId=" + _planContextId;

            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 5000;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var json = sr.ReadToEnd();
                    var cfg = _js.Deserialize<ConfigResp>(json);

                    _propFirmAccountId = cfg.propFirmAccountId;
                    _ddLimit = (decimal)cfg.trailingDrawdownLimit;
                    _bufferExtra = (decimal)cfg.bufferExtra;
                    _currency = string.IsNullOrWhiteSpace(cfg.currency) ? "USD" : cfg.currency;
					
					if (_overrideDdLimit > 0m)
					{
					    Log(s, $"[TDD][OVERRIDE] Using override DD={_overrideDdLimit:0.00} (server was {_ddLimit:0.00})");
					    _ddLimit = _overrideDdLimit;
					}


                    // Accept config when ddLimit is present (plan-only mode is valid)
                    _hasConfig = _ddLimit > 0m;

                    Log(s, $"[TDD][CFG] OK acct='{externalAccountNumber}' planContextId={_planContextId} ddLimit={_ddLimit:0.00} bufferExtra={_bufferExtra:0.00} propFirmAccountId={_propFirmAccountId} hasConfig={_hasConfig}");
                }
            }
            catch (WebException wex)
            {
                Log(s, $"[TDD][CFG] FAIL url={url} ex={wex.Message} {ReadWebExceptionBody(wex)}");
                _hasConfig = false;
            }
            catch (Exception ex)
            {
                Log(s, $"[TDD][CFG] FAIL url={url} ex={ex.Message}");
                _hasConfig = false;
            }
        }

        private void TryPostPoint(
            Strategy s,
            DateTime observedAtUtc,
            decimal currentEquity,
            decimal peakEquity,
            decimal ddFromPeak,
            decimal trailingThreshold,
            decimal remainingBuffer)
        {
            var externalAccountNumber = (s.Account?.Name ?? "").Trim();
            if (string.IsNullOrEmpty(externalAccountNumber))
                return;

            var url = _apiBaseUrl.TrimEnd('/') + "/api/trading/trailing-dd/points";

            try
            {
                var payload = new PointReq
                {
					runId = _tddRunId,
                    externalAccountNumber = externalAccountNumber,
                    planContextId = (_planContextId > 0 ? (int?)_planContextId : null),
                    observedAtUtc = observedAtUtc.ToString("o"),
                    instrument = (s.Instrument != null ? s.Instrument.FullName : null),
                    currency = _currency,

                    currentEquity = (double)currentEquity,
                    peakEquity = (double)peakEquity,
                    drawdownFromPeak = (double)ddFromPeak,

                    trailingDrawdownLimit = (double)_ddLimit,
                    bufferExtra = (double)_bufferExtra,
                    trailingThreshold = (double)trailingThreshold,
                    remainingBuffer = (double)remainingBuffer
                };

                var json = _js.Serialize(payload);
                var bytes = Encoding.UTF8.GetBytes(json);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
             	req.Timeout = _timeoutMs;
                req.ReadWriteTimeout = _timeoutMs;
                req.ContentLength = bytes.Length;

                using (var stream = req.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    Log(s, $"[TDD][POST] OK HTTP {(int)resp.StatusCode} acct='{externalAccountNumber}'");
                }
            }
            catch (WebException wex)
            {
                Log(s, $"[TDD][POST] FAIL url={url} ex={wex.Message} {ReadWebExceptionBody(wex)}");
            }
            catch (Exception ex)
            {
                Log(s, $"[TDD][POST] FAIL url={url} ex={ex.Message}");
            }
        }

        private void Log(Strategy s, string msg)
        {
            if (!_debug) return;
            s.Print(msg);
        }

        private static string ReadWebExceptionBody(WebException wex)
        {
            try
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp == null) return "";

                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var body = sr.ReadToEnd();
                    if (body.Length > 240) body = body.Substring(0, 240) + "...";
                    return $"http={(int)resp.StatusCode} {resp.StatusCode} body='{body}'";
                }
            }
            catch { return ""; }
        }

        private sealed class ConfigResp
        {
            public int propFirmAccountId { get; set; }
            public double trailingDrawdownLimit { get; set; }
            public double bufferExtra { get; set; }
            public string currency { get; set; }
        }

        private sealed class PointReq
        {
			public Guid runId {get; set;}
            public string externalAccountNumber { get; set; }
            public int? planContextId { get; set; }
            public string observedAtUtc { get; set; }

            public string instrument { get; set; }
            public string currency { get; set; }

            public double currentEquity { get; set; }
            public double peakEquity { get; set; }
            public double drawdownFromPeak { get; set; }

            public double trailingDrawdownLimit { get; set; }
            public double bufferExtra { get; set; }

            public double trailingThreshold { get; set; }
            public double remainingBuffer { get; set; }
        }
    }
}
