namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        internal sealed class ActiveBracketSpec
        {
            public int StopTicks;
            public int TargetTicks;

            public bool IsBuy;
            public int Qty;

            public double EntryPrice;
            public double OriginalStopPrice;
            public double CurrentStopPrice;
            public double TargetPrice;

            public bool IsFreeTradeApplied;

            public string StopOrderName;
            public string TargetOrderName;
            public string StopOco;
        }
    }
}