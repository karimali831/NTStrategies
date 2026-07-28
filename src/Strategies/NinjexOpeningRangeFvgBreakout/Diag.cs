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
            double gapTicks,
            double minGapTicks,
            double maxGapTicks,
            double distanceTicks,
            double maxDistanceTicks,
            double stopPrice,
            double dailyPnl)
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
            sb.AppendLine($"[FVG DIAG] {Time[0]:yyyy-MM-dd HH:mm:ss} | {side} | {decision}");
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
            sb.AppendLine($"     Gap size            = {TicksText(gapTicks)}");
            sb.AppendLine($"     Min gap             = {TicksText(minGapTicks)}");
            sb.AppendLine($"     Max gap             = {(MaxFvgGapTicks <= 0 ? "OFF" : TicksText(maxGapTicks))}");
            sb.AppendLine($"     Gap within max      = {OkIcon(fvgWithinGapSize)}");
            sb.AppendLine($"     Distance from range = {TicksText(distanceTicks)}");
            sb.AppendLine($"     Max distance        = {(MaxFvgDistanceFromRangeTicks <= 0 ? "OFF" : TicksText(maxDistanceTicks))}");
            sb.AppendLine($"     Distance ok         = {OkIcon(fvgWithinMaxDistance)}");

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
            double distanceTicks,
            double maxDistanceTicks)
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
                if (MaxFvgDistanceFromRangeTicks > 0 && distanceTicks > maxDistanceTicks)
                    return $"FVG is too far from opening range: {distanceTicks:0.##} ticks exceeds MaxFvgDistanceFromRangeTicks {maxDistanceTicks:0.##}.";

                return "FVG distance failed max-distance filter.";
            }

            return "All filters passed.";
        }
    }
}