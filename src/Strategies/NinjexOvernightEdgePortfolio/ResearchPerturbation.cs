using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private sealed class ResearchPerturbationScenario
        {
            public string Family = string.Empty;
            public string Name = string.Empty;
            public int AdverseEntryShiftTicks;
            public int StopTicks;
            public int TargetTicks;
            public bool BreakEvenConfigured;
            public bool BreakEvenConditionMet;
            public int BreakEvenTriggerTicks;
            public int BreakEvenPlusTicks;
            public double EntryPrice;
            public double ActiveStopPrice;
            public double TargetPrice;
            public bool BreakEvenActivated;
            public bool Completed;
            public DateTime ExitTime = Core.Globals.MinDate;
            public double ExitPrice = double.NaN;
            public string ExitReason = string.Empty;
            public double GrossPnl = double.NaN;
            public double MfeTicks;
            public double MaeTicks;
        }

        private sealed class ResearchPerturbationBatch
        {
            public string TradeId = string.Empty;
            public NinjexOvernightEdgeResearchRow Snapshot;
            public PendingDirection Direction;
            public DateTime EntryTime = Core.Globals.MinDate;
            public double ActualEntryPrice = double.NaN;
            public DateTime MaxHoldExitTime = Core.Globals.MinDate;
            public bool ActualExitKnown;
            public DateTime ActualExitTime = Core.Globals.MinDate;
            public double ActualExitPrice = double.NaN;
            public string ActualExitName = string.Empty;
            public readonly List<ResearchPerturbationScenario> Scenarios =
                new List<ResearchPerturbationScenario>();
        }

        private readonly List<ResearchPerturbationBatch> activeResearchPerturbationBatches =
            new List<ResearchPerturbationBatch>();

        private string researchPerturbationOutputPath = string.Empty;
        private bool researchPerturbationOutputFaulted;

        private void RegisterResearchPerturbationTrade(
            NinjexOvernightEdgeResearchRow snapshot,
            DateTime entryTime,
            double actualEntryPrice,
            DateTime maxHoldExitTime)
        {
            if (!EnableResearchTelemetry
                || !EnableResearchPerturbationScenarios
                || researchTelemetryFaulted
                || researchPerturbationOutputFaulted
                || snapshot == null
                || !IsFinite(actualEntryPrice))
            {
                return;
            }

            var direction = string.Equals(
                    snapshot.Direction,
                    PendingDirection.Long.ToString(),
                    StringComparison.Ordinal)
                ? PendingDirection.Long
                : PendingDirection.Short;

            var batch = new ResearchPerturbationBatch
            {
                TradeId = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyyMMddHHmmssfff}|{1}|{2}",
                    entryTime,
                    snapshot.Signal ?? string.Empty,
                    snapshot.ModelTradeOccurrenceToday),
                Snapshot = snapshot.Clone(),
                Direction = direction,
                EntryTime = entryTime,
                ActualEntryPrice = actualEntryPrice,
                MaxHoldExitTime = maxHoldExitTime
            };

            AddEntryShiftScenarios(batch);
            AddTargetScenarios(batch);
            AddBreakEvenScenarios(batch, false);
            AddBreakEvenScenarios(batch, true);

            activeResearchPerturbationBatches.Add(batch);
        }

        private void RegisterResearchPerturbationCandidate(
            NinjexOvernightEdgeResearchRow snapshot,
            DateTime entryTime,
            double entryPrice,
            DateTime maxHoldExitTime)
        {
            if (!EnableResearchTelemetry
                || !EnableResearchPerturbationScenarios
                || researchTelemetryFaulted
                || researchPerturbationOutputFaulted
                || snapshot == null
                || !IsOvernightBreakoutResearchSignal(snapshot.Signal)
                || !IsFinite(entryPrice))
            {
                return;
            }

            var direction = string.Equals(
                    snapshot.Direction,
                    PendingDirection.Long.ToString(),
                    StringComparison.Ordinal)
                ? PendingDirection.Long
                : PendingDirection.Short;

            var batch = new ResearchPerturbationBatch
            {
                TradeId = string.Format(
                    CultureInfo.InvariantCulture,
                    "RC|{0:yyyyMMddHHmmssfff}|{1}|{2}",
                    entryTime,
                    snapshot.Signal ?? string.Empty,
                    snapshot.CandidateOccurrenceToday),
                Snapshot = snapshot.Clone(),
                Direction = direction,
                EntryTime = entryTime,
                ActualEntryPrice = entryPrice,
                MaxHoldExitTime = maxHoldExitTime,

                // Research-only candidate: there is intentionally no actual
                // strategy exit to wait for. Mark the batch write-ready once
                // every hypothetical scenario has completed.
                ActualExitKnown = true
            };

            AddEntryShiftScenarios(batch);
            AddTargetScenarios(batch);
            AddBreakEvenScenarios(batch, false);
            AddBreakEvenScenarios(batch, true);

            activeResearchPerturbationBatches.Add(batch);
        }

        private void AddEntryShiftScenarios(ResearchPerturbationBatch batch)
        {
            var shifts = new[] { -2, -1, 0, 1, 2 };

            for (var i = 0; i < shifts.Length; i++)
            {
                AddResearchPerturbationScenario(
                    batch,
                    "EntryShift",
                    "AdverseEntryShift=" + shifts[i].ToString(CultureInfo.InvariantCulture) + "t",
                    shifts[i],
                    StopLossTicks,
                    ProfitTargetTicks,
                    false,
                    false,
                    0,
                    0);
            }
        }

        private void AddTargetScenarios(ResearchPerturbationBatch batch)
        {
            var targets = new[] { 30, 32, 34, 36, 38, 39, 40, 41, 42, 44, 46, 48, 50 };

            for (var i = 0; i < targets.Length; i++)
            {
                AddResearchPerturbationScenario(
                    batch,
                    "Target",
                    "Target=" + targets[i].ToString(CultureInfo.InvariantCulture) + "t",
                    0,
                    StopLossTicks,
                    targets[i],
                    false,
                    false,
                    0,
                    0);
            }
        }

        private void AddBreakEvenScenarios(
            ResearchPerturbationBatch batch,
            bool afterFirstWinnerOnly)
        {
            var triggers = new[] { 8, 10, 12, 15, 18, 20, 22, 25, 30, 35 };
            var plusTicks = new[] { 0, 2, 4 };

            var conditionMet =
                !afterFirstWinnerOnly
                || (batch.Snapshot != null
                    && batch.Snapshot.WinnersToday >= 1);

            for (var triggerIndex = 0; triggerIndex < triggers.Length; triggerIndex++)
            {
                for (var plusIndex = 0; plusIndex < plusTicks.Length; plusIndex++)
                {
                    AddResearchPerturbationScenario(
                        batch,
                        afterFirstWinnerOnly
                            ? "BreakEvenAfterFirstWinner"
                            : "BreakEvenAll",
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Trigger={0}t;Plus={1}t",
                            triggers[triggerIndex],
                            plusTicks[plusIndex]),
                        0,
                        StopLossTicks,
                        ProfitTargetTicks,
                        true,
                        conditionMet,
                        triggers[triggerIndex],
                        plusTicks[plusIndex]);
                }
            }
        }

        private void AddResearchPerturbationScenario(
            ResearchPerturbationBatch batch,
            string family,
            string name,
            int adverseEntryShiftTicks,
            int stopTicks,
            int targetTicks,
            bool breakEvenConfigured,
            bool breakEvenConditionMet,
            int breakEvenTriggerTicks,
            int breakEvenPlusTicks)
        {
            var adversePriceDirection =
                batch.Direction == PendingDirection.Long
                    ? 1.0
                    : -1.0;

            var entryPrice =
                batch.ActualEntryPrice
                + adversePriceDirection
                * adverseEntryShiftTicks
                * TickSize;

            var stopPrice =
                batch.Direction == PendingDirection.Long
                    ? entryPrice - stopTicks * TickSize
                    : entryPrice + stopTicks * TickSize;

            var targetPrice =
                batch.Direction == PendingDirection.Long
                    ? entryPrice + targetTicks * TickSize
                    : entryPrice - targetTicks * TickSize;

            batch.Scenarios.Add(
                new ResearchPerturbationScenario
                {
                    Family = family ?? string.Empty,
                    Name = name ?? string.Empty,
                    AdverseEntryShiftTicks = adverseEntryShiftTicks,
                    StopTicks = stopTicks,
                    TargetTicks = targetTicks,
                    BreakEvenConfigured = breakEvenConfigured,
                    BreakEvenConditionMet = breakEvenConditionMet,
                    BreakEvenTriggerTicks = breakEvenTriggerTicks,
                    BreakEvenPlusTicks = breakEvenPlusTicks,
                    EntryPrice = entryPrice,
                    ActiveStopPrice = stopPrice,
                    TargetPrice = targetPrice
                });
        }

        private void ProcessResearchPerturbationTick(
            DateTime time,
            double price)
        {
            if (!EnableResearchTelemetry
                || !EnableResearchPerturbationScenarios
                || researchPerturbationOutputFaulted
                || !IsFinite(price)
                || activeResearchPerturbationBatches.Count == 0)
            {
                return;
            }

            for (var batchIndex = activeResearchPerturbationBatches.Count - 1;
                 batchIndex >= 0;
                 batchIndex--)
            {
                var batch = activeResearchPerturbationBatches[batchIndex];

                for (var scenarioIndex = 0;
                     scenarioIndex < batch.Scenarios.Count;
                     scenarioIndex++)
                {
                    var scenario = batch.Scenarios[scenarioIndex];

                    if (scenario.Completed
                        || time <= batch.EntryTime)
                    {
                        continue;
                    }

                    ApplyResearchPerturbationTick(
                        batch,
                        scenario,
                        time,
                        price);
                }

                if (batch.ActualExitKnown
                    && AreResearchPerturbationScenariosComplete(batch))
                {
                    WriteResearchPerturbationBatch(batch);
                    activeResearchPerturbationBatches.RemoveAt(batchIndex);
                }
            }
        }

        private void ApplyResearchPerturbationTick(
            ResearchPerturbationBatch batch,
            ResearchPerturbationScenario scenario,
            DateTime time,
            double price)
        {
            var favorableTicks =
                batch.Direction == PendingDirection.Long
                    ? (price - scenario.EntryPrice) / TickSize
                    : (scenario.EntryPrice - price) / TickSize;

            var adverseTicks =
                batch.Direction == PendingDirection.Long
                    ? (scenario.EntryPrice - price) / TickSize
                    : (price - scenario.EntryPrice) / TickSize;

            if (favorableTicks > 0)
            {
                scenario.MfeTicks =
                    Math.Max(scenario.MfeTicks, favorableTicks);
            }

            if (adverseTicks > 0)
            {
                scenario.MaeTicks =
                    Math.Max(scenario.MaeTicks, adverseTicks);
            }

            var stopHit =
                batch.Direction == PendingDirection.Long
                    ? price <= scenario.ActiveStopPrice
                    : price >= scenario.ActiveStopPrice;

            var targetHit =
                batch.Direction == PendingDirection.Long
                    ? price >= scenario.TargetPrice
                    : price <= scenario.TargetPrice;

            if (stopHit)
            {
                CompleteResearchPerturbationScenario(
                    batch,
                    scenario,
                    time,
                    scenario.ActiveStopPrice,
                    scenario.BreakEvenActivated
                        ? (scenario.BreakEvenPlusTicks == 0
                            ? "BreakEven"
                            : "BreakEvenPlus")
                        : "StopLoss");
                return;
            }

            if (targetHit)
            {
                CompleteResearchPerturbationScenario(
                    batch,
                    scenario,
                    time,
                    scenario.TargetPrice,
                    "ProfitTarget");
                return;
            }

            if (scenario.BreakEvenConfigured
                && scenario.BreakEvenConditionMet
                && !scenario.BreakEvenActivated
                && favorableTicks >= scenario.BreakEvenTriggerTicks)
            {
                scenario.BreakEvenActivated = true;

                scenario.ActiveStopPrice =
                    batch.Direction == PendingDirection.Long
                        ? scenario.EntryPrice
                            + scenario.BreakEvenPlusTicks * TickSize
                        : scenario.EntryPrice
                            - scenario.BreakEvenPlusTicks * TickSize;
            }

            if (ToTimeValue(time) >= FlattenTime)
            {
                CompleteResearchPerturbationScenario(
                    batch,
                    scenario,
                    time,
                    price,
                    "FlattenTime");
                return;
            }

            if (batch.MaxHoldExitTime != Core.Globals.MinDate
                && time >= batch.MaxHoldExitTime)
            {
                CompleteResearchPerturbationScenario(
                    batch,
                    scenario,
                    time,
                    price,
                    "MaxHold");
            }
        }

        private void CompleteResearchPerturbationScenario(
            ResearchPerturbationBatch batch,
            ResearchPerturbationScenario scenario,
            DateTime exitTime,
            double exitPrice,
            string reason)
        {
            scenario.Completed = true;
            scenario.ExitTime = exitTime;
            scenario.ExitPrice = exitPrice;
            scenario.ExitReason = reason ?? string.Empty;

            var points =
                batch.Direction == PendingDirection.Long
                    ? exitPrice - scenario.EntryPrice
                    : scenario.EntryPrice - exitPrice;

            var pointValue =
                Instrument == null
                    ? 0.0
                    : Instrument.MasterInstrument.PointValue;

            scenario.GrossPnl =
                points
                * pointValue
                * Math.Max(1, OrderQuantity);
        }

        private void CompleteResearchPerturbationActualTrade(
            DateTime exitTime,
            double exitPrice,
            string exitName)
        {
            if (!EnableResearchTelemetry
                || !EnableResearchPerturbationScenarios)
            {
                return;
            }

            for (var index = activeResearchPerturbationBatches.Count - 1;
                 index >= 0;
                 index--)
            {
                var batch = activeResearchPerturbationBatches[index];

                if (batch.ActualExitKnown)
                    continue;

                batch.ActualExitKnown = true;
                batch.ActualExitTime = exitTime;
                batch.ActualExitPrice = exitPrice;
                batch.ActualExitName = exitName ?? string.Empty;

                if (AreResearchPerturbationScenariosComplete(batch))
                {
                    WriteResearchPerturbationBatch(batch);
                    activeResearchPerturbationBatches.RemoveAt(index);
                }

                return;
            }
        }

        private static bool AreResearchPerturbationScenariosComplete(
            ResearchPerturbationBatch batch)
        {
            for (var index = 0; index < batch.Scenarios.Count; index++)
            {
                if (!batch.Scenarios[index].Completed)
                    return false;
            }

            return true;
        }

        private void ResetResearchPerturbationDailyState()
        {
            if (activeResearchPerturbationBatches.Count > 0)
            {
                activeResearchPerturbationBatches.Clear();
            }
        }

        private void WriteResearchPerturbationBatch(
            ResearchPerturbationBatch batch)
        {
            if (batch == null
                || batch.Snapshot == null
                || researchPerturbationOutputFaulted)
            {
                return;
            }

            try
            {
                EnsureResearchPerturbationOutput();

                if (researchPerturbationOutputFaulted
                    || string.IsNullOrEmpty(researchPerturbationOutputPath))
                {
                    return;
                }

                var lines = new List<string>();

                for (var index = 0; index < batch.Scenarios.Count; index++)
                {
                    var scenario = batch.Scenarios[index];

                    var hasActualExit =
                        batch.ActualExitKnown
                        && batch.ActualExitTime != Core.Globals.MinDate;

                    var secondsVsActual =
                        hasActualExit
                            ? (scenario.ExitTime - batch.ActualExitTime).TotalSeconds
                            : double.NaN;

                    lines.Add(
                        string.Join(
                            ",",
                            CsvPerturbation(batch.TradeId),
                            DatePerturbation(batch.EntryTime),
                            CsvPerturbation(batch.Snapshot.Model),
                            CsvPerturbation(batch.Snapshot.Signal),
                            CsvPerturbation(batch.Snapshot.Direction),
                            NumberPerturbation(batch.ActualEntryPrice),
                            DatePerturbation(batch.ActualExitTime),
                            NumberPerturbation(batch.ActualExitPrice),
                            CsvPerturbation(batch.ActualExitName),
                            IntPerturbation(batch.Snapshot.TradesToday),
                            IntPerturbation(batch.Snapshot.WinnersToday),
                            IntPerturbation(batch.Snapshot.LossesToday),
                            CsvPerturbation(scenario.Family),
                            CsvPerturbation(scenario.Name),
                            IntPerturbation(scenario.AdverseEntryShiftTicks),
                            IntPerturbation(scenario.StopTicks),
                            IntPerturbation(scenario.TargetTicks),
                            BoolPerturbation(scenario.BreakEvenConfigured),
                            BoolPerturbation(scenario.BreakEvenConditionMet),
                            IntPerturbation(scenario.BreakEvenTriggerTicks),
                            IntPerturbation(scenario.BreakEvenPlusTicks),
                            BoolPerturbation(scenario.BreakEvenActivated),
                            NumberPerturbation(scenario.EntryPrice),
                            DatePerturbation(scenario.ExitTime),
                            NumberPerturbation(scenario.ExitPrice),
                            CsvPerturbation(scenario.ExitReason),
                            NumberPerturbation(scenario.GrossPnl),
                            NumberPerturbation(scenario.MfeTicks),
                            NumberPerturbation(scenario.MaeTicks),
                            BoolPerturbation(
                                hasActualExit
                                && scenario.ExitTime > batch.ActualExitTime),
                            NumberPerturbation(secondsVsActual)));
                }

                File.AppendAllLines(
                    researchPerturbationOutputPath,
                    lines);
            }
            catch (Exception ex)
            {
                researchPerturbationOutputFaulted = true;

                Diagnostic(
                    DateTime.Now,
                    "RESEARCH PERTURBATION DISABLED WriteError={0}",
                    ex.Message);
            }
        }

        private void EnsureResearchPerturbationOutput()
        {
            if (!string.IsNullOrEmpty(researchPerturbationOutputPath)
                || researchPerturbationOutputFaulted)
            {
                return;
            }

            try
            {
                var directory =
                    Path.Combine(
                        Core.Globals.UserDataDir,
                        "NinjexResearch",
                        "OvernightEdgePortfolio");

                Directory.CreateDirectory(directory);

                var instrumentName =
                    Instrument == null
                        ? "UnknownInstrument"
                        : SanitizeFileName(Instrument.FullName);

                researchPerturbationOutputPath =
                    Path.Combine(
                        directory,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "overnight_edge_perturbation_{0}_{1}_{2}.csv",
                            instrumentName,
                            DateTime.Now.ToString(
                                "yyyyMMdd_HHmmss_fff",
                                CultureInfo.InvariantCulture),
                            Guid.NewGuid().ToString("N").Substring(0, 8)));

                File.WriteAllText(
                    researchPerturbationOutputPath,
                    "TradeId,ActualEntryTime,Model,Signal,Direction,ActualEntryPrice,"
                    + "ActualExitTime,ActualExitPrice,ActualExitName,TradesAtEntry,WinnersAtEntry,LossesAtEntry,"
                    + "ScenarioFamily,ScenarioName,AdverseEntryShiftTicks,StopTicks,TargetTicks,"
                    + "BreakEvenConfigured,BreakEvenConditionMet,BreakEvenTriggerTicks,BreakEvenPlusTicks,"
                    + "BreakEvenActivated,HypotheticalEntryPrice,HypotheticalExitTime,HypotheticalExitPrice,"
                    + "HypotheticalExitReason,HypotheticalGrossPnl,MfeTicks,MaeTicks,ExitedAfterActual,SecondsVsActualExit"
                    + Environment.NewLine);

                Diagnostic(
                    DateTime.Now,
                    "RESEARCH PERTURBATION READY Path={0}",
                    researchPerturbationOutputPath);
            }
            catch (Exception ex)
            {
                researchPerturbationOutputFaulted = true;
                researchPerturbationOutputPath = string.Empty;

                Diagnostic(
                    DateTime.Now,
                    "RESEARCH PERTURBATION DISABLED InitError={0}",
                    ex.Message);
            }
        }

        private static string DatePerturbation(DateTime value)
        {
            return value == Core.Globals.MinDate
                ? string.Empty
                : value.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture);
        }

        private static string NumberPerturbation(double value)
        {
            return double.IsNaN(value)
                || double.IsInfinity(value)
                    ? string.Empty
                    : value.ToString(
                        "0.########",
                        CultureInfo.InvariantCulture);
        }

        private static string IntPerturbation(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string BoolPerturbation(bool value)
        {
            return value ? "1" : "0";
        }

        private static string CsvPerturbation(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return "\""
                + value.Replace("\"", "\"\"")
                + "\"";
        }
    }
}
