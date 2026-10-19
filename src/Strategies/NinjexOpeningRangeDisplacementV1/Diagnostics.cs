using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeDisplacementV1
    {
        private readonly object logSync = new object();
        private readonly string instanceId = Guid.NewGuid().ToString("N").Substring(0, 12);
        private readonly System.Collections.Generic.Dictionary<string, StreamWriter> fileWriters =
            new System.Collections.Generic.Dictionary<string, StreamWriter>(StringComparer.Ordinal);
        private DateTime firstLogDate = DateTime.MinValue;
        private bool fileFailed;
        private bool fileErrorReported;

        private static string Csv(object value)
        {
            string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        private static string SafePathPart(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "Unknown";
            foreach (char c in Path.GetInvalidFileNameChars())
                input = input.Replace(c, '_');
            return input.Replace(' ', '_');
        }

        private static string ChannelFor(string kind)
        {
            if (kind == "BAR")
                return "research";
            if (kind.Contains("EXECUTION") || kind.StartsWith("ORDER_", StringComparison.Ordinal)
                || kind.StartsWith("BRACKET_", StringComparison.Ordinal)
                || kind == "EXIT_PNL" || kind == "TRADE_COMPLETE"
                || kind == "INVALID_GAP_FILL" || kind == "EMERGENCY_EXIT_SUBMITTED")
                return "executions";
            return "signals";
        }

        private string FileFor(string channel)
        {
            string folder = Path.Combine(NinjaTrader.Core.Globals.UserDataDir,
                "NinjexLogs", "NinjexOpeningRangeDisplacementV1");
            string instrument = SafePathPart(Instrument == null ? null : Instrument.FullName);
            string account = SafePathPart(Account == null ? null : Account.Name);
            return Path.Combine(folder, "NinjexOpeningRangeDisplacementV1_v" + Version
                + "_" + instrument + "_" + account + "_"
                + firstLogDate.ToString("yyyyMMdd") + "_" + instanceId
                + "_" + channel + ".csv");
        }

        private void WriteDiagnostic(string kind, int direction, BreakoutAttempt attempt, string detail, DateTime? eventTime = null)
        {
            if (!EnableFileLogging && !EnablePrintDiagnostics)
                return;

            DateTime timestamp = eventTime.HasValue ? eventTime.Value : (CurrentBar >= 0 ? Time[0] : DateTime.Now);
            double atr = CurrentBar >= 14 && atr14 != null ? GetAtrTicks() : double.NaN;
            double adx = CurrentBar >= 28 && adx14 != null ? GetAdx() : double.NaN;
            double bodyTicks = CurrentBar >= 0 ? Math.Abs(Close[0] - Open[0]) / TickSize : double.NaN;
            double candleTicks = CurrentBar >= 0 ? (High[0] - Low[0]) / TickSize : double.NaN;
            double bodyAtr = atr > 0 ? bodyTicks / atr : double.NaN;
            double bodyPct = candleTicks > 0 ? bodyTicks / candleTicks : double.NaN;

            string[] fields =
            {
                timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Version,
                Instrument == null ? "" : Instrument.FullName,
                Account == null ? "" : Account.Name,
                State.ToString(),
                kind,
                direction.ToString(CultureInfo.InvariantCulture),
                orHigh.ToString("R", CultureInfo.InvariantCulture),
                orLow.ToString("R", CultureInfo.InvariantCulture),
                CurrentBar >= 0 ? Open[0].ToString("R", CultureInfo.InvariantCulture) : "",
                CurrentBar >= 0 ? High[0].ToString("R", CultureInfo.InvariantCulture) : "",
                CurrentBar >= 0 ? Low[0].ToString("R", CultureInfo.InvariantCulture) : "",
                CurrentBar >= 0 ? Close[0].ToString("R", CultureInfo.InvariantCulture) : "",
                atr.ToString("R", CultureInfo.InvariantCulture),
                adx.ToString("R", CultureInfo.InvariantCulture),
                bodyTicks.ToString("R", CultureInfo.InvariantCulture),
                candleTicks.ToString("R", CultureInfo.InvariantCulture),
                bodyAtr.ToString("R", CultureInfo.InvariantCulture),
                bodyPct.ToString("R", CultureInfo.InvariantCulture),
                attempt == null ? "" : attempt.BreakTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                attempt == null ? "" : attempt.Stop.ToString("R", CultureInfo.InvariantCulture),
                attempt == null ? "" : attempt.AtrTicks.ToString("R", CultureInfo.InvariantCulture),
                attempt == null ? "" : attempt.Adx.ToString("R", CultureInfo.InvariantCulture),
                attempt == null ? "" : attempt.BodyAtr.ToString("R", CultureInfo.InvariantCulture),
                attempt == null ? "" : attempt.WasRetest.ToString(),
                detail ?? ""
            };

            string line = string.Join(",", Array.ConvertAll(fields, Csv));
            if (EnablePrintDiagnostics && kind != "BAR")
                Print("NinjexOpeningRangeDisplacementV1 | " + timestamp.ToString("s") + " | " + kind + " | " + detail);

            if (!EnableFileLogging || fileFailed)
                return;

            lock (logSync)
            {
                try
                {
                    if (firstLogDate == DateTime.MinValue)
                        firstLogDate = timestamp.Date;
                    string channel = ChannelFor(kind);
                    StreamWriter writer;
                    if (!fileWriters.TryGetValue(channel, out writer))
                    {
                        string path = FileFor(channel);
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        var stream = new FileStream(path, FileMode.Append,
                            FileAccess.Write, FileShare.ReadWrite);
                        writer = new StreamWriter(stream, new UTF8Encoding(false));
                        writer.AutoFlush = true;
                        fileWriters.Add(channel, writer);
                        if (stream.Length == 0)
                            writer.WriteLine("TimeET,Version,Instrument,Account,State,Event,Direction,ORHigh,ORLow,Open,High,Low,Close,ATR14Ticks,ADX14,BodyTicks,CandleTicks,BodyATR,BodyFraction,BreakTimeET,BreakoutStop,BreakoutATR14Ticks,BreakoutADX14,BreakoutBodyATR,IsRetest,Detail");
                    }
                    writer.WriteLine(line);
                }
                catch (Exception ex)
                {
                    fileFailed = true;
                    CloseDiagnosticFileWriter();
                    if (!fileErrorReported)
                    {
                        fileErrorReported = true;
                        try
                        {
                            string message = "NinjexOpeningRangeDisplacementV1 FILE_LOG_FAILED: "
                                + ex.GetType().Name + " " + ex.Message;
                            Print(message);
                            Log(message, NinjaTrader.Cbi.LogLevel.Warning);
                        }
                        catch { /* Never allow diagnostics to interfere with execution safety. */ }
                    }
                }
            }
        }

        private void CloseDiagnosticFileWriter()
        {
            foreach (StreamWriter writer in fileWriters.Values)
            {
                try { writer.Flush(); writer.Dispose(); }
                catch { }
            }
            fileWriters.Clear();
        }

        private void CloseDiagnosticFile()
        {
            lock (logSync)
                CloseDiagnosticFileWriter();
        }
    }
}