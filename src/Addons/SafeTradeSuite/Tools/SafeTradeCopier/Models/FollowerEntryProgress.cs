using System;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class FollowerEntryProgress
        {
            public string MasterEntryName;
            public string FollowerAccountName;
            public string FollowerOrderName;
            public int RequestedQty;
            public int FilledQty;
            public DateTime LastUpdateUtc;
        }
    }
}