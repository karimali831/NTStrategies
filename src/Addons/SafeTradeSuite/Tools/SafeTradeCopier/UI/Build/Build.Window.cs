using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Button _btnWindowMinimize;
        private Button _btnWindowMaximize;
        private Button _btnWindowClose;
        private Border _windowTitleBar;
        
        private UIElement BuildChromedWindowContent(SafeCopierEngine eng)
        {
            var root = new Grid
            {
                Background = WindowBackgroundBrush(),
                SnapsToDevicePixels = true
            };

            var outerBorder = new Border
            {
                Background = WindowBackgroundBrush(),
                BorderBrush = IsDarkTheme()
                    ? new SolidColorBrush(Color.FromRgb(42, 42, 42))
                    : new SolidColorBrush(Color.FromRgb(210, 210, 210)),
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true
            };

            var chromeHost = new Grid();
            chromeHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            chromeHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _windowTitleBar = BuildWindowTitleBar();
            Grid.SetRow(_windowTitleBar, 0);
            chromeHost.Children.Add(_windowTitleBar);

            var contentHost = new Border
            {
                Background = WindowBackgroundBrush(),
                Padding = new Thickness(0),
                Child = SafeBuildUi(eng)
            };
            Grid.SetRow(contentHost, 1);
            chromeHost.Children.Add(contentHost);

            outerBorder.Child = chromeHost;
            root.Children.Add(outerBorder);

            return root;
        }
        
        private Border BuildWindowTitleBar()
        {
            var bg = IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(24, 24, 24))
                : new SolidColorBrush(Color.FromRgb(245, 245, 245));

            var borderBrush = IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(42, 42, 42))
                : new SolidColorBrush(Color.FromRgb(210, 210, 210));

            var titleBar = new Border
            {
                Background = bg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Height = 42
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new System.Windows.Shapes.Path
            {
                Data = CreateCopierIcon(),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                Fill = ThemeColor(),
                Margin = new Thickness(12, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(icon, 0);

            var title = new TextBlock
            {
                Text = "Safe Trade Copier (V2.1)",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WindowForegroundBrush(),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(title, 1);

            _btnWindowMinimize = BuildCaptionButton("—");
            _btnWindowMinimize.Click += (s, e) =>
            {
                if (_window != null)
                    _window.WindowState = WindowState.Minimized;
            };
            Grid.SetColumn(_btnWindowMinimize, 2);

            _btnWindowMaximize = BuildCaptionButton("☐");
            _btnWindowMaximize.Click += (s, e) =>
            {
                if (_window == null)
                    return;

                _window.WindowState = _window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;

                UpdateWindowCaptionButtons();
            };
            Grid.SetColumn(_btnWindowMaximize, 3);

            _btnWindowClose = BuildCaptionButton("✕", isCloseButton: true);
            _btnWindowClose.Click += (s, e) =>
            {
                _window?.Close();
            };
            Grid.SetColumn(_btnWindowClose, 4);

            grid.Children.Add(icon);
            grid.Children.Add(title);
            grid.Children.Add(_btnWindowMinimize);
            grid.Children.Add(_btnWindowMaximize);
            grid.Children.Add(_btnWindowClose);

            titleBar.Child = grid;

            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                if (_window == null)
                    return;

                if (e.ClickCount == 2)
                {
                    if (_window.ResizeMode != ResizeMode.NoResize)
                    {
                        _window.WindowState = _window.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;

                        UpdateWindowCaptionButtons();
                    }

                    return;
                }

                _window.DragMove();
            };

            return titleBar;
        }
        
        private void UpdateWindowCaptionButtons()
        {
            if (_btnWindowMaximize == null || _window == null)
                return;

            _btnWindowMaximize.Content = _window.WindowState == WindowState.Maximized ? "❐" : "☐";
        }
        
        private static Button BuildCaptionButton(string text, bool isCloseButton = false)
        {
            var fg = WindowForegroundBrush();

            var btn = new Button
            {
                Content = text,
                Width = 46,
                Height = 42,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Foreground = fg,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = null,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            btn.MouseEnter += (s, e) =>
            {
                btn.Background = isCloseButton
                    ? new SolidColorBrush(Color.FromRgb(196, 43, 28))
                    : IsDarkTheme()
                        ? new SolidColorBrush(Color.FromRgb(52, 52, 52))
                        : new SolidColorBrush(Color.FromRgb(225, 225, 225));
            };

            btn.MouseLeave += (s, e) =>
            {
                btn.Background = Brushes.Transparent;
            };

            return btn;
        }
    }
}