#region Using declarations
using System;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis
{
    public sealed class BreakoutEventDetector
    {
        private readonly Dictionary<TradeDirection, int> attemptCounts = new Dictionary<TradeDirection, int>();
        private DateTime activeRangeDate = DateTime.MinValue;
        private bool longBreakoutActive;
        private bool shortBreakoutActive;

        public void Reset(DateTime tradingDate)
        {
            activeRangeDate = tradingDate;
            attemptCounts.Clear();
            longBreakoutActive = false;
            shortBreakoutActive = false;
        }

        public IList<BreakoutEvent> Detect(ModelBarContext context, int minimumDistanceTicks)
        {
            var events = new List<BreakoutEvent>();
            if (context?.Session == null || context.Bar == null)
                return events;

            if (activeRangeDate != context.Session.TradingDate)
                Reset(context.Session.TradingDate);

            var tickSize = context.Session.TickSize;
            if (tickSize <= 0)
                return events;

            var highLevel = context.Session.PremarketHigh;
            var lowLevel = context.Session.PremarketLow;
            var highThreshold = highLevel + minimumDistanceTicks * tickSize;
            var lowThreshold = lowLevel - minimumDistanceTicks * tickSize;

            var longQualifies = context.Bar.Close >= highThreshold;
            var shortQualifies = context.Bar.Close <= lowThreshold;

            if (context.Bar.Close <= highLevel)
                longBreakoutActive = false;
            if (context.Bar.Close >= lowLevel)
                shortBreakoutActive = false;

            if (longQualifies && !longBreakoutActive)
            {
                events.Add(CreateEvent(context, TradeDirection.Long));
                longBreakoutActive = true;
            }

            if (shortQualifies && !shortBreakoutActive)
            {
                events.Add(CreateEvent(context, TradeDirection.Short));
                shortBreakoutActive = true;
            }

            return events;
        }

        private BreakoutEvent CreateEvent(ModelBarContext context, TradeDirection direction)
        {
            var attempt = 1;
            if (attemptCounts.TryGetValue(direction, out int count))
                attempt = count + 1;
            attemptCounts[direction] = attempt;

            var level = direction == TradeDirection.Long
                ? context.Session.PremarketHigh
                : context.Session.PremarketLow;

            var distanceTicks = direction == TradeDirection.Long
                ? (context.Bar.Close - level) / context.Session.TickSize
                : (level - context.Bar.Close) / context.Session.TickSize;

            var side = direction == TradeDirection.Long ? "LONG" : "SHORT";

            return new BreakoutEvent
            {
                EventId =
                    $"{context.Session.TradingDate:yyyyMMdd}" +
                    $"-{side}-{attempt:00}",

                TradingDate = context.Session.TradingDate,
                Contract = context.Session.Contract,

                Direction = direction,
                AttemptNumber = attempt,

                BreakoutTime = context.Bar.Time,
                BreakoutBarIndex = context.Bar.BarIndex,

                RangeLevel = level,
                BreakoutClose = context.Bar.Close,
                DistanceOutsideTicks = distanceTicks,

                Candle = context.Bar,
                Metrics = context.Metrics,

                RawRetestMinimumOutsideDistanceTicks =
                    double.MaxValue,

                RawRetestStatus = "NotObserved",
            };
        }
    }
}
