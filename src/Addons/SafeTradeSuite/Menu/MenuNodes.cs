namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Menu
{
    public static class SafeTradeSuiteMenuNodes
    {
        private const string SuiteHeader = "SafeTrade Suite";

        public static MenuNode[] Build(System.Action openSafeTradeCopier)
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
                            onClick: openSafeTradeCopier
                        )
                    }
                )
            };
        }
    }
}