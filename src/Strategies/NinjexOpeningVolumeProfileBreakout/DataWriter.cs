using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private void EnsureCsvHeader()
        {
            if (string.IsNullOrWhiteSpace(dataFilePath))
                return;

            var expectedHeader = BuildDataHeader();

            if (!File.Exists(dataFilePath))
            {
                File.AppendAllText(dataFilePath, expectedHeader + Environment.NewLine);
                return;
            }

            var existingHeader = File.ReadLines(dataFilePath).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(existingHeader))
            {
                File.AppendAllText(dataFilePath, expectedHeader + Environment.NewLine);
                return;
            }

            if (!existingHeader.StartsWith("run_id,", StringComparison.OrdinalIgnoreCase))
            {
                var backupPath = dataFilePath + ".old_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                File.Move(dataFilePath, backupPath);
                File.AppendAllText(dataFilePath, expectedHeader + Environment.NewLine);
            }
        }
        
         private void ConfigureDataCollection()
        {
            if (!EnableDataCollection)
                return;

            try
            {
                runId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);

                var directory = Path.Combine(Core.Globals.UserDataDir, DataDirectoryName);
                Directory.CreateDirectory(directory);

                var instrumentName = Instrument != null
                    ? SanitizeFileName(Instrument.FullName)
                    : "instrument";

                var fileName = DataFilePrefix + "_" + instrumentName + "_" + runId + ".csv";

                dataFilePath = Path.Combine(directory, fileName);

                EnsureCsvHeader();
            }
            catch (Exception ex)
            {
                Print(Name + " | Data collection setup failed: " + ex.Message);
                dataFilePath = string.Empty;
            }
        }
         
        private void AppendDataRow(
            string eventType,
            string decision,
            DateTime dateEt,
            DateTime timeChart,
            DateTime timeEt,
            string direction,
            double open,
            double high,
            double low,
            double close,
            double bodyHigh,
            double bodyLow,
            double entryPrice,
            double entryDistanceTicks,
            double entryDistancePoints,
            double stopPrice,
            double targetPrice,
            int barsTracked,
            double mfeUsd,
            double maeUsd,
            double realizedPnlUsd,
            string outcome,
            DateTime exitTimeChart,
            double exitPrice,
            string notes)
        {
            if (!EnableDataCollection)
                return;

            if (string.IsNullOrWhiteSpace(dataFilePath))
                return;

            try
            {
                EnsureCsvHeader();

                var instrumentName = Instrument != null ? Instrument.FullName : "";

                var profileWidth = IsValidLevel(activeVAH) && IsValidLevel(activeVAL)
                    ? activeVAH - activeVAL
                    : double.NaN;

                var row = string.Join(",",
                    Csv(runId),
                    Csv(Name),
                    Csv(instrumentName),
                    Csv(eventType),
                    Csv(decision),
                    Csv(dateEt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv(FormatDateTime(timeChart)),
                    Csv(FormatDateTime(timeEt)),
                    Csv(direction),
                    CsvNumber(activeVAH),
                    CsvNumber(activeVAL),
                    CsvNumber(activePOC),
                    CsvNumber(profileWidth),
                    ProfileStartTime.ToString(CultureInfo.InvariantCulture),
                    ProfileEndTime.ToString(CultureInfo.InvariantCulture),
                    EntryStartTime.ToString(CultureInfo.InvariantCulture),
                    EntryEndTime.ToString(CultureInfo.InvariantCulture),
                    CsvNumber(BreakEvenProfitTriggerUsd),
                    CsvNumber(BreakEvenPlusUsd),
                    UseTickDataForProfile.ToString(),
                    EnableResearchMode.ToString(),
                    EntryOffsetTicks.ToString(CultureInfo.InvariantCulture),
                    MinRetracementTicks.ToString(CultureInfo.InvariantCulture),
                    MaxDistanceTicksFromBreakoutLevel.ToString(CultureInfo.InvariantCulture),
                    EnableLongs.ToString(),
                    EnableShorts.ToString(),
                    longBreakoutArmed.ToString(),
                    shortBreakoutArmed.ToString(),
                    CsvNumber(open),
                    CsvNumber(high),
                    CsvNumber(low),
                    CsvNumber(close),
                    CsvNumber(bodyHigh),
                    CsvNumber(bodyLow),
                    CsvNumber(entryPrice),
                    CsvNumber(entryDistanceTicks),
                    CsvNumber(entryDistancePoints),
                    CsvNumber(stopPrice),
                    CsvNumber(targetPrice),
                    CsvNumber(StopLossUsd),
                    CsvNumber(ProfitTargetUsd),
                    Quantity.ToString(CultureInfo.InvariantCulture),
                    barsTracked.ToString(CultureInfo.InvariantCulture),
                    CsvNumber(mfeUsd),
                    CsvNumber(maeUsd),
                    CsvNumber(realizedPnlUsd),
                    Csv(outcome),
                    Csv(exitTimeChart == Core.Globals.MinDate ? "" : FormatDateTime(exitTimeChart)),
                    CsvNumber(exitPrice),
                    Csv(notes));

                File.AppendAllText(dataFilePath, row + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Print(Name + " | Data collection write failed: " + ex.Message);
            }
        }

        private static string Csv(string value)
        {
            if (value == null)
                value = string.Empty;

            value = value.Replace("\"", "\"\"");

            return "\"" + value + "\"";
        }

        private static string CsvNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "";

            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "instrument";

            value = Path.GetInvalidFileNameChars()
                .Aggregate(value, (current, invalidChar) => current.Replace(invalidChar, '_'));

            return value.Replace(' ', '_');
        }

        private string BuildDataHeader()
        {
            return string.Join(",",
                "run_id",
                "strategy",
                "instrument",
                "event_type",
                "decision",
                "date_et",
                "time_chart",
                "time_et",
                "direction",
                "vah",
                "val",
                "poc",
                "profile_width",
                "profile_start_time",
                "profile_end_time",
                "entry_start_time",
                "entry_end_time",
                "be_profit_trigger_usd",
                "be_plus_usd",
                "use_tick_data_for_profile",
                "enable_research_mode",
                "entry_offset_ticks",
                "min_retracement_ticks",
                "max_distance_ticks",
                "enable_longs",
                "enable_shorts",
                "long_armed",
                "short_armed",
                "open",
                "high",
                "low",
                "close",
                "body_high",
                "body_low",
                "entry_price",
                "entry_distance_ticks",
                "entry_distance_points",
                "stop_price",
                "target_price",
                "stop_loss_usd",
                "profit_target_usd",
                "quantity",
                "bars_tracked",
                "mfe_usd",
                "mae_usd",
                "realized_pnl_usd",
                "outcome",
                "exit_time_chart",
                "exit_price",
                "notes");
        }
    }
}