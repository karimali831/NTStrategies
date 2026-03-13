using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    { 
        private Window _settingsWindow;
        private ContentControl _riskFieldsetHost;

        private void OpenSettingsPanel()
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Activate();
                return;
            }

            var root = new StackPanel
            {
                Margin = new Thickness(10)
            };

            var generalFieldset = RenderGeneralFieldset();
            var beFieldset = RenderBreakEvenFieldset();

            _riskFieldsetHost = new ContentControl
            {
                Content = RenderRiskFieldset()
            };

            root.Children.Add(generalFieldset);
            root.Children.Add(beFieldset);
            root.Children.Add(_riskFieldsetHost);

            _settingsWindow = new Window
            {
                Title = "Safe Trade Copier Settings",
                Width = 700,
                Height = 520,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = root
                }
            };

            _settingsWindow.Closed += (s, e) =>
            {
                _settingsWindow = null;
                _riskFieldsetHost = null;
            };

            _settingsWindow.Show();
        }
        
        private void RefreshRiskFieldset()
        {
            if (_riskFieldsetHost == null)
                return;

            _riskFieldsetHost.Content = RenderRiskFieldset();
        }
    }
}