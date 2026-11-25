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

            // A poison observation must be written to Quarantine before the
            // spool ACK advances; valid records must not be blocked behind it.
            var firstFile = docs.OrderBy(x => x).First();
            var firstTwo = File.ReadLines(firstFile).Take(2).ToArray();
            first.Instance.AcknowledgeReceipt(firstFile, 0, firstTwo,
                "{\"processed\":2,\"accepted\":1,\"duplicates\":0,\"rejected\":1," +
                "\"rejections\":[{\"index\":1,\"reason\":\"15m STALE test record\"}]}");
            Check(first.Instance.Pending == 598, "Mixed batch ACK advances 2 entries");
            Check(File.ReadAllText(firstFile + ".ack").Trim() == "2", "ACK persisted");
            var quarantined = Directory.GetFiles(Path.Combine(folder, "Quarantine"), "*.rejected.jsonl");
            Check(quarantined.Length == 1, "Invalid record quarantined");
            var quarantinedText = File.ReadAllText(quarantined[0]);
            Check(quarantinedText.Contains("15m STALE") &&
                  quarantinedText.Contains(firstTwo[1]) == false &&
                  quarantinedText.Contains("originalJson"), "Quarantine holds reason + raw JSON");
            Check(File.ReadLines(firstFile).Count() > 2, "Original spool remains intact");
        }
        using (var recovered = new SpoolLifetime(url, secret))
            Check(recovered.Instance.Pending >= 598, "Unacknowledged observations survive restart");
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
