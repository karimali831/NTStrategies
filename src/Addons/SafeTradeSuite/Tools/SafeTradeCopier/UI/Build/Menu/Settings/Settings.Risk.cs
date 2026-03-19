using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBox _masterMaxDailyProfitBox;
        private TextBox _masterMaxDailyLossBox;
        private ContentControl _riskFieldsetHost;

        private readonly Dictionary<string, CheckBox> _followerUseMasterRiskChecks =
            new Dictionary<string, CheckBox>(StringComparer.Ordinal);

        private readonly Dictionary<string, TextBox> _followerMaxDailyProfitBoxes =
            new Dictionary<string, TextBox>(StringComparer.Ordinal);

        private readonly Dictionary<string, TextBox> _followerMaxDailyLossBoxes =
            new Dictionary<string, TextBox>(StringComparer.Ordinal);
        
        private UIElement RenderRiskFieldset()
        {
            var riskPanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            var masterRiskGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            masterRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            masterRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            masterRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            masterRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var masterProfitLbl = new TextBlock
            {
                Text = "Master max daily profit:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            _masterMaxDailyProfitBox = new TextBox
            {
                Height = 24,
                Width = 80,
                Text = _masterMaxDailyProfit.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var masterLossLbl = new TextBlock
            {
                Text = "Master max daily loss:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0)
            };

            _masterMaxDailyLossBox = new TextBox
            {
                Height = 24,
                Width = 80,
                Text = _masterMaxDailyLoss.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(masterProfitLbl, 0);
            Grid.SetColumn(_masterMaxDailyProfitBox, 1);
            Grid.SetColumn(masterLossLbl, 2);
            Grid.SetColumn(_masterMaxDailyLossBox, 3);

            masterRiskGrid.Children.Add(masterProfitLbl);
            masterRiskGrid.Children.Add(_masterMaxDailyProfitBox);
            masterRiskGrid.Children.Add(masterLossLbl);
            masterRiskGrid.Children.Add(_masterMaxDailyLossBox);

            riskPanel.Children.Add(masterRiskGrid);

            riskPanel.Children.Add(new TextBlock
            {
                Text = "0 = disabled",
                Foreground = MutedForegroundBrush(),
                Margin = new Thickness(0, 0, 0, 10)
            });

            _followerUseMasterRiskChecks.Clear();
            _followerMaxDailyProfitBoxes.Clear();
            _followerMaxDailyLossBoxes.Clear();

            var followerRiskGrid = new Grid
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            followerRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); // account
            followerRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // use master
            followerRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // max profit
            followerRiskGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // max loss

            followerRiskGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            void AddRiskHeader(string text, int col)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(6, 4, 6, 6),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = WindowForegroundBrush(),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(tb, 0);
                Grid.SetColumn(tb, col);
                followerRiskGrid.Children.Add(tb);
            }

            AddRiskHeader("Follower", 0);
            AddRiskHeader("Use Master", 1);
            AddRiskHeader("Max Profit", 2);
            AddRiskHeader("Max Loss", 3);

            var settingsAccounts = GetSelectableAccounts();
            var currentMaster = _masterBox?.SelectedItem as NinjaTrader.Cbi.Account;
            var rowIndex = 1;

            foreach (var acc in settingsAccounts)
            {
                if (acc == null)
                    continue;

                if (currentMaster != null && string.Equals(acc.Name, currentMaster.Name, StringComparison.Ordinal))
                    continue;

                if (!_followerUseMasterRisk.ContainsKey(acc.Name))
                    _followerUseMasterRisk[acc.Name] = true;

                if (!_followerMaxDailyProfit.ContainsKey(acc.Name))
                    _followerMaxDailyProfit[acc.Name] = _masterMaxDailyProfit;

                if (!_followerMaxDailyLoss.ContainsKey(acc.Name))
                    _followerMaxDailyLoss[acc.Name] = _masterMaxDailyLoss;

                followerRiskGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var accountTb = new TextBlock
                {
                    Text = acc.Name,
                    Margin = new Thickness(6, 4, 6, 4),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var useMasterCheck = new CheckBox
                {
                    IsChecked = _followerUseMasterRisk[acc.Name],
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4)
                };

                var maxProfitBox = new TextBox
                {
                    Height = 24,
                    Width = 90,
                    Margin = new Thickness(6, 2, 6, 2),
                    Text = _followerMaxDailyProfit[acc.Name].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                var maxLossBox = new TextBox
                {
                    Height = 24,
                    Width = 90,
                    Margin = new Thickness(6, 2, 6, 2),
                    Text = _followerMaxDailyLoss[acc.Name].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                void ApplyFollowerRiskEnabledState()
                {
                    var enabled = useMasterCheck.IsChecked != true;
                    maxProfitBox.IsEnabled = enabled;
                    maxLossBox.IsEnabled = enabled;
                    maxProfitBox.Opacity = enabled ? 1.0 : 0.60;
                    maxLossBox.Opacity = enabled ? 1.0 : 0.60;
                }

                useMasterCheck.Checked += (s, e) =>
                {
                    _followerUseMasterRisk[acc.Name] = true;
                    ApplyFollowerRiskEnabledState();
                    ApplyConfigFromUi();
                };

                useMasterCheck.Unchecked += (s, e) =>
                {
                    _followerUseMasterRisk[acc.Name] = false;
                    ApplyFollowerRiskEnabledState();
                    ApplyConfigFromUi();
                };

                maxProfitBox.LostKeyboardFocus += (s, e) =>
                {
                    if (!double.TryParse(
                            (maxProfitBox.Text ?? "").Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var val))
                    {
                        val = _followerMaxDailyProfit[acc.Name];
                    }

                    if (val < 0)
                        val = 0;

                    _followerMaxDailyProfit[acc.Name] = val;
                    maxProfitBox.Text = val.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyConfigFromUi();
                };

                maxLossBox.LostKeyboardFocus += (s, e) =>
                {
                    if (!double.TryParse(
                            (maxLossBox.Text ?? "").Trim(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var val))
                    {
                        val = _followerMaxDailyLoss[acc.Name];
                    }

                    if (val < 0)
                        val = 0;

                    _followerMaxDailyLoss[acc.Name] = val;
                    maxLossBox.Text = val.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyConfigFromUi();
                };

                ApplyFollowerRiskEnabledState();

                _followerUseMasterRiskChecks[acc.Name] = useMasterCheck;
                _followerMaxDailyProfitBoxes[acc.Name] = maxProfitBox;
                _followerMaxDailyLossBoxes[acc.Name] = maxLossBox;

                Grid.SetRow(accountTb, rowIndex);
                Grid.SetColumn(accountTb, 0);

                Grid.SetRow(useMasterCheck, rowIndex);
                Grid.SetColumn(useMasterCheck, 1);

                Grid.SetRow(maxProfitBox, rowIndex);
                Grid.SetColumn(maxProfitBox, 2);

                Grid.SetRow(maxLossBox, rowIndex);
                Grid.SetColumn(maxLossBox, 3);

                followerRiskGrid.Children.Add(accountTb);
                followerRiskGrid.Children.Add(useMasterCheck);
                followerRiskGrid.Children.Add(maxProfitBox);
                followerRiskGrid.Children.Add(maxLossBox);

                rowIndex++;
            }

            riskPanel.Children.Add(followerRiskGrid);

            _masterMaxDailyProfitBox.LostKeyboardFocus += (s, e) =>
            {
                if (!double.TryParse(
                        (_masterMaxDailyProfitBox.Text ?? "").Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var val))
                {
                    val = _masterMaxDailyProfit;
                }

                if (val < 0)
                    val = 0;

                _masterMaxDailyProfit = val;
                _masterMaxDailyProfitBox.Text = val.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                ApplyConfigFromUi();
            };

            _masterMaxDailyLossBox.LostKeyboardFocus += (s, e) =>
            {
                if (!double.TryParse(
                        (_masterMaxDailyLossBox.Text ?? "").Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var val))
                {
                    val = _masterMaxDailyLoss;
                }

                if (val < 0)
                    val = 0;

                _masterMaxDailyLoss = val;
                _masterMaxDailyLossBox.Text = val.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                ApplyConfigFromUi();
            };

            return BuildFieldset("Risk", riskPanel);
        }
        
        private void RefreshRiskFieldset()
        {
            if (_riskFieldsetHost == null)
                return;

            _riskFieldsetHost.Content = RenderRiskFieldset();
        }
    }
}