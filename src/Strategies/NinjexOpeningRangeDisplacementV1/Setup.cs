using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningRangeDisplacementV1
    {
        private sealed class BreakoutAttempt
        {
            public int Direction;
            public int BreakBar;
            public DateTime BreakTime;
            public double Stop;
            public double BreakClose;
            public double AtrTicks;
            public double Adx;
            public double BodyAtr;
            public bool RetestDuringConfirmation;
            public bool WasRetest;
        }

        private void ProcessDirection(int direction)
        {
            bool up = direction > 0;
            double level = up ? orHigh : orLow;
            BreakoutAttempt attempt = up ? longAttempt : shortAttempt;

            if (attempt != null)
            {
                if (CurrentBar > attempt.BreakBar)
                {
                    // A return into the OR during the maximum two-candle confirmation
                    // wait is remembered, but cannot spawn an overlapping attempt.
                    if (up ? Low[0] <= level : High[0] >= level)
                        attempt.RetestDuringConfirmation = true;
                }

                int age = CurrentBar - attempt.BreakBar;
                if (age <= 2 && ConfirmFvg(attempt))
                {
                    if (up) longAttempt = null;
                    else shortAttempt = null;
                    // A second trade in the same direction always needs a new retest.
                    return;
                }

                if (age < 2)
                    return;

                WriteDiagnostic("ATTEMPT_EXPIRED", direction, attempt,
                    "No confirmed, qualified FVG containing the breakout candle within 3 bars.");
                if (up)
                {
                    longRetested = attempt.RetestDuringConfirmation;
                    longAttempt = null;
                }
                else
                {
                    shortRetested = attempt.RetestDuringConfirmation;
                    shortAttempt = null;
                }
            }

            bool needsRetest = up ? longNeedsRetest : shortNeedsRetest;
            bool retested = up ? longRetested : shortRetested;
            // First break must close through the level, not merely poke through it.
            bool closesBeyond = up ? Close[0] > level : Close[0] < level;
            bool directionalBody = up ? Close[0] > Open[0] : Close[0] < Open[0];
            bool previousCloseInside = up ? Close[1] <= level : Close[1] >= level;
            bool touchesOnThisBar = up ? Low[0] <= level : High[0] >= level;

            if (needsRetest && touchesOnThisBar)
            {
                retested = true;
                if (up) longRetested = true;
                else shortRetested = true;
            }

            // Crossing from inside OR, or a same-bar touch/reclaim after a prior
            // failed breakout, qualifies as a fresh attempt.
            bool freshBreak = previousCloseInside
                || (needsRetest && retested && touchesOnThisBar);

            if (!closesBeyond || !directionalBody || !freshBreak
                || (needsRetest && !retested))
                return;

            double atr = GetAtrTicks();
            double body = Math.Abs(Close[0] - Open[0]) / TickSize;
            var newAttempt = new BreakoutAttempt
            {
                Direction = direction,
                BreakBar = CurrentBar,
                BreakTime = Time[0],
                BreakClose = Close[0],
                Stop = up ? Low[0] : High[0],
                AtrTicks = atr,
                Adx = GetAdx(),
                BodyAtr = atr > 0 ? body / atr : double.NaN,
                WasRetest = needsRetest
            };

            if (up)
            {
                longAttempt = newAttempt;
                longNeedsRetest = true;
                longRetested = false;
            }
            else
            {
                shortAttempt = newAttempt;
                shortNeedsRetest = true;
                shortRetested = false;
            }

            WriteDiagnostic("BODY_BREAKOUT", direction, newAttempt,
                needsRetest ? "Retest breakout confirmed by candle close." : "First breakout confirmed by candle close.");

            // A breakout candle can itself be candle 3 of a fully confirmed FVG.
            if (ConfirmFvg(newAttempt))
            {
                if (up) longAttempt = null;
                else shortAttempt = null;
            }
        }

        private bool ConfirmFvg(BreakoutAttempt attempt)
        {
            // Three *completed* candles are [2] reference, [1] displacement,
            // [0] confirmation. The breakout candle must be in this trio.
            if (CurrentBar < 2 || CurrentBar - attempt.BreakBar > 2)
                return false;

            bool up = attempt.Direction > 0;
            double gap = up ? Low[0] - High[2] : Low[2] - High[0];
            if (gap < MinFvgTicks * TickSize - TickSize * 0.000001)
                return false;

            double level = up ? orHigh : orLow;
            bool confirmationOutside = up ? Close[0] > level : Close[0] < level;
            double outerGapEdge = up ? Low[0] : High[0];
            bool fvgAtBreakout = up ? outerGapEdge > level : outerGapEdge < level;
            if (!confirmationOutside || !fvgAtBreakout)
            {
                WriteDiagnostic("FVG_REJECTED_LOCATION", attempt.Direction, attempt,
                    "FVG exists but third close/gap is not through opening range. gapTicks="
                    + (gap / TickSize).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                return false;
            }

            WriteDiagnostic("FVG_CONFIRMED", attempt.Direction, attempt,
                "GapTicks=" + (gap / TickSize).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + "; ThirdCandle=" + Time[0].ToString("HH:mm:ss")
                + "; breakoutBar=" + attempt.BreakTime.ToString("HH:mm:ss")
                + "; retest=" + attempt.WasRetest);

            // These filters collect data by default but do not reject trades unless
            // individually enabled for subsequent research runs.
            string blocked = null;
            if (EnableAtrFilter && (double.IsNaN(attempt.AtrTicks) || attempt.AtrTicks < MinAtrTicks))
                blocked = "ATR_FILTER";
            else if (EnableAdxFilter && (double.IsNaN(attempt.Adx) || attempt.Adx < MinAdx))
                blocked = "ADX_FILTER";
            else if (EnableBodyAtrFilter && (double.IsNaN(attempt.BodyAtr) || attempt.BodyAtr < MinBreakoutBodyAtr))
                blocked = "BODY_ATR_FILTER";
            if (blocked != null)
            {
                WriteDiagnostic(blocked, attempt.Direction, attempt, "Optional research filter rejected eligible FVG.");
                return true; // This attempt is consumed; re-entry requires a retest.
            }

            SubmitQualifiedEntry(attempt, gap / TickSize);
            return true;
        }

        private double GetAtrTicks()
        {
            return CurrentBar >= 14 ? atr14[0] / TickSize : double.NaN;
        }

        private double GetAdx()
        {
            return CurrentBar >= 28 ? adx14[0] : double.NaN;
        }
    }
}