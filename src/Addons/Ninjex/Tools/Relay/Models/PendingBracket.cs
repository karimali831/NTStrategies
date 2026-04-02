namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private sealed class PendingBracket
        {
            public string EntryName;
            public int OriginalQty;
            public bool IsBuy;
            public int StopTicks;
            public int TargetTicks;

            public int FilledQty;
            public double EntryValueSum;

            public bool BracketSubmitted;
            public string BracketOco;
        }
    }
}