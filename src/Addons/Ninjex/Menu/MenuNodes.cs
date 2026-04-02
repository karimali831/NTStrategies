namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Menu
{
    public static class NinjexMenuNodes
    {
        private const string SuiteHeader = "Ninjex";

        public static MenuNode[] Build(System.Action openRelayTool)
        {
            return new[]
            {
                new MenuNode(
                    header: SuiteHeader,
                    automationId: "Ninjex_Root",
                    children: new[]
                    {
                        new MenuNode(
                            header: "RelayTool",
                            automationId: "Ninjex_RelayTool",
                            onClick: openRelayTool
                        )
                    }
                )
            };
        }
    }
}