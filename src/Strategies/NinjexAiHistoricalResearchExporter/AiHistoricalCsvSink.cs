using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace NinjaTrader.NinjaScript.Strategies
{
    // One canonical row per completed candle, not overlapping 100-bar windows.
    // This class does not invent trade outcomes, trades or machine learning labels.
    internal sealed class AiHistoricalCsvSink : IDisposable
    {
        private readonly string contract;
        private readonly string schema = "ninjex-ohlcv-v1";
        private readonly string datasetTag;
        private readonly TimeZoneInfo easternZone;
        private readonly DateTime fromDate;
        private readonly DateTime throughDate;
        private readonly Dictionary<int, StreamWriter> output = new Dictionary<int, StreamWriter>();
        private readonly Dictionary<int, DateTime> previousUtc = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, long> counts = new Dictionary<int, long>();
        private readonly StreamWriter quality;
        private bool closed;
        private long issues;

        public string Folder { get; private set; }
        public long Issues { get { return issues; } }
        public long Valid { get { return counts.Values.Sum(); } }

        public AiHistoricalCsvSink(string baseFolder, string futuresContract,
            string tag, TimeZoneInfo zone, DateTime start, DateTime end)
        {
            if (string.IsNullOrWhiteSpace(futuresContract)) throw new ArgumentException("Contract required");
            if (end.Date < start.Date) throw new ArgumentException("Invalid date range");
            contract = futuresContract;
            datasetTag = tag ?? "";
            easternZone = zone ?? throw new ArgumentNullException("zone");
            fromDate = start.Date;
            throughDate = end.Date;
            var safe = string.Concat(futuresContract.Select(c =>
                char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
            Folder = Path.Combine(baseFolder, safe + "_" +
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) +
                "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            foreach (var minutes in new[] { 1, 5, 15, 60 })
            {
                var writer = new StreamWriter(Path.Combine(Folder, "bars_" + minutes + "m.csv"),
                    false, new UTF8Encoding(false), 131072);
                writer.WriteLine("schemaVersion,contract,frameMinutes,endUtc,endEastern,utcOffsetMinutes,open,high,low,close,volume,flags");
                output.Add(minutes, writer);
                counts.Add(minutes, 0);
            }
            quality = new StreamWriter(Path.Combine(Folder, "quality_issues.csv"),
                false, new UTF8Encoding(false), 32768);
            quality.WriteLine("frameMinutes,endEastern,issue,details");
        }

        public void Append(int minutes, DateTime easternEnd, double open,
            double high, double low, double close, long volume)
        {
            if (closed) throw new ObjectDisposedException("AiHistoricalCsvSink");
            if (!output.ContainsKey(minutes)) throw new ArgumentException("Unsupported timeframe");
            var et = DateTime.SpecifyKind(easternEnd, DateTimeKind.Unspecified);
            if (et.Date < fromDate || et.Date > throughDate) return;

            if (double.IsNaN(open) || double.IsInfinity(open) ||
                double.IsNaN(high) || double.IsInfinity(high) ||
                double.IsNaN(low) || double.IsInfinity(low) ||
                double.IsNaN(close) || double.IsInfinity(close) ||
                low <= 0 || high < Math.Max(open, close) ||
                low > Math.Min(open, close) || volume < 0)
            {
                Issue(minutes, et, "INVALID_OHLCV", "Bar omitted");
                return;
            }
            if (easternZone.IsInvalidTime(et))
            {
                Issue(minutes, et, "INVALID_ET_TIME", "Spring DST missing hour");
                return;
            }

            var ambiguous = easternZone.IsAmbiguousTime(et);
            var offsets = ambiguous
                ? easternZone.GetAmbiguousTimeOffsets(et)
                : new[] { easternZone.GetUtcOffset(et) };
            var candidates = offsets.Select(offset => new DateTimeOffset(et, offset))
                .OrderBy(x => x.UtcDateTime).ToArray();
            DateTime previous;
            var hasPrevious = previousUtc.TryGetValue(minutes, out previous);
            var choice = candidates.FirstOrDefault(x =>
                !hasPrevious || x.UtcDateTime > previous);
            if (choice == default(DateTimeOffset))
            {
                Issue(minutes, et, "NON_MONOTONIC", "No increasing UTC offset candidate; bar omitted");
                return;
            }

            if (ambiguous && !hasPrevious)
                Issue(minutes, et, "DST_OFFSET_ASSUMED", "First bar is within ambiguous ET hour; verify offset");
            var utc = choice.UtcDateTime;
            var flags = ambiguous ? "DST_OFFSET_RESOLVED" : "OK";
            if (hasPrevious && (utc - previous).TotalMinutes > minutes * 1.01)
            {
                flags += "|TIME_GAP";
                Issue(minutes, et, "TIME_GAP", "previousUtc=" +
                    previous.ToString("O", CultureInfo.InvariantCulture) +
                    " currentUtc=" + utc.ToString("O", CultureInfo.InvariantCulture) +
                    "; compare with exchange trading calendar");
            }
            previousUtc[minutes] = utc;
            var row = new[]
            {
                schema, contract,
                minutes.ToString(CultureInfo.InvariantCulture),
                utc.ToString("O", CultureInfo.InvariantCulture),
                et.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture),
                ((int)choice.Offset.TotalMinutes).ToString(CultureInfo.InvariantCulture),
                open.ToString("R", CultureInfo.InvariantCulture),
                high.ToString("R", CultureInfo.InvariantCulture),
                low.ToString("R", CultureInfo.InvariantCulture),
                close.ToString("R", CultureInfo.InvariantCulture),
                volume.ToString(CultureInfo.InvariantCulture), flags
            };
            output[minutes].WriteLine(string.Join(",", row.Select(Escape)));
            counts[minutes]++;
            if (counts[minutes] % 1000 == 0) output[minutes].Flush();
        }

        private void Issue(int minutes, DateTime et, string reason, string details)
        {
            issues++;
            quality.WriteLine(minutes + "," +
                Escape(et.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)) +
                "," + Escape(reason) + "," + Escape(details));
            if (issues % 100 == 0) quality.Flush();
        }

        private static string Escape(string value)
        {
            var x = (value ?? "").Replace("\"", "\"\"");
            return x.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + x + "\"" : x;
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            foreach (var writer in output.Values) writer.Dispose();
            quality.Dispose();
            var manifest = new Dictionary<string, object>
            {
                { "schemaVersion", schema },
                { "contract", contract },
                { "datasetTag", datasetTag },
                { "source", "NINJATRADER_HISTORICAL_OHLCV" },
                { "timezone", easternZone.Id },
                { "fromEastern", fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "throughEastern", throughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                { "valid", Valid },
                { "qualityIssues", issues },
                { "frameCounts", counts.ToDictionary(x => x.Key + "m", x => x.Value) },
                { "warning", "Historical OHLCV, not tick-level quotes, trade labels, or a trained ML model." }
            };
            File.WriteAllText(Path.Combine(Folder, "manifest.json"),
                new JavaScriptSerializer().Serialize(manifest), new UTF8Encoding(false));
        }
    }
}
