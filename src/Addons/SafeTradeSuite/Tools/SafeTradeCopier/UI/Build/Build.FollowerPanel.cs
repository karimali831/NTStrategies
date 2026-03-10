using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Button _btnCopyOn;
        private readonly List<FollowerRow> _followerRows = new List<FollowerRow>();
        
        private void RenderFollowerPanel(SafeCopierEngine eng, Grid root)
        {
            var followersStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var followersTitleRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            followersTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            followersTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var followersTitleText = new TextBlock
            {
                Text = "Followers",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden
            };

            _btnCopyOn = new Button
            {
                Height = 28,
                MinWidth = 120,
                Padding = new Thickness(10, 2, 10, 2),
                Margin = new Thickness(10, 0, 0, 0),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Content = eng.CopyEnabled ? "Armed" : "Disarmed",
                Background = eng.CopyEnabled ? Brushes.DarkGreen : Brushes.Maroon,
                BorderBrush = eng.CopyEnabled ? Brushes.DarkGreen : Brushes.Maroon
            };

            Grid.SetColumn(followersTitleText, 0);
            Grid.SetColumn(_btnCopyOn, 1);

            followersTitleRow.Children.Add(followersTitleText);
            followersTitleRow.Children.Add(_btnCopyOn);

            followersStack.Children.Add(followersTitleRow);
            followersStack.Children.Add(BuildFollowerHeaderRow());

            _followersPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var followersScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = true,
                Height = 240,
                Content = _followersPanel,
                Background = SystemColors.ControlLightBrush
            };

            followersStack.Children.Add(followersScroll);

            var followersFieldset = BuildFieldset("Followers", followersStack);
            Grid.SetRow(followersFieldset, 4);
            root.Children.Add(followersFieldset);

            _btnCopyOn.Click += (s, e) =>
            {
                if (eng.CopyEnabled)
                {
                    RequestCopyDisabled(
                        manual: true,
                        allowAutoRearm: false,
                        reason: "Copy manually disabled.");
                    return;
                }

                _userManuallyDisarmed = false;
                _autoRearmPending = false;
                RequestCopyEnabled("Manual enable requested.");
            };
        }

        private void RenderButtons(bool copyOn)
        {
            if (_btnCopyOn == null)
                return;

            _btnCopyOn.IsEnabled = true;
            _btnCopyOn.Content = copyOn ? "Armed" : "Disarmed";
            _btnCopyOn.Background = copyOn ? Brushes.DarkGreen : Brushes.Maroon;
            _btnCopyOn.BorderBrush = copyOn ? Brushes.DarkGreen : Brushes.Maroon;
        }
    }
}