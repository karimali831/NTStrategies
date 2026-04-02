namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public enum GuardAction
        {
            Ignore,
            LogOnly,
            DisableFollower,
            Flatten,
            FlattenAndDisable
            // RetryThenDisable,
            // RetryThenFlatten
        }
    }
}