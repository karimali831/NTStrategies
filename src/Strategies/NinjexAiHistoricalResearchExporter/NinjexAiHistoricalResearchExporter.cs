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
