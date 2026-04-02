namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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