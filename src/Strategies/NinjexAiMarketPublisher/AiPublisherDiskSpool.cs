using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.Strategies
{
    // Public parameterless types for .NET Framework's JavaScriptSerializer.
    public sealed class AiBatchReceipt
    {
        public int processed { get; set; }
        public int accepted { get; set; }
        public int duplicates { get; set; }
        public int rejected { get; set; }
        public AiBatchRejection[] rejections { get; set; }
    }

    public sealed class AiBatchRejection
    {
        public int index { get; set; }
        public string reason { get; set; }
    }

    /// <summary>
    /// Disk-first at-least-once publisher. Capture runs on NT's OnBarClose thread;
    /// network upload runs independently. A failed POST retains data on disk.
    /// Backend deduplicates by user/contract/bar-end timestamp.
    /// </summary>
    internal sealed class AiPublisherDiskSpool : IDisposable
    {
        private const int BatchSize = 10;
        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly string folder;
        private readonly string prefix;
        private readonly string endpoint;
        private readonly string secret;
        private readonly Action<string> log;
        private readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        private readonly Task worker;
        private volatile bool stopping;
        private long captured;
        private long pending;
        private long quarantined;
        private int consecutiveFailures;

        public string Folder { get { return folder; } }
        public long Captured { get { return Interlocked.Read(ref captured); } }
        public long Pending { get { return Interlocked.Read(ref pending); } }
        public long Quarantined { get { return Interlocked.Read(ref quarantined); } }

        public AiPublisherDiskSpool(string baseUrl, string publisherKey, string contract, Action<string> logger)
        {
            var uri = new Uri(baseUrl.TrimEnd('/') + "/");
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Publishing requires HTTPS.");
            if (string.IsNullOrWhiteSpace(publisherKey) || publisherKey.Length < 32)
                throw new ArgumentException("Publisher key is missing or too short.");

            endpoint = uri.ToString().TrimEnd('/') + "/api/trading/ai-market/observe-batch";
            secret = publisherKey;
            log = logger;
            // Keep local and deployed destinations in separate persistent spools.
            var host = Clean(uri.Host + "_" + uri.Port);
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ninjex", "AiMarket", "Spool", host);
            Directory.CreateDirectory(folder);
            prefix = Clean(contract) + "_" + Guid.NewGuid().ToString("N") + "_";
            // Existing unacknowledged files survive a NinjaTrader restart.
            foreach (var file in Directory.GetFiles(folder, "*.jsonl"))
            {
                var total = File.ReadLines(file).LongCount();
                var acknowledged = Math.Min(total, ReadAcknowledged(file));
                Interlocked.Add(ref pending, total - acknowledged);
            }
            worker = Task.Factory.StartNew(Work, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            log("[NinjexAI] Disk spool ready: " + folder + "; pending=" + Pending);
        }

        public void Capture(string json, DateTime utcBarEnd)
        {
            if (stopping) throw new ObjectDisposedException("AiPublisherDiskSpool");
            if (json.IndexOf('\n') >= 0 || json.IndexOf('\r') >= 0)
                throw new ArgumentException("Spool messages must be single-line JSON.");
            var path = Path.Combine(folder, prefix + utcBarEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");
            lock (sync)
            {
                // Close and flush before marking the record available to the worker.
                File.AppendAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
                Interlocked.Increment(ref captured);
                Interlocked.Increment(ref pending);
            }
            wake.Set();
        }

        // These are intentional, timestamped data-quality gaps. They are not
        // claimed as captured or stored market classifications.
        public void RecordSkip(DateTime utcBarEnd, string reason)
        {
            var skipFolder = Path.Combine(folder, "Skipped");
            Directory.CreateDirectory(skipFolder);
            var record = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Serialize(new Dictionary<string, object>
                {
                    { "barEndUtc", utcBarEnd.ToString("o", CultureInfo.InvariantCulture) },
                    { "reason", reason }
                });
            var file = Path.Combine(skipFolder,
                prefix + utcBarEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".skipped.jsonl");
            lock (sync)
                File.AppendAllText(file, record + Environment.NewLine, new UTF8Encoding(false));
        }

        private sealed class Batch
        {
            public string File;
            public long Offset;
            public string[] Messages;
        }

        private Batch Next()
        {
            lock (sync)
            {
                foreach (var file in Directory.GetFiles(folder, "*.jsonl").OrderBy(x => x, StringComparer.Ordinal))
                {
                    var count = ReadAcknowledged(file);
                    var lines = File.ReadLines(file).Skip((int)Math.Min(count, int.MaxValue))
                        .Take(BatchSize).ToArray();
                    if (lines.Length > 0)
                        return new Batch { File = file, Offset = count, Messages = lines };
                }
            }
            return null;
        }

        private static long ReadAcknowledged(string file)
        {
            long value;
            var path = file + ".ack";
            return File.Exists(path) &&
                long.TryParse(File.ReadAllText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                value >= 0 ? value : 0;
        }

        private static void SaveAcknowledged(string file, long count)
        {
            var path = file + ".ack";
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, count.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private bool Upload(Batch batch)
        {
            var payload = "{\"observations\":[" + string.Join(",", batch.Messages) + "]}";
            using (var req = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                req.Headers.TryAddWithoutValidation("X-Ninjex-AI-Key", secret);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using (var response = client.SendAsync(req).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var failures = Interlocked.Increment(ref consecutiveFailures);
                        if (failures == 1 || failures == 5 || failures % 30 == 0)
                        {
                            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            log("[NinjexAI] Batch HTTP " + (int)response.StatusCode +
                                " refused; original spool retained; failure #" + failures +
                                ": " + body.Substring(0, Math.Min(body.Length, 180)));
                        }
                        return false;
                    }
                    var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    ProcessReceipt(batch, result);
                    Interlocked.Exchange(ref consecutiveFailures, 0);
                    return true;
                }
            }
        }

        private void ProcessReceipt(Batch batch, string responseJson)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var receipt = serializer.Deserialize<AiBatchReceipt>(responseJson);
            if (receipt == null || receipt.processed != batch.Messages.Length ||
                receipt.accepted < 0 || receipt.duplicates < 0 || receipt.rejected < 0 ||
                receipt.accepted + receipt.duplicates + receipt.rejected != receipt.processed)
                throw new InvalidDataException("Unexpected batch receipt; retaining raw data.");

            var bad = receipt.rejections ?? new AiBatchRejection[0];
            if (bad.Length != receipt.rejected)
                throw new InvalidDataException("Inconsistent rejection details; retaining data.");
            var uniqueIndexes = new HashSet<int>();
            foreach (var rejection in bad)
                if (rejection == null || rejection.index < 0 || rejection.index >= batch.Messages.Length ||
                    !uniqueIndexes.Add(rejection.index) || string.IsNullOrWhiteSpace(rejection.reason))
                    throw new InvalidDataException("Invalid rejection index/reason; retaining raw data.");

            lock (sync)
            {
                var current = ReadAcknowledged(batch.File);
                if (current != batch.Offset) return; // Another worker checkpointed it.
                if (bad.Length > 0)
                {
                    // Never pretend a rejected observation was stored in SQL.
                    // Durable quarantine write ALWAYS precedes ACK advancement.
                    var rejectedFolder = Path.Combine(folder, "Quarantine");
                    Directory.CreateDirectory(rejectedFolder);
                    var quarantinePath = Path.Combine(rejectedFolder,
                        Path.GetFileName(batch.File) + ".rejected.jsonl");
                    var records = bad.Select(item => serializer.Serialize(new Dictionary<string, object>
                    {
                        { "sourceFile", Path.GetFileName(batch.File) },
                        { "sourceLineIndex", batch.Offset + item.index },
                        { "reason", item.reason },
                        { "originalJson", batch.Messages[item.index] }
                    }));
                    File.AppendAllText(quarantinePath,
                        string.Join(Environment.NewLine, records) + Environment.NewLine,
                        new UTF8Encoding(false));
                }
                SaveAcknowledged(batch.File, current + batch.Messages.Length);
                Interlocked.Add(ref pending, -batch.Messages.Length);
                Interlocked.Add(ref quarantined, receipt.rejected);
            }
            log("[NinjexAI] Batch HTTP 200 processed=" + receipt.processed +
                " inserted=" + receipt.accepted + " duplicates=" + receipt.duplicates +
                " REJECTED_QUARANTINED=" + receipt.rejected +
                " pending=" + Pending);
        }

        private void Work()
        {
            while (!stopping)
            {
                try
                {
                    var batch = Next();
                    if (batch == null) { wake.WaitOne(700); continue; }
                    if (!Upload(batch)) wake.WaitOne(2000);
                }
                catch (Exception e)
                {
                    log("[NinjexAI] Upload postponed; spool preserved: " + e.GetType().Name +
                        " " + e.Message.Substring(0, Math.Min(e.Message.Length, 140)));
                    wake.WaitOne(2000);
                }
            }
        }

        private static string Clean(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(ch => invalid.Contains(ch) ||
                ch == ' ' || ch == '/' || ch == '\\' || ch == ':' ? '_' : ch).ToArray());
        }

        public void Dispose()
        {
            stopping = true;
            wake.Set();
            // Do not block NinjaTrader's UI on a slow network. Pending records
            // remain on disk and are recovered when the next instance starts.
        }
    }
}
