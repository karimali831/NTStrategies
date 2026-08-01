using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch : Strategy
    {
        private void UpdateRawRetestArm(
            BreakoutEvent breakout,
            CandleSnapshot bar,
            double tickSize)
        {
            if (breakout == null
                || bar == null
                || tickSize <= 0
                || breakout.RawRetestObserved
                || breakout.RawRetestArmed)
            {
                return;
            }

            double excursionTicks =
                breakout.Direction == TradeDirection.Long
                    ? (bar.High - breakout.RangeLevel) / tickSize
                    : (breakout.RangeLevel - bar.Low) / tickSize;

            breakout.FurthestExcursionBeforeRawRetestTicks =
                Math.Max(
                    breakout.FurthestExcursionBeforeRawRetestTicks,
                    excursionTicks);

            if (breakout.FurthestExcursionBeforeRawRetestTicks
                <= RetestOutsideDistanceTicks)
            {
                return;
            }

            breakout.RawRetestArmed = true;
            breakout.RawRetestArmedBarIndex = bar.BarIndex;

            ExportBreakoutAudit(
                "RawRetestArmed",
                breakout);

            FlushExportWriters();

            Diagnostic(
                bar.Time,
                "RAW RETEST ARMED {0} Bar={1} " +
                "Excursion={2:0.0} ticks",
                breakout.EventId,
                bar.BarIndex,
                breakout.FurthestExcursionBeforeRawRetestTicks);
        }
        
        private void UpdateRawRetestObservations(
            ModelBarContext context)
        {
            if (context?.Session == null
                || context.Bar == null)
            {
                return;
            }

            var tickSize = context.Session.TickSize;

            if (tickSize <= 0)
                return;

            foreach (var breakout in breakoutEvents)
            {
                if (breakout == null
                    || breakout.IsResolved)
                {
                    continue;
                }

                var barsAfter =
                    context.Bar.BarIndex
                    - breakout.BreakoutBarIndex;

                if (barsAfter <= 0)
                    continue;

                ObserveRawRetest(
                    breakout,
                    context,
                    barsAfter,
                    tickSize);
            }
        }
        
        private void ObserveRawRetest(
            BreakoutEvent breakout,
            ModelBarContext context,
            int barsAfter,
            double tickSize)
        {
            var bar = context.Bar;
            var level = breakout.RangeLevel;

            // Update continuation after an already-observed retest on every bar.
            UpdatePostRetestExcursion(
                breakout,
                bar,
                tickSize);

            // A retest cannot exist until price has first moved away from the level.
            UpdateRawRetestArm(
                breakout,
                bar,
                tickSize);

            if (!breakout.RawRetestArmed
                || bar.BarIndex <= breakout.RawRetestArmedBarIndex)
            {
                return;
            }

            double insideDepthTicks;
            double outsideDistanceTicks;
            bool zoneTouched;
            bool exactLevelTouched;

            if (breakout.Direction == TradeDirection.Long)
            {
                double zoneTop =
                    level
                    + RetestOutsideDistanceTicks * tickSize;

                double zoneBottom =
                    level
                    - RetestInsideDistanceTicks * tickSize;

                zoneTouched =
                    bar.Low <= zoneTop
                    && bar.High >= zoneBottom;

                exactLevelTouched =
                    bar.Low <= level
                    && bar.High >= level;

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
                double zoneTop =
                    level
                    + RetestInsideDistanceTicks * tickSize;

                double zoneBottom =
                    level
                    - RetestOutsideDistanceTicks * tickSize;

                zoneTouched =
                    bar.High >= zoneBottom
                    && bar.Low <= zoneTop;

                exactLevelTouched =
                    bar.High >= level
                    && bar.Low <= level;

                insideDepthTicks =
                    Math.Max(
                        0,
                        (bar.High - level) / tickSize);

                outsideDistanceTicks =
                    Math.Max(
                        0,
                        (level - bar.High) / tickSize);
            }

            if (!zoneTouched)
                return;

            breakout.RawRetestMaximumInsideDepthTicks =
                Math.Max(
                    breakout.RawRetestMaximumInsideDepthTicks,
                    insideDepthTicks);

            breakout.RawRetestWithinDepthTolerance =
                breakout.RawRetestMaximumInsideDepthTicks
                <= RetestInsideDistanceTicks;

            breakout.RawRetestMinimumOutsideDistanceTicks =
                Math.Min(
                    breakout.RawRetestMinimumOutsideDistanceTicks,
                    outsideDistanceTicks);

            breakout.RawRetestTouchedExactLevel |=
                exactLevelTouched;
            
            if (breakout.RawRetestObserved
                && !breakout.RawRetestWithinDepthTolerance)
            {
                breakout.RawRetestStatus =
                    "ObservedBeyondInsideTolerance";
            }

            if (!breakout.RawRetestObserved)
            {
                breakout.RawRetestObserved = true;

                breakout.FirstRawRetestTime =
                    bar.Time;

                breakout.FirstRawRetestBarIndex =
                    bar.BarIndex;

                breakout.FirstRawRetestBarsAfterBreakout =
                    barsAfter;

                breakout.FirstRawRetestMinutesAfterBreakout =
                    (bar.Time - breakout.BreakoutTime)
                    .TotalMinutes;

                breakout.RawRetestWasWithinModelBarWindow =
                    barsAfter <= MaximumRetestBars;

                breakout.MfeBeforeRawRetestTicks =
                    breakout.MfeTicks;
                
                breakout.FirstRawRetestReferencePrice =
                    breakout.Direction == TradeDirection.Long
                        ? bar.Low
                        : bar.High;

                if (!breakout.RawRetestWithinDepthTolerance)
                {
                    breakout.RawRetestStatus =
                        "ObservedBeyondInsideTolerance";
                }
                else
                {
                    breakout.RawRetestStatus =
                        breakout.RawRetestWasWithinModelBarWindow
                            ? "ObservedWithinModelWindow"
                            : "ObservedOutsideModelWindow";
                }

                ExportBreakoutAudit(
                    "RawRetestObserved",
                    breakout);

                FlushExportWriters();

                Diagnostic(
                    bar.Time,
                    "RAW RETEST {0} BarsAfter={1} " +
                    "MinutesAfter={2:0.0} " +
                    "InsideDepth={3:0.0} " +
                    "OutsideDistance={4:0.0} " +
                    "ExactLevel={5} " +
                    "WithinModelBarWindow={6}",
                    breakout.EventId,
                    barsAfter,
                    breakout.FirstRawRetestMinutesAfterBreakout,
                    insideDepthTicks,
                    outsideDistanceTicks,
                    exactLevelTouched,
                    breakout.RawRetestWasWithinModelBarWindow);
            }

            CheckRawRetestConfirmation(
                breakout,
                context);
        }
        
        private void UpdatePostRetestExcursion(
            BreakoutEvent breakout,
            CandleSnapshot bar,
            double tickSize)
        {
            if (breakout == null
                || bar == null
                || tickSize <= 0
                || !breakout.RawRetestObserved
                || breakout.FirstRawRetestReferencePrice <= 0)
            {
                return;
            }

            double favorableTicks =
                breakout.Direction == TradeDirection.Long
                    ? (bar.High
                       - breakout.FirstRawRetestReferencePrice)
                      / tickSize
                    : (breakout.FirstRawRetestReferencePrice
                       - bar.Low)
                      / tickSize;

            breakout.MfeAfterRawRetestTicks =
                Math.Max(
                    breakout.MfeAfterRawRetestTicks,
                    Math.Max(0, favorableTicks));
        }
        
        private void CheckRawRetestConfirmation(
            BreakoutEvent breakout,
            ModelBarContext context)
        {
            if (!breakout.RawRetestObserved
                || breakout.RawRetestConfirmed)
            {
                return;
            }

            if (!breakout.RawRetestWithinDepthTolerance)
                return;

            if (context.Bar.BarIndex
                <= breakout.FirstRawRetestBarIndex)
            {
                return;
            }

            // Existing directional/body confirmation follows.

            var directionalCandle =
                breakout.Direction == TradeDirection.Long
                    ? context.Bar.IsBullish
                    : context.Bar.IsBearish;

            var closedBeyondLevel =
                breakout.Direction == TradeDirection.Long
                    ? context.Bar.Close
                      > breakout.RangeLevel
                    : context.Bar.Close
                      < breakout.RangeLevel;

            bool bodyQualified =
                context.Metrics != null
                && context.Metrics.BodyPercent
                >= MinimumRetestConfirmationBodyPercent;

            if (!directionalCandle
                || !closedBeyondLevel
                || !bodyQualified)
            {
                return;
            }

            breakout.RawRetestConfirmed = true;
            breakout.RawRetestConfirmationTime =
                context.Bar.Time;

            breakout.RawRetestStatus =
                breakout.RawRetestWasWithinModelBarWindow
                    ? "ConfirmedWithinModelWindow"
                    : "ConfirmedOutsideModelWindow";

            ExportBreakoutAudit(
                "RawRetestConfirmed",
                breakout);

            FlushExportWriters();

            Diagnostic(
                context.Bar.Time,
                "RAW RETEST CONFIRMED {0} " +
                "EligibleForEntryModel={1}",
                breakout.EventId,
                breakout.RawRetestWasWithinModelBarWindow);
        }
    }
}