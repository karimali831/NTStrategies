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
        }
    }
}