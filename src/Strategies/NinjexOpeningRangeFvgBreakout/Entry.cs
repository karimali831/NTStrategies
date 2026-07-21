using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
       private void HandleOneMinuteEntryModel()
        {
            var easternBarTime = ToEastern(Time[0]);

            if (activeEasternDate != easternBarTime.Date)
                ResetForNewDay(easternBarTime.Date);

            if (!openingRangeComplete)
            {
                LogDiag("Blocked: opening range not complete.");
                return;
            }

            if (!IsOpeningRangeValid())
            {
                LogDiag(
                    $"Blocked: invalid opening range. ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                    $"RangeTicks={(openingRangeHigh - openingRangeLow) / TickSize}");

                return;
            }

            if (!printedRangeComplete)
            {
                printedRangeComplete = true;
                LogDiag($"Opening range complete. High={openingRangeHigh}, Low={openingRangeLow}");
            }

            if (!IsInsideEntryWindow(easternBarTime))
            {
                LogDiag($"Blocked: outside entry window. Time={easternBarTime:HH:mm:ss}");
                return;
            }

            if (!CanTradeToday())
            {
                LogDiag($"Blocked: daily PnL limit reached. DailyPnL={GetDailyTotalPnl():0.00}");
                return;
            }

            if (pendingEntry)
            {
                LogDiag("Blocked: pending entry already active.");
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                LogDiag($"Blocked: position not flat. Position={Position.MarketPosition}");
                return;
            }

            if (CurrentBar < 5)
                return;

            var minGap = MinFvgGapTicks * TickSize;
            var maxGap = MaxFvgGapTicks * TickSize;

            var minDistance = MinFvgDistanceFromRangeTicks * TickSize;
            var maxDistance = MaxFvgDistanceFromRangeTicks * TickSize;

            // With Calculate.OnEachTick + IsFirstTickOfBar:
            // [1] = candle that just closed
            // [2] = middle candle
            // [3] = first candle of the 3-candle FVG structure

            var bullishFvg =
                Low[1] > High[3] &&
                (Low[1] - High[3]) >= minGap;

            var bearishFvg =
                High[1] < Low[3] &&
                (Low[3] - High[1]) >= minGap;

            // Bullish FVG gap:
            // lower boundary = High[3]
            // upper boundary = Low[1]
            var bullGapBottom = High[3];
            var bullGapTop = Low[1];

            // Bearish FVG gap:
            // upper boundary = Low[3]
            // lower boundary = High[1]
            var bearGapTop = Low[3];
            var bearGapBottom = High[1];
            
            var bullGapSize = bullGapTop - bullGapBottom;
            var bearGapSize = bearGapTop - bearGapBottom;

            var bullishFvgWithinGapSize =
                bullishFvg &&
                (MaxFvgGapTicks <= 0 || bullGapSize <= maxGap);

            var bearishFvgWithinGapSize =
                bearishFvg &&
                (MaxFvgGapTicks <= 0 || bearGapSize <= maxGap);
            
            var bullishFvgDistanceFromOpeningHigh = Math.Max(0, bullGapBottom - openingRangeHigh);
            var bearishFvgDistanceFromOpeningLow = Math.Max(0, openingRangeLow - bearGapTop);

            var bullishFvgWithinMaxDistance =
                MaxFvgDistanceFromRangeTicks <= 0 ||
                bullishFvgDistanceFromOpeningHigh <= maxDistance;

            var bearishFvgWithinMaxDistance =
                MaxFvgDistanceFromRangeTicks <= 0 ||
                bearishFvgDistanceFromOpeningLow <= maxDistance;

            // Valid long if:
            // 1. FVG crosses OR high: bottom <= ORH and top >= ORH
            // OR
            // 2. Full FVG is above OR high, within optional distance allowance.
            var bullishFvgCrossesOpeningHigh =
                bullishFvg &&
                bullGapBottom <= openingRangeHigh &&
                bullGapTop >= openingRangeHigh;

            var bullishFvgAboveOpeningHigh =
                bullishFvg &&
                bullGapBottom >= openingRangeHigh + minDistance;

            var validLongFvg =
                (bullishFvgCrossesOpeningHigh || bullishFvgAboveOpeningHigh) &&
                bullishFvgWithinGapSize &&
                bullishFvgWithinMaxDistance;

            // Valid short if:
            // 1. FVG crosses OR low: top >= ORL and bottom <= ORL
            // OR
            // 2. Full FVG is below OR low, within optional distance allowance.
            var bearishFvgCrossesOpeningLow =
                bearishFvg &&
                bearGapTop >= openingRangeLow &&
                bearGapBottom <= openingRangeLow;

            var bearishFvgBelowOpeningLow =
                bearishFvg &&
                bearGapTop <= openingRangeLow - minDistance;

            var validShortFvg =
                (bearishFvgCrossesOpeningLow || bearishFvgBelowOpeningLow) &&
                bearishFvgWithinGapSize &&
                bearishFvgWithinMaxDistance;

            LogDiag(
                $"Check: ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                $"BullFVG={bullishFvg}, BearFVG={bearishFvg}, " +
                $"BullGapBottom={bullGapBottom}, BullGapTop={bullGapTop}, " +
                $"BearGapBottom={bearGapBottom}, BearGapTop={bearGapTop}, " +
                $"BullCrossORH={bullishFvgCrossesOpeningHigh}, BullAboveORH={bullishFvgAboveOpeningHigh}, " +
                $"BearCrossORL={bearishFvgCrossesOpeningLow}, BearBelowORL={bearishFvgBelowOpeningLow}, " +
                $"BullDistTicks={bullishFvgDistanceFromOpeningHigh / TickSize}, " +
                $"BearDistTicks={bearishFvgDistanceFromOpeningLow / TickSize}, " +
                $"BullWithinMax={bullishFvgWithinMaxDistance}, BearWithinMax={bearishFvgWithinMaxDistance}, " +
                $"BullGapTicks={bullGapSize / TickSize}, BearGapTicks={bearGapSize / TickSize}, " +
                $"BullGapWithinMax={bullishFvgWithinGapSize}, BearGapWithinMax={bearishFvgWithinGapSize}, " +
                $"ValidLong={validLongFvg}, ValidShort={validShortFvg}");

            if (validLongFvg)
            {
                var approximateEntry = GetCurrentAsk();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                // Simple stop model:
                // If MaxStopTicks > 0, PrepareLongManagedBracket uses fixed max stop.
                // If MaxStopTicks == 0, use the low of the FVG confirmation candle.
                var candleStop = Low[1];

                if (!PrepareLongManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = true;

                LogDiag($"LONG submitted. ApproxEntry={approximateEntry}, Stop={activeStopPrice}, Target={activeTargetPrice}");
                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (validShortFvg)
            {
                var approximateEntry = GetCurrentBid();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                // Simple stop model:
                // If MaxStopTicks > 0, PrepareShortManagedBracket uses fixed max stop.
                // If MaxStopTicks == 0, use the high of the FVG confirmation candle.
                var candleStop = High[1];

                if (!PrepareShortManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = false;

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