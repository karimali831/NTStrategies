using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Window _settingsWindow;
        private bool _showStatusBox;
        private TextBlock _statusLabel;
        private double _freeTradeMinProfitPoints = 1.0;
        private TextBox _freeTradeMinProfitPointsBox;

        private void OpenSettingsPanel()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            var stack = new StackPanel
            {
                Margin = new Thickness(10)
            };

            var simMode = new CheckBox
            {
                Content = "Simulation Mode",
                IsChecked = _simOnlyMode,
                Margin = new Thickness(0, 0, 0, 8)
            };

            simMode.Checked += (s, e) =>
            {
                _simOnlyMode = true;
                OnSimModeChanged();
            };

            simMode.Unchecked += (s, e) =>
            {
                _simOnlyMode = false;
                OnSimModeChanged();
            };

            var showStatusBox = new CheckBox
            {
                Content = "Show status log",
                IsChecked = _showStatusBox,
                Margin = new Thickness(0, 0, 0, 8)
            };

            showStatusBox.Checked += (s, e) =>
            {
                _showStatusBox = true;
                ApplyStatusBoxVisibility();
            };

            showStatusBox.Unchecked += (s, e) =>
            {
                _showStatusBox = false;
                ApplyStatusBoxVisibility();
            };

            var ftRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            ftRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ftRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            ftRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var ftLbl = new TextBlock
            {
                Text = "Free Trade min profit points:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _freeTradeMinProfitPointsBox = new TextBox
            {
                Height = 24,
                Width = 80,
                Text = _freeTradeMinProfitPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var ftHint = new TextBlock
            {
                Text = "0 = disabled",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brushes.DimGray
            };

            Grid.SetColumn(ftLbl, 0);
            Grid.SetColumn(_freeTradeMinProfitPointsBox, 1);
            Grid.SetColumn(ftHint, 2);

            ftRow.Children.Add(ftLbl);
            ftRow.Children.Add(_freeTradeMinProfitPointsBox);
            ftRow.Children.Add(ftHint);

            _freeTradeMinProfitPointsBox.LostKeyboardFocus += (s, e) =>
            {
                if (!double.TryParse(
                        (_freeTradeMinProfitPointsBox.Text ?? "").Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var pts))
                {
                    pts = _freeTradeMinProfitPoints;
                }

                if (pts < 0)
                    pts = 0;

                _freeTradeMinProfitPoints = pts;
                _freeTradeMinProfitPointsBox.Text = _freeTradeMinProfitPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            };

            stack.Children.Add(simMode);
            stack.Children.Add(showStatusBox);
            stack.Children.Add(ftRow);

            _settingsWindow = new Window
            {
                Title = "Safe Trade Copier Settings",
                Width = 460,
                Height = 340,
                Content = stack
            };

            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        
        private void OnSimModeChanged()
        {
            EnforceSimOnlyModeUi(GetSelectableAccounts());
            ApplyConfigFromUi();
            RenderFollowerRowsState();
            RenderFlattenAllButtonState();
            RefreshStatusBar();
        }
        
        private void ApplyStatusBoxVisibility()
        {
            var visibility = _showStatusBox ? Visibility.Visible : Visibility.Collapsed;

            if (_statusLabel != null)
                _statusLabel.Visibility = visibility;

            if (_statusBox != null)
                _statusBox.Visibility = visibility;
        }
    }
}

