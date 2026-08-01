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
            public bool ZoneTouched { get; set; }
            public bool Invalidated { get; set; }
            public int ZoneTouchBarIndex { get; set; }
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
            if (breakoutEvent == null)
                return;

            states.Add(new RetestState
            {
                Breakout = breakoutEvent,
                ZoneTouchBarIndex = -1,
                MinimumOutsideDistanceTicks = double.MaxValue
            });
        }

        public IEnumerable<EntryCandidate> Evaluate(ModelBarContext context)
        {
            var candidates = new List<EntryCandidate>();

            if (!IsEnabled || context == null || context.Bar == null)
                return candidates;

            var tickSize = context.Session.TickSize;

            for (var i = states.Count - 1; i >= 0; i--)
            {
                var state = states[i];
                var barsAfter =
                    context.Bar.BarIndex - state.Breakout.BreakoutBarIndex;

                if (barsAfter <= 0)
                    continue;

                if (barsAfter > MaximumBarsAfterBreakout)
                {
                    states.RemoveAt(i);
                    continue;
                }

                UpdateRetestState(state, context.Bar, tickSize);

                if (state.Invalidated)
                {
                    states.RemoveAt(i);
                    continue;
                }

                if (!state.ZoneTouched)
                    continue;

                var confirmed = IsConfirmation(context, state.Breakout.Direction);
                if (!confirmed)
                    continue;

                candidates.Add(BuildCandidate(context, state, barsAfter));
                states.RemoveAt(i);
            }

            return candidates;
        }
        
        private void UpdateRetestState(
            RetestState state,
            CandleSnapshot bar,
            double tickSize)
        {
            var level = state.Breakout.RangeLevel;

            if (state.Breakout.Direction == TradeDirection.Long)
            {
                var zoneTop = level + OutsideDistanceTicks * tickSize;
                var zoneBottom = level - InsideDistanceTicks * tickSize;

                var touched = bar.Low <= zoneTop && bar.High >= zoneBottom;
                if (touched)
                {
                    state.ZoneTouched = true;
                    if (state.ZoneTouchBarIndex < 0)
                        state.ZoneTouchBarIndex = bar.BarIndex;

                    var insideDepth =
                        Math.Max(0, (level - bar.Low) / tickSize);
                    var outsideDistance =
                        Math.Max(0, (bar.Low - level) / tickSize);

                    state.MaximumInsideDepthTicks =
                        Math.Max(state.MaximumInsideDepthTicks, insideDepth);
                    state.MinimumOutsideDistanceTicks =
                        Math.Min(state.MinimumOutsideDistanceTicks, outsideDistance);
                }

                if (bar.Low < zoneBottom)
                    state.Invalidated = true;
            }
            else
            {
                var zoneTop = level + InsideDistanceTicks * tickSize;
                var zoneBottom = level - OutsideDistanceTicks * tickSize;

                var touched = bar.High >= zoneBottom && bar.Low <= zoneTop;
                if (touched)
                {
                    state.ZoneTouched = true;
                    if (state.ZoneTouchBarIndex < 0)
                        state.ZoneTouchBarIndex = bar.BarIndex;

                    var insideDepth =
                        Math.Max(0, (bar.High - level) / tickSize);
                    var outsideDistance =
                        Math.Max(0, (level - bar.High) / tickSize);

                    state.MaximumInsideDepthTicks =
                        Math.Max(state.MaximumInsideDepthTicks, insideDepth);
                    state.MinimumOutsideDistanceTicks =
                        Math.Min(state.MinimumOutsideDistanceTicks, outsideDistance);
                }

                if (bar.High > zoneTop)
                    state.Invalidated = true;
            }
        }

        private bool IsConfirmation(
            ModelBarContext context,
            TradeDirection direction)
        {
            if (context.Metrics == null)
                return false;

            var directionOk = direction == TradeDirection.Long
                ? context.Bar.IsBullish
                : context.Bar.IsBearish;

            var closesReclaimed = direction == TradeDirection.Long
                ? context.Bar.Close > context.Session.PremarketHigh
                : context.Bar.Close < context.Session.PremarketLow;

            return directionOk
                   && closesReclaimed
                   && context.Metrics.BodyPercent >= MinimumConfirmationBodyPercent;
        }

        private EntryCandidate BuildCandidate(
            ModelBarContext context,
            RetestState state,
            int barsAfter)
        {
            var breakout = state.Breakout;

            return new EntryCandidate
            {
                CandidateId = breakout.EventId + "-RETEST",
                BreakoutEventId = breakout.EventId,
                ModelName = Name,
                Direction = breakout.Direction,
                SignalTime = context.Bar.Time,
                SignalBarIndex = context.Bar.BarIndex,
                RangeLevel = breakout.RangeLevel,
                ConfirmationCandle = context.Bar,
                Metrics = context.Metrics,
                BarsAfterBreakout = barsAfter,
                RetestInsideDepthTicks = state.MaximumInsideDepthTicks,
                RetestOutsideDistanceTicks =
                    state.MinimumOutsideDistanceTicks == double.MaxValue
                        ? 0
                        : state.MinimumOutsideDistanceTicks,
                StrongCandleQualified = true,
                DirectionPassed = true,
                BodyPassed = true,
                CloseLocationPassed = true,
                RelativeBodyPassed = true,
                FinalStatus = "SignalQualified",
                QualificationReason = "Retest zone touched and directional confirmation candle closed beyond the broken level.",
                StructuralStopPrice = breakout.Direction == TradeDirection.Long
                    ? context.Bar.Low
                    : context.Bar.High
            };
        }
    }
}
