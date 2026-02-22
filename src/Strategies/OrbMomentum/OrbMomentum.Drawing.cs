#region Using declarations
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
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
    }
}