namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            public sealed class ActiveBracketSpec
            {
                public bool AutoBeSuppressedUntilFlat { get; set; }

                public int StopTicks;
                public int TargetTicks;

                public bool IsBuy;
                public int Qty;

                public int EntryFilledQty;
                public double EntryValueSum;

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
}