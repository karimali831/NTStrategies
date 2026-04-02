using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        internal sealed class ReadyStatus
        {
            public string PrimaryReason { get; set; }
            public Brush PrimaryReasonColor { get; set; }
            public string SecondaryReason { get; set; }
            public Brush PrimaryDotColour { get; set; }
            public Brush SecondaryDotColour { get; set; }
        }
        
        private Border _relayReadyDot;
        private Border _relayReadySecondaryDot;
        private TextBlock _relayReadyText;
        private TextBlock _relayReadySecondaryText;

        private void RenderRelayStatusPanel(Grid root)
        {
            var statusStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };

            statusStack.Children.Add(BuildStatusLine(out _relayReadyDot, out _relayReadyText));
            statusStack.Children.Add(BuildStatusLine(out _relayReadySecondaryDot, out _relayReadySecondaryText));

            var statusFieldset = BuildFieldset("Status", statusStack);

            Grid.SetColumn(statusFieldset, 2);
            Grid.SetRow(statusFieldset, 1);

            root.Children.Add(statusFieldset);
            
            RefreshRelayStatusPanel();
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

        private void RefreshRelayStatusPanel()
        {
            if (_relayReadyText == null || _relayReadyDot == null)
                return;

            var reason = GetReadyReason();

            _relayReadyDot.Background = reason.PrimaryDotColour ?? DotOffBrush();
            _relayReadyText.Text = reason.PrimaryReason ?? string.Empty;
            _relayReadyText.Foreground = reason.PrimaryReasonColor ?? PrimaryTextBrush();

            if (_relayReadySecondaryDot != null && _relayReadySecondaryText != null)
            {
                var hasSecondary = !string.IsNullOrWhiteSpace(reason.SecondaryReason);

                _relayReadySecondaryDot.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;
                _relayReadySecondaryText.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;

                _relayReadySecondaryDot.Background = reason.SecondaryDotColour ?? DotOffBrush();
                _relayReadySecondaryText.Text = reason.SecondaryReason ?? string.Empty;
            }
        }

        public bool Armed()
        {
            return _engine != null && _engine.IsRequested && _engine.Armed;
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
                readyStatus.PrimaryReason = "Engine initialisation error";
                return readyStatus;
            }

            if (!IsMasterConnected(out _))
            {
                readyStatus.PrimaryReason = "Master disconnected";
                readyStatus.PrimaryDotColour = disconnectedColor;
                return readyStatus;
            }

            var armed = Armed();
            var requested = ActiveSessionRequested();

            var anyCheckedFollowers = HasAnyCheckedFollowers();
            var healthyCheckedFollowers = CountCheckedFollowersHealthy();
            var allCheckedFollowersHealthy = anyCheckedFollowers && AreAllCheckedFollowersHealthy();
            
            var ready = armed && allCheckedFollowersHealthy;

            readyStatus.PrimaryDotColour = connectedColor;
            readyStatus.PrimaryReason = $"{(_simOnlyMode ? "Sim" : "Live")} {(ready ? "relay" : "master")} ready";

            if (AllFollowersUnhealthy())
            {
                readyStatus.SecondaryReason = "All followers unhealthy";
                readyStatus.SecondaryDotColour = disconnectedColor;
                return readyStatus;
            }

            if (!anyCheckedFollowers)
            {
                readyStatus.SecondaryReason = requested
                    ? "Arm requested - select followers"
                    : "No followers selected";
                readyStatus.SecondaryDotColour = warningColor;
                return readyStatus;
            }

            if (!allCheckedFollowersHealthy)
            {
                readyStatus.SecondaryReason = requested
                    ? "Arm requested - check followers"
                    : "Selected followers unhealthy";
                readyStatus.SecondaryDotColour = warningColor;
                return readyStatus;
            }

            if (requested && !armed)
            {
                readyStatus.SecondaryReason = "Arm requested - waiting";
                readyStatus.SecondaryDotColour = warningColor;
                return readyStatus;
            }

            if (ready && !_simOnlyMode)
                readyStatus.PrimaryReasonColor = DotConnectedOnBrush();

            readyStatus.SecondaryReason = armed
                ? $"Follower{(healthyCheckedFollowers == 1 ? "" : "s")} armed"
                : $"Follower{(healthyCheckedFollowers == 1 ? "" : "s")} disarmed";

            readyStatus.SecondaryDotColour = armed ? connectedColor : warningColor;
            return readyStatus;
        }
    }
}