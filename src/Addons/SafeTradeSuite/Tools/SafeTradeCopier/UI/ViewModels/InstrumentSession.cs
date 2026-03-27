using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class InstrumentSession
        {
            public string InstrumentName { get; set; } = "";
            public Account MasterAccount { get; set; }
            public int MasterQty { get; set; } = 1;
            public string MasterAtm { get; set; } = "None";
            public bool IsArmedRequested { get; set; }

            public Dictionary<string, bool> FollowersEnabled { get; } =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            public Dictionary<string, int> FollowerQtyOverrides { get; } =
                new Dictionary<string, int>(StringComparer.Ordinal);

            public Dictionary<string, string> FollowerAtmOverrides { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

            public override string ToString()
            {
                return string.IsNullOrWhiteSpace(InstrumentName) ? "(instrument)" : InstrumentName;
            }
        }
    }
}