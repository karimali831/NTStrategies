using System;
using System.IO;
using System.Linq;
using NinjaTrader.NinjaScript.Strategies;

static class Program
{
    static void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); }
    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "ninjex-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
            string dir;
            using (var sink = new AiHistoricalCsvSink(root, "ES 12-26", "test",
                zone, new DateTime(2026, 10, 1), new DateTime(2026, 11, 1)))
            {
                dir = sink.Folder;
                sink.Append(5, new DateTime(2026, 10, 1, 9, 35, 0), 5000, 5002, 4998, 5001, 100);
                sink.Append(5, new DateTime(2026, 10, 1, 9, 40, 0), 5001, 5003, 4999, 5002, 101);
                sink.Append(1, new DateTime(2026, 10, 1, 9, 36, 0), 5000, 5001, 4999, 5000, 80);
                sink.Append(15, new DateTime(2026, 10, 1, 9, 45, 0), 5000, 5003, 4998, 5001, 110);
                sink.Append(60, new DateTime(2026, 10, 1, 10, 0, 0), 5000, 5003, 4998, 5001, 120);
                sink.Append(5, new DateTime(2026, 10, 1, 9, 40, 0), 5001, 5003, 4999, 5002, 101);
                sink.Append(5, new DateTime(2026, 10, 1, 9, 45, 0), 5001, 4990, 4999, 5002, 101);
                Check(sink.Valid == 5 && sink.Issues == 2, "Invalid/duplicate row handling");
                // DST fold: first 1am EDT, second 1am EST, without inventing timestamps.
                sink.Append(1, new DateTime(2026, 11, 1, 0, 59, 0), 5000, 5001, 4999, 5000, 5);
                sink.Append(1, new DateTime(2026, 11, 1, 1, 0, 0), 5000, 5001, 4999, 5000, 5);
                sink.Append(1, new DateTime(2026, 11, 1, 1, 59, 0), 5000, 5001, 4999, 5000, 5);
                sink.Append(1, new DateTime(2026, 11, 1, 1, 0, 0), 5000, 5001, 4999, 5000, 5);
            }
            var five = File.ReadAllLines(Path.Combine(dir, "bars_5m.csv"));
            Check(five.Length == 3 && five[1].Contains("2026-10-01T13:35:00"),
                "ET->UTC conversion and 5m row counts");
            var minute = File.ReadAllLines(Path.Combine(dir, "bars_1m.csv"));
            Check(minute.Any(x=>x.Contains("2026-11-01T05:00:00")) &&
                  minute.Any(x=>x.Contains("2026-11-01T06:00:00")),
                  "Fall-back repeated ET hour uses distinct UTC timestamps");
            Check(File.ReadAllText(Path.Combine(dir, "manifest.json")).Contains("ninjex-ohlcv-v1"),
                "Manifest preserves schema version");
            Check(File.ReadAllLines(Path.Combine(dir, "quality_issues.csv")).Length >= 3,
                "Quality report preserved");
            Console.WriteLine("Historical exporter checks passed");
        }
        finally { Directory.Delete(root, true); }
    }
}
