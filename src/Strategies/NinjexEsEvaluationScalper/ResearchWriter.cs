using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
namespace NinjaTrader.NinjaScript.Strategies
{
    internal sealed class EvaluationResearchWriter : IDisposable
    {
        private readonly Dictionary<string, StreamWriter> writers = new Dictionary<string, StreamWriter>();
        private DateTime lastFlush;
        internal string DirectoryPath { get; private set; }
        internal EvaluationResearchWriter(string root, string instrument, bool raw)
        {
            DirectoryPath = Path.Combine(root, "ES_Evaluation_" + instrument.Replace(' ', '_') + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(DirectoryPath);
            Open("manifest", "Key,Value", false);
            Open("sessions", "Time,State,Event,RealizedNet", false);
            Open("quality", "Time,State,Event,Detail", false);
            Open("candidates", "Time,State,Id,Model,Direction,LevelName,Level,SignalPrice,StructuralStop,StopTicks,Quantity,Vwap,AtrTicks,Decision", false);
            Open("shadows", "SignalTime,EndTime,CandidateId,Model,StopTicks,RewardRisk,Outcome,EndMoveTicks,MfeTicks,MaeTicks,Assumptions", false);
            Open("context", "Time,Vwap,AtrTicks,LevelsAsOf", false);
            Open("orders", "Time,State,OrderId,Name,OrderState,Quantity,Filled,AverageFill,Limit,Stop,Error,Comment", false);
            Open("fills", "Time,State,ExecutionId,OrderId,Name,Action,Price,Quantity,RealizedNet,OpenQuantity", false);
            Open("trades", "EntryTime,ExitTime,Signal,Model,Direction,AverageEntry,LastExit,Gross,Fees,Net,MfeUSD,MaeUSD", false);
            Open("equity", "Time,State,RealizedNet,UnrealizedGross,LiquidationNet,OpenQuantity,Signal", true);
            if (raw) Open("ticks", "Time,Price,Volume,OpenQuantity", true);
        }
        private void Open(string name, string header, bool compressed)
        {
            Stream stream = File.Create(Path.Combine(DirectoryPath, name + (compressed ? ".csv.gz" : ".csv")));
            if (compressed) stream = new GZipStream(stream, CompressionMode.Compress);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
            writers.Add(name, writer); writer.WriteLine(header); writer.Flush();
        }
        internal void Write(string table, params object[] values)
        {
            writers[table].WriteLine(string.Join(",", values.Select(Format)));
        }
        private static string Format(object value)
        {
            string s = value is DateTime ? ((DateTime)value).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        internal void Raw(DateTime time, double price, double volume, int quantity, bool inWindow)
        {
            if (writers.ContainsKey("ticks") && (inWindow || quantity > 0)) Write("ticks", time, price, volume, quantity);
        }
        internal void Checkpoint(DateTime time)
        {
            if ((time - lastFlush).TotalSeconds < 30) return;
            foreach (var writer in writers.Values) writer.Flush();
            lastFlush = time;
        }
        public void Dispose() { foreach (var writer in writers.Values) writer.Dispose(); writers.Clear(); }
    }
}
