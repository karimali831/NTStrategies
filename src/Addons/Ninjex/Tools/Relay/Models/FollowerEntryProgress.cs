using System;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
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