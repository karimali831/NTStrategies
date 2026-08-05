using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis
{
    public sealed class AcceptanceContextTracker
    {
        private readonly Dictionary<string, AcceptanceObservationState> states =
            new Dictionary<string, AcceptanceObservationState>(StringComparer.Ordinal);

        public void Reset()
        {
            states.Clear();
        }

        public void Register(BreakoutSignalSnapshot breakout, double tickSize)
        {
            if (breakout == null || states.ContainsKey(breakout.EventId))
                return;

            var state = new AcceptanceObservationState(breakout);
            states.Add(breakout.EventId, state);
            UpdateState(state, breakout.Candle, tickSize);
        }

        public void Update(CandleSnapshot bar, double tickSize)
        {
            if (bar == null || tickSize <= 0)
                return;

            foreach (var state in states.Values)
                UpdateState(state, bar, tickSize);
        }

        public AcceptanceFeatureSnapshot Capture(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)
                || !states.TryGetValue(eventId, out var state))
            {
                return AcceptanceFeatureSnapshot.Empty;
            }

            return new AcceptanceFeatureSnapshot(
                state.ConsecutiveClosesOutside,
                state.BarsContinuouslyOutside,
                state.MinimumCloseDistanceOutsideTicks == double.MaxValue
                    ? 0
                    : state.MinimumCloseDistanceOutsideTicks,
                state.MaximumExcursionTicks,
                state.ReturnedInside);
        }

        public void Resolve(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId))
                states.Remove(eventId);
        }

        private static void UpdateState(
            AcceptanceObservationState state,
            CandleSnapshot bar,
            double tickSize)
        {
            if (state == null || bar == null || tickSize <= 0
                || bar.BarIndex < state.Breakout.BreakoutBarIndex
                || bar.BarIndex == state.LastProcessedBarIndex)
            {
                return;
            }

            state.LastProcessedBarIndex = bar.BarIndex;
            var level = state.Breakout.RangeLevel;
            var outside = state.Breakout.Direction == TradeDirection.Long
                ? bar.Close > level
                : bar.Close < level;

            var closeDistance = state.Breakout.Direction == TradeDirection.Long
                ? (bar.Close - level) / tickSize
                : (level - bar.Close) / tickSize;

            var excursion = state.Breakout.Direction == TradeDirection.Long
                ? (bar.High - level) / tickSize
                : (level - bar.Low) / tickSize;

            state.MaximumExcursionTicks = Math.Max(
                state.MaximumExcursionTicks,
                Math.Max(0, excursion));

            if (!outside)
            {
                state.ConsecutiveClosesOutside = 0;
                state.ReturnedInside = true;
                return;
            }

            state.ConsecutiveClosesOutside++;
            state.BarsContinuouslyOutside++;
            state.MinimumCloseDistanceOutsideTicks = Math.Min(
                state.MinimumCloseDistanceOutsideTicks,
                Math.Max(0, closeDistance));
        }
    }

    public sealed class AcceptanceFeatureSnapshot
    {
        public static readonly AcceptanceFeatureSnapshot Empty =
            new AcceptanceFeatureSnapshot(0, 0, 0, 0, false);

        public int ConsecutiveClosesOutside { get; }
        public int BarsContinuouslyOutside { get; }
        public double MinimumCloseDistanceOutsideTicks { get; }
        public double MaximumExcursionTicks { get; }
        public bool ReturnedInside { get; }

        public AcceptanceFeatureSnapshot(
            int consecutiveClosesOutside,
            int barsContinuouslyOutside,
            double minimumCloseDistanceOutsideTicks,
            double maximumExcursionTicks,
            bool returnedInside)
        {
            ConsecutiveClosesOutside = consecutiveClosesOutside;
            BarsContinuouslyOutside = barsContinuouslyOutside;
            MinimumCloseDistanceOutsideTicks = minimumCloseDistanceOutsideTicks;
            MaximumExcursionTicks = maximumExcursionTicks;
            ReturnedInside = returnedInside;
        }
    }
}
