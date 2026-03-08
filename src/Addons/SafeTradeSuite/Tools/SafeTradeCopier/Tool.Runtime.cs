using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    internal static class SafeTradeSuiteRuntime
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> ChartInstruments =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly object CopierGate = new object();
        private static SafeTradeCopierTool _copier;

        public static SafeTradeCopierTool GetOrCreateCopier()
        {
            lock (CopierGate)
            {
                if (_copier == null)
                {
                    _copier = new SafeTradeCopierTool();
                    PrintLog("Created SafeTradeCopierTool singleton.");
                }

                return _copier;
            }
        }

        public static void DisposeCopierIfExists()
        {
            lock (CopierGate)
            {
                if (_copier == null)
                    return;

                try
                {
                    _copier.Dispose();
                }
                catch (Exception ex)
                {
                    PrintLog("DisposeCopierIfExists failed: " + ex);
                }
                finally
                {
                    _copier = null;
                    PrintLog("Disposed SafeTradeCopierTool singleton.");
                }
            }
        }

        public static void RegisterChartInstrument(string instrumentName)
        {
            var n = (instrumentName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(n))
                return;

            lock (Gate)
            {
                if (ChartInstruments.Add(n))
                    PrintLog("Chart instrument registered: " + n);
            }
        }

        public static void RemoveChartInstrument(string instrumentName)
        {
            var n = (instrumentName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(n))
                return;

            lock (Gate)
            {
                if (ChartInstruments.Remove(n))
                    PrintLog("Chart instrument removed: " + n);
            }
        }

        public static List<string> GetChartInstrumentsSnapshot()
        {
            lock (Gate)
            {
                return ChartInstruments
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Where(x => !string.Equals(x, "Chart", StringComparison.OrdinalIgnoreCase))
                    .Where(x => Instrument.GetInstrument(x) != null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }
        }

        public static void PrintLog(string msg)
        {
            Code.Output.Process(
                $"[SafeTradeSuite DEBUG] {DateTime.Now:HH:mm:ss.fff} {msg}",
                PrintTo.OutputTab1);
        }
    }
}