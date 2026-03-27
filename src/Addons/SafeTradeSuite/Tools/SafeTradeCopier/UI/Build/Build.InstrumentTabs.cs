using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private UIElement _instrumentTabsHost;
        private const int TabWidth = 92;

        private void RenderInstrumentTabs(Grid root)
        {
            var bottom = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            bottom.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var instrumentTabsWrapper = BuildInstrumentTabsWrapper();
            _instrumentTabsHost = instrumentTabsWrapper;
            
            Grid.SetRow(instrumentTabsWrapper, 3);
            Grid.SetColumn(instrumentTabsWrapper, 0);

            bottom.Children.Add(instrumentTabsWrapper);

            Grid.SetRow(bottom, 3);
            root.Children.Add(bottom);
        }
        
        private Border BuildInstrumentTabsWrapper()
        {
            var tabsHost = new Border
            {
                BorderBrush = SectionBorderBrush(),
                BorderThickness = new Thickness(1),
                Background = TabBackgroundBrush(),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 1),
                Height = 40,
                VerticalAlignment = VerticalAlignment.Center
            };

            var tabControlStyle = new Style(typeof(TabControl));
            tabControlStyle.Setters.Add(new Setter(Control.TemplateProperty, BuildFlatInstrumentTabControlTemplate()));
            
            var tabItemStyle = new Style(typeof(TabItem));
            tabItemStyle.Setters.Add(new Setter(Control.TemplateProperty, BuildFlatInstrumentTabItemTemplate()));
            tabItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            tabItemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            tabItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            tabItemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));

            _instrumentTabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Style = tabControlStyle,
                ItemContainerStyle = tabItemStyle
            };

            tabsHost.Child = _instrumentTabs;
            return tabsHost;
        }
        
        private static ControlTemplate BuildFlatInstrumentTabItemTemplate()
        {
            var template = new ControlTemplate(typeof(TabItem));

            var root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            root.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            root.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            root.SetValue(Border.PaddingProperty, new Thickness(0));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);

            root.AppendChild(presenter);
            template.VisualTree = root;

            return template;
        }
        
        private static ControlTemplate BuildFlatInstrumentTabControlTemplate()
        {
            var template = new ControlTemplate(typeof(TabControl));

            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.PaddingProperty, new Thickness(0));
            border.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);

            var tabPanel = new FrameworkElementFactory(typeof(TabPanel));
            tabPanel.SetValue(Panel.IsItemsHostProperty, true);
            tabPanel.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
            tabPanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            tabPanel.SetValue(FrameworkElement.HeightProperty, 38.0);

            border.AppendChild(tabPanel);
            root.AppendChild(border);

            template.VisualTree = root;
            return template;
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
            
            var dotState = GetInstrumentTabDotState(session);

            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = dotState == InstrumentTabDotState.None ? Visibility.Collapsed : Visibility.Visible,
                Background = dotState == InstrumentTabDotState.Green
                    ? SuccessActionBrush()
                    : WarningActionBrush()
            };

            var textBlock = new TextBlock
            {
                Text = headerText,
                Margin = new Thickness(0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = isActive ? WindowForegroundBrush() : MutedForegroundBrush(),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var headerPanel = new Grid
            {
                MinWidth = TabWidth,
                Height = 38,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = session
            };

            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition());
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(textBlock, 1);
            Grid.SetColumn(closeButton, 2);

            headerPanel.Children.Add(dot);
            headerPanel.Children.Add(textBlock);
            headerPanel.Children.Add(closeButton);

            var headerBorder = new Border
            {
                Background = isActive ? TabSelectedBackgroundBrush() : TabBackgroundBrush(),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0),
                Child = headerPanel,
                SnapsToDevicePixels = true,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 38,
                Tag = session
            };

            headerBorder.PreviewMouseLeftButtonDown += OnInstrumentTabHeaderMouseLeftButtonDown;
            headerBorder.MouseMove += OnInstrumentTabHeaderMouseMove;

            return new TabItem
            {
                Header = headerBorder,
                Tag = session,
                MinWidth = TabWidth,
                Height = 38,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = WindowForegroundBrush(),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }
    }
}