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
            // [1] = candle that just closed
            // [2] = candle before [1]
            // [3] = candle before [2]
            //
            // We now separate the model into:
            // 1. First close outside range arms the setup.
            // 2. FVG confirmation may happen within FvgConfirmBarsAfterBreakout bars.
            // 3. Stop remains fixed at the first candle that closed outside the range.

            var firstCloseAboveRange =
                Close[1] > openingRangeHigh &&
                Close[2] <= openingRangeHigh;

            var firstCloseBelowRange =
                Close[1] < openingRangeLow &&
                Close[2] >= openingRangeLow;

            if (firstCloseAboveRange)
            {
                longBreakoutArmed = true;
                longBreakoutBar = CurrentBar - 1;
                longBreakoutStop = Low[1];

                shortBreakoutArmed = false;

                LogDiag($"LONG breakout armed. BreakoutTime={Time[1]:HH:mm:ss}, Stop={longBreakoutStop}");
            }

            if (firstCloseBelowRange)
            {
                shortBreakoutArmed = true;
                shortBreakoutBar = CurrentBar - 1;
                shortBreakoutStop = High[1];

                longBreakoutArmed = false;

                LogDiag($"SHORT breakout armed. BreakoutTime={Time[1]:HH:mm:ss}, Stop={shortBreakoutStop}");
            }

            var bullishFvg =
                Low[1] > High[3] &&
                (Low[1] - High[3]) >= minGap;

            var bearishFvg =
                High[1] < Low[3] &&
                (Low[3] - High[1]) >= minGap;

            // Strict FVG-through-range rule.
            // Bullish gap must straddle / clear OR high.
            // Bearish gap must straddle / clear OR low.
            var bullishFvgThroughOpeningHigh =
                bullishFvg &&
                High[3] <= openingRangeHigh &&
                Low[1] >= openingRangeHigh;

            var bearishFvgThroughOpeningLow =
                bearishFvg &&
                Low[3] >= openingRangeLow &&
                High[1] <= openingRangeLow;

            var longBarsSinceBreakout = longBreakoutArmed
                ? (CurrentBar - 1) - longBreakoutBar
                : int.MaxValue;

            var shortBarsSinceBreakout = shortBreakoutArmed
                ? (CurrentBar - 1) - shortBreakoutBar
                : int.MaxValue;

            if (longBreakoutArmed && longBarsSinceBreakout > FvgConfirmBarsAfterBreakout)
            {
                LogDiag($"LONG breakout expired. BarsSince={longBarsSinceBreakout}");
                longBreakoutArmed = false;
            }

            if (shortBreakoutArmed && shortBarsSinceBreakout > FvgConfirmBarsAfterBreakout)
            {
                LogDiag($"SHORT breakout expired. BarsSince={shortBarsSinceBreakout}");
                shortBreakoutArmed = false;
            }

            LogDiag(
                $"Check: ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                $"FirstAbove={firstCloseAboveRange}, FirstBelow={firstCloseBelowRange}, " +
                $"LongArmed={longBreakoutArmed}, ShortArmed={shortBreakoutArmed}, " +
                $"BullFVG={bullishFvg}, BearFVG={bearishFvg}, " +
                $"BullThrough={bullishFvgThroughOpeningHigh}, BearThrough={bearishFvgThroughOpeningLow}, " +
                $"LongBarsSince={longBarsSinceBreakout}, ShortBarsSince={shortBarsSinceBreakout}");

            if (longBreakoutArmed && bullishFvgThroughOpeningHigh)
            {
                var approximateEntry = GetCurrentAsk();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                var candleStop = longBreakoutStop;

                if (!PrepareLongManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = true;

                longBreakoutArmed = false;
                shortBreakoutArmed = false;

                LogDiag($"LONG submitted. ApproxEntry={approximateEntry}, Stop={activeStopPrice}, Target={activeTargetPrice}");
                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (shortBreakoutArmed && bearishFvgThroughOpeningLow)
            {
                var approximateEntry = GetCurrentBid();
                if (approximateEntry <= 0)
                    approximateEntry = Close[0];

                var candleStop = shortBreakoutStop;

                if (!PrepareShortManagedBracket(approximateEntry, candleStop))
                    return;

                pendingEntry = true;
                pendingLong = false;

                shortBreakoutArmed = false;
                longBreakoutArmed = false;

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