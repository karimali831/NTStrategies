using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        // This exporter is deliberately observational: no order, position or
        // strategy qualification state is modified by any method in this file.
        private readonly object researchSessionExportSync = new object();
        private StreamWriter researchBarsWriter;
        private StreamWriter researchContextWriter;
        private StreamWriter researchSessionsWriter;
        private bool researchSessionExportFaulted;
        private string researchRunId = string.Empty;
        private DateTime researchSessionDate = Core.Globals.MinDate;
        private DateTime researchLastBarEnd = Core.Globals.MinDate;
        private int researchRthMinuteBars;
        private int researchPremarketMinuteBars;
        private int researchBarsSinceFlush;
        private int researchContextSinceFlush;

        private void InitializeResearchSessionExport()
        {
            // Shared by all research files, even when the session exporter is off.
            researchRunId = string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}",
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture),
                Guid.NewGuid().ToString("N").Substring(0, 8));

            researchSessionExportFaulted = false;
            researchSessionDate = Core.Globals.MinDate;
            researchLastBarEnd = Core.Globals.MinDate;
            researchRthMinuteBars = 0;
            researchPremarketMinuteBars = 0;
            researchBarsSinceFlush = 0;
            researchContextSinceFlush = 0;

            if (!EnableResearchSessionExport)
                return;

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir,
                    "NinjexResearch",
                    "OvernightEdgePortfolio");

                Directory.CreateDirectory(directory);
                var instrumentName = Instrument == null
                    ? "UnknownInstrument"
                    : SanitizeFileName(Instrument.FullName);
                var prefix = "overnight_edge_";

                researchBarsWriter = NewResearchCsvWriter(
                    Path.Combine(directory, prefix + "bars_" + instrumentName + "_" + researchRunId + ".csv"),
                    "RunId,Instrument,TradingDate,BarEndET,Open,High,Low,Close,Volume," +
                    "ATR5Ticks,EMA5Fast,EMA5Slow,Context5mBarEndET,OvernightHigh,OvernightLow," +
                    "PremarketHigh,PremarketLow,PriorDayClose,RthOpen,ProcessingState");

                researchContextWriter = NewResearchCsvWriter(
                    Path.Combine(directory, prefix + "context_" + instrumentName + "_" + researchRunId + ".csv"),
                    "RunId,Instrument,BarEndET,Next5mBarEndET,Open,High,Low,Close,Volume," +
                    "ATR5Ticks,EMA5Fast,EMA5Slow,ProcessingState");

                researchSessionsWriter = NewResearchCsvWriter(
                    Path.Combine(directory, prefix + "sessions_" + instrumentName + "_" + researchRunId + ".csv"),
                    "RunId,Instrument,TradingDate,LastBarEndET,PremarketMinuteBars,RthMinuteBars," +
                    "ExpectedNormalRthMinuteBars,NormalRthCoverageComplete,OvernightRangeReady," +
                    "PremarketRangeReady,OvernightBars,PremarketBars,OvernightHigh,OvernightLow," +
                    "PremarketHigh,PremarketLow,PriorDayClose,RthOpen,Trades,Winners,Losses," +
                    "GrossPnl,ProcessingState");

                Diagnostic(DateTime.Now,
                    "RESEARCH SESSION EXPORT READY RunId={0} Directory={1}",
                    researchRunId, directory);
            }
            catch (Exception ex)
            {
                DisableResearchSessionExport("InitError", ex);
            }
        }

        private static StreamWriter NewResearchCsvWriter(string path, string header)
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.WriteLine(header);
            writer.Flush();
            return writer;
        }

        private void CaptureResearchSessionMinuteBar(
            DateTime barEnd, double open, double high, double low,
            double close, long volume)
        {
            if (!EnableResearchSessionExport || researchSessionExportFaulted)
                return;

            var clock = ToTimeValue(barEnd);
            if (clock < PremarketStartTime || clock > 160500)
                return;

            lock (researchSessionExportSync)
            {
                try
                {
                    if (researchBarsWriter == null)
                        return;

                    // RTH dates are determined by actual bar timestamps, not
                    // system clock or the contract-roll calendar.
                    if (researchSessionDate != barEnd.Date)
                    {
                        FlushResearchSessionSummaryCore();
                        researchSessionDate = barEnd.Date;
                        researchRthMinuteBars = 0;
                        researchPremarketMinuteBars = 0;
                        researchLastBarEnd = Core.Globals.MinDate;
                    }

                    // Historical-to-realtime transition can deliver a repeated
                    // completed bar; never double-count or duplicate it.
                    if (researchLastBarEnd != Core.Globals.MinDate
                        && barEnd <= researchLastBarEnd)
                        return;

                    if (clock > MarketOpenTime && clock <= FlattenTime)
                        researchRthMinuteBars++;
                    else if (clock <= MarketOpenTime)
                        researchPremarketMinuteBars++;

                    researchLastBarEnd = barEnd;

                    researchBarsWriter.WriteLine(string.Join(",", new[]
                    {
                        CsvSession(researchRunId),
                        CsvSession(Instrument == null ? string.Empty : Instrument.FullName),
                        DaySession(barEnd),
                        TimeSession(barEnd),
                        NumberSession(open), NumberSession(high), NumberSession(low),
                        NumberSession(close), volume.ToString(CultureInfo.InvariantCulture),
                        NumberSession(last5mAtrTicks),
                        NumberSession(last5mEmaFast),
                        NumberSession(last5mEmaSlow),
                        TimeSession(last5mTime),
                        NumberSession(overnightHigh), NumberSession(overnightLow),
                        NumberSession(premarketHigh), NumberSession(premarketLow),
                        NumberSession(priorDayClose), NumberSession(rthOpen),
                        CsvSession(State.ToString())
                    }));

                    if (++researchBarsSinceFlush >= 100)
                    {
                        researchBarsWriter.Flush();
                        researchBarsSinceFlush = 0;
                    }
                }
                catch (Exception ex)
                {
                    DisableResearchSessionExport("MinuteWriteError", ex);
                }
            }
        }

        private void CaptureResearchSessionFiveMinuteBar(
            DateTime barEnd, DateTime nextBarEnd,
            double open, double high, double low, double close, long volume)
        {
            if (!EnableResearchSessionExport || researchSessionExportFaulted)
                return;

            lock (researchSessionExportSync)
            {
                try
                {
                    if (researchContextWriter == null)
                        return;

                    researchContextWriter.WriteLine(string.Join(",", new[]
                    {
                        CsvSession(researchRunId),
                        CsvSession(Instrument == null ? string.Empty : Instrument.FullName),
                        TimeSession(barEnd),
                        TimeSession(nextBarEnd),
                        NumberSession(open), NumberSession(high), NumberSession(low),
                        NumberSession(close), volume.ToString(CultureInfo.InvariantCulture),
                        NumberSession(last5mAtrTicks),
                        NumberSession(last5mEmaFast),
                        NumberSession(last5mEmaSlow),
                        CsvSession(State.ToString())
                    }));

                    if (++researchContextSinceFlush >= 100)
                    {
                        researchContextWriter.Flush();
                        researchContextSinceFlush = 0;
                    }
                }
                catch (Exception ex)
                {
                    DisableResearchSessionExport("ContextWriteError", ex);
                }
            }
        }

        private void FlushResearchSessionSummary()
        {
            if (!EnableResearchSessionExport || researchSessionExportFaulted)
                return;

            lock (researchSessionExportSync)
            {
                try
                {
                    FlushResearchSessionSummaryCore();
                    if (researchBarsWriter != null)
                        researchBarsWriter.Flush();
                    if (researchContextWriter != null)
                        researchContextWriter.Flush();
                    if (researchSessionsWriter != null)
                        researchSessionsWriter.Flush();
                }
                catch (Exception ex)
                {
                    DisableResearchSessionExport("SummaryWriteError", ex);
                }
            }
        }

        private void FlushResearchSessionSummaryCore()
        {
            if (researchSessionsWriter == null
                || researchSessionDate == Core.Globals.MinDate)
                return;

            // Ignore weekends/overnight-only dates; preserve zero-trade RTH days.
            if (researchRthMinuteBars > 0)
            {
                researchSessionsWriter.WriteLine(string.Join(",", new[]
                {
                    CsvSession(researchRunId),
                    CsvSession(Instrument == null ? string.Empty : Instrument.FullName),
                    DaySession(researchSessionDate),
                    TimeSession(researchLastBarEnd),
                    researchPremarketMinuteBars.ToString(CultureInfo.InvariantCulture),
                    researchRthMinuteBars.ToString(CultureInfo.InvariantCulture),
                    "390",
                    researchRthMinuteBars >= 390 ? "1" : "0",
                    overnightRangeReady ? "1" : "0",
                    premarketRangeReady ? "1" : "0",
                    overnightBars.ToString(CultureInfo.InvariantCulture),
                    premarketBars.ToString(CultureInfo.InvariantCulture),
                    NumberSession(overnightHigh), NumberSession(overnightLow),
                    NumberSession(premarketHigh), NumberSession(premarketLow),
                    NumberSession(priorDayClose), NumberSession(rthOpen),
                    tradesToday.ToString(CultureInfo.InvariantCulture),
                    winnersToday.ToString(CultureInfo.InvariantCulture),
                    lossesToday.ToString(CultureInfo.InvariantCulture),
                    NumberSession(grossPnlToday),
                    CsvSession(State.ToString())
                }));
                researchSessionsWriter.Flush();
            }

            researchSessionDate = Core.Globals.MinDate;
            researchRthMinuteBars = 0;
            researchPremarketMinuteBars = 0;
        }

        private void DisposeResearchSessionExport()
        {
            FlushResearchSessionSummary();
            lock (researchSessionExportSync)
            {
                DisposeResearchCsvWriter(ref researchBarsWriter);
                DisposeResearchCsvWriter(ref researchContextWriter);
                DisposeResearchCsvWriter(ref researchSessionsWriter);
            }
        }

        private void DisableResearchSessionExport(string reason, Exception error)
        {
            researchSessionExportFaulted = true;
            DisposeResearchCsvWriter(ref researchBarsWriter);
            DisposeResearchCsvWriter(ref researchContextWriter);
            DisposeResearchCsvWriter(ref researchSessionsWriter);
            Diagnostic(DateTime.Now,
                "RESEARCH SESSION EXPORT DISABLED Reason={0} Error={1}",
                reason, error == null ? "Unknown" : error.Message);
        }

        private static void DisposeResearchCsvWriter(ref StreamWriter writer)
        {
            if (writer == null)
                return;
            try { writer.Dispose(); }
            catch { /* Never interfere with trading lifecycle. */ }
            writer = null;
        }

        private static string TimeSession(DateTime value)
        {
            return value == Core.Globals.MinDate
                ? string.Empty
                : value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string DaySession(DateTime value)
        {
            return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string NumberSession(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string CsvSession(string value)
        {
            var sanitized = (value ?? string.Empty).Replace("\"", "\"\"");
            return "\"" + sanitized + "\"";
        }
    }
}
