using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite
{
    internal static partial class SafeTradeSuiteRuntime
    {
        private static readonly object Gate = new object();

        private static readonly HashSet<string> SavedInstruments =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly string SavedInstrumentsFilePath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8",
                "templates",
                "SafeTradeSuite",
                "saved-instruments.txt");

        private static bool _savedInstrumentsLoaded;

        public static void RememberInstrument(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return;

            EnsureSavedInstrumentsLoaded();

            lock (Gate)
            {
                if (SavedInstruments.Add(n))
                    SaveSavedInstrumentsToDisk();
            }
        }

        public static void ForgetInstrument(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return;

            EnsureSavedInstrumentsLoaded();

            lock (Gate)
            {
                if (SavedInstruments.Remove(n))
                    SaveSavedInstrumentsToDisk();
            }
        }

        public static List<string> GetSavedInstrumentsSnapshot()
        {
            EnsureSavedInstrumentsLoaded();

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

        private static void EnsureSavedInstrumentsLoaded()
        {
            lock (Gate)
            {
                if (_savedInstrumentsLoaded)
                    return;

                _savedInstrumentsLoaded = true;

                try
                {
                    if (!File.Exists(SavedInstrumentsFilePath))
                        return;

                    foreach (var line in File.ReadAllLines(SavedInstrumentsFilePath))
                    {
                        var n = NormalizeInstrumentName(line);
                        if (!string.IsNullOrWhiteSpace(n))
                            SavedInstruments.Add(n);
                    }
                }
                catch (Exception ex)
                {
                    PrintLog("EnsureSavedInstrumentsLoaded failed: " + ex);
                }
            }
        }

        private static void SaveSavedInstrumentsToDisk()
        {
            lock (Gate)
            {
                try
                {
                    var dir = Path.GetDirectoryName(SavedInstrumentsFilePath);
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllLines(
                        SavedInstrumentsFilePath,
                        SavedInstruments
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(NormalizeInstrumentName)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(x => x)
                            .ToArray());
                }
                catch (Exception ex)
                {
                    PrintLog("SaveSavedInstrumentsToDisk failed: " + ex);
                }
            }
        }
    }
}