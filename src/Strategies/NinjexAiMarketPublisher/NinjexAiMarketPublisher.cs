#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Web.Script.Serialization;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Publishes fully closed 5m/15m/60m bars to Ninjex's observe-only
    /// regime service. NO trading calls exist anywhere in this strategy.
    /// Only captures in State.Realtime, never during historical processing.
    /// Every captured observation is first written to a local replay spool.
    /// Configured NT chart timezone must be Eastern Standard Time (ET).
    /// </summary>
    public sealed class NinjexAiMarketPublisher : Strategy
    {
        private const int BarCount = 100;
        private const string SecretEnvironmentVariable = "NINJEX_AI_PUBLISHER_KEY";
        private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        private TimeZoneInfo exchangeTimeZone;
        private AiPublisherDiskSpool spool;
        private DateTime previousCapturedBar = DateTime.MinValue;
        private long barCount;

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
                BackendUrl = "https://localhost:7016";
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
            else if (State == State.Terminated)
            {
                if (spool != null) { spool.Dispose(); spool = null; }
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
            try
            {
                // Use a disk-first append operation. Upload speed cannot drop
                // observations. High Playback speeds may incur disk backpressure,
                // but the record is saved before OnBarUpdate returns.
                if (spool == null)
                    spool = new AiPublisherDiskSpool(BackendUrl, key, name.Trim(),
                        m => messages.Enqueue(m));
                var barEnd = ConvertEtToUtc(Times[0][0]);
                if (barEnd == previousCapturedBar) return;
                var instrument = name.Substring(0, 2).ToUpperInvariant();
                var payload = new Dictionary<string, object>
                {
                    { "instrument", instrument },
                    { "contract", name.Trim() },
                    { "frames", new[] { BuildFrame(0, 5), BuildFrame(1, 15), BuildFrame(2, 60) } }
                };
                var json = new JavaScriptSerializer { MaxJsonLength = 1_000_000 }.Serialize(payload);
                spool.Capture(json, barEnd);
                previousCapturedBar = barEnd;
                barCount++;
                if (barCount % 12 == 0)
                    messages.Enqueue("[NinjexAI] Captured this instance=" + spool.Captured +
                        " pending upload=" + spool.Pending +
                        " last UTC=" + barEnd.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                messages.Enqueue("[NinjexAI] CAPTURE FAILED (inspect before high-speed replay): " +
                    ex.GetType().Name + " " + ex.Message);
            }
        }

        private DateTime ConvertEtToUtc(DateTime time)
        {
            var local = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            if (exchangeTimeZone.IsAmbiguousTime(local) || exchangeTimeZone.IsInvalidTime(local))
                throw new InvalidOperationException("Ambiguous/invalid ET candle timestamp.");
            return TimeZoneInfo.ConvertTimeToUtc(local, exchangeTimeZone);
        }

        private Dictionary<string, object> BuildFrame(int series, int minutes)
        {
            var bars = new List<object>(BarCount);
            for (var back = BarCount - 1; back >= 0; back--)
            {
                var utc = ConvertEtToUtc(Times[series][back]);
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
