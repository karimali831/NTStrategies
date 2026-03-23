using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    internal sealed class SafeTradeCopierUiState
    {
        public bool SimOnlyMode { get; set; }
        public bool ShowStatusBox { get; set; }
        public int ThemeMode { get; set; }

        public int BreakEvenMode { get; set; }
        public double FreeTradeMinProfitPoints { get; set; }
        public double FreeTradePlusPoints { get; set; }

        public string ActiveInstrumentName { get; set; }
        public string ActiveMainMenuTab { get; set; }

        public bool FollowerGuardEnabled { get; set; } = true;
        public int FollowerGuardEntryFillTimeoutSeconds { get; set; }
        public int FollowerGuardDesyncGraceSeconds { get; set; }
        public int FollowerGuardOnEntryReject { get; set; }
        public int FollowerGuardOnEntryTimeout { get; set; }
        public int FollowerGuardOnDesync { get; set; }

        public List<InstrumentSessionState> InstrumentSessions { get; set; } =
            new List<InstrumentSessionState>();
    }

    internal sealed class InstrumentSessionState
    {
        public string InstrumentName { get; set; }
        public string MasterAccountName { get; set; }
        public int MasterQty { get; set; }
        public string MasterAtm { get; set; }

        public Dictionary<string, bool> FollowersEnabled { get; set; } =
            new Dictionary<string, bool>();

        public Dictionary<string, int> FollowerQtyOverrides { get; set; } =
            new Dictionary<string, int>();

        public Dictionary<string, string> FollowerAtmOverrides { get; set; } =
            new Dictionary<string, string>();
    }
}