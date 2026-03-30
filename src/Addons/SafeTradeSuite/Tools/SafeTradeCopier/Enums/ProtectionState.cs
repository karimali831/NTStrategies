namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        internal enum ProtectionState
        {
            Flat = 0,
            EntryPending = 1,
            BracketPending = 2,
            Protected = 3,
            ExitPending = 4,
            FlattenPending = 5,
            Faulted = 6
        }
    }
}