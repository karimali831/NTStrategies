using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private UIElement BuildSettingsContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(0)
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

            return CreateScrollbar(root);
        }
    }
}