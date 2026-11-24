using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.Strategies
{
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

        public string Folder { get { return folder; } }
        public long Captured { get { return Interlocked.Read(ref captured); } }
        public long Pending { get { return Interlocked.Read(ref pending); } }

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
                        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        log("[NinjexAI] Batch HTTP " + (int)response.StatusCode +
                            " rejected; retained on disk: " + body.Substring(0, Math.Min(body.Length, 180)));
                        return false;
                    }
                    var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var receipt = new System.Web.Script.Serialization.JavaScriptSerializer()
                        .Deserialize<System.Collections.Generic.Dictionary<string, object>>(result);
                    if (receipt == null || !receipt.ContainsKey("processed") ||
                        !receipt.ContainsKey("accepted") || !receipt.ContainsKey("duplicates"))
                        throw new InvalidDataException("Missing batch receipt fields; retaining data.");
                    var processed = Convert.ToInt32(receipt["processed"], CultureInfo.InvariantCulture);
                    var accepted = Convert.ToInt32(receipt["accepted"], CultureInfo.InvariantCulture);
                    var duplicates = Convert.ToInt32(receipt["duplicates"], CultureInfo.InvariantCulture);
                    if (processed != batch.Messages.Length || accepted < 0 || duplicates < 0 ||
                        accepted + duplicates != processed)
                        throw new InvalidDataException("Unexpected batch receipt; retaining data for retry.");
                    lock (sync)
                    {
                        var current = ReadAcknowledged(batch.File);
                        // One worker per NinjaTrader instance; if another publisher
                        // advanced this file, avoid regressing its checkpoint.
                        if (current == batch.Offset)
                        {
                            SaveAcknowledged(batch.File, current + batch.Messages.Length);
                            Interlocked.Add(ref pending, -batch.Messages.Length);
                        }
                    }
                    log("[NinjexAI] Batch HTTP 200 processed=" + processed +
                        " inserted=" + accepted + " duplicates=" + duplicates +
                        " pending=" + Pending);
                    return true;
                }
            }
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
