using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Window _settingsWindow;

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
                IsChecked = _simOnlyMode
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

            stack.Children.Add(simMode);

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
    }
}

