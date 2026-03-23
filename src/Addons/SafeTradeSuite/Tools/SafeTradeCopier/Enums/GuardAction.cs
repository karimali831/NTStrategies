namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public enum GuardAction
        {
            Ignore,
            LogOnly,
            DisableFollower,
            Flatten,
            FlattenAndDisable,
            RetryThenDisable,
            RetryThenFlatten
        }
    }
}