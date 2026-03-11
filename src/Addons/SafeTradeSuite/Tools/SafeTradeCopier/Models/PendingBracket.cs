namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class PendingBracket
        {
            public string EntryName;
            public int Qty;
            public bool IsBuy;
            public int StopTicks;
            public int TargetTicks;
        }
    }
}