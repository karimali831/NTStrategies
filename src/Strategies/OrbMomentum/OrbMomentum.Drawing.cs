#region Using declarations

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
        
        private void DrawDoubleTopBottomMarkerIfAny()
        {
            if (!EnableDoubleTopBottomFilter)
                return;

            // once per bar (OnEachTick safe)
            if (CurrentBar == lastDtbMarkBar)
                return;

            lastDtbMarkBar = CurrentBar;

            if (!TryGetDoubleTopBottomClosedBars(out var isTop, out var isBot, out var lvl, out var dbg))
                return;

            if (!isTop && !isBot)
                return;

            // Per your request: purple dot at the BOTTOM of the candle
            var y = Low[0] - 2 * TickSize;
            var tag = isTop ? $"DTB_TOP_{CurrentBar}" : $"DTB_BOT_{CurrentBar}";
            Draw.Dot(this, tag, false, 0, y, Brushes.Purple);

            if (EnableDiagnostics)
                LogDiag($"DTB MARK: {(isTop ? "DOUBLE_TOP" : "DOUBLE_BOTTOM")} lvl={lvl:F2} {dbg}", oncePerBar: true);
        }
    }
}