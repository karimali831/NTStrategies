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

            var root = new StackPanel
            {
                Margin = new Thickness(10)
            };

            // ---------------- General----------------
            var generalFieldset = RenderGeneralFieldset();

            // ---------------- Break-even ----------------
            var beFieldset = RenderBreakEvenFieldset();
            
            // ---------------- Risk ----------------
            var riskFieldset = RenderRiskFieldset();

            root.Children.Add(generalFieldset);
            root.Children.Add(beFieldset);
            root.Children.Add(riskFieldset);

            // ---------------- Settings Window ----------------
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

            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
            _settingsWindow.Show();
        }
    }
}

