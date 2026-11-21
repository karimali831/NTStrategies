using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightStructurePortfolio
    {
        private readonly object outputSync = new object();
        private string structureRunId = "";
        private string structureOutputDirectory = "";
        private StreamWriter minuteWriter;
        private StreamWriter contextWriter;
        private StreamWriter sessionWriter;
        private StreamWriter candidateWriter;
        private StreamWriter tradeWriter;
        private StreamWriter equityWriter;
        private bool researchFaulted;
        private int minuteFlushCounter;
        private int equityFlushCounter;
        private DateTime lastWrittenSummary = Core.Globals.MinDate;

        private void InitializeStructureResearch()
        {
            structureRunId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ",
                CultureInfo.InvariantCulture)
                + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            if (!EnableResearchExport)
                return;

            try
            {
                structureOutputDirectory = Path.Combine(
                    Core.Globals.UserDataDir, "NinjexResearch",
                    "OvernightStructurePortfolio", Version, structureRunId);
                Directory.CreateDirectory(structureOutputDirectory);

                var instrumentName = CleanFileName(
                    Instrument == null ? "UnknownInstrument" : Instrument.FullName);
                var prefix = "overnight_structure_" + instrumentName + "_"
                    + structureRunId + "_";

                minuteWriter = CsvWriter(prefix + "bars.csv",
                    "RunId,Instrument,TradingDate,BarEndET,Open,High,Low,Close,Volume,"
                    + "ONH,ONL,PMH,PML,PDC,RTHOpen,PMWidthTicks,CompressionThresholdTicks,"
                    + "Compressed,CompressionHistoryCount,EMA5Fast,EMA5Slow,EMA5SlowSlopeTicks,ATR5Ticks,ProcessingState");
                contextWriter = CsvWriter(prefix + "context.csv",
                    "RunId,Instrument,BarEndET,Open,High,Low,Close,Volume,EMA5Fast,"
                    + "EMA5Slow,SlowSlopeTicks,ATR5Ticks,ProcessingState");
                sessionWriter = CsvWriter(prefix + "sessions.csv",
                    "RunId,Instrument,Date,Event,OvernightReady,PremarketReady,"
                    + "ONH,ONL,PMH,PML,OvernightBars,PremarketBars,PMWidthTicks,"
                    + "PriorHistoryCount,CompressionThresholdTicks,Compressed,"
                    + "RTHMinuteBars,Trades,Winners,Losses,GrossPnl,ProcessingState");
                candidateWriter = CsvWriter(prefix + "candidates.csv",
                    "RunId,Instrument,Date,SignalEndET,Signal,Family,Direction,Level,"
                    + "Open,High,Low,Close,StopTicks,TargetTicks,Qualified,Disposition,"
                    + "ONH,ONL,PMH,PML,PDC,RTHOpen,PMWidthTicks,HistoryCount,"
                    + "CompressionThresholdTicks,Compressed,EMA5Fast,EMA5Slow,"
                    + "SlowSlopeTicks,ATR5Ticks,ProcessingState");
                tradeWriter = CsvWriter(prefix + "trades.csv",
                    "RunId,Instrument,Date,Signal,SignalEndET,EntryTimeET,EntryPrice,Quantity,"
                    + "ExitTimeET,ExitPrice,ExitName,StopTicks,TargetTicks,MaxHoldMinutes,"
                    + "MfeTicks,MaeTicks,GrossPnl,DayTrades,DayWinners,DayLosses,"
                    + "CumulativeGross,ProcessingState");
                equityWriter = CsvWriter(prefix + "equity.csv",
                    "RunId,Instrument,EventTimeET,LastPrice,RealizedGross,OpenGross,"
                    + "Equity,PeakEquity,CurrentDrawdown,MaxIntratradeDD,"
                    + "ActiveSignal,ProcessingState");
            }
            catch (Exception ex)
            {
                DisableResearch("Initialize", ex);
            }
        }

        private StreamWriter CsvWriter(string name, string header)
        {
            var path = Path.Combine(structureOutputDirectory, name);
            var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            writer.WriteLine(header);
            writer.Flush();
            return writer;
        }

        private void WriteMinuteBar(DateTime end, double open, double high,
            double low, double close, double volume)
        {
            if (!EnableResearchExport || researchFaulted || minuteWriter == null)
                return;

            // Full overnight can be reconstructed from the 5m context; keep
            // high-resolution 1m from premarket through RTH.
            var clock = Clock(end);
            if (clock < PremarketStartTime || clock > FlattenTime)
                return;

            lock (outputSync)
            {
                try
                {
                    minuteWriter.WriteLine(string.Join(",", new[] {
                        Csv(structureRunId), Csv(Instrument.FullName),
                        Day(end.Date), Stamp(end), Num(open), Num(high),
                        Num(low), Num(close), Num(volume), Num(onHigh),
                        Num(onLow), Num(pmHigh), Num(pmLow),
                        Num(priorRthClose), Num(rthOpen),
                        Num(Valid(pmHigh) && Valid(pmLow) && TickSize > 0
                            ? (pmHigh - pmLow) / TickSize : double.NaN),
                        Num(compressionThreshold), compressedToday ? "1" : "0",
                        compressionHistoryCount.ToString(CultureInfo.InvariantCulture),
                        Num(emaFast), Num(emaSlow), Num(emaSlowSlopeTicks),
                        Num(atrTicks), Csv(State.ToString()) }));
                    if (++minuteFlushCounter >= 300)
                    {
                        minuteWriter.Flush();
                        minuteFlushCounter = 0;
                    }
                }
                catch (Exception ex) { DisableResearch("MinuteBar", ex); }
            }
        }

        private void WriteContextBar(DateTime end, double open, double high,
            double low, double close, double volume)
        {
            if (!EnableResearchExport || researchFaulted || contextWriter == null)
                return;
            lock (outputSync)
            {
                try
                {
                    contextWriter.WriteLine(string.Join(",", new[] {
                        Csv(structureRunId), Csv(Instrument.FullName),
                        Stamp(end), Num(open), Num(high), Num(low),
                        Num(close), Num(volume), Num(emaFast),
                        Num(emaSlow), Num(emaSlowSlopeTicks),
                        Num(atrTicks), Csv(State.ToString()) }));
                    if (end.Minute == 0) contextWriter.Flush();
                }
                catch (Exception ex) { DisableResearch("ContextBar", ex); }
            }
        }

        private void WriteStructureSessionSnapshot(DateTime when, double pmWidth)
        {
            WriteSession(when, "PremarketCompleted", pmWidth, 0);
        }

        private void FlushStructureDailySummary()
        {
            if (rthDate == Core.Globals.MinDate
                || rthDate == lastWrittenSummary)
                return;
            var time = rthDate.AddHours(16);
            WriteSession(time, "RthSummary",
                Valid(pmHigh) && Valid(pmLow) && TickSize > 0
                    ? (pmHigh - pmLow) / TickSize : double.NaN,
                rthMinuteBars);
            lastWrittenSummary = rthDate;
            FlushAllResearch();
        }

        private void FinalizeStructureSession()
        {
            FlushStructureDailySummary();
            if (EnableDiagnosticLogging)
                LogEvent(DateTime.Now, "TERMINATED Gross=" + Num(cumulativeGross)
                    + " Peak=" + Num(equityPeak) + " MaxIntratradeDD=" + Num(maxDrawdown));
        }

        private void WriteSession(DateTime when, string ev, double width, int rthBars)
        {
            if (!EnableResearchExport || researchFaulted || sessionWriter == null)
                return;
            lock (outputSync)
            {
                try
                {
                    sessionWriter.WriteLine(string.Join(",", new[] {
                        Csv(structureRunId), Csv(Instrument.FullName),
                        Day(ev == "RthSummary" ? rthDate : pmDate),
                        Csv(ev), onReady ? "1":"0", pmReady ? "1":"0",
                        Num(onHigh), Num(onLow), Num(pmHigh), Num(pmLow),
                        onBarCount.ToString(CultureInfo.InvariantCulture),
                        pmBarCount.ToString(CultureInfo.InvariantCulture),
                        Num(width), compressionHistoryCount.ToString(CultureInfo.InvariantCulture),
                        Num(compressionThreshold), compressedToday ? "1":"0",
                        rthBars.ToString(CultureInfo.InvariantCulture),
                        (ev == "RthSummary" ? tradesToday : 0).ToString(CultureInfo.InvariantCulture),
                        (ev == "RthSummary" ? winnersToday : 0).ToString(CultureInfo.InvariantCulture),
                        (ev == "RthSummary" ? lossesToday : 0).ToString(CultureInfo.InvariantCulture),
                        Num(ev == "RthSummary" ? dailyGross : 0.0),
                        Csv(State.ToString()) }));
                    sessionWriter.Flush();
                }
                catch (Exception ex) { DisableResearch("Session", ex); }
            }
        }

        private void WriteCandidate(Candidate c, string disposition)
        {
            if (!EnableResearchExport || researchFaulted || candidateWriter == null)
                return;
            lock (outputSync)
            {
                try
                {
                    candidateWriter.WriteLine(string.Join(",", new[] {
                        Csv(structureRunId), Csv(Instrument.FullName), Day(c.Time.Date),
                        Stamp(c.Time), Csv(c.Signal), Csv(c.Family),
                        c.Long ? "Long" : "Short", Num(c.Level),
                        Num(c.Open), Num(c.High), Num(c.Low), Num(c.Close),
                        c.StopTicks.ToString(CultureInfo.InvariantCulture),
                        c.TargetTicks.ToString(CultureInfo.InvariantCulture),
                        c.Qualified ? "1":"0", Csv(disposition),
                        Num(onHigh), Num(onLow), Num(pmHigh), Num(pmLow),
                        Num(priorRthClose), Num(rthOpen),
                        Num(Valid(pmHigh) && Valid(pmLow) && TickSize > 0
                            ? (pmHigh - pmLow) / TickSize : double.NaN),
                        c.HistoryCount.ToString(CultureInfo.InvariantCulture),
                        Num(c.CompressionThreshold), compressedToday ? "1" : "0",
                        Num(emaFast), Num(emaSlow), Num(emaSlowSlopeTicks),
                        Num(atrTicks), Csv(State.ToString()) }));
                    candidateWriter.Flush();
                }
                catch (Exception ex) { DisableResearch("Candidate", ex); }
            }
        }

        private void WriteTrade(DateTime exitTime, string exitName, double exitPrice)
        {
            if (!EnableResearchExport || researchFaulted || tradeWriter == null)
                return;
            lock (outputSync)
            {
                try
                {
                    tradeWriter.WriteLine(string.Join(",", new[] {
                        Csv(structureRunId), Csv(Instrument.FullName), Day(entryTime.Date),
                        Csv(activeSignal), Stamp(activeSignalEnd),
                        Stamp(entryTime), Num(entryAverage),
                        entryFillQty.ToString(CultureInfo.InvariantCulture),
                        Stamp(exitTime), Num(exitPrice), Csv(exitName),
                        activeStopTicks.ToString(CultureInfo.InvariantCulture),
                        activeTargetTicks.ToString(CultureInfo.InvariantCulture),
                        MaximumHoldMinutes.ToString(CultureInfo.InvariantCulture),
                        Num(mfeTicks), Num(maeTicks), Num(exitGross),
                        tradesToday.ToString(CultureInfo.InvariantCulture),
                        winnersToday.ToString(CultureInfo.InvariantCulture),
                        lossesToday.ToString(CultureInfo.InvariantCulture),
                        Num(cumulativeGross), Csv(State.ToString()) }));
                    tradeWriter.Flush();
                }
                catch (Exception ex) { DisableResearch("Trade", ex); }
            }
        }

        private void WriteEquity(DateTime time, double last, double unrealized,
            double eq, double dd)
        {
            if (!EnableResearchExport || researchFaulted || equityWriter == null)
                return;
            lock (outputSync)
            {
                try
                {
                    equityWriter.WriteLine(string.Join(",", new[] {
                        Csv(structureRunId), Csv(Instrument.FullName), Stamp(time),
                        Num(last), Num(cumulativeGross), Num(unrealized),
                        Num(eq), Num(equityPeak), Num(dd), Num(maxDrawdown),
                        Csv(activeSignal), Csv(State.ToString()) }));
                    if (++equityFlushCounter >= 100)
                    {
                        equityWriter.Flush();
                        equityFlushCounter = 0;
                    }
                }
                catch (Exception ex) { DisableResearch("Equity", ex); }
            }
        }

        private void LogEvent(DateTime when, string message)
        {
            if (!EnableDiagnosticLogging)
                return;
            try
            {
                var folder = Path.Combine(Core.Globals.UserDataDir,
                    "NinjexLogs", "OvernightStructurePortfolio", Version);
                Directory.CreateDirectory(folder);
                var filename = Version + "_" + when.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture) + ".log";
                File.AppendAllText(Path.Combine(folder, filename),
                    Stamp(when) + " | " + structureRunId + " | " + message
                    + Environment.NewLine);
            }
            catch { /* Logging must not alter live order behaviour. */ }
        }

        private void FlushAllResearch()
        {
            if (researchFaulted || !EnableResearchExport)
                return;
            lock (outputSync)
            {
                try
                {
                    if (minuteWriter != null) minuteWriter.Flush();
                    if (contextWriter != null) contextWriter.Flush();
                    if (candidateWriter != null) candidateWriter.Flush();
                    if (tradeWriter != null) tradeWriter.Flush();
                    if (equityWriter != null) equityWriter.Flush();
                }
                catch (Exception ex) { DisableResearch("Flush", ex); }
            }
        }

        private void DisposeStructureResearch()
        {
            FlushAllResearch();
            lock (outputSync)
            {
                foreach (var w in new[] { minuteWriter, contextWriter, sessionWriter,
                    candidateWriter, tradeWriter, equityWriter })
                {
                    if (w == null) continue;
                    try { w.Dispose(); } catch { }
                }
            }
        }

        private void DisableResearch(string operation, Exception ex)
        {
            researchFaulted = true;
            LogEvent(DateTime.Now, "RESEARCH_DISABLED Operation=" + operation
                + " Error=" + (ex == null ? "" : ex.Message));
        }

        private static string CleanFileName(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value.Replace(' ', '_');
        }

        private static string Num(double v)
        {
            return Valid(v) ? v.ToString("0.########",
                CultureInfo.InvariantCulture) : "";
        }

        private static string Stamp(DateTime v)
        {
            return v.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string Day(DateTime v)
        {
            return v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string Csv(string text)
        {
            return "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
