using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TabItem BuildInstrumentTabItem(InstrumentSession session)
        {
            var headerText = NormalizeInstrumentName(session?.InstrumentName);
            if (string.IsNullOrWhiteSpace(headerText))
                headerText = "(instrument)";

            var closeButton = new Button
            {
                Content = "×",
                Tag = session,
                Width = 18,
                Height = 18,
                MinWidth = 18,
                MinHeight = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                Visibility = Visibility.Visible,
                Focusable = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Brushes.DimGray,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Remove instrument"
            };

            closeButton.MouseEnter += (s, e) => closeButton.Foreground = Brushes.Red;
            closeButton.MouseLeave += (s, e) => closeButton.Foreground = Brushes.DimGray;
            closeButton.Click += OnInstrumentTabCloseClick;

            var textBlock = new TextBlock
            {
                Text = headerText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var headerPanel = new DockPanel
            {
                LastChildFill = false,
                MinWidth = 92,
                Margin = new Thickness(0),
                Tag = session
            };

            headerPanel.PreviewMouseLeftButtonDown += OnInstrumentTabHeaderMouseLeftButtonDown;
            headerPanel.MouseMove += OnInstrumentTabHeaderMouseMove;

            DockPanel.SetDock(closeButton, Dock.Right);
            DockPanel.SetDock(textBlock, Dock.Left);

            headerPanel.Children.Add(closeButton);
            headerPanel.Children.Add(textBlock);

            return new TabItem
            {
                Header = headerPanel,
                Tag = session,
                MinWidth = 92,
                Padding = new Thickness(6, 2, 6, 2)
            };
        }
    }
}