using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _showStatusBox;
        private ComboBox _themeSelector;
        
        private UIElement RenderGeneralFieldset()
        {
            var generalPanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };

            // Sim mode
            var simMode = CreateCheckBox(
                text: "Simulation Mode",
                isChecked: _simOnlyMode,
                margin: new Thickness(0, 0, 0, 8));

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

            // Status log
            var showStatusBox = CreateCheckBox(
                text: "Show status log",
                isChecked: _showStatusBox);

            showStatusBox.Checked += (s, e) =>
            {
                _showStatusBox = true;
                RebuildMainMenuTabs();
            };

            showStatusBox.Unchecked += (s, e) =>
            {
                _showStatusBox = false;
                RebuildMainMenuTabs();
            };
            
            // Theme
            var themeRow = new Grid
            {
                Margin = new Thickness(0, 6, 0, 0)
            };

            themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            themeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var themeLbl = new TextBlock
            {
                Text = "Theme",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0,0,8,0),
                Foreground = WindowForegroundBrush()
            };

            _themeSelector = CreateFormComboBox(width: 125);

            _themeSelector.Items.Add("System");
            _themeSelector.Items.Add("Light");
            _themeSelector.Items.Add("Dark");

            _themeSelector.SelectedIndex = (int)_themeMode;

            _themeSelector.SelectionChanged += (s, e) =>
            {
                if (_themeSelector.SelectedIndex < 0)
                    return;

                var newTheme = (ThemeMode)_themeSelector.SelectedIndex;
                if (_themeMode == newTheme)
                    return;

                _themeMode = newTheme;
                SavePersistentUiState();
                ReopenWindowForThemeChange();
            };

            Grid.SetColumn(themeLbl, 0);
            Grid.SetColumn(_themeSelector, 1);

            themeRow.Children.Add(themeLbl);
            themeRow.Children.Add(_themeSelector);

            generalPanel.Children.Add(simMode);
            generalPanel.Children.Add(showStatusBox);
            generalPanel.Children.Add(themeRow);

            return BuildFieldset("General", generalPanel);
        }
        
        private void OnSimModeChanged()
        {
            var accounts = GetSelectableAccounts();

            RebindMasterAccounts(accounts);
            RefreshUiAfterAccountScopeChanged();
        }
    }
}