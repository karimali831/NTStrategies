using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Brush DotDisconnectedBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(190, 70, 70))
                : new SolidColorBrush(Color.FromRgb(170, 70, 70));
        }
    }
}