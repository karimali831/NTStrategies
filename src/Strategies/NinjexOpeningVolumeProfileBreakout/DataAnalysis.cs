using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private string dataFilePath = string.Empty;
        private string runId = string.Empty;
        private DateTime lastProfileLoggedDate = NinjaTrader.Core.Globals.MinDate;

        private PendingSetup pendingSetup;

        private class PendingSetup
        {
            public DateTime SignalDateEt;
            public DateTime SignalTimeChart;
            public DateTime SignalTimeEt;

            public string Direction;
            public string Decision;

            public double VAH;
            public double VAL;
            public double POC;

            public double Open;
            public double High;
            public double Low;
            public double Close;
            public double BodyHigh;
            public double BodyLow;

            public double EntryPrice;
            public double StopPrice;
            public double TargetPrice;

            public int Quantity;
            public int BarsTracked;

            public double MfeUsd;
            public double MaeUsd;
            
            public double EntryDistanceTicks;
            public double EntryDistancePoints;
        }
        
        private void ConfigureDataCollection()
        {
            if (!EnableDataCollection)
                return;

            try
            {
                runId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

                var directory = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, DataDirectoryName);
                Directory.CreateDirectory(directory);

                var instrumentName = Instrument != null
                    ? SanitizeFileName(Instrument.FullName)
                    : "instrument";

                var fileName = DataFilePrefix + "_" + instrumentName + ".csv";

                dataFilePath = Path.Combine(directory, fileName);

                if (!File.Exists(dataFilePath))
                    File.AppendAllText(dataFilePath, BuildDataHeader() + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Print(Name + " | Data collection setup failed: " + ex.Message);
                dataFilePath = string.Empty;
            }
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
                "outcome",
                "exit_time_chart",
                "exit_price",
                "notes");
        }

        private void LogDailyProfileIfNeeded(DateTime easternNow)
        {
            if (!EnableDataCollection || !LogDailyProfileRows)
                return;

            if (string.IsNullOrWhiteSpace(dataFilePath))
                return;

            var profileDate = easternNow.Date;

            if (lastProfileLoggedDate == profileDate)
                return;

            if (!IsValidLevel(activeVAH) || !IsValidLevel(activeVAL))
                return;

            lastProfileLoggedDate = profileDate;

            AppendDataRow(
                eventType: "PROFILE",
                decision: "Completed",
                dateEt: profileDate,
                timeChart: Time[0],
                timeEt: easternNow,
                direction: "",
                open: double.NaN,
                high: double.NaN,
                low: double.NaN,
                close: double.NaN,
                bodyHigh: double.NaN,
                bodyLow: double.NaN,
                entryPrice: double.NaN,
                entryDistanceTicks: double.NaN,
                entryDistancePoints: double.NaN,
                stopPrice: double.NaN,
                targetPrice: double.NaN,
                barsTracked: 0,
                mfeUsd: 0,
                maeUsd: 0,
                outcome: "",
                exitTimeChart: NinjaTrader.Core.Globals.MinDate,
                exitPrice: double.NaN,
                notes: "Daily profile levels");
        }

        private void LogRejectedSetup(string direction, string reason, double bodyHigh, double bodyLow)
        {
            if (!EnableDataCollection || !LogRejectedSetups)
                return;

            var easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];
            
            var entryDistanceTicks = GetEntryDistanceTicks(direction, Close[0]);
            var entryDistancePoints = entryDistanceTicks * TickSize;

            AppendDataRow(
                eventType: "REJECTED_SETUP",
                decision: reason,
                dateEt: easternNow.Date,
                timeChart: Time[0],
                timeEt: easternNow,
                direction: direction,
                open: Open[0],
                high: High[0],
                low: Low[0],
                close: Close[0],
                bodyHigh: bodyHigh,
                bodyLow: bodyLow,
                entryPrice: Close[0],
                entryDistanceTicks,
                entryDistancePoints,
                stopPrice: double.NaN,
                targetPrice: double.NaN,
                barsTracked: 0,
                mfeUsd: 0,
                maeUsd: 0,
                outcome: "Rejected",
                exitTimeChart: NinjaTrader.Core.Globals.MinDate,
                exitPrice: double.NaN,
                notes: reason);
        }

        private void StartPendingSetup(string direction, string decision, double entryPrice, double stopPrice, double targetPrice)
        {
            if (!EnableDataCollection || !TrackForwardOutcome)
                return;

            var easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            var bodyHigh = Math.Max(Open[0], Close[0]);
            var bodyLow = Math.Min(Open[0], Close[0]);
            
            var entryDistanceTicks = GetEntryDistanceTicks(direction, entryPrice);
            var entryDistancePoints = entryDistanceTicks * TickSize;

            pendingSetup = new PendingSetup
            {
                SignalDateEt = easternNow.Date,
                SignalTimeChart = Time[0],
                SignalTimeEt = easternNow,
                Direction = direction,
                Decision = decision,

                VAH = activeVAH,
                VAL = activeVAL,
                POC = activePOC,

                Open = Open[0],
                High = High[0],
                Low = Low[0],
                Close = Close[0],
                BodyHigh = bodyHigh,
                BodyLow = bodyLow,

                EntryPrice = entryPrice,
                EntryDistanceTicks = entryDistanceTicks,
                EntryDistancePoints = entryDistancePoints,
                StopPrice = stopPrice,
                TargetPrice = targetPrice,

                Quantity = Quantity,
                BarsTracked = 0,
                MfeUsd = 0,
                MaeUsd = 0
            };
        }

        private void UpdatePendingSetupOutcome()
        {
            if (!EnableDataCollection || !TrackForwardOutcome)
                return;

            if (pendingSetup == null)
                return;

            if (Time[0] <= pendingSetup.SignalTimeChart)
                return;

            pendingSetup.BarsTracked++;

            var pointValue = Instrument.MasterInstrument.PointValue;
            var safeQuantity = Math.Max(1, pendingSetup.Quantity);

            var targetHit = false;
            var stopHit = false;

            if (pendingSetup.Direction == "LONG")
            {
                var mfe = (High[0] - pendingSetup.EntryPrice) * pointValue * safeQuantity;
                var mae = (Low[0] - pendingSetup.EntryPrice) * pointValue * safeQuantity;

                pendingSetup.MfeUsd = Math.Max(pendingSetup.MfeUsd, mfe);
                pendingSetup.MaeUsd = Math.Min(pendingSetup.MaeUsd, mae);

                targetHit = High[0] >= pendingSetup.TargetPrice;
                stopHit = Low[0] <= pendingSetup.StopPrice;
            }
            else if (pendingSetup.Direction == "SHORT")
            {
                var mfe = (pendingSetup.EntryPrice - Low[0]) * pointValue * safeQuantity;
                var mae = (pendingSetup.EntryPrice - High[0]) * pointValue * safeQuantity;

                pendingSetup.MfeUsd = Math.Max(pendingSetup.MfeUsd, mfe);
                pendingSetup.MaeUsd = Math.Min(pendingSetup.MaeUsd, mae);

                targetHit = Low[0] <= pendingSetup.TargetPrice;
                stopHit = High[0] >= pendingSetup.StopPrice;
            }

            if (targetHit && stopHit)
            {
                var outcome = SameBarStopFirst ? "SameBarBoth_StopFirst" : "SameBarBoth_TargetFirst";
                var exitPrice = SameBarStopFirst ? pendingSetup.StopPrice : pendingSetup.TargetPrice;

                FinalizePendingSetup(outcome, Time[0], exitPrice, "Both stop and target touched in same bar. OHLC cannot determine true sequence.");
                return;
            }

            if (targetHit)
            {
                FinalizePendingSetup("TargetHit", Time[0], pendingSetup.TargetPrice, "");
                return;
            }

            if (stopHit)
            {
                FinalizePendingSetup("StopHit", Time[0], pendingSetup.StopPrice, "");
                return;
            }

            if (pendingSetup.BarsTracked >= ForwardBarsToTrack)
            {
                FinalizePendingSetup("Timeout", Time[0], Close[0], "Forward tracking limit reached.");
            }
        }

        private void FinalizePendingSetup(string outcome, DateTime exitTimeChart, double exitPrice, string notes)
        {
            if (pendingSetup == null)
                return;

            AppendDataRow(
                eventType: "SETUP_OUTCOME",
                decision: pendingSetup.Decision,
                dateEt: pendingSetup.SignalDateEt,
                timeChart: pendingSetup.SignalTimeChart,
                timeEt: pendingSetup.SignalTimeEt,
                direction: pendingSetup.Direction,
                open: pendingSetup.Open,
                high: pendingSetup.High,
                low: pendingSetup.Low,
                close: pendingSetup.Close,
                bodyHigh: pendingSetup.BodyHigh,
                bodyLow: pendingSetup.BodyLow,
                entryPrice: pendingSetup.EntryPrice,
                entryDistanceTicks: pendingSetup.EntryDistanceTicks,
                entryDistancePoints: pendingSetup.EntryDistancePoints,
                stopPrice: pendingSetup.StopPrice,
                targetPrice: pendingSetup.TargetPrice,
                barsTracked: pendingSetup.BarsTracked,
                mfeUsd: pendingSetup.MfeUsd,
                maeUsd: pendingSetup.MaeUsd,
                outcome: outcome,
                exitTimeChart: exitTimeChart,
                exitPrice: exitPrice,
                notes: notes);

            pendingSetup = null;
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
                    Csv(outcome),
                    Csv(exitTimeChart == NinjaTrader.Core.Globals.MinDate ? "" : FormatDateTime(exitTimeChart)),
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
    }
}