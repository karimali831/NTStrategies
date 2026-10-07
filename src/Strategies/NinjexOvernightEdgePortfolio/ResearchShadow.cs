using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private sealed class NinjexOvernightEdgeResearchShadow
        {
            public string CandidateId;
            public NinjexOvernightEdgeResearchRow Snapshot;
            public PendingDirection Direction;
            public DateTime SignalTime;
            public bool Entered;
            public DateTime EntryTime = Core.Globals.MinDate;
            public double EntryPrice = double.NaN;
            public DateTime MaxHoldExitTime = Core.Globals.MinDate;
            public double MfeDollars;
            public double MaeDollars;
        }

        private sealed class NinjexOvernightEdgeResearchTick
        {
            public DateTime Time;
            public double Price;
        }

        private readonly HashSet<string> registeredResearchShadowCandidates =
            new HashSet<string>(StringComparer.Ordinal);

        private readonly List<NinjexOvernightEdgeResearchShadow> activeResearchShadows =
            new List<NinjexOvernightEdgeResearchShadow>();

        private int lastObservedResearchSnapshotCount = -1;
        private string researchShadowOutputPath = string.Empty;
        private bool researchShadowOutputFaulted;

        /// <summary>
        /// Research-only shadow execution. This callback observes Last market-data
        /// updates during Playback/live data. It never submits, changes, cancels,
        /// or adopts NinjaTrader orders and never modifies the real portfolio state.
        ///
        /// Qualified four-model candidates are taken from the existing telemetry
        /// snapshots, including candidates blocked by a live position or daily cap.
        /// Each candidate is then scored independently with the official Run 4
        /// mechanics: first causal tick, fixed stop/target, 60-minute max hold and
        /// 16:00 flatten.
        /// </summary>
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate == null
                || marketDataUpdate.MarketDataType != MarketDataType.Last)
            {
                return;
            }

            if (!EnableResearchTelemetry
                || researchTelemetryFaulted)
            {
                return;
            }

            var time = marketDataUpdate.Time;
            var price = marketDataUpdate.Price;

            if (!IsFinite(price))
                return;

            // Perturbation research is intentionally independent of the
            // shadow CSV sink. A shadow-output fault must not stop it.
            ProcessResearchPerturbationTick(time, price);

            if (researchShadowOutputFaulted)
                return;

            RegisterNewResearchShadowCandidates();

            if (activeResearchShadows.Count == 0)
                return;

            for (var i = activeResearchShadows.Count - 1; i >= 0; i--)
            {
                var shadow = activeResearchShadows[i];

                if (!shadow.Entered)
                {
                    // OnMarketData ordering relative to a secondary-series
                    // OnBarUpdate is not guaranteed. Recover the earliest causal
                    // tick already present in the 1-tick BIP so a callback that
                    // arrives one update later does not shift the hypothetical
                    // entry by a tick.
                    var replayTicks = CollectResearchTicksSince(shadow.SignalTime);

                    var finishedWhileReplaying = false;

                    for (var t = 0; t < replayTicks.Count; t++)
                    {
                        if (ApplyResearchShadowTick(
                                shadow,
                                replayTicks[t].Time,
                                replayTicks[t].Price))
                        {
                            finishedWhileReplaying = true;
                            break;
                        }
                    }

                    if (finishedWhileReplaying)
                    {
                        activeResearchShadows.RemoveAt(i);
                        continue;
                    }
                }

                if (!shadow.Entered)
                {
                    var currentTimeValue = ToTimeValue(time);

                    if (time.Date > shadow.SignalTime.Date
                        || currentTimeValue >= EntryEndTime)
                    {
                        WriteResearchShadowOutcome(
                            shadow,
                            time,
                            double.NaN,
                            "NoEntryBeforeWindowClose");

                        activeResearchShadows.RemoveAt(i);
                    }

                    continue;
                }

                if (ApplyResearchShadowTick(
                        shadow,
                        time,
                        price))
                {
                    activeResearchShadows.RemoveAt(i);
                }
            }
        }

        private void RegisterNewResearchShadowCandidates()
        {
            // The dictionary grows only when a raw candidate is captured and is
            // cleared at the next trading date. Avoid scanning it on every tick.
            if (researchSignalSnapshots.Count == lastObservedResearchSnapshotCount)
                return;

            lastObservedResearchSnapshotCount = researchSignalSnapshots.Count;

            foreach (var pair in researchSignalSnapshots)
            {
                var snapshot = pair.Value;

                if (snapshot == null
                    || !snapshot.Qualified.HasValue
                    || !snapshot.Qualified.Value)
                {
                    continue;
                }

                var candidateId = BuildResearchShadowCandidateId(snapshot);

                if (!registeredResearchShadowCandidates.Add(candidateId))
                    continue;

                var direction = string.Equals(
                        snapshot.Direction,
                        PendingDirection.Long.ToString(),
                        StringComparison.Ordinal)
                    ? PendingDirection.Long
                    : PendingDirection.Short;

                activeResearchShadows.Add(
                    new NinjexOvernightEdgeResearchShadow
                    {
                        CandidateId = candidateId,
                        Snapshot = snapshot.Clone(),
                        Direction = direction,
                        SignalTime = snapshot.EventTime
                    });
            }
        }

        private List<NinjexOvernightEdgeResearchTick> CollectResearchTicksSince(
            DateTime signalTime)
        {
            var result = new List<NinjexOvernightEdgeResearchTick>();

            if (CurrentBars == null
                || CurrentBars.Length <= TickSeriesIndex
                || CurrentBars[TickSeriesIndex] < 0)
            {
                return result;
            }

            // Registration normally occurs within one market-data update of the
            // candidate. The generous cap is defensive and prevents an accidental
            // unbounded scan if a host delivers callbacks unusually late.
            var maximumBarsAgo = Math.Min(CurrentBars[TickSeriesIndex], 20000);

            for (var barsAgo = 0; barsAgo <= maximumBarsAgo; barsAgo++)
            {
                var tickTime = Times[TickSeriesIndex][barsAgo];

                if (tickTime < signalTime)
                    break;

                result.Add(
                    new NinjexOvernightEdgeResearchTick
                    {
                        Time = tickTime,
                        Price = Closes[TickSeriesIndex][barsAgo]
                    });
            }

            // BarsAgo enumeration is newest -> oldest. Shadow execution must be
            // replayed oldest -> newest so stop/target path ordering is preserved.
            result.Reverse();
            return result;
        }

        private bool ApplyResearchShadowTick(
            NinjexOvernightEdgeResearchShadow shadow,
            DateTime time,
            double price)
        {
            if (shadow == null || !IsFinite(price))
                return false;

            var timeValue = ToTimeValue(time);

            if (!shadow.Entered)
            {
                if (time < shadow.SignalTime
                    || time.Date != shadow.SignalTime.Date
                    || timeValue >= EntryEndTime)
                {
                    return false;
                }

                shadow.Entered = true;
                shadow.EntryTime = time;
                shadow.EntryPrice = price;
                shadow.MaxHoldExitTime =
                    MaxHoldMinutes > 0
                        ? shadow.SignalTime.AddMinutes(MaxHoldMinutes)
                        : Core.Globals.MinDate;

                if (IsOvernightBreakoutResearchSignal(shadow.Snapshot == null ? string.Empty : shadow.Snapshot.Signal))
                {
                    RegisterResearchPerturbationCandidate(
                        shadow.Snapshot,
                        time,
                        price,
                        shadow.MaxHoldExitTime);
                }

                // The entry tick establishes zero excursion. Protective levels
                // become active after this causal entry rather than retroactively
                // evaluating the same tick.
                return false;
            }

            var pointValue = Instrument == null
                ? 0.0
                : Instrument.MasterInstrument.PointValue;

            var quantity = Math.Max(1, OrderQuantity);

            var favorablePoints =
                shadow.Direction == PendingDirection.Long
                    ? price - shadow.EntryPrice
                    : shadow.EntryPrice - price;

            var adversePoints =
                shadow.Direction == PendingDirection.Long
                    ? shadow.EntryPrice - price
                    : price - shadow.EntryPrice;

            if (favorablePoints > 0)
            {
                shadow.MfeDollars = Math.Max(
                    shadow.MfeDollars,
                    favorablePoints * pointValue * quantity);
            }

            if (adversePoints > 0)
            {
                shadow.MaeDollars = Math.Max(
                    shadow.MaeDollars,
                    adversePoints * pointValue * quantity);
            }

            var stopPrice =
                shadow.Direction == PendingDirection.Long
                    ? shadow.EntryPrice - StopLossTicks * TickSize
                    : shadow.EntryPrice + StopLossTicks * TickSize;

            var targetPrice =
                shadow.Direction == PendingDirection.Long
                    ? shadow.EntryPrice + ProfitTargetTicks * TickSize
                    : shadow.EntryPrice - ProfitTargetTicks * TickSize;

            var stopHit =
                StopLossTicks > 0
                && (shadow.Direction == PendingDirection.Long
                    ? price <= stopPrice
                    : price >= stopPrice);

            var targetHit =
                ProfitTargetTicks > 0
                && (shadow.Direction == PendingDirection.Long
                    ? price >= targetPrice
                    : price <= targetPrice);

            if (stopHit)
            {
                WriteResearchShadowOutcome(
                    shadow,
                    time,
                    stopPrice,
                    "StopLoss");
                return true;
            }

            if (targetHit)
            {
                WriteResearchShadowOutcome(
                    shadow,
                    time,
                    targetPrice,
                    "ProfitTarget");
                return true;
            }

            if (timeValue >= FlattenTime)
            {
                WriteResearchShadowOutcome(
                    shadow,
                    time,
                    price,
                    "FlattenTime");
                return true;
            }

            if (shadow.MaxHoldExitTime != Core.Globals.MinDate
                && time >= shadow.MaxHoldExitTime)
            {
                WriteResearchShadowOutcome(
                    shadow,
                    time,
                    price,
                    "MaxHold");
                return true;
            }

            return false;
        }

        private void WriteResearchShadowOutcome(
            NinjexOvernightEdgeResearchShadow shadow,
            DateTime exitTime,
            double exitPrice,
            string exitReason)
        {
            if (shadow == null
                || shadow.Snapshot == null
                || researchShadowOutputFaulted)
            {
                return;
            }

            try
            {
                EnsureResearchShadowOutput();

                if (researchShadowOutputFaulted
                    || string.IsNullOrEmpty(researchShadowOutputPath))
                {
                    return;
                }

                var grossPnl = double.NaN;

                if (shadow.Entered && IsFinite(exitPrice))
                {
                    var points =
                        shadow.Direction == PendingDirection.Long
                            ? exitPrice - shadow.EntryPrice
                            : shadow.EntryPrice - exitPrice;

                    var pointValue = Instrument == null
                        ? 0.0
                        : Instrument.MasterInstrument.PointValue;

                    grossPnl = points
                        * pointValue
                        * Math.Max(1, OrderQuantity);
                }

                var line = string.Join(
                    ",",
                    shadow.Snapshot.ToCsv(),
                    CsvResearchShadow(shadow.CandidateId),
                    DateResearchShadow(shadow.EntryTime),
                    NumberResearchShadow(shadow.EntryPrice),
                    DateResearchShadow(exitTime),
                    NumberResearchShadow(exitPrice),
                    CsvResearchShadow(exitReason),
                    NumberResearchShadow(grossPnl),
                    NumberResearchShadow(shadow.MfeDollars),
                    NumberResearchShadow(shadow.MaeDollars));

                File.AppendAllText(
                    researchShadowOutputPath,
                    line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                researchShadowOutputFaulted = true;

                Diagnostic(
                    exitTime,
                    "RESEARCH SHADOW DISABLED WriteError={0}",
                    ex.Message);
            }
        }

        private void EnsureResearchShadowOutput()
        {
            if (!string.IsNullOrEmpty(researchShadowOutputPath)
                || researchShadowOutputFaulted)
            {
                return;
            }

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir,
                    "NinjexResearch",
                    "OvernightEdgePortfolio");

                Directory.CreateDirectory(directory);

                var instrumentName = Instrument == null
                    ? "UnknownInstrument"
                    : SanitizeFileName(Instrument.FullName);

                var fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "overnight_edge_shadow_{0}_{1}_{2}.csv",
                    instrumentName,
                    DateTime.Now.ToString(
                        "yyyyMMdd_HHmmss_fff",
                        CultureInfo.InvariantCulture),
                    Guid.NewGuid().ToString("N").Substring(0, 8));

                researchShadowOutputPath = Path.Combine(directory, fileName);

                var header =
                    NinjexOvernightEdgeResearchRow.CsvHeader
                    + ",ShadowCandidateId,HypotheticalEntryTime,HypotheticalEntryPrice,"
                    + "HypotheticalExitTime,HypotheticalExitPrice,HypotheticalExitReason,"
                    + "HypotheticalGrossPnl,HypotheticalMFE,HypotheticalMAE";

                File.WriteAllText(
                    researchShadowOutputPath,
                    header + Environment.NewLine);

                Diagnostic(
                    DateTime.Now,
                    "RESEARCH SHADOW READY Path={0}",
                    researchShadowOutputPath);
            }
            catch (Exception ex)
            {
                researchShadowOutputFaulted = true;
                researchShadowOutputPath = string.Empty;

                Diagnostic(
                    DateTime.Now,
                    "RESEARCH SHADOW DISABLED InitError={0}",
                    ex.Message);
            }
        }

        private static string BuildResearchShadowCandidateId(
            NinjexOvernightEdgeResearchRow snapshot)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:yyyyMMddHHmmssfff}|{1}|{2}",
                snapshot.EventTime,
                snapshot.Signal ?? string.Empty,
                snapshot.CandidateOccurrenceToday);
        }

        private static string DateResearchShadow(DateTime value)
        {
            return value == Core.Globals.MinDate
                ? string.Empty
                : value.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture);
        }

        private static string NumberResearchShadow(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string CsvResearchShadow(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return "\""
                + value.Replace("\"", "\"\"")
                + "\"";
        }
    }
}
