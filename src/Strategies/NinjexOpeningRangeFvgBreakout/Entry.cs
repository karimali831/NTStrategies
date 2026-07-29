using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private void HandleOneMinuteEntryModel()
        {
            if (EnableRangeFilter)
            {
                if (!openingRangeComplete)
                {
                    LogDiagOncePerBar(
                        "OR_NOT_COMPLETE",
                        "Entry blocked: the opening range is not complete.");

                    return;
                }

                if (!IsOpeningRangeValid())
                {
                    var rangeTicks =
                        openingRangeHigh > openingRangeLow
                            ? (openingRangeHigh - openingRangeLow) / TickSize
                            : 0;

                    LogDiagOncePerBar(
                        "INVALID_OR",
                        $"Entry blocked: opening range is invalid. " +
                        $"High={openingRangeHigh}, Low={openingRangeLow}, " +
                        $"Size={rangeTicks:0.##} ticks, Minimum={MinOpeningRangeTicks} ticks.");

                    return;
                }

                if (!printedRangeComplete)
                {
                    printedRangeComplete = true;

                    LogDiag(
                        $"Opening range ready. " +
                        $"High={openingRangeHigh}, Low={openingRangeLow}, " +
                        $"Size={(openingRangeHigh - openingRangeLow) / TickSize:0.##} ticks.");
                }
            }

            var decisionBarTime = ToEastern(Time[0]);

            if (!IsInsideEntryWindow(decisionBarTime))
            {
                LogDiagOncePerBar(
                    "OUTSIDE_ENTRY_WINDOW",
                    $"No entry check: {decisionBarTime:HH:mm:ss} is outside the " +
                    $"{EntryStartTime:000000}-{EntryEndTime:000000} entry window.");

                return;
            }

            if (!CanTradeToday())
            {
                LogDiagOncePerBar(
                    "DAILY_PNL_LIMIT",
                    $"Entry blocked: daily PnL limit reached. " +
                    $"DailyPnL={GetDailyTotalPnl():0.00}.");

                return;
            }

            if (pendingEntry)
            {
                LogDiagOncePerBar(
                    "PENDING_ENTRY",
                    "Entry blocked: an entry order is already pending.");

                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                LogDiagOncePerBar(
                    "POSITION_NOT_FLAT",
                    $"Entry blocked: strategy position is {Position.MarketPosition}.");

                return;
            }

            // [1] is the completed FVG candle and [3] is its reference candle.
            if (CurrentBar < 3)
                return;
            
            /*
             * DIRECT FVG:
             *
             * The indicator detects a completed FVG using:
             *   Bullish: Low[0] > High[2]
             *   Bearish: High[0] < Low[2]
             *
             * Because this method runs on IsFirstTickOfBar:
             *   [1] is the candle that just closed.
             *   [3] is the first candle of its three-candle FVG structure.
             */
            var directBullGapPrice = Low[1] - High[3];
            var directBearGapPrice = Low[3] - High[1];

            var directBullGapTicks = directBullGapPrice / TickSize;
            var directBearGapTicks = directBearGapPrice / TickSize;

            // Match the indicator's structural tests exactly.
            var directBullishFvgExists =
                Low[1] > High[3];

            var directBearishFvgExists =
                High[1] < Low[3];

            var directBullMinGapPassed =
                directBullishFvgExists &&
                directBullGapTicks >= MinFvgGapTicks;

            var directBearMinGapPassed =
                directBearishFvgExists &&
                directBearGapTicks >= MinFvgGapTicks;

            var directBullMaxGapPassed =
                directBullishFvgExists &&
                (MaxFvgGapTicks <= 0 ||
                 directBullGapTicks <= MaxFvgGapTicks);

            var directBearMaxGapPassed =
                directBearishFvgExists &&
                (MaxFvgGapTicks <= 0 ||
                 directBearGapTicks <= MaxFvgGapTicks);

            var directBullishFvg =
                directBullishFvgExists &&
                directBullMinGapPassed &&
                directBullMaxGapPassed;

            var directBearishFvg =
                directBearishFvgExists &&
                directBearMinGapPassed &&
                directBearMaxGapPassed;

            // A breakout requires a close outside the range.
            var signalClosesAboveRange =
                !EnableRangeFilter ||
                Close[1] > openingRangeHigh;

            var signalClosesBelowRange =
                !EnableRangeFilter ||
                Close[1] < openingRangeLow;
            
            // Bullish FVG boundaries:
            // bottom = High[3]
            // top    = Low[1]
            var directBullGapBottom = High[3];
            var directBullGapTop = Low[1];

            // Bearish FVG boundaries:
            // top    = Low[3]
            // bottom = High[1]
            var directBearGapTop = Low[3];
            var directBearGapBottom = High[1];

            // LONG FVG LOCATION
            // Valid when the gap crosses ORH or is entirely above ORH.
            var directBullFvgCrossesOpeningHigh =
                !EnableRangeFilter ||
                (directBullGapBottom <= openingRangeHigh &&
                 directBullGapTop > openingRangeHigh);

            var directBullFvgEntirelyAboveOpeningHigh =
                !EnableRangeFilter ||
                directBullGapBottom > openingRangeHigh;

            // If the full gap is above ORH, measure from ORH to its nearest edge.
            var directBullFvgDistanceTicks =
                !EnableRangeFilter
                    ? 0
                    : Math.Max(
                        0,
                        (directBullGapBottom - openingRangeHigh) / TickSize);

            var directBullFvgWithinMaxDistance =
                !EnableRangeFilter ||
                directBullFvgCrossesOpeningHigh ||
                MaxEntryDistanceFromRangeTicks <= 0 ||
                directBullFvgDistanceTicks <= MaxEntryDistanceFromRangeTicks;

            var directBullFvgValidLocation =
                !EnableRangeFilter ||
                directBullFvgCrossesOpeningHigh ||
                (directBullFvgEntirelyAboveOpeningHigh &&
                 directBullFvgWithinMaxDistance);


            // SHORT FVG LOCATION
            // Valid when the gap crosses ORL or is entirely below ORL.
            var directBearFvgCrossesOpeningLow =
                !EnableRangeFilter ||
                (directBearGapTop >= openingRangeLow &&
                 directBearGapBottom < openingRangeLow);

            var directBearFvgEntirelyBelowOpeningLow =
                !EnableRangeFilter ||
                directBearGapTop < openingRangeLow;

            // If the full gap is below ORL, measure from ORL to its nearest edge.
            var directBearFvgDistanceTicks =
                !EnableRangeFilter
                    ? 0
                    : Math.Max(
                        0,
                        (openingRangeLow - directBearGapTop) / TickSize);

            var directBearFvgWithinMaxDistance =
                !EnableRangeFilter ||
                directBearFvgCrossesOpeningLow ||
                MaxEntryDistanceFromRangeTicks <= 0 ||
                directBearFvgDistanceTicks <= MaxEntryDistanceFromRangeTicks;

            var directBearFvgValidLocation =
                !EnableRangeFilter ||
                directBearFvgCrossesOpeningLow ||
                (directBearFvgEntirelyBelowOpeningLow &&
                 directBearFvgWithinMaxDistance);


            // FINAL DIRECT PATTERNS
            var directLongPattern =
                directBullishFvg &&
                signalClosesAboveRange &&
                directBullFvgValidLocation;

            var directShortPattern =
                directBearishFvg &&
                signalClosesBelowRange &&
                directBearFvgValidLocation;

            var signalBarTime = ToEastern(Time[1]);
            
            if (EnableDiagnostics &&
                (directBullishFvgExists || directBearishFvgExists))
            {
                if (directBearishFvgExists)
                {
                    var bearLocation =
                        directBearFvgCrossesOpeningLow
                            ? "crosses ORL"
                            : directBearFvgEntirelyBelowOpeningLow
                                ? "below ORL"
                                : "inside opening range";

                    LogDiag(
                        $"BEAR FVG LOCATION | " +
                        $"Signal={signalBarTime:HH:mm:ss} | " +
                        $"GapTop={directBearGapTop} | " +
                        $"GapBottom={directBearGapBottom} | " +
                        $"ORL={openingRangeLow} | " +
                        $"Location={bearLocation} | " +
                        $"Distance={directBearFvgDistanceTicks:0.##} ticks | " +
                        $"MaxDistance={MaxEntryDistanceFromRangeTicks} | " +
                        $"LocationEligible={directBearFvgValidLocation}");
                }

                if (directBullishFvgExists)
                {
                    var bullLocation =
                        directBullFvgCrossesOpeningHigh
                            ? "crosses ORH"
                            : directBullFvgEntirelyAboveOpeningHigh
                                ? "above ORH"
                                : "inside opening range";

                    LogDiag(
                        $"BULL FVG LOCATION | " +
                        $"Signal={signalBarTime:HH:mm:ss} | " +
                        $"GapBottom={directBullGapBottom} | " +
                        $"GapTop={directBullGapTop} | " +
                        $"ORH={openingRangeHigh} | " +
                        $"Location={bullLocation} | " +
                        $"Distance={directBullFvgDistanceTicks:0.##} ticks | " +
                        $"MaxDistance={MaxEntryDistanceFromRangeTicks} | " +
                        $"LocationEligible={directBullFvgValidLocation}");
                }
            }

            LogEntryModelDecision(
                signalBarTime,
                decisionBarTime,
                directBullishFvg,
                directBearishFvg,
                signalClosesAboveRange,
                signalClosesBelowRange,
                directBullFvgCrossesOpeningHigh,
                directBearFvgCrossesOpeningLow,
                directBullFvgEntirelyAboveOpeningHigh,
                directBearFvgEntirelyBelowOpeningLow,
                directBullFvgWithinMaxDistance,
                directBearFvgWithinMaxDistance,
                directBullFvgDistanceTicks,
                directBearFvgDistanceTicks,
                directLongPattern,
                directShortPattern);

            if (directLongPattern)
            {
                var approximateEntry = GetCurrentAsk();

                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                // Stop belongs to the completed breakout/signal candle.
                var candleStop = Low[1];

                if (!PrepareLongManagedBracket(approximateEntry, candleStop))
                {
                    LogDiag(
                        $"LONG entry rejected by bracket validation. " +
                        $"ApproxEntry={approximateEntry}, SignalLow={candleStop}.");

                    return;
                }

                pendingEntry = true;
                pendingLong = true;
                pendingStopPrice = activeStopPrice;
                lastEntrySignalBar = CurrentBar;
                
                LogDiag(
                    "LONG ENTRY SUBMITTED | " +
                    $"Signal={signalBarTime:HH:mm:ss} | " +
                    $"EntryBar={decisionBarTime:HH:mm:ss} | " +
                    $"SignalClose={Close[1]} | ORH={openingRangeHigh} | " +
                    $"Distance={directBullFvgDistanceTicks:0.##} ticks | " +
                    $"Gap={directBullGapPrice / TickSize:0.##} ticks | " +
                    $"ApproxEntry={approximateEntry} | Stop={activeStopPrice} | " +
                    $"Target={activeTargetPrice}.");

                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (directShortPattern)
            {
                var approximateEntry = GetCurrentBid();

                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                // Stop belongs to the completed breakout/signal candle.
                var candleStop = High[1];

                if (!PrepareShortManagedBracket(approximateEntry, candleStop))
                {
                    LogDiag(
                        $"SHORT entry rejected by bracket validation. " +
                        $"ApproxEntry={approximateEntry}, SignalHigh={candleStop}.");

                    return;
                }

                pendingEntry = true;
                pendingLong = false;
                pendingStopPrice = activeStopPrice;
                lastEntrySignalBar = CurrentBar;
                
                LogDiag(
                    "SHORT ENTRY SUBMITTED | " +
                    $"Signal={signalBarTime:HH:mm:ss} | " +
                    $"EntryBar={decisionBarTime:HH:mm:ss} | " +
                    $"SignalClose={Close[1]} | ORL={openingRangeLow} | " +
                    $"Distance={directBearFvgDistanceTicks:0.##} ticks | " +
                    $"Gap={directBearGapPrice / TickSize:0.##} ticks | " +
                    $"ApproxEntry={approximateEntry} | Stop={activeStopPrice} | " +
                    $"Target={activeTargetPrice}.");

                EnterShort(Quantity, ShortEntryName);
            }
        }
       
        private bool CanTradeToday()
        {
            return !dailyPnlLimitHit;
        }

        private bool IsOpeningRangeValid()
        {
            if (!openingRangeComplete)
                return false;

            if (openingRangeHigh <= openingRangeLow)
                return false;

            var rangeTicks = (openingRangeHigh - openingRangeLow) / TickSize;

            return rangeTicks >= MinOpeningRangeTicks;
        }

        private bool IsInsideEntryWindow(DateTime easternBarTime)
        {
            var start = activeEasternDate.Add(ToTimeSpan(EntryStartTime));
            var end = activeEasternDate.Add(ToTimeSpan(EntryEndTime));

            return easternBarTime >= start && easternBarTime <= end;
        }
    }
}