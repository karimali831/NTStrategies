using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBox _statusBox;
        private TextBox RenderStatusBox()
        {
            _statusBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 110,
                MinHeight = 110,
                Background = SectionBackgroundBrush(),
                Foreground = WindowForegroundBrush(),
                Visibility = Visibility.Collapsed
            };
            
            return _statusBox;
        }
    }
}