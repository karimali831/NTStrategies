using System;
using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _followerGuardEnabled = true;
        private int _followerGuardEntryFillTimeoutSeconds = 5;
        private int _followerGuardDesyncGraceSeconds = 3;
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
                Margin = new Thickness(0, 0, 0, 10)
            });

            _fgEnabledCheckBox = CreateCheckBox(
                "Enable", 
                isChecked: _followerGuardEnabled,
                margin: new Thickness(0, 0, 0, 10)
            );
            
            _fgEnabledCheckBox.Checked += (_, __) => ApplyFollowerGuardSettingsFromUi();
            _fgEnabledCheckBox.Unchecked += (_, __) => ApplyFollowerGuardSettingsFromUi();
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

            _fgEntryTimeoutTextBox.TextChanged += (_, __) => ApplyFollowerGuardSettingsFromUi();
            _fgDesyncGraceTextBox.TextChanged += (_, __) => ApplyFollowerGuardSettingsFromUi();

            _fgOnEntryRejectComboBox.SelectionChanged += (_, __) => ApplyFollowerGuardSettingsFromUi();
            _fgOnEntryTimeoutComboBox.SelectionChanged += (_, __) => ApplyFollowerGuardSettingsFromUi();
            _fgOnDesyncComboBox.SelectionChanged += (_, __) => ApplyFollowerGuardSettingsFromUi();

            LoadFollowerGuardSettingsIntoUi();

            return BuildFieldset("Follower Shield", panel);
        }

        private static UIElement BuildFollowerGuardNumberRow(
            string label,
            string helpText,
            out TextBox textBox,
            string defaultValue)
        {
            var row = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            row.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4)
            });

            row.Children.Add(new TextBlock
            {
                Text = helpText,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            textBox = CreateFormTextBox(defaultValue);
            row.Children.Add(textBox);
            return row;
        }

        private static UIElement BuildFollowerGuardActionRow(
            string label,
            string helpText,
            out ComboBox comboBox)
        {
            var row = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            row.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 0, 0, 4)
            });

            row.Children.Add(new TextBlock
            {
                Text = helpText,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            comboBox = CreateFormComboBox();
            comboBox.MinWidth = 180;
            comboBox.MaxWidth = 260;
            comboBox.ItemsSource = Enum.GetValues(typeof(GuardAction));
            
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