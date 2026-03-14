using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBox _statusBox;
        private RowDefinition _footerStatusLabelRow;
        private RowDefinition _footerStatusBoxRow;
        
        private void RenderFooter(Grid root)
        {
            var bottom = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // instrument tabs
            _footerStatusLabelRow = new RowDefinition { Height = GridLength.Auto };
            _footerStatusBoxRow = new RowDefinition { Height = GridLength.Auto };
            bottom.RowDefinitions.Add(_footerStatusLabelRow); // status label
            bottom.RowDefinitions.Add(_footerStatusBoxRow);   // status box

            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
            _instrumentTabs = new TabControl
            {
                Height = 34,
                Margin = new Thickness(0, 0, 6, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            _statusBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 110,
                MinHeight = 110,
                Background = SystemColors.ControlLightBrush,
                Foreground = SystemColors.ControlTextBrush
            };

            _statusLabel = new TextBlock
            {
                Text = "Status:",
                Foreground = SystemColors.WindowTextBrush,
                Margin = new Thickness(0, 0, 0, 4)
            };
                
            Grid.SetRow(_instrumentTabs, 0);
            Grid.SetColumn(_instrumentTabs, 0);

            Grid.SetRow(_statusLabel, 1);
            Grid.SetColumn(_statusLabel, 0);

            Grid.SetRow(_statusBox, 2);
            Grid.SetColumn(_statusBox, 0);


            bottom.Children.Add(_instrumentTabs);
            bottom.Children.Add(_statusLabel);
            bottom.Children.Add(_statusBox);

            Grid.SetRow(bottom, 6);
            root.Children.Add(bottom);
            
            ApplyStatusBoxVisibility();
        }
    }
}
