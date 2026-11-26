#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Opt-in, offline historical research extractor. Never places orders,
    /// sends HTTP or modifies the existing AI Market Publisher.
    /// Add to a dated ES/NQ 5-minute chart with CME ETH and ET timezone.
    /// </summary>
    public class NinjexAiHistoricalResearchExporter : Strategy
    {
        private AiHistoricalCsvSink sink;
        private bool invalidSetup;
        private long errors;
        private DateTime effectiveThroughDate;

        [NinjaScriptProperty]
        [Display(Name = "Enable export", Order = 1, GroupName = "Ninjex AI Research")]
        public bool EnableExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "From ET (yyyy-MM-dd)", Order = 2, GroupName = "Ninjex AI Research")]
        public string FromEasternDate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Through ET (yyyy-MM-dd)", Order = 3, GroupName = "Ninjex AI Research")]
        public string ThroughEasternDate { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dataset tag", Order = 4, GroupName = "Ninjex AI Research")]
        public string DatasetTag { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Recover source 17:00 close", Order = 5, GroupName = "Ninjex AI Research")]
        public bool RecoverTerminalSessionClose { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto quarterly contract windows (basket)", Order = 6, GroupName = "Ninjex AI Research")]
        public bool AutoQuarterlyContractWindows { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex AI Historical Research Exporter";
                Description = "Offline OHLCV extraction only, no orders, no HTTP";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade = 0;
                IsInstantiatedOnEachOptimizationIteration = true;
                EnableExport = false;
                FromEasternDate = "2023-09-01";
                ThroughEasternDate = "2026-11-25";
                DatasetTag = "research";
                // Disabled by default until real NT8 runtime validation.
                RecoverTerminalSessionClose = false;
                AutoQuarterlyContractWindows = false;
            }
            else if (State == State.Configure)
            {
                // BIP0=5-minute primary chart, BIP1=1m, BIP2=15m, BIP3=60m.
                AddDataSeries(BarsPeriodType.Minute, 1);
                AddDataSeries(BarsPeriodType.Minute, 15);
                AddDataSeries(BarsPeriodType.Minute, 60);
            }
            else if (State == State.DataLoaded && EnableExport)
            {
                try
                {
                    if (BarsPeriod == null || BarsPeriod.BarsPeriodType != BarsPeriodType.Minute ||
                        BarsPeriod.Value != 5)
                        throw new InvalidOperationException("Research exporter requires a 5-minute chart.");

                    var contract = Instrument == null ? "" : Instrument.FullName;
                    if (!contract.StartsWith("ES ", StringComparison.OrdinalIgnoreCase) &&
                        !contract.StartsWith("NQ ", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Attach to an ES/NQ dated futures contract.");

                    DateTime first, last;
                    if (!DateTime.TryParseExact(FromEasternDate, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out first) ||
                        !DateTime.TryParseExact(ThroughEasternDate, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out last) || last < first)
                        throw new InvalidOperationException("Dates must be valid inclusive yyyy-MM-dd ET dates.");

                    if (AutoQuarterlyContractWindows)
                    {
                        // This is one independent Strategy instance per dated
                        // instrument in Strategy Analyzer Basket Testing. Its
                        // research filter is automatically intersected with
                        // CME's conventional roll windows, so no individual
                        // expiry/date configuration is needed by the user.
                        var window = AiQuarterlyResearchWindow.Resolve(contract, first, last);
                        first = window.FirstEasternDate;
                        last = window.LastEasternDate;
                        Print("[NinjexAI-EXPORT] BASKET contract=" + window.Contract +
                            " firstET=" + first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                            " throughET=" + last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                            " customaryNextRollMonday=" +
                            window.CurrentQuarterRollMonday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }

                    effectiveThroughDate = last;
                    var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    var folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "Ninjex", "AiMarket", "Research");

                    sink = new AiHistoricalCsvSink(folder, contract, DatasetTag, zone, first, last);
                    Print("[NinjexAI-EXPORT] Output: " + sink.Folder +
                        " | historical-only | 1m/5m/15m/60m | zero orders");
                }
                catch (Exception e)
                {
                    invalidSetup = true;
                    Print("[NinjexAI-EXPORT] STARTUP ERROR: " + e.Message);
                }
            }
            else if (State == State.Terminated && sink != null)
            {
                try
                {
                    if (EnableExport && RecoverTerminalSessionClose && !invalidSetup)
                        RecoverSourceTerminalCloses();
                    sink.Dispose();
                    Print("[NinjexAI-EXPORT] Finished: " + sink.Folder +
                        " valid=" + sink.Valid +
                        " qualityIssues=" + sink.Issues +
                        " writeErrors=" + errors);
                }
                catch (Exception e)
                {
                    Print("[NinjexAI-EXPORT] FINALIZATION FAILED: " + e.Message);
                }
                finally { sink = null; }
            }
        }

        /// <summary>
        /// A final 17:00 ET bar can exist in NinjaTrader's loaded historical
        /// Bars series even when no later tick triggers its OnBarClose callback.
        /// Recover only a bar actually present in the BarsArray on the
        /// requested final ET date. Never synthesize, extrapolate or mutate NT.
        /// Access might be unavailable at State.Terminated on some hosts:
        /// this path deliberately fails safely and logs diagnostics.
        /// </summary>
        private void RecoverSourceTerminalCloses()
        {
            DateTime through = effectiveThroughDate;
            if (through == default(DateTime) &&
                !DateTime.TryParseExact(ThroughEasternDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out through))
                return;

            var minutesByBip = new[] { 5, 1, 15, 60 };
            for (int bip = 0; bip < minutesByBip.Length; bip++)
            {
                var minutes = minutesByBip[bip];
                try
                {
                    if (BarsArray == null || BarsArray.Length <= bip ||
                        BarsArray[bip] == null || BarsArray[bip].Count == 0)
                    {
                        Print("[NinjexAI-EXPORT] Terminal " + minutes +
                            "m: BarsArray unavailable; no bar reconstructed.");
                        continue;
                    }

                    var bars = BarsArray[bip];
                    var index = bars.Count - 1;
                    // A Strategy Analyzer end date after our Through ET filter
                    // may load next-session bars: walk backward to our last date.
                    while (index >= 0 && bars.GetTime(index).Date > through.Date)
                        index--;
                    if (index < 0)
                    {
                        Print("[NinjexAI-EXPORT] Terminal " + minutes +
                            "m: no bars on/before the requested through date.");
                        continue;
                    }

                    DateTime actualClose = bars.GetTime(index);
                    if (actualClose.Date != through.Date ||
                        actualClose.TimeOfDay != TimeSpan.FromHours(17))
                    {
                        Print("[NinjexAI-EXPORT] Terminal " + minutes +
                            "m: loaded last=" + actualClose.ToString("yyyy-MM-dd HH:mm:ss") +
                            " ET, not requested 17:00 ET; skipping recovery.");
                        continue;
                    }

                    var added = sink.TryAppendTerminalClose(minutes, actualClose,
                        bars.GetOpen(index), bars.GetHigh(index),
                        bars.GetLow(index), bars.GetClose(index),
                        bars.GetVolume(index));
                    Print("[NinjexAI-EXPORT] Terminal " + minutes +
                        "m: source last=" + actualClose.ToString("yyyy-MM-dd HH:mm:ss") +
                        " ET, volume=" + bars.GetVolume(index) +
                        (added ? " RECOVERED_FROM_LOADED_BARS" :
                                 " already present or rejected; no changes"));
                }
                catch (Exception e)
                {
                    errors++;
                    Print("[NinjexAI-EXPORT] Terminal " + minutes +
                        "m: unavailable (" + e.Message +
                        "); no fabricated bars. Existing CSV preserved.");
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (!EnableExport || invalidSetup || sink == null ||
                State != State.Historical) return;
            var bip = BarsInProgress;
            if (bip < 0 || bip > 3 || CurrentBars == null ||
                CurrentBars.Length < 4 || CurrentBars[bip] < 0) return;

            var minutes = bip == 0 ? 5 : bip == 1 ? 1 : bip == 2 ? 15 : 60;
            try
            {
                sink.Append(minutes, Times[bip][0],
                    Opens[bip][0], Highs[bip][0], Lows[bip][0],
                    Closes[bip][0], Convert.ToInt64(Volumes[bip][0]));
            }
            catch (Exception e)
            {
                errors++;
                if (errors <= 5 || errors % 100 == 0)
                    Print("[NinjexAI-EXPORT] WRITE ERROR #" + errors + ": " + e.Message);
            }
        }
    }
}
