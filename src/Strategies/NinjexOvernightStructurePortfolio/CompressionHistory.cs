using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Shared research-only, process-local premarket-width history. Every
    /// OvernightStructure strategy instance in the same study group contributes
    /// its completed premarket sessions, including across ES rollovers.
    ///
    /// Causal invariant: the percentile for trading date D is calculated only
    /// from UNIQUE valid trading dates strictly less than D. D is published
    /// after the percentile is resolved. No future dates can be selected.
    ///
    /// No file persistence or order submission. Study group names isolate
    /// unrelated replays; the ledger is intentionally in memory only.
    /// </summary>
    internal static class NinjexOvernightStructureCompressionHistory
    {
        private const int MaximumCalendarLookbackDays = 50;

        internal sealed class Observation
        {
            public int AvailableSessions;
            public int SelectedSessions;
            public int CrossContractSessions;
            public int ConflictingPriorSessions;
            public double ThresholdTicks = double.NaN;
            public bool Compressed;
            public bool Published;
            public bool ConflictingCurrentSession;
            public bool RewindDetected;
        }

        private sealed class PremarketRecord
        {
            public double WidthTicks;
            public string Instrument;
            public bool Conflicted;
        }

        private sealed class Study
        {
            public readonly SortedDictionary<DateTime, PremarketRecord>
                Sessions = new SortedDictionary<DateTime, PremarketRecord>();

            public DateTime LatestRealtimeDate = DateTime.MinValue;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Study> Studies =
            new Dictionary<string, Study>(StringComparer.Ordinal);

        internal static Observation Observe(
            string studyKey,
            DateTime tradingDate,
            string instrument,
            double widthTicks,
            bool isValidCompletedPremarket,
            int lookbackSessions,
            int percentile,
            bool isRealtime)
        {
            if (string.IsNullOrWhiteSpace(studyKey))
                throw new ArgumentException("Study key must not be empty.", "studyKey");
            if (tradingDate == DateTime.MinValue)
                throw new ArgumentException("Trading date must be valid.", "tradingDate");
            if (lookbackSessions < 1 || percentile < 1 || percentile > 99)
                throw new ArgumentOutOfRangeException("lookbackSessions");

            tradingDate = tradingDate.Date;
            instrument = instrument ?? string.Empty;

            var outcome = new Observation();

            lock (Gate)
            {
                Study study;
                if (!Studies.TryGetValue(studyKey, out study))
                {
                    study = new Study();
                    Studies.Add(studyKey, study);
                }

                // If Playback is moved backwards by multiple sessions, do
                // not let the previous run's later samples remain in this
                // group's causal path. Historical preload is never treated as
                // proof of the current Playback clock advancing.
                if (isRealtime
                    && study.LatestRealtimeDate != DateTime.MinValue
                    && tradingDate < study.LatestRealtimeDate.AddDays(-2))
                {
                    var remove = new List<DateTime>();
                    foreach (var date in study.Sessions.Keys)
                    {
                        if (date >= tradingDate)
                            remove.Add(date);
                    }
                    foreach (var date in remove)
                        study.Sessions.Remove(date);

                    study.LatestRealtimeDate = DateTime.MinValue;
                    outcome.RewindDetected = true;
                }

                if (isRealtime && tradingDate > study.LatestRealtimeDate)
                    study.LatestRealtimeDate = tradingDate;

                // Restrict records to recent, distinct and already completed
                // sessions. This also refuses stale history after large gaps.
                var recent = new List<PremarketRecord>();
                var earliest = tradingDate.AddDays(-MaximumCalendarLookbackDays);
                foreach (var item in study.Sessions)
                {
                    if (item.Key >= tradingDate)
                        break;

                    if (item.Key < earliest)
                        continue;

                    if (item.Value.Conflicted)
                    {
                        outcome.ConflictingPriorSessions++;
                        continue;
                    }

                    if (!IsFiniteWidth(item.Value.WidthTicks))
                        continue;

                    recent.Add(item.Value);
                }

                outcome.AvailableSessions = recent.Count;

                if (recent.Count >= lookbackSessions)
                {
                    var widths = new List<double>(lookbackSessions);
                    for (var i = recent.Count - lookbackSessions;
                        i < recent.Count;
                        i++)
                    {
                        var previous = recent[i];
                        widths.Add(previous.WidthTicks);
                        if (!string.Equals(previous.Instrument,
                            instrument, StringComparison.OrdinalIgnoreCase))
                        {
                            outcome.CrossContractSessions++;
                        }
                    }

                    widths.Sort();
                    var rank = (int)Math.Ceiling(
                        lookbackSessions * (percentile / 100.0));
                    var index = Math.Max(0, Math.Min(widths.Count - 1, rank - 1));

                    outcome.SelectedSessions = lookbackSessions;
                    outcome.ThresholdTicks = widths[index];
                    outcome.Compressed = isValidCompletedPremarket
                        && IsFiniteWidth(widthTicks)
                        && widthTicks <= outcome.ThresholdTicks;
                }

                // Today's width is only made available AFTER its own
                // compression classification is finished. A duplicate date
                // is counted once. If two contract streams disagree on that
                // day's width, quarantine the date instead of silently using
                // the wrong front month or importing duplicated data.
                if (isValidCompletedPremarket && IsFiniteWidth(widthTicks))
                {
                    PremarketRecord existing;
                    if (study.Sessions.TryGetValue(tradingDate, out existing))
                    {
                        if (Math.Abs(existing.WidthTicks - widthTicks) >= 0.125)
                            existing.Conflicted = true;

                        outcome.ConflictingCurrentSession = existing.Conflicted;
                        outcome.Published = !existing.Conflicted;

                        if (existing.Conflicted)
                            outcome.Compressed = false;
                    }
                    else
                    {
                        study.Sessions[tradingDate] = new PremarketRecord
                        {
                            WidthTicks = widthTicks,
                            Instrument = instrument
                        };
                        outcome.Published = true;
                    }
                }

                return outcome;
            }
        }

        private static bool IsFiniteWidth(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value)
                && value > 0.0;
        }
    }
}
