using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    internal static class SafeTradeSuiteRuntime
    {
        private static readonly object Gate = new object();
        private static readonly object CopierGate = new object();

        private static readonly HashSet<string> SavedInstruments =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        public static void RememberInstrument(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return;

            lock (Gate)
            {
                SavedInstruments.Add(n);
            }
        }

        public static void ForgetInstrument(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return;

            lock (Gate)
            {
                SavedInstruments.Remove(n);
            }
        }

        public static List<string> GetSavedInstrumentsSnapshot()
        {
            lock (Gate)
            {
                return SavedInstruments
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeInstrumentName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();
            }
        }

        private static string NormalizeInstrumentName(string instrumentName)
        {
            return (instrumentName ?? "").Trim().ToUpperInvariant();
        }

        public static void PrintLog(string msg)
        {
            Code.Output.Process(
                $"[SafeTradeSuite DEBUG] {DateTime.Now:HH:mm:ss.fff} {msg}",
                PrintTo.OutputTab1);
        }
    }
}