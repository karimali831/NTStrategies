using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _breakEvenEnabled = true;
        private double _freeTradeMinProfitPoints = 4;
        private double _freeTradePlusPoints = 1;

        private TextBox _freeTradeMinProfitPointsBox;
        private TextBox _freeTradePlusPointsBox;
        private CheckBox _breakEvenEnabledCheck;
        
        private UIElement RenderBreakEvenFieldset()
        {
              var bePanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            _breakEvenEnabledCheck = new CheckBox
            {
                Content = "Enable break-even",
                IsChecked = _breakEvenEnabled,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _breakEvenEnabledCheck.Checked += (s, e) =>
            {
                _breakEvenEnabled = true;
                RenderBreakEvenEnablementUi();
                ApplyConfigFromUi();
            };

            _breakEvenEnabledCheck.Unchecked += (s, e) =>
            {
                _breakEvenEnabled = false;
                RenderBreakEvenEnablementUi();
                ApplyConfigFromUi();
            };

            var minProfitRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            minProfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            minProfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            minProfitRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var minProfitLbl = new TextBlock
            {
                Text = "Min favour points:",
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

            var minProfitHint = new TextBlock
            {
                Text = "Required before Break-even Allowed",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brushes.DimGray
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
                Text = "Stop beyond entry points:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _freeTradePlusPointsBox = new TextBox
            {
                Height = 24,
                Width = 80,
                Text = _freeTradePlusPoints.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var plusPointsHint = new TextBlock
            {
                Text = "0 = exact break-even",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Foreground = Brushes.DimGray
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

            bePanel.Children.Add(_breakEvenEnabledCheck);
            bePanel.Children.Add(minProfitRow);
            bePanel.Children.Add(plusPointsRow);

            return BuildFieldset("Break-even", bePanel);
        }
    }
}