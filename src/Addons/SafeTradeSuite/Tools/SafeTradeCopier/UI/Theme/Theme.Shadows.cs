using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static DropShadowEffect SoftShadow()
        {
            return new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = IsDarkTheme() ? 0.22 : 0.12,
                Color = Colors.Black
            };
        }
    }
}