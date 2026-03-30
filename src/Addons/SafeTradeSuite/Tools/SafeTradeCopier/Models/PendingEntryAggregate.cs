using System;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class PendingEntryAggregate
        {
            public string EntryName;
            public string AccountName;
            public string InstrumentName;

            public bool IsBuy;
            public int IntendedQty;

            public int FilledQty;
            public double FilledNotional;   // sum(fillPrice * fillQty)
            public double AverageFillPrice => FilledQty > 0 ? FilledNotional / FilledQty : 0.0;

            public int StopTicks;
            public int TargetTicks;

            public bool BracketSubmitted;
            public string SubmittedOco;

            public DateTime FirstFillUtc;
            public DateTime LastFillUtc;
        }
    }
}