using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static Border CreateSectionCard(UIElement content, Thickness? margin = null)
        {
            var border = new Border
            {
                Margin = margin ?? new Thickness(0),
                Padding = new Thickness(12),
                Child = content
            };

            ApplyCardChrome(border);
            return border;
        }
    }
}