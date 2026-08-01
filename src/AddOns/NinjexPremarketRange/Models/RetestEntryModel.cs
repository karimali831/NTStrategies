#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models
{
    public sealed class RetestEntryModel : IEntryModel
    {
        private sealed class RetestState
        {
            public BreakoutEvent Breakout { get; set; }

            public bool Armed { get; set; }

            public int ArmedBarIndex { get; set; }
            
            public int ExpiryBarIndex { get; set; }
            public double FurthestExcursionTicks { get; set; }

            public bool ZoneTouched { get; set; }

            public int ZoneTouchBarIndex { get; set; }

            public bool Invalidated { get; set; }

            public double MaximumInsideDepthTicks { get; set; }

            public double MinimumOutsideDistanceTicks { get; set; }
        }

        private readonly List<RetestState> states =
            new List<RetestState>();

        public string Name => "Retest";

        public bool IsEnabled { get; set; }

        public int MaximumBarsAfterBreakout { get; set; }

        public int OutsideDistanceTicks { get; set; }

        public int InsideDistanceTicks { get; set; }

        public double MinimumConfirmationBodyPercent { get; set; }

        public void Reset(RangeSessionContext session)
        {
            states.Clear();
        }

        public void OnBreakout(BreakoutEvent breakoutEvent)
        {
            if (!IsEnabled
                || breakoutEvent == null)
            {
                return;
            }

            states.Add(new RetestState
            {
                Breakout = breakoutEvent,

                ExpiryBarIndex =
                    breakoutEvent.BreakoutBarIndex
                    + Math.Max(0, MaximumBarsAfterBreakout),

                ArmedBarIndex = -1,
                ZoneTouchBarIndex = -1,

                MinimumOutsideDistanceTicks =
                    double.MaxValue
            });
        }

        public IEnumerable<EntryCandidate> Evaluate(
            ModelBarContext context)
        {
            var candidates =
                new List<EntryCandidate>();

            if (!IsEnabled
                || context?.Session == null
                || context.Bar == null)
            {
                return candidates;
            }

            var tickSize =
                context.Session.TickSize;

            if (tickSize <= 0)
                return candidates;

            for (var i = states.Count - 1; i >= 0; i--)
            {
                var state = states[i];

                if (state.Breakout == null
                    || state.Breakout.IsResolved)
                {
                    states.RemoveAt(i);
                    continue;
                }

                if (context.Bar.BarIndex > state.ExpiryBarIndex)
                {
                    states.RemoveAt(i);
                    continue;
                }

                var barsAfter =
                    context.Bar.BarIndex
                    - state.Breakout.BreakoutBarIndex;

                UpdateArmState(
                    state,
                    context.Bar,
                    tickSize);

                if (!state.Armed
                    || context.Bar.BarIndex
                    <= state.ArmedBarIndex)
                {
                    continue;
                }

                UpdateRetestState(
                    state,
                    context.Bar,
                    tickSize);

                if (state.Invalidated)
                {
                    states.RemoveAt(i);
                    continue;
                }

                if (!state.ZoneTouched)
                    continue;

                // Confirmation must occur after the first retest bar.
                if (context.Bar.BarIndex
                    <= state.ZoneTouchBarIndex)
                {
                    continue;
                }

                if (!IsConfirmation(
                        context,
                        state.Breakout.Direction))
                {
                    continue;
                }

                if (barsAfter > MaximumBarsAfterBreakout)
                {
                    states.RemoveAt(i);
                    continue;
                }

                candidates.Add(
                    BuildCandidate(
                        context,
                        state,
                        barsAfter));

                states.RemoveAt(i);
                
            }

            return candidates;
        }

        private void UpdateArmState(
            RetestState state,
            CandleSnapshot bar,
            double tickSize)
        {
            if (state.Armed)
                return;

            var excursionTicks =
                state.Breakout.Direction
                == TradeDirection.Long
                    ? (bar.High
                       - state.Breakout.RangeLevel)
                      / tickSize
                    : (state.Breakout.RangeLevel
                       - bar.Low)
                      / tickSize;

            state.FurthestExcursionTicks =
                Math.Max(
                    state.FurthestExcursionTicks,
                    excursionTicks);

            if (state.FurthestExcursionTicks
                <= OutsideDistanceTicks)
            {
                return;
            }

            state.Armed = true;
            state.ArmedBarIndex = bar.BarIndex;
        }

        private void UpdateRetestState(
            RetestState state,
            CandleSnapshot bar,
            double tickSize)
        {
            var level = state.Breakout.RangeLevel;

            double insideDepthTicks;
            double outsideDistanceTicks;
            bool zoneTouched;
            bool exceededInsideBoundary;

            if (state.Breakout.Direction
                == TradeDirection.Long)
            {
                var zoneTop =
                    level
                    + OutsideDistanceTicks * tickSize;

                var zoneBottom =
                    level
                    - InsideDistanceTicks * tickSize;

                zoneTouched =
                    bar.Low <= zoneTop
                    && bar.High >= zoneBottom;

                exceededInsideBoundary =
                    bar.Low < zoneBottom;

                insideDepthTicks =
                    Math.Max(
                        0,
                        (level - bar.Low) / tickSize);

                outsideDistanceTicks =
                    Math.Max(
                        0,
                        (bar.Low - level) / tickSize);
            }
            else
            {
                var zoneTop =
                    level
                    + InsideDistanceTicks * tickSize;

                var zoneBottom =
                    level
                    - OutsideDistanceTicks * tickSize;

                zoneTouched =
                    bar.High >= zoneBottom
                    && bar.Low <= zoneTop;

                exceededInsideBoundary =
                    bar.High > zoneTop;

                insideDepthTicks =
                    Math.Max(
                        0,
                        (bar.High - level) / tickSize);

                outsideDistanceTicks =
                    Math.Max(
                        0,
                        (level - bar.High) / tickSize);
            }

            if (exceededInsideBoundary)
            {
                state.MaximumInsideDepthTicks =
                    Math.Max(
                        state.MaximumInsideDepthTicks,
                        insideDepthTicks);

                state.Invalidated = true;
                return;
            }

            if (!zoneTouched)
                return;

            state.MaximumInsideDepthTicks =
                Math.Max(
                    state.MaximumInsideDepthTicks,
                    insideDepthTicks);

            state.MinimumOutsideDistanceTicks =
                Math.Min(
                    state.MinimumOutsideDistanceTicks,
                    outsideDistanceTicks);

            if (!state.ZoneTouched)
            {
                state.ZoneTouched = true;
                state.ZoneTouchBarIndex =
                    bar.BarIndex;
            }
        }

        private bool IsConfirmation(
            ModelBarContext context,
            TradeDirection direction)
        {
            if (context.Metrics == null)
                return false;

            var directionOk =
                direction == TradeDirection.Long
                    ? context.Bar.IsBullish
                    : context.Bar.IsBearish;

            var closesReclaimed =
                direction == TradeDirection.Long
                    ? context.Bar.Close
                      > context.Session.PremarketHigh
                    : context.Bar.Close
                      < context.Session.PremarketLow;

            return directionOk
                   && closesReclaimed
                   && context.Metrics.BodyPercent
                   >= MinimumConfirmationBodyPercent;
        }

        private EntryCandidate BuildCandidate(
            ModelBarContext context,
            RetestState state,
            int barsAfter)
        {
            var breakout =
                state.Breakout;

            return new EntryCandidate
            {
                CandidateId =
                    breakout.EventId + "-RETEST",

                BreakoutEventId =
                    breakout.EventId,

                ModelName = Name,

                Direction =
                    breakout.Direction,

                SignalTime =
                    context.Bar.Time,

                SignalBarIndex =
                    context.Bar.BarIndex,

                RangeLevel =
                    breakout.RangeLevel,

                ConfirmationCandle =
                    context.Bar,

                Metrics =
                    context.Metrics,

                BarsAfterBreakout =
                    barsAfter,

                RetestInsideDepthTicks =
                    state.MaximumInsideDepthTicks,

                RetestOutsideDistanceTicks =
                    state.MinimumOutsideDistanceTicks
                    == double.MaxValue
                        ? 0
                        : state.MinimumOutsideDistanceTicks,

                StrongCandleQualified = true,

                DirectionPassed = true,
                BodyPassed = true,
                CloseLocationPassed = true,
                RelativeBodyPassed = true,

                FinalStatus =
                    "SignalQualified",

                QualificationReason =
                    "Breakout moved away from the range, a later bar retested the configured zone, and a subsequent directional confirmation candle closed beyond the broken level.",

                StructuralStopPrice =
                    breakout.Direction
                    == TradeDirection.Long
                        ? context.Bar.Low
                        : context.Bar.High
            };
        }
    }
}