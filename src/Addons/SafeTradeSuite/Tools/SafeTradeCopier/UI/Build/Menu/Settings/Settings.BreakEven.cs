using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private double _freeTradeMinProfitPoints = 4;
        private double _freeTradePlusPoints = 1;
        private BreakEvenMode _breakEvenMode = BreakEvenMode.Manual;
        private TextBox _freeTradeMinProfitPointsBox;
        private TextBox _freeTradePlusPointsBox;
        private ComboBox _breakEvenModeSelector;
        private bool BreakEvenDisabled => _breakEvenMode == BreakEvenMode.None;
        private bool BreakEvenAutoMode => _breakEvenMode == BreakEvenMode.Auto;
        private bool BreakEvenManualMode => _breakEvenMode == BreakEvenMode.Manual;
        
        private UIElement RenderBreakEvenFieldset()
        {
              var bePanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            var modeRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            modeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var modeLbl = new TextBlock
            {
                Text = "Mode",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0,0,8,0),
                Foreground = WindowForegroundBrush()
            };

            _breakEvenModeSelector = CreateFormComboBox(width: 120);
            _breakEvenModeSelector.Items.Add("None");
            _breakEvenModeSelector.Items.Add("Auto");
            _breakEvenModeSelector.Items.Add("Manual");

            _breakEvenModeSelector.SelectedIndex = (int)_breakEvenMode;

            _breakEvenModeSelector.SelectionChanged += (s, e) =>
            {
                _breakEvenMode = (BreakEvenMode)_breakEvenModeSelector.SelectedIndex;
                RenderBreakEvenEnablementUi();
                ApplyConfigFromUi();
            };

            Grid.SetColumn(modeLbl, 0);
            Grid.SetColumn(_breakEvenModeSelector, 1);

            modeRow.Children.Add(modeLbl);
            modeRow.Children.Add(_breakEvenModeSelector);

            var minProfitRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            minProfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            minProfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            minProfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var minProfitLbl = new TextBlock
            {
                Text = "Min profit points",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = !BreakEvenDisabled
            };

            _freeTradeMinProfitPointsBox = CreateFormTextBox(
                _freeTradeMinProfitPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                width: 80,
                isEnabled: !BreakEvenDisabled);

            var minProfitHint = new TextBlock
            {
                Text = "Required before Break-even Allowed",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = MutedForegroundBrush()
            };

            Grid.SetColumn(minProfitLbl, 0);
            Grid.SetColumn(_freeTradeMinProfitPointsBox, 1);
            Grid.SetColumn(minProfitHint, 2);

            minProfitRow.Children.Add(minProfitLbl);
            minProfitRow.Children.Add(_freeTradeMinProfitPointsBox);
            minProfitRow.Children.Add(minProfitHint);

            var plusPointsRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 0)
            };
            plusPointsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            plusPointsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            plusPointsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var plusPointsLbl = new TextBlock
            {
                Text = "Stop beyond entry points",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _freeTradePlusPointsBox = CreateFormTextBox(
                _freeTradePlusPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                width: 80);

            var plusPointsHint = new TextBlock
            {
                Text = "0 = exact break-even",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = MutedForegroundBrush()
            };

            Grid.SetColumn(plusPointsLbl, 0);
            Grid.SetColumn(_freeTradePlusPointsBox, 1);
            Grid.SetColumn(plusPointsHint, 2);

            plusPointsRow.Children.Add(plusPointsLbl);
            plusPointsRow.Children.Add(_freeTradePlusPointsBox);
            plusPointsRow.Children.Add(plusPointsHint);

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
                _freeTradeMinProfitPointsBox.Text =
                    _freeTradeMinProfitPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                RenderBreakEvenEnablementUi();
                ApplyConfigFromUi();
            };

            _freeTradePlusPointsBox.LostKeyboardFocus += (s, e) =>
            {
                if (!double.TryParse(
                        (_freeTradePlusPointsBox.Text ?? "").Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var pts))
                {
                    pts = _freeTradePlusPoints;
                }

                if (pts < 0)
                    pts = 0;

                _freeTradePlusPoints = pts;
                _freeTradePlusPointsBox.Text =
                    _freeTradePlusPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                RenderBreakEvenEnablementUi();
                ApplyConfigFromUi();
            };

            bePanel.Children.Add(modeRow);
            bePanel.Children.Add(minProfitRow);
            bePanel.Children.Add(plusPointsRow);

            return BuildFieldset("Break-even", bePanel);
        }
    }
}