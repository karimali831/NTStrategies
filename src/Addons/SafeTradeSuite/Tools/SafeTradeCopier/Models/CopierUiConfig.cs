using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class CopierUiConfig
        {
            public Account MasterAccount;
            public string InstrumentName;
            public int MasterQty;
            public string MasterAtm;
            public List<Account> Followers;
            public Dictionary<string, int> FollowerQtyOverrides;
            public Dictionary<string, string> FollowerAtmOverrides;
            public double MasterMaxDailyProfit { get; set; }
            public double MasterMaxDailyLoss { get; set; }

            public Dictionary<string, bool> FollowerUseMasterRisk { get; set; } =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            public Dictionary<string, double> FollowerMaxDailyProfit { get; set; } =
                new Dictionary<string, double>(StringComparer.Ordinal);

            public Dictionary<string, double> FollowerMaxDailyLoss { get; set; } =
                new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }
}