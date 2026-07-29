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

            // We need [4] to detect an FVG confirmed on candle [2].
            if (CurrentBar < 5)
                return;

            var minGapPrice = MinFvgGapTicks * TickSize;
            var maxGapPrice = MaxFvgGapTicks * TickSize;

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

            var directBullishFvg =
                Low[1] > High[3] &&
                directBullGapPrice >= minGapPrice &&
                (MaxFvgGapTicks <= 0 || directBullGapPrice <= maxGapPrice);

            var directBearishFvg =
                High[1] < Low[3] &&
                directBearGapPrice >= minGapPrice &&
                (MaxFvgGapTicks <= 0 || directBearGapPrice <= maxGapPrice);

            /*
             * PRIOR-CANDLE FVG:
             *
             * This detects an FVG that completed on candle [2].
             * Candle [1] is then allowed to be the one immediate breakout candle.
             */
            var priorBullGapPrice = Low[2] - High[4];
            var priorBearGapPrice = Low[4] - High[2];

            var priorBullishFvg =
                Low[2] > High[4] &&
                priorBullGapPrice >= minGapPrice &&
                (MaxFvgGapTicks <= 0 || priorBullGapPrice <= maxGapPrice);

            var priorBearishFvg =
                High[2] < Low[4] &&
                priorBearGapPrice >= minGapPrice &&
                (MaxFvgGapTicks <= 0 || priorBearGapPrice <= maxGapPrice);

            // A breakout requires a close outside the range.
            var signalClosesAboveRange =
                !EnableRangeFilter ||
                Close[1] > openingRangeHigh;

            var signalClosesBelowRange =
                !EnableRangeFilter ||
                Close[1] < openingRangeLow;

            // A continuation setup is only needed when the original FVG candle
            // closed inside the opening range.
            var priorBullFvgClosedInsideRange =
                EnableRangeFilter &&
                Close[2] <= openingRangeHigh;

            var priorBearFvgClosedInsideRange =
                EnableRangeFilter &&
                Close[2] >= openingRangeLow;

            var directLongPattern =
                directBullishFvg &&
                signalClosesAboveRange;

            var directShortPattern =
                directBearishFvg &&
                signalClosesBelowRange;

            var continuationLongPattern =
                priorBullishFvg &&
                priorBullFvgClosedInsideRange &&
                signalClosesAboveRange;

            var continuationShortPattern =
                priorBearishFvg &&
                priorBearFvgClosedInsideRange &&
                signalClosesBelowRange;

            /*
             * Distance is measured from the completed breakout candle's close.
             * This makes the eligibility result stable and prevents the new
             * candle's bid/ask movement from changing a confirmed signal.
             */
            var longDistanceTicks =
                EnableRangeFilter
                    ? Math.Max(0, (Close[1] - openingRangeHigh) / TickSize)
                    : 0;

            var shortDistanceTicks =
                EnableRangeFilter
                    ? Math.Max(0, (openingRangeLow - Close[1]) / TickSize)
                    : 0;

            var longWithinEntryDistance =
                !EnableRangeFilter ||
                MaxEntryDistanceFromRangeTicks <= 0 ||
                longDistanceTicks <= MaxEntryDistanceFromRangeTicks;

            var shortWithinEntryDistance =
                !EnableRangeFilter ||
                MaxEntryDistanceFromRangeTicks <= 0 ||
                shortDistanceTicks <= MaxEntryDistanceFromRangeTicks;

            var validLongFvg =
                (directLongPattern || continuationLongPattern) &&
                longWithinEntryDistance;

            var validShortFvg =
                (directShortPattern || continuationShortPattern) &&
                shortWithinEntryDistance;

            var signalBarTime = ToEastern(Time[1]);

            LogEntryModelDecision(
                signalBarTime,
                decisionBarTime,
                directBullishFvg,
                directBearishFvg,
                priorBullishFvg,
                priorBearishFvg,
                directLongPattern,
                directShortPattern,
                continuationLongPattern,
                continuationShortPattern,
                longDistanceTicks,
                shortDistanceTicks,
                validLongFvg,
                validShortFvg);

            if (validLongFvg)
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

                var setupType =
                    directLongPattern
                        ? "direct FVG breakout"
                        : "one-candle continuation breakout";

                LogDiag(
                    $"LONG ENTRY SUBMITTED | Setup={setupType} | " +
                    $"Signal={signalBarTime:HH:mm:ss} | " +
                    $"EntryBar={decisionBarTime:HH:mm:ss} | " +
                    $"SignalClose={Close[1]} | ORH={openingRangeHigh} | " +
                    $"Distance={longDistanceTicks:0.##} ticks | " +
                    $"Gap={(directLongPattern ? directBullGapPrice : priorBullGapPrice) / TickSize:0.##} ticks | " +
                    $"ApproxEntry={approximateEntry} | Stop={activeStopPrice} | " +
                    $"Target={activeTargetPrice}.");

                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (validShortFvg)
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

                var setupType =
                    directShortPattern
                        ? "direct FVG breakout"
                        : "one-candle continuation breakout";

                LogDiag(
                    $"SHORT ENTRY SUBMITTED | Setup={setupType} | " +
                    $"Signal={signalBarTime:HH:mm:ss} | " +
                    $"EntryBar={decisionBarTime:HH:mm:ss} | " +
                    $"SignalClose={Close[1]} | ORL={openingRangeLow} | " +
                    $"Distance={shortDistanceTicks:0.##} ticks | " +
                    $"Gap={(directShortPattern ? directBearGapPrice : priorBearGapPrice) / TickSize:0.##} ticks | " +
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