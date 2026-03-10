using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Window _settingsWindow;
        private bool _showStatusBox = false;
        private TextBlock _statusLabel;

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

            simMode.Checked += (s,e) =>
            {
                _simOnlyMode = true;
                OnSimModeChanged();
            };

            simMode.Unchecked += (s,e) =>
            {
                _simOnlyMode = false;
                OnSimModeChanged();
            };

            var showStatusBox = new CheckBox
            {
                Content = "Show status log",
                IsChecked = _showStatusBox
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

            stack.Children.Add(simMode);
            stack.Children.Add(showStatusBox);

            _settingsWindow = new Window
            {
                Title = "Safe Trade Copier Settings",
                Width = 420,
                Height = 320,
                Content = stack
            };

            _settingsWindow.Closed += (s,e) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        
        private void OnSimModeChanged()
        {
            EnforceSimOnlyModeUi(GetSelectableAccounts());
            ApplyConfigFromUi();

            RenderFollowerRowsState();
            RenderFlattenEnablementUi();
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

