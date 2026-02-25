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
            var lookbackBars = Math.Max(2, DoubleTopBottomLookbackBars);   // includes bar 0
            var maxDiffTicks = Math.Max(1, DoubleTopBottomMaxDiffTicks);

            if (CurrentBar < lookbackBars - 1)
            {
                // nothing to draw yet
                return (false, false);
            }

            // Stable tags (same during the life of CurrentBar, overwrite on each tick)
            var tagB0 = $"DTB_B0_{CurrentBar}";
            var tagB1 = $"DTB_B1_{CurrentBar}";
            var tagT0 = $"DTB_T0_{CurrentBar}";
            var tagT1 = $"DTB_T1_{CurrentBar}";

            // Helper: remove stale dots when invalid
            void ClearBottom()
            {
                RemoveDrawObject(tagB0);
                RemoveDrawObject(tagB1);
            }

            void ClearTop()
            {
                RemoveDrawObject(tagT0);
                RemoveDrawObject(tagT1);
            }

            var isCurrentDoubleBottom = false;
            var isCurrentDoubleTop = false;

            // -------------------------
            // Double Bottom (current bar must participate)
            // -------------------------
            {
                var cur = Low[0];

                // Find the closest prior low within lookback (bars 1..lookbackBars-1)
                var bestIdx = -1;
                var bestDiffTicks = double.MaxValue;

                for (var i = 1; i < lookbackBars; i++)
                {
                    var dTicks = Math.Abs(cur - Low[i]) / TickSize;
                    if (dTicks < bestDiffTicks)
                    {
                        bestDiffTicks = dTicks;
                        bestIdx = i;
                    }
                }

                // Extra guard: current low should be near the lowest area in the window
                var minLow = double.MaxValue;
                for (int i = 0; i < lookbackBars; i++)
                    if (Low[i] < minLow) minLow = Low[i];

                var nearWindowLow = (cur - minLow) / TickSize <= maxDiffTicks;

                if (bestIdx > 0 && bestDiffTicks <= maxDiffTicks && nearWindowLow)
                {
                    // Draw / update (every tick). If price moves away later, we clear.
                    Draw.Dot(this, tagB0, false, 0, Low[0], Brushes.Purple);
                    Draw.Dot(this, tagB1, false, bestIdx, Low[bestIdx], Brushes.Purple);
                    isCurrentDoubleBottom = true;
                }
                else
                {
                    // preferred behaviour: remove as soon as it becomes invalid intrabar
                    ClearBottom();
                }
            }

            // -------------------------
            // Double Top (current bar must participate)
            // -------------------------
            {
                var cur = High[0];

                var bestIdx = -1;
                var bestDiffTicks = double.MaxValue;

                for (var i = 1; i < lookbackBars; i++)
                {
                    var dTicks = Math.Abs(cur - High[i]) / TickSize;
                    if (dTicks < bestDiffTicks)
                    {
                        bestDiffTicks = dTicks;
                        bestIdx = i;
                    }
                }

                var maxHigh = double.MinValue;
                for (int i = 0; i < lookbackBars; i++)
                    if (High[i] > maxHigh) maxHigh = High[i];

                var nearWindowHigh = (maxHigh - cur) / TickSize <= maxDiffTicks;

                if (bestIdx > 0 && bestDiffTicks <= maxDiffTicks && nearWindowHigh)
                {
                    Draw.Dot(this, tagT0, false, 0, High[0], Brushes.Purple);
                    Draw.Dot(this, tagT1, false, bestIdx, High[bestIdx], Brushes.Purple);
                    isCurrentDoubleTop = true;
                }
                else
                {
                    ClearTop();
                }
            }

            return (isCurrentDoubleTop, isCurrentDoubleBottom);
        }
    }
}