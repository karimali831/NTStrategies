using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class InstrumentSession
        {
            public string InstrumentName;
            public Account MasterAccount;
            public int MasterQty = 1;
            public string MasterAtm = "None";

            public Dictionary<string, bool> FollowersEnabled = new Dictionary<string, bool>(StringComparer.Ordinal);
            public Dictionary<string, int> FollowerQtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            public Dictionary<string, string> FollowerAtmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            public bool UserManuallyDisarmed;
            public bool AutoRearmPending;

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(InstrumentName) ? "(instrument)" : InstrumentName;
            }
        }
    }
}