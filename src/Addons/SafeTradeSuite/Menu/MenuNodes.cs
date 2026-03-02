#region Using declarations
using NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Menu
{
    public static class SafeTradeSuiteMenuNodes
    {
        private const string SuiteHeader = "SafeTrade Suite";
        
        public static class ToolKeys
        {
            public const string SafeTradeCopier = "SafeTradeSuite.SafeTradeCopier";
        }

        public static MenuNode[] Build(ToolRegistry tools)
        {
            return new[]
            {
                new MenuNode(
                    header: SuiteHeader,
                    automationId: "SafeTradeSuite_Root",
                    children: new[]
                    {
                        new MenuNode(
                            header: "SafeTradeCopier",
                            automationId: "SafeTradeSuite_SafeTradeCopier",
                            onClick: () => tools.Get<SafeTradeCopierTool>(ToolKeys.SafeTradeCopier).Show()
                        )
                    }
                )
            };
        }
    }
}