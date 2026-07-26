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
                    LogDiagOncePerBar("OR_NOT_COMPLETE", "Blocked: opening range not complete.");
                    return;
                }

                if (!IsOpeningRangeValid())
                {
                    LogDiagOncePerBar(
                        "INVALID_OR",
                        $"Blocked: invalid opening range. ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                        $"RangeTicks={(openingRangeHigh - openingRangeLow) / TickSize}");

                    return;
                }

                if (!printedRangeComplete)
                {
                    printedRangeComplete = true;
                    LogDiag($"Opening range complete. High={openingRangeHigh}, Low={openingRangeLow}");
                }
            }
            
            var easternBarTime = ToEastern(Time[0]);

            if (!IsInsideEntryWindow(easternBarTime))
            {
                LogDiagOncePerBar(
                    "OUTSIDE_ENTRY_WINDOW",
                    $"Blocked: outside entry window. Time={easternBarTime:HH:mm:ss}");

                return;
            }

            if (!CanTradeToday())
            {
                LogDiagOncePerBar(
                    "DAILY_PNL_LIMIT",
                    $"Blocked: daily PnL limit reached. DailyPnL={GetDailyTotalPnl():0.00}");

                return;
            }

            if (pendingEntry)
            {
                LogDiagOncePerBar("PENDING_ENTRY", "Blocked: pending entry already active.");
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                LogDiagOncePerBar(
                    "POSITION_NOT_FLAT",
                    $"Blocked: position not flat. Position={Position.MarketPosition}");

                return;
            }

            if (CurrentBar < 5)
                return;

            // Prevent duplicate entries on the same live/forming candle.
            if (CurrentBar == lastEntrySignalBar)
                return;

            var minGap = MinFvgGapTicks * TickSize;
            var maxGap = MaxFvgGapTicks * TickSize;

            var minDistance = MinFvgDistanceFromRangeTicks * TickSize;
            var maxDistance = MaxFvgDistanceFromRangeTicks * TickSize;

            // LIVE FVG MODEL:
            // [0] = current forming FVG candle
            // [1] = middle candle
            // [2] = first candle of the 3-candle FVG structure
            //
            // This enters one bar earlier than the confirmed [1]/[3] model.

            var bullishFvg =
                Low[0] > High[2] &&
                (Low[0] - High[2]) >= minGap;

            var bearishFvg =
                High[0] < Low[2] &&
                (Low[2] - High[0]) >= minGap;

            // Bullish FVG gap:
            // lower boundary = High[2]
            // upper boundary = Low[0]
            var bullGapBottom = High[2];
            var bullGapTop = Low[0];

            // Bearish FVG gap:
            // upper boundary = Low[2]
            // lower boundary = High[0]
            var bearGapTop = Low[2];
            var bearGapBottom = High[0];

            var bullGapSize = bullGapTop - bullGapBottom;
            var bearGapSize = bearGapTop - bearGapBottom;

            var bullishFvgWithinGapSize =
                bullishFvg &&
                (MaxFvgGapTicks <= 0 || bullGapSize <= maxGap);

            var bearishFvgWithinGapSize =
                bearishFvg &&
                (MaxFvgGapTicks <= 0 || bearGapSize <= maxGap);

            var bullishFvgDistanceFromOpeningHigh = EnableRangeFilter
                ? Math.Max(0, bullGapBottom - openingRangeHigh)
                : 0;

            var bearishFvgDistanceFromOpeningLow = EnableRangeFilter
                ? Math.Max(0, openingRangeLow - bearGapTop)
                : 0;

            var bullishFvgWithinMaxDistance =
                !EnableRangeFilter ||
                MaxFvgDistanceFromRangeTicks <= 0 ||
                bullishFvgDistanceFromOpeningHigh <= maxDistance;

            var bearishFvgWithinMaxDistance =
                !EnableRangeFilter ||
                MaxFvgDistanceFromRangeTicks <= 0 ||
                bearishFvgDistanceFromOpeningLow <= maxDistance;

            // Valid long if:
            // 1. Current price is above OR high.
            // AND
            // 2. FVG crosses OR high: bottom <= ORH and top >= ORH
            // OR full FVG is above OR high, respecting MinFvgDistanceFromRangeTicks.
            var bullishFvgCrossesOpeningHigh =
                !EnableRangeFilter ||
                bullishFvg &&
                bullGapBottom <= openingRangeHigh &&
                bullGapTop >= openingRangeHigh;

            var bullishFvgAboveOpeningHigh =
                !EnableRangeFilter ||
                bullishFvg &&
                bullGapBottom >= openingRangeHigh + minDistance;

            // Directional price gates.
            // These force the live entry to respect the opening range boundary.
            var currentLongPrice = GetCurrentAsk();
            if (currentLongPrice <= 0)
                currentLongPrice = Close[0];

            var currentShortPrice = GetCurrentBid();
            if (currentShortPrice <= 0)
                currentShortPrice = Close[0];

            var priceAboveOpeningHigh =
                !EnableRangeFilter || currentLongPrice > openingRangeHigh;

            var priceBelowOpeningLow =
                !EnableRangeFilter || currentShortPrice < openingRangeLow;

            var liveLongCandleOutsideRange =
                !EnableRangeFilter || Low[0] > openingRangeHigh;

            var liveShortCandleOutsideRange =
                !EnableRangeFilter || High[0] < openingRangeLow;

            var validLongFvg =
                bullishFvg &&
                priceAboveOpeningHigh &&
                liveLongCandleOutsideRange &&
                (bullishFvgCrossesOpeningHigh || bullishFvgAboveOpeningHigh) &&
                bullishFvgWithinGapSize &&
                bullishFvgWithinMaxDistance;
            
            // Valid short if:
            // 1. FVG crosses OR low: top >= ORL and bottom <= ORL
            // OR
            // 2. Full FVG is below OR low, respecting MinFvgDistanceFromRangeTicks.
            // AND current price is below OR low.
            var bearishFvgCrossesOpeningLow =
                !EnableRangeFilter ||
                bearishFvg &&
                bearGapTop >= openingRangeLow &&
                bearGapBottom <= openingRangeLow;

            var bearishFvgBelowOpeningLow =
                !EnableRangeFilter ||
                bearishFvg &&
                bearGapTop <= openingRangeLow - minDistance;

            var validShortFvg =
                bearishFvg &&
                priceBelowOpeningLow &&
                liveShortCandleOutsideRange &&
                (bearishFvgCrossesOpeningLow || bearishFvgBelowOpeningLow) &&
                bearishFvgWithinGapSize &&
                bearishFvgWithinMaxDistance;

            var bullGapTicks = bullGapSize / TickSize;
            var bearGapTicks = bearGapSize / TickSize;

            var bullDistanceTicks = bullishFvgDistanceFromOpeningHigh / TickSize;
            var bearDistanceTicks = bearishFvgDistanceFromOpeningLow / TickSize;

            var maxGapTicks = MaxFvgGapTicks;
            var maxDistanceTicks = MaxFvgDistanceFromRangeTicks;

            var longFailReason = BuildFvgFailReason(
                priceAboveOpeningHigh,
                liveLongCandleOutsideRange,
                bullishFvg,
                bullishFvgCrossesOpeningHigh,
                bullishFvgAboveOpeningHigh,
                bullishFvgWithinGapSize,
                bullishFvgWithinMaxDistance,
                bullGapTicks,
                maxGapTicks,
                bullDistanceTicks,
                maxDistanceTicks);

            var shortFailReason = BuildFvgFailReason(
                priceBelowOpeningLow,
                liveShortCandleOutsideRange,
                bearishFvg,
                bearishFvgCrossesOpeningLow,
                bearishFvgBelowOpeningLow,
                bearishFvgWithinGapSize,
                bearishFvgWithinMaxDistance,
                bearGapTicks,
                maxGapTicks,
                bearDistanceTicks,
                maxDistanceTicks);

            if (validLongFvg)
            {
                LogEntryDiagnostic(
                    "LONG",
                    true,
                    "VALID ENTRY",
                    longFailReason,
                    currentLongPrice,
                    priceAboveOpeningHigh,
                    liveLongCandleOutsideRange,
                    bullishFvg,
                    bullishFvgCrossesOpeningHigh,
                    bullishFvgAboveOpeningHigh,
                    bullishFvgWithinGapSize,
                    bullishFvgWithinMaxDistance,
                    bullGapTicks,
                    MinFvgGapTicks,
                    maxGapTicks,
                    bullDistanceTicks,
                    maxDistanceTicks,
                    Low[0],
                    GetDailyTotalPnl());
            }
            else if (validShortFvg)
            {
                LogEntryDiagnostic(
                    "SHORT",
                    true,
                    "VALID ENTRY",
                    shortFailReason,
                    currentShortPrice,
                    priceBelowOpeningLow,
                    liveShortCandleOutsideRange,
                    bearishFvg,
                    bearishFvgCrossesOpeningLow,
                    bearishFvgBelowOpeningLow,
                    bearishFvgWithinGapSize,
                    bearishFvgWithinMaxDistance,
                    bearGapTicks,
                    MinFvgGapTicks,
                    maxGapTicks,
                    bearDistanceTicks,
                    maxDistanceTicks,
                    High[0],
                    GetDailyTotalPnl());
            }
            else if (priceAboveOpeningHigh || bullishFvg)
            {
                LogEntryDiagnostic(
                    "LONG",
                    false,
                    "NO ENTRY",
                    longFailReason,
                    currentLongPrice,
                    priceAboveOpeningHigh,
                    liveLongCandleOutsideRange,
                    bullishFvg,
                    bullishFvgCrossesOpeningHigh,
                    bullishFvgAboveOpeningHigh,
                    bullishFvgWithinGapSize,
                    bullishFvgWithinMaxDistance,
                    bullGapTicks,
                    MinFvgGapTicks,
                    maxGapTicks,
                    bullDistanceTicks,
                    maxDistanceTicks,
                    Low[0],
                    GetDailyTotalPnl());
            }
            else if (priceBelowOpeningLow || bearishFvg)
            {
                LogEntryDiagnostic(
                    "SHORT",
                    false,
                    "NO ENTRY",
                    shortFailReason,
                    currentShortPrice,
                    priceBelowOpeningLow,
                    liveShortCandleOutsideRange,
                    bearishFvg,
                    bearishFvgCrossesOpeningLow,
                    bearishFvgBelowOpeningLow,
                    bearishFvgWithinGapSize,
                    bearishFvgWithinMaxDistance,
                    bearGapTicks,
                    MinFvgGapTicks,
                    maxGapTicks,
                    bearDistanceTicks,
                    maxDistanceTicks,
                    High[0],
                    GetDailyTotalPnl());
            }

            if (validLongFvg)
            {
                var approximateEntry = GetCurrentAsk();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                // Live model:
                // If MaxStopTicks > 0, PrepareLongManagedBracket uses fixed max stop.
                // If MaxStopTicks == 0, use the low of the current/live FVG candle.
                var candleStop = Low[0];

                if (!PrepareLongManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = true;
                lastEntrySignalBar = CurrentBar;

                LogDiag($"LONG submitted. ApproxEntry={approximateEntry}, Stop={activeStopPrice}, Target={activeTargetPrice}");
                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (validShortFvg)
            {
                var approximateEntry = GetCurrentBid();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                // Live model:
                // If MaxStopTicks > 0, PrepareShortManagedBracket uses fixed max stop.
                // If MaxStopTicks == 0, use the high of the current/live FVG candle.
                var candleStop = High[0];

                if (!PrepareShortManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = false;
                lastEntrySignalBar = CurrentBar;

                LogDiag($"SHORT submitted. ApproxEntry={approximateEntry}, Stop={activeStopPrice}, Target={activeTargetPrice}");
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