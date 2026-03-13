using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBlock _totalPnlText;
        
        private void RenderHeader(Grid root)
        {
            var menu = BuildMenu();
            Grid.SetRow(menu, 0);
            root.Children.Add(menu);
                
            var statusBar = BuildStatusBar();
            Grid.SetRow(statusBar, 1);
            root.Children.Add(statusBar);
                
            _totalPnlText = new TextBlock
            {
                Text = "Total   R $0.00   •   U $0.00",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 2, 0, 12),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(_totalPnlText, 2);
            root.Children.Add(_totalPnlText);
        }
    }
}