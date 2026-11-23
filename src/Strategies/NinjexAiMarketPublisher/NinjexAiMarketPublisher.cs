#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Publishes fully closed 5m/15m/60m bars to Ninjex's observe-only
    /// regime service. NO trading calls exist anywhere in this strategy.
    /// Only sends in State.Realtime, never during historical processing.
    /// Configured NT chart timezone must be Eastern Standard Time (ET).
    /// </summary>
    public sealed class NinjexAiMarketPublisher : Strategy
    {
        private const int BarCount = 100;
        private const string SecretEnvironmentVariable = "NINJEX_AI_PUBLISHER_KEY";
        private static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        private TimeZoneInfo exchangeTimeZone;
        private int sending;

        [NinjaScriptProperty]
        [Display(Name = "Enable publishing", Order = 1, GroupName = "Ninjex AI")]
        public bool EnablePublishing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Backend URL", Order = 2, GroupName = "Ninjex AI")]
        public string BackendUrl { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex AI Market Publisher (observe only)";
                Description = "Sends completed OHLCV candles to Ninjex AI; never places orders.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade = BarCount;
                IsInstantiatedOnEachOptimizationIteration = false;
                EnablePublishing = false;
                BackendUrl = "https://api.ninjex.co";
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 15);
                AddDataSeries(BarsPeriodType.Minute, 60);
            }
            else if (State == State.DataLoaded)
            {
                // NT Tools > Options > General > Time zone MUST be set to ET.
                exchangeTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            string msg;
            while (messages.TryDequeue(out msg)) Print(msg);
            if (State != State.Realtime || !EnablePublishing) return;
            if (BarsPeriod == null || BarsPeriod.BarsPeriodType != BarsPeriodType.Minute ||
                BarsPeriod.Value != 5 || CurrentBars.Length < 3 ||
                CurrentBars[0] < BarCount - 1 || CurrentBars[1] < BarCount - 1 ||
                CurrentBars[2] < BarCount - 1) return;
            var name = Instrument != null ? Instrument.FullName : "";
            if (!name.StartsWith("ES ", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("NQ ", StringComparison.OrdinalIgnoreCase)) return;
            var key = Environment.GetEnvironmentVariable(SecretEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            {
                messages.Enqueue("[NinjexAI] Missing 32+ character NINJEX_AI_PUBLISHER_KEY environment variable.");
                return;
            }
            if (string.IsNullOrWhiteSpace(BackendUrl) ||
                !BackendUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                messages.Enqueue("[NinjexAI] Backend URL must be HTTPS.");
                return;
            }
            if (Interlocked.CompareExchange(ref sending, 1, 0) != 0) return;
            string json;
            try
            {
                var instrument = name.Substring(0, 2).ToUpperInvariant();
                var payload = new Dictionary<string, object>
                {
                    { "instrument", instrument },
                    { "contract", name.Trim() },
                    { "frames", new[] { BuildFrame(0, 5), BuildFrame(1, 15), BuildFrame(2, 60) } }
                };
                json = new JavaScriptSerializer { MaxJsonLength = 1_000_000 }.Serialize(payload);
            }
            catch (Exception ex)
            {
                messages.Enqueue("[NinjexAI] Invalid source bars: " + ex.Message);
                Interlocked.Exchange(ref sending, 0);
                return;
            }

            // Pure serialized snapshot crosses the thread boundary; no NinjaTrader
            // series or account objects are accessed by the HTTP worker.
            var url = BackendUrl.TrimEnd('/') + "/api/trading/ai-market/observe";
            Task.Run(async () =>
            {
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        req.Headers.TryAddWithoutValidation("X-Ninjex-AI-Key", key);
                        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        using (var response = await Client.SendAsync(req).ConfigureAwait(false))
                        {
                            messages.Enqueue("[NinjexAI] HTTP " + (int)response.StatusCode +
                                (response.IsSuccessStatusCode ? " observation submitted" : " publish rejected"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    messages.Enqueue("[NinjexAI] Transport failed: " + ex.GetType().Name);
                }
                finally { Interlocked.Exchange(ref sending, 0); }
            });
        }

        private Dictionary<string, object> BuildFrame(int series, int minutes)
        {
            var bars = new List<object>(BarCount);
            for (var back = BarCount - 1; back >= 0; back--)
            {
                DateTime local = DateTime.SpecifyKind(Times[series][back], DateTimeKind.Unspecified);
                if (exchangeTimeZone.IsAmbiguousTime(local) || exchangeTimeZone.IsInvalidTime(local))
                    throw new InvalidOperationException("Ambiguous/invalid ET candle time; skipping observation.");
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, exchangeTimeZone);
                bars.Add(new Dictionary<string, object>
                {
                    { "endUtc", utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) },
                    { "open", Opens[series][back] },
                    { "high", Highs[series][back] },
                    { "low", Lows[series][back] },
                    { "close", Closes[series][back] },
                    { "volume", Convert.ToInt64(Volumes[series][back]) }
                });
            }
            return new Dictionary<string, object> { { "minutes", minutes }, { "bars", bars } };
        }
    }
}
