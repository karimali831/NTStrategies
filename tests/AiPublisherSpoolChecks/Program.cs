using System;
using System.IO;
using System.Linq;
using NinjaTrader.NinjaScript.Strategies;

// A portable compile + disk-durability check. NinjaTrader itself still
// requires a real Windows NT8 compile and Playback smoke test.
static class Program
{
    static void Main()
    {
        var port = new Random().Next(58000, 61000);
        var url = "https://localhost:" + port;
        var secret = new string('K', 64);
        string folder;
        using (var first = new SpoolLifetime(url, secret))
        {
            folder = first.Instance.Folder;
            for (var i = 0; i < 600; i++)
                first.Instance.Capture("{\"instrument\":\"ES\",\"n\":" + i + "}",
                    new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc).AddMinutes(i * 5));
            Check(first.Instance.Captured == 600, "All high-speed simulated bars captured");
            Check(first.Instance.Pending >= 600, "Pending until server acknowledges");
            var docs = Directory.GetFiles(folder, "*.jsonl");
            Check(docs.Length == 3, "Day-partitioned spool files");
            Check(docs.Sum(doc => File.ReadLines(doc).Count()) == 600, "All 600 records durable");
            Check(docs.All(doc => !File.ReadAllText(doc).Contains(secret)), "Secret never stored in spool");
        }
        using (var recovered = new SpoolLifetime(url, secret))
            Check(recovered.Instance.Pending >= 600, "Unacknowledged observations survive restart");
        Console.WriteLine("AI publisher spool compile/durability checks passed.");
    }
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }
    sealed class SpoolLifetime : IDisposable
    {
        public readonly AiPublisherDiskSpool Instance;
        public SpoolLifetime(string url, string secret) =>
            Instance = new AiPublisherDiskSpool(url, secret, "ES 12-26", _ => { });
        public void Dispose() => Instance.Dispose();
    }
}
