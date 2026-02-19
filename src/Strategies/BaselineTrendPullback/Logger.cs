#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public sealed class BaselineLogger : Strategy, IDisposable
    {
        private readonly string _strategyName;
        private readonly string _instrument;
        private readonly bool _logToFile;
        private readonly string _filePath;
        private StreamWriter _writer;

        public BaselineLogger(string strategyName, string instrument, bool logToFile, string fileNamePrefix)
        {
            _strategyName = strategyName ?? "Strategy";
            _instrument = instrument ?? "";
            _logToFile = logToFile;

            if (_logToFile)
            {
                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var safeInstr = MakeSafeFilePart(_instrument);
                var safeName = MakeSafeFilePart(fileNamePrefix ?? _strategyName);

                // Writes to: Documents\NinjaTrader 8\ (UserDataDir)
                string dir = NinjaTrader.Core.Globals.UserDataDir;
                _filePath = Path.Combine(dir, $"{safeName}_{safeInstr}_{ts}.csv");

                _writer = new StreamWriter(_filePath, append: true, encoding: Encoding.UTF8);
                _writer.AutoFlush = true;

                // Header
                _writer.WriteLine(
                    "code,t,bar,bias,close,ef,es,slopeF,slopeS,sep,distEF,adx,atr,atrMed,wickPct,rngT,structOk,regimeOk,candleOk,reason,meta");
            }
        }

        public void Info(string code, Dictionary<string, object> fields)
        {
            // Prints as key=value map (good for NT Output window searching)
            PrintLine(code, fields);

            if (_logToFile)
                WriteCsv(code, fields, meta: "info");
        }

        public void Bar(string code, Dictionary<string, object> fields)
        {
            PrintLine(code, fields);

            if (_logToFile)
                WriteCsv(code, fields, meta: "bar");
        }

        private void PrintLine(string code, Dictionary<string, object> fields)
        {
            var sb = new StringBuilder();
            sb.Append("[DIAG] ");
            sb.Append(_strategyName);
            sb.Append(" ");
            sb.Append(code);
            sb.Append(" ");
            sb.Append(_instrument);
            sb.Append(" | ");

            bool first = true;
            foreach (var kv in fields)
            {
                if (!first) sb.Append(" ");
                first = false;
                sb.Append(kv.Key);
                sb.Append("=");
                sb.Append(kv.Value);
            }

            Print(sb.ToString());
        }

        private void WriteCsv(string code, Dictionary<string, object> f, string meta)
        {
            if (_writer == null)
                return;

            // Expect the standard BarFeatures keys; if missing, leave blank
            string Get(string k) => f.TryGetValue(k, out var v) ? EscapeCsv(v) : "";

            // For non-bar events, we still write a row but with empties.
            var line = string.Join(",",
                EscapeCsv(code),
                Get("t"),
                Get("bar"),
                Get("bias"),
                Get("close"),
                Get("ef"),
                Get("es"),
                Get("slopeF"),
                Get("slopeS"),
                Get("sep"),
                Get("distEF"),
                Get("adx"),
                Get("atr"),
                Get("atrMed"),
                Get("wickPct"),
                Get("rngT"),
                Get("structOk"),
                Get("regimeOk"),
                Get("candleOk"),
                Get("reason"),
                EscapeCsv(meta)
            );

            _writer.WriteLine(line);
        }

        private static string EscapeCsv(object o)
        {
            if (o == null) return "";
            var s = Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string MakeSafeFilePart(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "NA";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Trim();
        }

        public void Dispose()
        {
            try
            {
                _writer?.Flush();
            }
            catch
            {
            }

            try
            {
                _writer?.Dispose();
            }
            catch
            {
            }

            _writer = null;
        }
    }
}