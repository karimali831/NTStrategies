using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private void SubmitLongBracket(double entryPrice, int quantity)
        {
            var stopPrice = Instrument.MasterInstrument.RoundToTickSize(pendingStopPrice);
            var risk = entryPrice - stopPrice;

            if (risk <= TickSize)
            {
                ExitLong("InvalidLongRiskExit", LongEntryName);
                pendingEntry = false;
                return;
            }

            if (MaxStopTicks > 0 && risk / TickSize > MaxStopTicks)
            {
                ExitLong("MaxStopExceededLongExit", LongEntryName);
                pendingEntry = false;
                LogDiag($"LONG exited: filled risk exceeded MaxStopTicks. RiskTicks={risk / TickSize}, Max={MaxStopTicks}");
                return;
            }

            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + (risk * 2.0));

            activeEntryPrice = entryPrice;
            activeStopPrice = stopPrice;
            activeTargetPrice = targetPrice;
            activeDirection = 1;
            autoBreakevenApplied = false;

            ExitLongStopMarket(0, true, quantity, activeStopPrice, LongStopName, LongEntryName);
            ExitLongLimit(0, true, quantity, activeTargetPrice, LongTargetName, LongEntryName);

            LogDiag($"LONG bracket submitted. Entry={entryPrice}, Stop={activeStopPrice}, Target={activeTargetPrice}, RiskTicks={risk / TickSize}");
        }

        private void SubmitShortBracket(double entryPrice, int quantity)
        {
            var stopPrice = Instrument.MasterInstrument.RoundToTickSize(pendingStopPrice);
            var risk = stopPrice - entryPrice;

            if (risk <= TickSize)
            {
                ExitShort("InvalidShortRiskExit", ShortEntryName);
                pendingEntry = false;
                return;
            }

            if (MaxStopTicks > 0 && risk / TickSize > MaxStopTicks)
            {
                ExitShort("MaxStopExceededShortExit", ShortEntryName);
                pendingEntry = false;
                LogDiag($"SHORT exited: filled risk exceeded MaxStopTicks. RiskTicks={risk / TickSize}, Max={MaxStopTicks}");
                return;
            }

            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - (risk * 2.0));

            activeEntryPrice = entryPrice;
            activeStopPrice = stopPrice;
            activeTargetPrice = targetPrice;
            activeDirection = -1;
            autoBreakevenApplied = false;

            ExitShortStopMarket(0, true, quantity, activeStopPrice, ShortStopName, ShortEntryName);
            ExitShortLimit(0, true, quantity, activeTargetPrice, ShortTargetName, ShortEntryName);

            LogDiag($"SHORT bracket submitted. Entry={entryPrice}, Stop={activeStopPrice}, Target={activeTargetPrice}, RiskTicks={risk / TickSize}");
        }

        private bool CanTradeToday()
        {
            if (MaxLosingTradesPerDay > 0 && losingTradesToday >= MaxLosingTradesPerDay)
                return false;

            if (MaxWinningTradesPerDay > 0 && winningTradesToday >= MaxWinningTradesPerDay)
                return false;

            return true;
        }
        
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

            var rangeStart = activeEasternDate.Add(ToTimeSpan(RangeStartTime));
            var rangeEnd = rangeStart.AddMinutes(RangeMinutes);

            if (easternBarTime <= rangeEnd)
            {
                LogDiag($"Blocked: still inside opening range window. Time={easternBarTime:HH:mm:ss}, RangeEnd={rangeEnd:HH:mm:ss}");
                return;
            }

            if (CurrentBar < 4)
                return;

            var minGap = MinFvgGapTicks * TickSize;

            // Standard bullish 3-candle FVG:
            // Candle [2] = impulse / breakout candle
            // Candle [0] = FVG confirmation candle
            // Gap is between High[2] and Low[0]
            var bullishFvg =
                Low[0] > High[2] &&
                (Low[0] - High[2]) >= minGap;

            // Standard bearish 3-candle FVG:
            // Candle [2] = impulse / breakout candle
            // Candle [0] = FVG confirmation candle
            // Gap is between Low[2] and High[0]
            var bearishFvg =
                High[0] < Low[2] &&
                (Low[2] - High[0]) >= minGap;

            // Long model:
            // Candle [2] must be the first candle that closed above the opening range high.
            // The actual FVG gap must be above / through the opening range high.
            var breakoutCandleFirstCloseAboveRange =
                Close[2] > openingRangeHigh &&
                Close[3] <= openingRangeHigh;

            var bullishFvgThroughOpeningHigh =
                bullishFvg &&
                breakoutCandleFirstCloseAboveRange &&
                High[2] >= openingRangeHigh;

            // Short model:
            // Candle [2] must be the first candle that closed below the opening range low.
            // The actual FVG gap must be below / through the opening range low.
            var breakoutCandleFirstCloseBelowRange =
                Close[2] < openingRangeLow &&
                Close[3] >= openingRangeLow;

            var bearishFvgThroughOpeningLow =
                bearishFvg &&
                breakoutCandleFirstCloseBelowRange &&
                Low[2] <= openingRangeLow;

            LogDiag(
                $"Check: Close0={Close[0]}, ORH={openingRangeHigh}, ORL={openingRangeLow}, " +
                $"BullFVG={bullishFvg}, BearFVG={bearishFvg}, " +
                $"BreakoutCandleAbove={breakoutCandleFirstCloseAboveRange}, " +
                $"BreakoutCandleBelow={breakoutCandleFirstCloseBelowRange}, " +
                $"BullThroughORH={bullishFvgThroughOpeningHigh}, BearThroughORL={bearishFvgThroughOpeningLow}");

            if (bullishFvgThroughOpeningHigh)
            {
                pendingEntry = true;
                pendingLong = true;

                // Stop goes at the first candle that closed outside the range.
                pendingStopPrice = Low[2];

                if (!IsStopSizeAllowed(true, Close[0], pendingStopPrice))
                {
                    DrawDiag("BLOCK_LONG_MAX_STOP", "Max stop", Low[0] - 4 * TickSize);
                    LogDiag($"LONG blocked: stop too large. EntryApprox={Close[0]}, Stop={pendingStopPrice}");
                    return;
                }

                DrawDiag("LONG_SIGNAL", "LONG", Low[0] - 4 * TickSize);
                LogDiag($"LONG submitted. BreakoutCandle={Time[2]:HH:mm:ss}, Stop={pendingStopPrice}");

                pendingEntry = true;
                pendingLong = true;

                EnterLong(Quantity, LongEntryName);
                return;
            }

            if (bearishFvgThroughOpeningLow)
            {
                pendingEntry = true;
                pendingLong = false;

                // Stop goes at the first candle that closed outside the range.
                pendingStopPrice = High[2];

                if (!IsStopSizeAllowed(false, Close[0], pendingStopPrice))
                {
                    DrawDiag("BLOCK_SHORT_MAX_STOP", "Max stop", High[0] + 4 * TickSize);
                    LogDiag($"SHORT blocked: stop too large. EntryApprox={Close[0]}, Stop={pendingStopPrice}");
                    return;
                }

                DrawDiag("SHORT_SIGNAL", "SHORT", High[0] + 4 * TickSize);
                LogDiag($"SHORT submitted. BreakoutCandle={Time[2]:HH:mm:ss}, Stop={pendingStopPrice}");

                pendingEntry = true;
                pendingLong = false;

                EnterShort(Quantity, ShortEntryName);
                return;
            }

            if (Close[0] > openingRangeHigh && !bullishFvgThroughOpeningHigh)
                DrawDiag("BLOCK_LONG_STRICT", "No strict bull FVG", High[0] + 4 * TickSize);

            if (Close[0] < openingRangeLow && !bearishFvgThroughOpeningLow)
                DrawDiag("BLOCK_SHORT_STRICT", "No strict bear FVG", Low[0] - 4 * TickSize);
        }
    }
}