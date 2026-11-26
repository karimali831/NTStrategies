using System;
using System.IO;
using System.Linq;
using NinjaTrader.NinjaScript.Strategies;

static class Program
{
    static void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); }
    static void TerminalRecoveryCheck(string root, TimeZoneInfo zone)
    {
        string exported;
        var terminal = new DateTime(2023, 12, 8, 17, 0, 0);
        using (var sink = new AiHistoricalCsvSink(root, "ES 12-23",
            "source-bar-recovery-test", zone,
            new DateTime(2023, 12, 8), new DateTime(2023, 12, 8)))
        {
            exported = sink.Folder;
            // History finishes just before Friday's 17:00 ET close.
            // The terminal candles are present in BarsArray but not emitted
            // through OnBarUpdate. Recover exactly those original source bars.
            sink.Append(1, new DateTime(2023, 12, 8, 16, 59, 0),
                4607.75, 4607.75, 4607.25, 4607.25, 11602);
            sink.Append(5, new DateTime(2023, 12, 8, 16, 55, 0),
                4607.50, 4608, 4607.25, 4607.75, 100);
            sink.Append(15, new DateTime(2023, 12, 8, 16, 45, 0),
                4607.50, 4608, 4607.25, 4607.75, 100);
            sink.Append(60, new DateTime(2023, 12, 8, 16, 0, 0),
                4607.50, 4608.50, 4605, 4607.75, 100);

            var strategy = new NinjexAiHistoricalResearchExporter {
                EnableExport = true,
                RecoverTerminalSessionClose = true,
                ThroughEasternDate = "2023-12-08"
            };
            strategy.SetBarsForTests(new [] {
                new FakeBars { Data = new[] {
                    new FakeBar(terminal,4607.75,4608.00,4607.25,4607.25,33482) } },
                new FakeBars { Data = new[] {
                    new FakeBar(terminal,4607.50,4607.75,4607.25,4607.25,12208) } },
                new FakeBars { Data = new[] {
                    new FakeBar(terminal,4607.75,4608.00,4607.25,4607.25,35305) } },
                new FakeBars { Data = new[] {
                    new FakeBar(terminal,4608.25,4608.50,4605.00,4607.25,78892) } }
            });
            var privateSink = typeof(NinjexAiHistoricalResearchExporter).GetField(
                "sink", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            privateSink.SetValue(strategy, sink);
            var recover = typeof(NinjexAiHistoricalResearchExporter).GetMethod(
                "RecoverSourceTerminalCloses", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            recover.Invoke(strategy, null);
            Check(sink.Valid == 8, "Four source terminal bars appended");
            recover.Invoke(strategy, null);
            Check(sink.Valid == 8, "Source terminal recovery is idempotent");

            // Mismatching through date must never create a terminal candle.
            strategy.ThroughEasternDate = "2023-12-09";
            recover.Invoke(strategy, null);
            Check(sink.Valid == 8, "Do not extrapolate beyond source data");
        }
        foreach (var period in new[] { 1, 5, 15, 60 })
        {
            var text = File.ReadAllText(Path.Combine(exported, "bars_" + period + "m.csv"));
            Check(text.Contains("TERMINAL_FROM_LOADED_BARS"), period + "m terminal provenance saved");
            Check(text.Split("2023-12-08T22:00:00").Length == 2, period + "m terminal UTC recorded once");
        }
        var manifest = File.ReadAllText(Path.Combine(exported, "manifest.json"));
        Check(manifest.Contains("terminalRecoveredCounts"), "Recovery metadata in manifest");
    }

    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "ninjex-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
            TerminalRecoveryCheck(root, zone);
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
