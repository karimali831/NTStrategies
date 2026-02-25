#region Using declarations

using System;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class OrbMomentum : Strategy
    {
        private void ConfigureEmaVisuals()
        {
            // If you don't want EMAs on chart in Strategy Analyzer, you can guard it:
            // if (State != State.Realtime && State != State.Historical) return;

            if (emaFast != null)
            {
                AddChartIndicator(emaFast);

                // Fast EMA styling (Red, Solid, Line, 3px)
                emaFast.Plots[0].Brush = Brushes.Red;
                emaFast.Plots[0].Width = 3;
                emaFast.Plots[0].DashStyleHelper = DashStyleHelper.Solid;
                emaFast.Plots[0].PlotStyle = PlotStyle.Line;
            }

            if (emaSlow != null)
            {
                AddChartIndicator(emaSlow);

                // Slow EMA styling (Goldenrod, Solid, Line, 3px)
                emaSlow.Plots[0].Brush = Brushes.Goldenrod;
                emaSlow.Plots[0].Width = 3;
                emaSlow.Plots[0].DashStyleHelper = DashStyleHelper.Solid;
                emaSlow.Plots[0].PlotStyle = PlotStyle.Line;
            }
        }
        
        private void DrawWaitingConfirmMarker(int dir, string reason, string context)
        {
            if (!EnableDiagnostics)
                return;

            var tag = $"WAIT_{context}_{dir}_{CurrentBar}";
            var y = dir > 0 ? (Low[0] - 2 * TickSize) : (High[0] + 2 * TickSize);
            
            // LogDiag($"[CONFIRM] {tag} ({reason})");

            Draw.Dot(this, tag, false, 0, y, dir > 0 ? Brushes.LimeGreen : Brushes.OrangeRed);
            // Draw.Text(this, tag + "_T", false, reason, 0, y, 0,
            //     Brushes.Gray, new SimpleFont("Arial", 10),
            //     TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
        }
        
        // Looks at barsAgo 0..3 (4 bars total, includes current bar).
        // Returns true ONLY if current bar (0) forms part of a double top/bottom
        private (bool DoubleTop, bool DoubleBottom) DetectAndDrawDoubleTopBottomLast()
        {
            var lookbackBars = Math.Max(2, DoubleTopBottomLookbackBars);
            var maxDiffTicks = Math.Max(1, DoubleTopBottomMaxDiffTicks);
            
            if (CurrentBar < lookbackBars - 1)
                return (false, false);

            int lowIdx1 = -1, lowIdx2 = -1;
            double low1 = double.MaxValue, low2 = double.MaxValue;

            int highIdx1 = -1, highIdx2 = -1;
            double high1 = double.MinValue, high2 = double.MinValue;

            for (var i = 0; i < lookbackBars; i++)
            {
                var l = Low[i];
                if (l < low1)
                {
                    low2 = low1; lowIdx2 = lowIdx1;
                    low1 = l;    lowIdx1 = i;
                }
                else if (l < low2)
                {
                    low2 = l;    lowIdx2 = i;
                }

                var h = High[i];
                if (h > high1)
                {
                    high2 = high1; highIdx2 = highIdx1;
                    high1 = h;     highIdx1 = i;
                }
                else if (h > high2)
                {
                    high2 = h;     highIdx2 = i;
                }
            }

            var isCurrentDoubleBottom = false;
            var isCurrentDoubleTop = false;

            // ---- Double Bottom check ----
            if (lowIdx1 >= 0 && lowIdx2 >= 0)
            {
                double diffTicks = Math.Abs(low1 - low2) / TickSize;

                if (diffTicks <= maxDiffTicks)
                {
                    // draw dots
                    Draw.Dot(this, $"DTB_B_{CurrentBar}_{lowIdx1}", false, lowIdx1, Low[lowIdx1], Brushes.Purple);
                    Draw.Dot(this, $"DTB_B_{CurrentBar}_{lowIdx2}", false, lowIdx2, Low[lowIdx2], Brushes.Purple);

                    // current bar participates?
                    if (lowIdx1 == 0 || lowIdx2 == 0)
                        isCurrentDoubleBottom = true;
                }
            }

            // ---- Double Top check ----
            if (highIdx1 >= 0 && highIdx2 >= 0)
            {
                double diffTicks = Math.Abs(high1 - high2) / TickSize;

                if (diffTicks <= maxDiffTicks)
                {
                    Draw.Dot(this, $"DTB_T_{CurrentBar}_{highIdx1}", false, highIdx1, High[highIdx1], Brushes.Purple);
                    Draw.Dot(this, $"_DTB_T_{CurrentBar}_{highIdx2}", false, highIdx2, High[highIdx2], Brushes.Purple);

                    if (highIdx1 == 0 || highIdx2 == 0)
                        isCurrentDoubleTop = true;
                }
            }

            return (isCurrentDoubleTop, isCurrentDoubleBottom);
        }
    }
}