using System;
using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private bool _followerGuardEnabled = true;
        private int _followerGuardEntryFillTimeoutSeconds = 5;
        private int _followerGuardDesyncGraceSeconds = 3;
        private bool _isLoadingFollowerGuardUi;
        private GuardAction _followerGuardOnEntryReject = GuardAction.FlattenAndDisable;
        private GuardAction _followerGuardOnEntryTimeout = GuardAction.FlattenAndDisable;
        private GuardAction _followerGuardOnDesync = GuardAction.FlattenAndDisable;
        
        private CheckBox _fgEnabledCheckBox;
        private TextBox _fgEntryTimeoutTextBox;
        private TextBox _fgDesyncGraceTextBox;
        private ComboBox _fgOnEntryRejectComboBox;
        private ComboBox _fgOnEntryTimeoutComboBox;
        private ComboBox _fgOnDesyncComboBox;

        private UIElement RenderFollowerGuardFieldset()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Safety rules for follower entry failures, timeouts, and sync protection.",
                Opacity = 0.80,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = WindowForegroundBrush()
            });

            _fgEnabledCheckBox = CreateCheckBox(
                "Enable",
                isChecked: _followerGuardEnabled,
                margin: new Thickness(0, 0, 0, 14));

            _fgEnabledCheckBox.Checked += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            _fgEnabledCheckBox.Unchecked += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            panel.Children.Add(_fgEnabledCheckBox);

            panel.Children.Add(BuildFollowerGuardNumberRow(
                "Entry fill timeout (sec)",
                "How long a follower entry may remain unresolved before guard action is triggered.",
                out _fgEntryTimeoutTextBox,
                Math.Max(1, _followerGuardEntryFillTimeoutSeconds).ToString()));

            panel.Children.Add(BuildFollowerGuardNumberRow(
                "Desync grace (sec)",
                "How long a follower may remain out of sync before guard action is triggered.",
                out _fgDesyncGraceTextBox,
                Math.Max(1, _followerGuardDesyncGraceSeconds).ToString()));

            panel.Children.Add(BuildFollowerGuardActionRow(
                "On entry reject",
                "Action to take when a follower entry is explicitly rejected.",
                out _fgOnEntryRejectComboBox));

            panel.Children.Add(BuildFollowerGuardActionRow(
                "On entry timeout",
                "Action to take when a follower entry does not resolve within the timeout window.",
                out _fgOnEntryTimeoutComboBox));

            panel.Children.Add(BuildFollowerGuardActionRow(
                "On desync",
                "Action to take when a follower remains out of sync beyond the grace window.",
                out _fgOnDesyncComboBox));

            _fgEntryTimeoutTextBox.TextChanged += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            _fgDesyncGraceTextBox.TextChanged += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            _fgOnEntryRejectComboBox.SelectionChanged += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            _fgOnEntryTimeoutComboBox.SelectionChanged += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            _fgOnDesyncComboBox.SelectionChanged += (_, __) =>
            {
                if (_isLoadingFollowerGuardUi)
                    return;

                ApplyFollowerGuardSettingsFromUi();
            };

            LoadFollowerGuardSettingsIntoUi();

            return BuildFieldset("Follower Shield", panel);
        }

        private static UIElement BuildFollowerGuardNumberRow(
            string label,
            string helpText,
            out TextBox textBox,
            string defaultValue)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(220)
            });

            var left = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            left.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = WindowForegroundBrush(),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            left.Children.Add(new TextBlock
            {
                Text = helpText,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Foreground = WindowForegroundBrush()
            });

            textBox = CreateFormTextBox(defaultValue);
            textBox.Width = 120;
            textBox.HorizontalAlignment = HorizontalAlignment.Center;
            textBox.VerticalAlignment = VerticalAlignment.Center;
            textBox.Margin = new Thickness(0);

            Grid.SetColumn(left, 0);
            Grid.SetColumn(textBox, 1);

            row.Children.Add(left);
            row.Children.Add(textBox);

            return row;
        }

        private static UIElement BuildFollowerGuardActionRow(
            string label,
            string helpText,
            out ComboBox comboBox)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(220)
            });

            var left = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            left.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = WindowForegroundBrush(),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            left.Children.Add(new TextBlock
            {
                Text = helpText,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Foreground = WindowForegroundBrush()
            });

            comboBox = CreateFormComboBox();
            comboBox.Width = 180;
            comboBox.HorizontalAlignment = HorizontalAlignment.Center;
            comboBox.VerticalAlignment = VerticalAlignment.Center;
            comboBox.ItemsSource = Enum.GetValues(typeof(GuardAction));

            Grid.SetColumn(left, 0);
            Grid.SetColumn(comboBox, 1);

            row.Children.Add(left);
            row.Children.Add(comboBox);

            return row;
        }

        private static int ParseIntOrDefault(string raw, int fallback, int min, int max)
        {
            if (!int.TryParse((raw ?? "").Trim(), out var value))
                return fallback;

            if (value < min) value = min;
            if (value > max) value = max;
            return value;
        }

        private static GuardAction GetSelectedGuardAction(ComboBox comboBox, GuardAction fallback)
        {
            if (comboBox?.SelectedItem is GuardAction action)
                return action;

            return fallback;
        }
    }
}