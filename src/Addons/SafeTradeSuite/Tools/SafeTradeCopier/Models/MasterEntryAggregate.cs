namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
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