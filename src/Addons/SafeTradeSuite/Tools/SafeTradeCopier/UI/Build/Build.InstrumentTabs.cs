using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Border BuildInstrumentTabsWrapper()
        {
            var tabsHost = new Border
            {
                BorderBrush = SectionBorderBrush(), 
                BorderThickness = new Thickness(1), 
                Background = TabBackgroundBrush(), 
                CornerRadius = new CornerRadius(6), 
                Padding = new Thickness(6, 4, 6, 4), 
                Margin = new Thickness(0, 0, 0, 0)
            }; 
            
            _instrumentTabs = new TabControl
            {
                Background = Brushes.Transparent, 
                BorderThickness = new Thickness(0), 
                Padding = new Thickness(0), 
                Margin = new Thickness(0)
            }; 
            
            tabsHost.Child = _instrumentTabs; 
            return tabsHost;
        }
        
        private TabItem BuildInstrumentTabItem(InstrumentSession session)
        {
            var headerText = NormalizeInstrumentName(session?.InstrumentName);
            if (string.IsNullOrWhiteSpace(headerText))
                headerText = "(instrument)";

            var isActive = ReferenceEquals(session, _activeInstrumentSession);

            var closeButton = new Button
            {
                Content = "×",
                Tag = session,
                Width = 18,
                Height = 18,
                MinWidth = 18,
                MinHeight = 18,
                Padding = new Thickness(0),
                Visibility = Visibility.Visible,
                Focusable = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = isActive ? WindowForegroundBrush() : MutedForegroundBrush(),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Remove instrument"
            };

            closeButton.MouseEnter += (s, e) => closeButton.Foreground = DangerActionBrush();
            closeButton.MouseLeave += (s, e) =>
                closeButton.Foreground = isActive ? WindowForegroundBrush() : MutedForegroundBrush();

            closeButton.Click += OnInstrumentTabCloseClick;

            var textBlock = new TextBlock
            {
                Text = headerText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = isActive ? WindowForegroundBrush() : MutedForegroundBrush(),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
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

            var headerBorder = new Border
            {
                Background = isActive ? TabSelectedBackgroundBrush() : TabBackgroundBrush(),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(10, 6, 10, 6),
                Child = headerPanel,
                SnapsToDevicePixels = true
            };

            return new TabItem
            {
                Header = headerBorder,
                Tag = session,
                MinWidth = 92,
                Background = TabBackgroundBrush(false),
                BorderBrush = TabBorderBrush(false),
                Foreground = WindowForegroundBrush(),
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
        }
    }
}