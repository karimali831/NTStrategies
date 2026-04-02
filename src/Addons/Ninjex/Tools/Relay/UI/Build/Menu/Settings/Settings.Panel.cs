using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private UIElement BuildSettingsContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(0)
            };

            var generalFieldset = RenderGeneralFieldset();
            var beFieldset = RenderBreakEvenFieldset();
            var followerGuardFieldset = RenderFollowerGuardFieldset();

            _riskFieldsetHost = new ContentControl
            {
                Content = RenderRiskFieldset()
            };

            root.Children.Add(generalFieldset);
            root.Children.Add(beFieldset);
            root.Children.Add(followerGuardFieldset);
            root.Children.Add(_riskFieldsetHost);

            return CreateScrollbar(root);
        }
    }
}