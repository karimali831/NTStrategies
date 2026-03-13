using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private System.Windows.Controls.Menu BuildMenu()
        {
            var menu = new System.Windows.Controls.Menu();

            var settings = new MenuItem { Header = "Settings" };

            var openSettings = new MenuItem { Header = "Open Settings" };
            openSettings.Click += (s, e) => OpenSettingsPanel();

            settings.Items.Add(openSettings);

            menu.Items.Add(settings);

            return menu;
        }
    }
}