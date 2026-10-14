using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        // One writer and one unique run identifier per strategy instance.
        // The run suffix prevents multiple charts/accounts and NT restarts from
        // truncating, sharing or racing over the same file.
        private readonly object diagnosticFileSync = new object();
        private readonly string diagnosticRunId =
            Guid.NewGuid().ToString("N").Substring(0, 12);

        private StreamWriter diagnosticFileWriter;
        private string diagnosticFilePath = string.Empty;
        private bool diagnosticFileUnavailable;
        private bool diagnosticFileErrorReported;

        private static string SafeDiagnosticFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var safe = new StringBuilder();
            foreach (char c in value)
            {
                if ((c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-' || c == '_' || c == '.')
                {
                    safe.Append(c);
                }
                else
                {
                    safe.Append('_');
                }

                if (safe.Length >= 48)
                    break;
            }

            var result = safe.ToString().Trim('_', '.');
            return result.Length == 0 ? "unknown" : result;
        }

        private string GetDiagnosticFilePath(DateTime eventTime)
        {
            // Historical/Playback use the event date instead of today's
            // machine date, so each replayed trading day gets its own file.
            // NinjaTrader is configured for ET in this strategy setup.
            var date = eventTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var root = Path.Combine(
                Core.Globals.UserDataDir,
                "NinjexLogs",
                "OvernightEdgePortfolio",
                SafeDiagnosticFilePart(StrategyVersion),
                date);

            var instrument = SafeDiagnosticFilePart(
                Instrument == null ? null : Instrument.FullName);
            var account = SafeDiagnosticFilePart(
                Account == null ? null : Account.Name);

            return Path.Combine(
                root,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "OvernightEdge_{0}_{1}_{2}_{3}_{4}.log",
                    SafeDiagnosticFilePart(StrategyVersion),
                    instrument,
                    account,
                    date,
                    diagnosticRunId));
        }

        /// <summary>
        /// Write every strategy diagnostic independently of NinjaScript Output.
        /// Flush each line to the OS immediately so closing/crashing NT does
        /// not discard a buffered session of diagnostics. Logging errors never
        /// propagate into order management or alter strategy decisions.
        /// </summary>
        private void AppendDiagnosticFile(DateTime eventTime, string line)
        {
            lock (diagnosticFileSync)
            {
                if (diagnosticFileUnavailable)
                    return;

                try
                {
                    var desiredPath = GetDiagnosticFilePath(eventTime);
                    if (diagnosticFileWriter == null
                        || !string.Equals(
                            diagnosticFilePath,
                            desiredPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DisposeDiagnosticFileWriterNoThrow();

                        Directory.CreateDirectory(
                            Path.GetDirectoryName(desiredPath));

                        var stream = new FileStream(
                            desiredPath,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.ReadWrite);

                        diagnosticFileWriter = new StreamWriter(
                            stream,
                            new UTF8Encoding(false));
                        diagnosticFileWriter.AutoFlush = true;
                        diagnosticFilePath = desiredPath;

                        diagnosticFileWriter.WriteLine(
                            "# Ninjex Overnight Edge Portfolio diagnostic log");
                        diagnosticFileWriter.WriteLine(
                            "# Version=" + StrategyVersion
                            + " Instance=" + diagnosticRunId
                            + " Instrument=" + (Instrument == null ? "unknown" : Instrument.FullName)
                            + " Account=" + (Account == null ? "unknown" : Account.Name)
                            + " CreatedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                        diagnosticFileWriter.WriteLine(
                            "# Event timestamps are NinjaTrader/chart time (ET by configuration); lifecycle READY timestamps use the machine clock.");
                    }

                    // Keep one physical line per diagnostic event.
                    diagnosticFileWriter.WriteLine(
                        (line ?? string.Empty)
                            .Replace("\r", "\\r")
                            .Replace("\n", "\\n"));
                }
                catch (Exception ex)
                {
                    DisposeDiagnosticFileWriterNoThrow();
                    // Avoid repeated file-system calls on each market tick if
                    // a VPS disk fills or the log directory is inaccessible.
                    diagnosticFileUnavailable = true;
                    if (!diagnosticFileErrorReported)
                    {
                        diagnosticFileErrorReported = true;
                        var warning =
                            "Ninjex Overnight Edge Portfolio FILE LOGGING FAILED: "
                            + ex.GetType().Name + " " + ex.Message
                            + " (no further file writes this strategy instance)";
                        try
                        {
                            Print(warning);
                            Log(warning, NinjaTrader.Cbi.LogLevel.Warning);
                        }
                        catch
                        {
                            // Diagnostic failures must not interrupt protection.
                        }
                    }
                }
            }
        }

        private void DisposeDiagnosticFileWriterNoThrow()
        {
            if (diagnosticFileWriter != null)
            {
                try
                {
                    diagnosticFileWriter.Flush();
                    diagnosticFileWriter.Dispose();
                }
                catch
                {
                    // Closing the writer must never change trading behaviour.
                }
            }

            diagnosticFileWriter = null;
            diagnosticFilePath = string.Empty;
        }

        private void CloseDiagnosticFile()
        {
            lock (diagnosticFileSync)
                DisposeDiagnosticFileWriterNoThrow();
        }
    }
}
