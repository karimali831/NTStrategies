using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        [Serializable]
        public sealed class SafeTradeCopierUiState
        {
            public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();
            public BreakEvenSettings BreakEven { get; set; } = new BreakEvenSettings();
            public RiskSettings Risk { get; set; } = new RiskSettings();
            public FollowerShieldSettings FollowerShield { get; set; } = new FollowerShieldSettings();

            public string ActiveInstrumentName { get; set; }
            public string ActiveMainMenuTab { get; set; }

            public List<InstrumentSessionState> InstrumentSessions { get; set; } =
                new List<InstrumentSessionState>();
        }

        [Serializable]
        public sealed class AppearanceSettings
        {
            public bool SimOnlyMode { get; set; } = true;
            public bool ShowStatusBox { get; set; } = true;
            public int ThemeMode { get; set; }
        }

        [Serializable]
        public sealed class BreakEvenSettings
        {
            public int BreakEvenMode { get; set; }
            public double FreeTradeMinProfitPoints { get; set; }
            public double FreeTradePlusPoints { get; set; }
        }

        [Serializable]
        public sealed class RiskSettings
        {
            public double MasterMaxDailyProfit { get; set; }
            public double MasterMaxDailyLoss { get; set; }

            public int AutoFlattenOnOrderReject { get; set; }
            public int AutoFlattenMissingBracket { get; set; }

            public Dictionary<string, bool> FollowerUseMasterRisk { get; set; } =
                new Dictionary<string, bool>(StringComparer.Ordinal);

            public Dictionary<string, double> FollowerMaxDailyProfit { get; set; } =
                new Dictionary<string, double>(StringComparer.Ordinal);

            public Dictionary<string, double> FollowerMaxDailyLoss { get; set; } =
                new Dictionary<string, double>(StringComparer.Ordinal);
        }

        [Serializable]
        public sealed class FollowerShieldSettings
        {
            public bool Enabled { get; set; } = true;
            public int EntryFillTimeoutSeconds { get; set; } = 5;
            public int DesyncGraceSeconds { get; set; } = 3;

            public int OnEntryReject { get; set; } = (int)GuardAction.FlattenAndDisable;
            public int OnEntryTimeout { get; set; } = (int)GuardAction.FlattenAndDisable;
            public int OnDesync { get; set; } = (int)GuardAction.FlattenAndDisable;
        }

        public sealed class InstrumentSessionState
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
}