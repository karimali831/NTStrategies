using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        internal sealed class ReadyStatus
        {
            public string PrimaryReason { get; set; }
            public string SecondaryReason { get; set; }
            public Brush PrimaryDotColour { get; set; }
            public Brush SecondaryDotColour { get; set; }
        }
        
        private Border _copierReadyDot;
        private Border _copierReadySecondaryDot;
        private TextBlock _copierReadyText;
        private TextBlock _copierReadySecondaryText;
        private TextBlock _totalPnlText;
        
        private void RenderCopierStatusPanel(Grid root)
        {
            var statusStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };

            statusStack.Children.Add(BuildStatusLine(out _copierReadyDot, out _copierReadyText));
            statusStack.Children.Add(BuildStatusLine(out _copierReadySecondaryDot, out _copierReadySecondaryText));

            statusStack.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 10, 0, 10),
                Background = SectionBorderBrush()
            });

            statusStack.Children.Add(BuildTextLine(out _totalPnlText));

            var statusFieldset = BuildFieldset("Status", statusStack);
            Grid.SetColumn(statusFieldset, 2);
            root.Children.Add(statusFieldset);

            RefreshCopierStatusPanel();
        }

        private static UIElement BuildStatusLine(out Border dot, out TextBlock text)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            dot = new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = DotOffBrush(),
                BorderBrush = DotBorderBrush(),
                BorderThickness = new Thickness(1)
            };

            text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WindowForegroundBrush(),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(text, 1);

            row.Children.Add(dot);
            row.Children.Add(text);

            return row;
        }

        private UIElement BuildTextLine(out TextBlock text)
        {
            text = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WindowForegroundBrush(),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            return text;
        }

        private void RefreshCopierStatusPanel()
        {
            if (_copierReadyText == null || _copierReadyDot == null)
                return;

            var reason = GetReadyReason();

            _copierReadyDot.Background = reason.PrimaryDotColour ?? DotOffBrush();
            _copierReadyText.Text = reason.PrimaryReason ?? string.Empty;

            if (_copierReadySecondaryDot != null && _copierReadySecondaryText != null)
            {
                var hasSecondary = !string.IsNullOrWhiteSpace(reason.SecondaryReason);

                _copierReadySecondaryDot.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;
                _copierReadySecondaryText.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;

                _copierReadySecondaryDot.Background = reason.SecondaryDotColour ?? DotOffBrush();
                _copierReadySecondaryText.Text = reason.SecondaryReason ?? string.Empty;
            }
            
            _totalPnlText = new TextBlock
            {
                Text = "Total Unrealized $0.00"
            };
        }
        
        private ReadyStatus GetReadyReason()
        {
            var disconnectedColor = DotDisconnectedBrush();
            var connectedColor = DotConnectedOnBrush();
            var warningColor = DotWarningBrush();
            var offColor = DotOffBrush();
            
            var readyStatus = new ReadyStatus
            {
                PrimaryReason = "Checking...",
                SecondaryReason = null,
                PrimaryDotColour = offColor,
                SecondaryDotColour = null
            };

            if (_engine is null)
            {
                readyStatus.PrimaryReason = "Engine Initialisation Error";
                return readyStatus;
            }

            if (!IsMasterConnected())
            {
                readyStatus.PrimaryReason = "Master Disconnected";
                readyStatus.PrimaryDotColour = disconnectedColor;
                return readyStatus;
            }

            readyStatus.PrimaryDotColour = connectedColor;
            var armed = _engine != null && _engine.CopyEnabled && _engine.Armed;
            
            if (_simOnlyMode)
            {
                readyStatus.PrimaryReason = "Simulation Ready";

                if (!HasAnyCheckedSimFollowersHealthy())
                {
                    readyStatus.SecondaryReason = "No followers selected";
                    readyStatus.SecondaryDotColour = warningColor;
                    return readyStatus;
                }

                readyStatus.SecondaryReason = armed ? "Copier armed" : "Copier disarmed";
                readyStatus.SecondaryDotColour = armed ? connectedColor : warningColor;
                return readyStatus;
            }
            
            readyStatus.PrimaryReason = "Ready";

            if (!HasAnyCheckedLiveFollowersHealthy())
            {
                readyStatus.SecondaryReason = "No live followers selected";
                readyStatus.SecondaryDotColour = warningColor;
                return readyStatus;
            }
            
            readyStatus.SecondaryReason = armed ? "Copier armed" : "Copier disarmed";
            readyStatus.SecondaryDotColour = armed ? connectedColor : warningColor;
            return readyStatus;
        }
    }
}