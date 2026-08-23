using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk
{
    /// <summary>
    /// Completed result for one risk scenario.
    ///
    /// This intentionally exposes the same data that was previously
    /// exported from a risk-scenario HypotheticalTrade.
    /// </summary>
    public sealed class RiskScenarioTradeResult
    {
        public EntryCandidate Candidate { get; internal set; }

        public RiskScenario RiskScenario { get; internal set; }

        public DateTime EntryTime { get; internal set; }

        public double EntryPrice { get; internal set; }

        public double InitialRiskTicks { get; internal set; }

        public double InitialStopPrice { get; internal set; }

        public double TargetPrice { get; internal set; }

        public bool StopWasCapped { get; internal set; }

        public ManagementOutcome Outcome { get; internal set; }
    }


    /// <summary>
    /// Processes the entire fixed-stop / fixed-target scenario grid
    /// for one candidate using one shared price-path tracker.
    ///
    /// Instead of:
    ///
    ///     84 trades x every tick
    ///
    /// we track:
    ///
    ///     maximum favorable excursion
    ///     maximum adverse excursion
    ///
    /// and resolve scenarios only when one of their unique
    /// stop/target thresholds is crossed for the first time.
    /// </summary>
    public sealed class RiskScenarioBatch
    {
        private const double ThresholdTolerance = 0.0000001;

        private readonly EntryCandidate candidate;
        private readonly DateTime entryTime;
        private readonly double entryPrice;
        private readonly TradeManagementSettings settings;

        private readonly List<ScenarioState> states =
            new List<ScenarioState>();

        private readonly List<ThresholdBucket> stopBuckets =
            new List<ThresholdBucket>();

        private readonly List<ThresholdBucket> targetBuckets =
            new List<ThresholdBucket>();

        private int nextStopBucketIndex;
        private int nextTargetBucketIndex;
        private int activeScenarioCount;

        private double maximumFavorableTicks;
        private double maximumAdverseTicks;


        public RiskScenarioBatch(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice,
            TradeManagementSettings settings,
            IReadOnlyCollection<RiskScenario> scenarios)
        {
            if (candidate == null)
                throw new ArgumentNullException(
                    nameof(candidate));

            if (settings == null)
                throw new ArgumentNullException(
                    nameof(settings));

            if (scenarios == null)
                throw new ArgumentNullException(
                    nameof(scenarios));

            if (settings.TickSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.TickSize));
            }

            this.candidate =
                candidate;

            this.entryTime =
                entryTime;

            this.entryPrice =
                entryPrice;

            this.settings =
                settings;

            BuildScenarioStates(
                scenarios);

            BuildThresholdBuckets();

            activeScenarioCount =
                states.Count;
        }
        
        public EntryCandidate Candidate =>
            candidate;

        public DateTime EntryTime =>
            entryTime;

        public double EntryPrice =>
            entryPrice;

        public int ScenarioCount =>
            states.Count;

        public int ActiveScenarioCount =>
            activeScenarioCount;

        public bool IsClosed =>
            activeScenarioCount == 0;


        private void BuildScenarioStates(
            IReadOnlyCollection<RiskScenario> scenarios)
        {
            var structuralRiskTicks =
                Math.Round(
                    Math.Max(
                        0,
                        candidate.StructuralRiskTicks),
                    8,
                    MidpointRounding.AwayFromZero);

            foreach (var scenario in scenarios)
            {
                if (scenario == null)
                    continue;

                var initialRiskTicks =
                    Math.Min(
                        structuralRiskTicks,
                        scenario.MaximumInitialStopTicks);

                initialRiskTicks =
                    Math.Round(
                        initialRiskTicks,
                        8,
                        MidpointRounding.AwayFromZero);

                if (initialRiskTicks <= 0)
                    continue;
                
                var rawTargetTicks =
                    initialRiskTicks
                    * scenario.RiskRewardRatio;

                var targetTicks =
                    Math.Ceiling(
                        rawTargetTicks
                        - 0.0000001);

                var initialStopPrice =
                    candidate.Direction == TradeDirection.Long
                        ? entryPrice
                          - initialRiskTicks * settings.TickSize
                        : entryPrice
                          + initialRiskTicks * settings.TickSize;

                var targetPrice =
                    candidate.Direction == TradeDirection.Long
                        ? entryPrice
                          + targetTicks * settings.TickSize
                        : entryPrice
                          - targetTicks * settings.TickSize;

                states.Add(
                    new ScenarioState
                    {
                        Scenario =
                            scenario,

                        InitialRiskTicks =
                            initialRiskTicks,

                        InitialStopPrice =
                            initialStopPrice,

                        TargetTicks =
                            targetTicks,

                        TargetPrice =
                            targetPrice,

                        StopWasCapped =
                            structuralRiskTicks
                            > scenario.MaximumInitialStopTicks
                    });
            }
        }


        private void BuildThresholdBuckets()
        {
            foreach (var state in states)
            {
                AddToBucket(
                    stopBuckets,
                    state.InitialRiskTicks,
                    state);

                AddToBucket(
                    targetBuckets,
                    state.TargetTicks,
                    state);
            }

            stopBuckets.Sort(
                CompareThresholdBuckets);

            targetBuckets.Sort(
                CompareThresholdBuckets);
        }


        private static int CompareThresholdBuckets(
            ThresholdBucket x,
            ThresholdBucket y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x == null)
                return -1;

            if (y == null)
                return 1;

            return x.ThresholdTicks.CompareTo(
                y.ThresholdTicks);
        }


        private static void AddToBucket(
            List<ThresholdBucket> buckets,
            double thresholdTicks,
            ScenarioState state)
        {
            var normalizedThreshold =
                NormalizeThreshold(
                    thresholdTicks);

            for (var i = 0;
                 i < buckets.Count;
                 i++)
            {
                var existing =
                    buckets[i];

                if (Math.Abs(
                        existing.ThresholdTicks
                        - normalizedThreshold)
                    > ThresholdTolerance)
                {
                    continue;
                }

                existing.States.Add(
                    state);

                return;
            }

            var bucket =
                new ThresholdBucket
                {
                    ThresholdTicks =
                        normalizedThreshold
                };

            bucket.States.Add(
                state);

            buckets.Add(
                bucket);
        }


        private static double NormalizeThreshold(
            double value)
        {
            return Math.Round(
                value,
                8,
                MidpointRounding.AwayFromZero);
        }


        public void ProcessTick(
            DateTime time,
            double price,
            Action<RiskScenarioTradeResult> onClosed)
        {
            if (IsClosed
                || double.IsNaN(price)
                || price <= 0)
            {
                return;
            }

            double favorableTicks;
            double adverseTicks;

            if (candidate.Direction
                == TradeDirection.Long)
            {
                favorableTicks =
                    (price - entryPrice)
                    / settings.TickSize;

                adverseTicks =
                    (entryPrice - price)
                    / settings.TickSize;
            }
            else
            {
                favorableTicks =
                    (entryPrice - price)
                    / settings.TickSize;

                adverseTicks =
                    (price - entryPrice)
                    / settings.TickSize;
            }

            var favorableAdvanced =
                favorableTicks
                > maximumFavorableTicks;

            var adverseAdvanced =
                adverseTicks
                > maximumAdverseTicks;

            if (favorableAdvanced)
            {
                maximumFavorableTicks =
                    favorableTicks;
            }

            if (adverseAdvanced)
            {
                maximumAdverseTicks =
                    adverseTicks;
            }

            /*
             * A single trade tick cannot simultaneously be on the
             * profitable and adverse side of the entry price.
             *
             * We nevertheless keep both tests independent so this
             * remains robust around exactly-at-entry ticks.
             */

            if (adverseAdvanced)
            {
                ProcessStopThresholds(
                    time,
                    onClosed);
            }

            if (favorableAdvanced)
            {
                ProcessTargetThresholds(
                    time,
                    onClosed);
            }
        }


        private void ProcessStopThresholds(
            DateTime time,
            Action<RiskScenarioTradeResult> onClosed)
        {
            while (
                nextStopBucketIndex
                < stopBuckets.Count)
            {
                var bucket =
                    stopBuckets[
                        nextStopBucketIndex];

                if (maximumAdverseTicks
                    + ThresholdTolerance
                    < bucket.ThresholdTicks)
                {
                    break;
                }

                foreach (var state
                         in bucket.States)
                {
                    if (state.IsClosed)
                        continue;

                    CloseState(
                        state,
                        time,
                        state.InitialStopPrice,
                        "Stop",
                        onClosed);
                }

                nextStopBucketIndex++;
            }
        }


        private void ProcessTargetThresholds(
            DateTime time,
            Action<RiskScenarioTradeResult> onClosed)
        {
            while (
                nextTargetBucketIndex
                < targetBuckets.Count)
            {
                var bucket =
                    targetBuckets[
                        nextTargetBucketIndex];

                if (maximumFavorableTicks
                    + ThresholdTolerance
                    < bucket.ThresholdTicks)
                {
                    break;
                }

                foreach (var state
                         in bucket.States)
                {
                    if (state.IsClosed)
                        continue;

                    CloseState(
                        state,
                        time,
                        state.TargetPrice,
                        "Target",
                        onClosed);
                }

                nextTargetBucketIndex++;
            }
        }


        public void ForceClose(
            DateTime time,
            double price,
            string reason,
            Action<RiskScenarioTradeResult> onClosed)
        {
            if (IsClosed
                || double.IsNaN(price)
                || price <= 0)
            {
                return;
            }

            /*
             * Preserve HypotheticalTrade.ForceClose semantics:
             * do not update MFE/MAE using the forced-close price.
             * Those excursions represent ticks already processed.
             */
            foreach (var state
                     in states)
            {
                if (state.IsClosed)
                    continue;

                CloseState(
                    state,
                    time,
                    price,
                    reason,
                    onClosed);
            }
        }


        private void CloseState(
            ScenarioState state,
            DateTime time,
            double exitPrice,
            string reason,
            Action<RiskScenarioTradeResult> onClosed)
        {
            if (state == null
                || state.IsClosed)
            {
                return;
            }

            state.IsClosed =
                true;

            activeScenarioCount--;

            var signedTicks =
                candidate.Direction
                == TradeDirection.Long
                    ? (exitPrice - entryPrice)
                      / settings.TickSize
                    : (entryPrice - exitPrice)
                      / settings.TickSize;

            var outcome =
                new ManagementOutcome
                {
                    PolicyName =
                        "RiskScenario",

                    IsClosed =
                        true,

                    ExitTime =
                        time,

                    ExitPrice =
                        exitPrice,

                    ExitReason =
                        reason,

                    RealizedTicks =
                        signedTicks,

                    RealizedUsd =
                        signedTicks
                        * settings.TickSize
                        * settings.PointValue
                        * Math.Max(
                            1,
                            settings.Quantity),

                    MfeTicks =
                        Math.Max(
                            0,
                            maximumFavorableTicks),

                    MaeTicks =
                        Math.Max(
                            0,
                            maximumAdverseTicks),

                    BreakEvenActivated =
                        false,

                    HighestTrailStepActivated =
                        0
                };

            if (onClosed == null)
                return;

            onClosed(
                new RiskScenarioTradeResult
                {
                    Candidate =
                        candidate,

                    RiskScenario =
                        state.Scenario,

                    EntryTime =
                        entryTime,

                    EntryPrice =
                        entryPrice,

                    InitialRiskTicks =
                        state.InitialRiskTicks,

                    InitialStopPrice =
                        state.InitialStopPrice,

                    TargetPrice =
                        state.TargetPrice,

                    StopWasCapped =
                        state.StopWasCapped,

                    Outcome =
                        outcome
                });
        }


        private sealed class ScenarioState
        {
            public RiskScenario Scenario;

            public double InitialRiskTicks;

            public double InitialStopPrice;

            public double TargetTicks;

            public double TargetPrice;

            public bool StopWasCapped;

            public bool IsClosed;
        }


        private sealed class ThresholdBucket
        {
            public double ThresholdTicks;

            public readonly List<ScenarioState>
                States =
                    new List<ScenarioState>();
        }
    }
}