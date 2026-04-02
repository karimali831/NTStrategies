namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private sealed class MasterEntryAggregate
        {
            public string EntryName;
            public bool IsBuy;
            public int TotalFilledQty;
            public double EntryValueSum;
        }
    }
}