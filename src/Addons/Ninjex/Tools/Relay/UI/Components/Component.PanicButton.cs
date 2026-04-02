using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private UIElement BuildTopMenuBar(RelayEngine eng)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mainMenuTabs = BuildMainMenuTabs();
            Grid.SetColumn(mainMenuTabs, 0);
            grid.Children.Add(mainMenuTabs);

            var panicButton = BuildPanicButton(eng);
            Grid.SetColumn(panicButton, 1);
            grid.Children.Add(panicButton);

            return grid;
        }

        private UIElement BuildPanicButton(RelayEngine eng)
        {
            var host = new Grid
            {
                Width = 55,
                Height = 55,
                Margin = new Thickness(12, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip =
                    "Emergency stop: cancel all working orders and flatten all open positions " +
                    "across configured copier accounts and instruments, then disarm.",
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.35
                }
            };

            var outerRing = new Ellipse
            {
                Fill = new LinearGradientBrush(
                    Color.FromRgb(210, 210, 210),
                    Color.FromRgb(145, 145, 145),
                    90),
                Stroke = new SolidColorBrush(Color.FromRgb(105, 105, 105)),
                StrokeThickness = 1.5
            };

            var face = new Ellipse
            {
                Margin = new Thickness(3),
                Fill = new LinearGradientBrush(
                    Color.FromRgb(255, 72, 72),
                    Color.FromRgb(186, 0, 0),
                    90),
                Stroke = new SolidColorBrush(Color.FromRgb(120, 0, 0)),
                StrokeThickness = 2
            };

            var gloss = new Ellipse
            {
                Margin = new Thickness(16, 12, 16, 42),
                Fill = new LinearGradientBrush(
                    Color.FromArgb(120, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255),
                    90),
                IsHitTestVisible = false
            };

            var label = new TextBlock
            {
                Text = "PANIC",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            host.Children.Add(outerRing);
            host.Children.Add(face);
            host.Children.Add(gloss);
            host.Children.Add(label);

            var pressedTransform = new ScaleTransform(1.0, 1.0);
            host.RenderTransformOrigin = new Point(0.5, 0.5);
            host.RenderTransform = pressedTransform;

            host.MouseEnter += (s, e) =>
            {
                pressedTransform.ScaleX = 1.03;
                pressedTransform.ScaleY = 1.03;
            };

            host.MouseLeave += (s, e) =>
            {
                pressedTransform.ScaleX = 1.0;
                pressedTransform.ScaleY = 1.0;
            };

            host.MouseLeftButtonDown += (s, e) =>
            {
                pressedTransform.ScaleX = 0.97;
                pressedTransform.ScaleY = 0.97;
                e.Handled = true;
            };

            host.MouseLeftButtonUp += (s, e) =>
            {
                pressedTransform.ScaleX = 1.03;
                pressedTransform.ScaleY = 1.03;
                e.Handled = true;

                if (eng == null)
                    return;

                var confirmed = ShowConfirmDialog(
                    _window,
                    "Emergency stop",
                    "This will cancel all working orders and flatten all open positions across all configured copier accounts and instruments, then disarm the copier.\n\nThis action cannot be undone.",
                    okText: "Panic Flatten",
                    cancelText: "Cancel");

                if (!confirmed)
                {
                    eng.Log("Emergency stop cancelled by user.");
                    return;
                }
                
                eng.Log("Emergency stop confirmed.");
                eng.EmergencyStopConfiguredAccounts();

                RefreshRelayStatusPanel();
                RenderFollowerRowsState();
                RenderMasterSubmitButtonsState();
                RefreshFollowerBulkActionButtons();
            };

            return host;
        }
    }
}