using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _showStatusBox;
        private TextBlock _statusLabel;
        
        private UIElement RenderGeneralFieldset()
        {
            var generalPanel = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
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
                Margin = new Thickness(0, 0, 0, 0)
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

            generalPanel.Children.Add(simMode);
            generalPanel.Children.Add(showStatusBox);

            return BuildFieldset("General", generalPanel);
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