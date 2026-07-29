using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private int lastBlockDiagnosticBar = -1;
        private string lastBlockDiagnosticKey = string.Empty;
        
        private void LogDiag(string message)
        {
            if (!EnableDiagnostics)
                return;

            Print($"{Time[0]:yyyy-MM-dd HH:mm:ss} | {Name} | {message}");
        }
        
       private void LogEntryModelDecision(
            DateTime signalBarTime,
            DateTime decisionBarTime,
            bool bullishFvg,
            bool bearishFvg,
            bool closesAboveRange,
            bool closesBelowRange,
            bool bullCrossesOrh,
            bool bearCrossesOrl,
            bool bullEntirelyAboveOrh,
            bool bearEntirelyBelowOrl,
            bool bullWithinDistance,
            bool bearWithinDistance,
            double bullDistanceTicks,
            double bearDistanceTicks,
            bool validLong,
            bool validShort)
        {
            if (!EnableDiagnostics)
                return;

            var hasBreakout =
                closesAboveRange ||
                closesBelowRange;

            if (!bullishFvg && !bearishFvg && !hasBreakout)
                return;

            string result;
            string reason;

            if (validLong)
            {
                result = "VALID LONG";
                reason = bullCrossesOrh
                    ? "Bullish FVG crosses the opening-range high."
                    : $"Bullish FVG is above ORH and within the maximum distance " +
                      $"({bullDistanceTicks:0.##} ticks).";
            }
            else if (validShort)
            {
                result = "VALID SHORT";
                reason = bearCrossesOrl
                    ? "Bearish FVG crosses the opening-range low."
                    : $"Bearish FVG is below ORL and within the maximum distance " +
                      $"({bearDistanceTicks:0.##} ticks).";
            }
            else if (bullishFvg && !closesAboveRange)
            {
                result = "NO ENTRY";
                reason = "Bullish FVG exists, but its candle did not close above ORH.";
            }
            else if (bearishFvg && !closesBelowRange)
            {
                result = "NO ENTRY";
                reason = "Bearish FVG exists, but its candle did not close below ORL.";
            }
            else if (bullishFvg &&
                     !bullCrossesOrh &&
                     !bullEntirelyAboveOrh)
            {
                result = "NO ENTRY";
                reason = "Bullish FVG formed inside the opening range.";
            }
            else if (bearishFvg &&
                     !bearCrossesOrl &&
                     !bearEntirelyBelowOrl)
            {
                result = "NO ENTRY";
                reason = "Bearish FVG formed inside the opening range.";
            }
            else if (bullishFvg && !bullWithinDistance)
            {
                result = "NO ENTRY";
                reason =
                    $"Bullish FVG is too far above ORH: " +
                    $"{bullDistanceTicks:0.##} ticks exceeds " +
                    $"{MaxEntryDistanceFromRangeTicks} ticks.";
            }
            else if (bearishFvg && !bearWithinDistance)
            {
                result = "NO ENTRY";
                reason =
                    $"Bearish FVG is too far below ORL: " +
                    $"{bearDistanceTicks:0.##} ticks exceeds " +
                    $"{MaxEntryDistanceFromRangeTicks} ticks.";
            }
            else if (!bullishFvg && closesAboveRange)
            {
                result = "NO ENTRY";
                reason = "Candle closed above ORH, but no eligible bullish FVG exists.";
            }
            else if (!bearishFvg && closesBelowRange)
            {
                result = "NO ENTRY";
                reason = "Candle closed below ORL, but no eligible bearish FVG exists.";
            }
            else
            {
                result = "NO ENTRY";
                reason = "No eligible FVG breakout exists.";
            }

            LogDiag(
                $"ENTRY CHECK | Result={result} | " +
                $"Signal={signalBarTime:HH:mm:ss} | " +
                $"EntryBar={decisionBarTime:HH:mm:ss} | " +
                $"Close={Close[1]} | ORH={openingRangeHigh} | ORL={openingRangeLow} | " +
                $"BullFVG={bullishFvg} | BearFVG={bearishFvg} | " +
                $"BullDistance={bullDistanceTicks:0.##} ticks | " +
                $"BearDistance={bearDistanceTicks:0.##} ticks | " +
                $"Reason={reason}");
        }
        
        private void LogDiagOncePerBar(string key, string message)
        {
            if (!EnableDiagnostics)
                return;

            if (lastBlockDiagnosticBar == CurrentBar && lastBlockDiagnosticKey == key)
                return;

            lastBlockDiagnosticBar = CurrentBar;
            lastBlockDiagnosticKey = key;

            Print($"{Time[0]:yyyy-MM-dd HH:mm:ss} | {Name} | {message}");
        }
    }
}