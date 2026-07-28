using System;
using System.Text;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeFvgBreakout : Strategy
    {
        private int lastDiagnosticBar = -1;
        private int lastBlockDiagnosticBar = -1;
        private string lastBlockDiagnosticKey = string.Empty;
        
        private void LogDiag(string message)
        {
            if (!EnableDiagnostics)
                return;

            Print($"{Time[0]:yyyy-MM-dd HH:mm:ss} | {Name} | {message}");
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

        private void LogEntryDiagnostic(
            string side,
            bool isSignal,
            string decision,
            string failReason,
            double currentPrice,
            bool priceOutsideRange,
            bool fvgCandleOutsideRange,
            bool fvgExists,
            bool fvgCrossesRange,
            bool fvgBeyondRange,
            bool fvgWithinGapSize,
            bool fvgWithinMaxDistance,
            bool entryWithinMaxDistance,
            double gapTicks,
            double minGapTicks,
            double maxGapTicks,
            double fvgDistanceTicks,
            double maxFvgDistanceTicks,
            double entryDistanceTicks,
            double maxEntryDistanceTicks,
            double stopPrice,
            double dailyPnl,
            DateTime signalBarTime,
            DateTime decisionBarTime)
        {
            if (!EnableDiagnostics)
                return;

            // Avoid tick-by-tick spam.
            // Always print valid signals, otherwise print only once per bar.
            if (!isSignal && lastDiagnosticBar == CurrentBar)
                return;

            lastDiagnosticBar = CurrentBar;

            var sb = new StringBuilder(768);

            sb.AppendLine(new string('-', 92));
            sb.AppendLine($"[FVG DIAG] Signal={signalBarTime:HH:mm:ss} | Decision={decisionBarTime:HH:mm:ss} | {side} | {decision}");
            sb.AppendLine($"  Reason: {failReason}");
            sb.AppendLine($"  a) Opening Range");
            sb.AppendLine($"     Range filter = {EnabledText(EnableRangeFilter)}");

            if (EnableRangeFilter)
            {
                sb.AppendLine($"     OR High = {openingRangeHigh:0.00}");
                sb.AppendLine($"     OR Low  = {openingRangeLow:0.00}");
                sb.AppendLine($"     Current = {currentPrice:0.00}");
                sb.AppendLine($"     Price outside range = {OkIcon(priceOutsideRange)}");
                sb.AppendLine($"     FVG candle outside = {OkIcon(fvgCandleOutsideRange)}");
            }
            else
            {
                sb.AppendLine($"     Opening range checks bypassed.");
                sb.AppendLine($"     Current = {currentPrice:0.00}");
            }
            
            sb.AppendLine($"  b) FVG Structure");
            sb.AppendLine($"     FVG exists          = {OkIcon(fvgExists)}");
            sb.AppendLine($"     FVG crosses level   = {OkIcon(fvgCrossesRange)}");
            sb.AppendLine($"     FVG beyond level    = {OkIcon(fvgBeyondRange)}");

            sb.AppendLine($"  c) FVG Filters");
            sb.AppendLine($"     Gap size            = {(fvgExists ? TicksText(gapTicks) : "N/A - no FVG")}");
            sb.AppendLine($"     Min gap             = {TicksText(minGapTicks)}");
            sb.AppendLine($"     Max gap             = {(MaxFvgGapTicks <= 0 ? "OFF" : TicksText(maxGapTicks))}");
            sb.AppendLine($"     Gap within max      = {OkIcon(fvgWithinGapSize)}");
            sb.AppendLine($"     FVG distance from range   = {TicksText(fvgDistanceTicks)}");
            sb.AppendLine($"     Max FVG distance          = {(MaxFvgDistanceFromRangeTicks <= 0 ? "OFF" : TicksText(maxFvgDistanceTicks))}");
            sb.AppendLine($"     FVG distance ok           = {OkIcon(fvgWithinMaxDistance)}");

            sb.AppendLine($"     Entry distance from range = {TicksText(entryDistanceTicks)}");
            sb.AppendLine($"     Max entry distance        = {(MaxEntryDistanceFromRangeTicks <= 0 ? "OFF" : TicksText(maxEntryDistanceTicks))}");
            sb.AppendLine($"     Entry distance ok         = {OkIcon(entryWithinMaxDistance)}");

            sb.AppendLine($"  d) Risk / State");
            sb.AppendLine($"     Stop candidate      = {stopPrice:0.00}");
            sb.AppendLine($"     Daily PnL           = {MoneyText(dailyPnl)}");
            sb.AppendLine($"     Pending entry       = {pendingEntry}");
            sb.AppendLine($"     Position            = {Position.MarketPosition}");

            sb.AppendLine(new string('-', 92));

            Print(sb.ToString());
        }

        private string BuildFvgFailReason(
            bool priceOutsideRange,
            bool fvgCandleOutsideRange,
            bool fvgExists,
            bool fvgCrossesRange,
            bool fvgBeyondRange,
            bool fvgWithinGapSize,
            bool fvgWithinMaxDistance,
            double gapTicks,
            double maxGapTicks,
            double fvgDistanceTicks,
            double maxFvgDistanceTicks,
            bool entryWithinMaxDistance,
            double entryDistanceTicks,
            double maxEntryDistanceTicks)
        {
            if (!priceOutsideRange)
                return "Price has not moved outside the opening range.";

            if (!fvgExists)
                return "No valid 3-candle FVG exists.";

            if (!fvgCandleOutsideRange)
                return "Confirmed FVG candle is still inside the opening range.";

            if (!fvgCrossesRange && !fvgBeyondRange)
                return "FVG does not cross or sit beyond the opening-range level.";

            if (!fvgWithinGapSize)
            {
                if (MaxFvgGapTicks > 0 && gapTicks > maxGapTicks)
                    return $"FVG gap is too large: {gapTicks:0.##} ticks exceeds MaxFvgGapTicks {maxGapTicks:0.##}.";

                return "FVG gap size failed min/max gap filter.";
            }

            if (!fvgWithinMaxDistance)
            {
                if (MaxFvgDistanceFromRangeTicks > 0 && fvgDistanceTicks > maxFvgDistanceTicks)
                    return $"FVG is too far from opening range: {fvgDistanceTicks:0.##} ticks exceeds MaxFvgDistanceFromRangeTicks {maxFvgDistanceTicks:0.##}.";

                return "FVG distance failed max-distance filter.";
            }
            
            if (!entryWithinMaxDistance)
            {
                if (MaxEntryDistanceFromRangeTicks > 0 && entryDistanceTicks > maxEntryDistanceTicks)
                    return $"Entry is too far from opening range: {entryDistanceTicks:0.##} ticks exceeds MaxEntryDistanceFromRangeTicks {maxEntryDistanceTicks:0.##}.";

                return "Entry distance failed max-entry-distance filter.";
            }

            return "All filters passed.";
        }
    }
}