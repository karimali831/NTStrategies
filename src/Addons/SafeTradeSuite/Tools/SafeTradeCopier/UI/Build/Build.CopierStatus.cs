using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
 
        private void RenderCopierStatusPanel(Grid root)
        {
            var statusStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            
            var masterFieldset = BuildFieldset("Status", statusStack);
        }
        
    }
}