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
                LogDiag($"Blocked: daily limits reached. Wins={winningTradesToday}, Losses={losingTradesToday}");
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

            // With Calculate.OnEachTick + IsFirstTickOfBar:
            // [1] = closed FVG confirmation candle
            // [3] = breakout candle
            // [4] = candle before breakout candle

            var bullishFvg =
                Low[1] > High[3] &&
                (Low[1] - High[3]) >= minGap;

            var bearishFvg =
                High[1] < Low[3] &&
                (Low[3] - High[1]) >= minGap;

            var breakoutCandleFirstCloseAboveRange =
                Close[3] > openingRangeHigh &&
                Close[4] <= openingRangeHigh;

            var breakoutCandleFirstCloseBelowRange =
                Close[3] < openingRangeLow &&
                Close[4] >= openingRangeLow;

            var bullishFvgThroughOpeningHigh =
                bullishFvg &&
                breakoutCandleFirstCloseAboveRange &&
                High[3] >= openingRangeHigh;

            var bearishFvgThroughOpeningLow =
                bearishFvg &&
                breakoutCandleFirstCloseBelowRange &&
                Low[3] <= openingRangeLow;

            LogDiag(
                $"Check: ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                $"BullFVG={bullishFvg}, BearFVG={bearishFvg}, " +
                $"BullThrough={bullishFvgThroughOpeningHigh}, BearThrough={bearishFvgThroughOpeningLow}");

            if (bullishFvgThroughOpeningHigh)
            {
                var approximateEntry = GetCurrentAsk();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                var candleStop = Low[3];

                if (!PrepareLongManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = true;

                LogDiag($"LONG submitted. ApproxEntry={approximateEntry}, Stop={activeStopPrice}, Target={activeTargetPrice}");
                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (bearishFvgThroughOpeningLow)
            {
                var approximateEntry = GetCurrentBid();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                var candleStop = High[3];

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
            if (MaxLosingTradesPerDay > 0 && losingTradesToday >= MaxLosingTradesPerDay)
                return false;

            if (MaxWinningTradesPerDay > 0 && winningTradesToday >= MaxWinningTradesPerDay)
                return false;

            return true;
        }
        
        private bool IsInsideEntryWindow(DateTime easternBarTime)
        {
            var start = activeEasternDate.Add(ToTimeSpan(EntryStartTime));
            var end = activeEasternDate.Add(ToTimeSpan(EntryEndTime));

            return easternBarTime >= start && easternBarTime <= end;
        }
    }
}