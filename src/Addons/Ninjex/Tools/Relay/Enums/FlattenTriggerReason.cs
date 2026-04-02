namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public enum FlattenTriggerReason
        {
            None = 0,
            ManualFlatten = 1,
            ManualFlattenAll = 2,
            Panic = 3,
            FollowMasterExit = 4,
            RiskProtection = 5,
            FollowerGuard = 6
        }
    }
}