using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        [Serializable]
        public sealed class RelayToolUiState
        {
            public AppearanceSettings Appearance { get; set; } = new AppearanceSettings();
            public BreakEvenSettings BreakEven { get; set; } = new BreakEvenSettings();
            public RiskSettings Risk { get; set; } = new RiskSettings();
            public FollowerShieldSettings FollowerShield { get; set; } = new FollowerShieldSettings();

            public string ActiveInstrumentName { get; set; }
            public string ActiveMainMenuTab { get; set; }

            public List<InstrumentSessionState> InstrumentSessions { get; set; } =
                new List<InstrumentSessionState>();
            public List<TradeHistoryItemState> TradeHistory { get; set; } =
                new List<TradeHistoryItemState>();
        }

        [Serializable]
        public sealed class AppearanceSettings
        {
            public bool SimOnlyMode { get; set; } = true;
            public bool ShowStatusBox { get; set; } = true;
            public ThemeMode ThemeMode { get; set; }
        }

        [Serializable]
        public sealed class BreakEvenSettings
        {
            public BreakEvenMode Mode { get; set; }
            public double MinProfitPoints { get; set; }
            public double PlusPoints { get; set; }
        }

        [Serializable]
        public sealed class RiskSettings
        {
            public double MasterMaxDailyProfit { get; set; }
            public double MasterMaxDailyLoss { get; set; }

            public AutoFlattenProtectionScope AutoFlattenOnOrderReject { get; set; }
            public AutoFlattenProtectionScope AutoFlattenMissingBracket { get; set; }

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

            public GuardAction OnEntryReject { get; set; } = GuardAction.FlattenAndDisable;
            public GuardAction OnEntryTimeout { get; set; } = GuardAction.FlattenAndDisable;
            public GuardAction OnDesync { get; set; } = GuardAction.FlattenAndDisable;
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
        
        [Serializable]
        public sealed class TradeHistoryItemState
        {
            public int TradeNumber { get; set; }
            public string InstrumentName { get; set; }
            public string MarketPosition { get; set; }
            public int OrderQty { get; set; }
            public string AccountName { get; set; }
            public DateTime EntryTimeUtc { get; set; }
            public DateTime? ExitTimeUtc { get; set; }
            public double EntryPrice { get; set; }
            public double ExitPrice { get; set; }
            public double RealizedPnL { get; set; }
            public string BracketUsed { get; set; }

            public bool IsMaster { get; set; }
            public string Outcome { get; set; }
            public bool BreakEvenApplied { get; set; }
            public BreakEvenTriggerKind BreakEvenKind { get; set; }
            public FlattenTriggerReason PendingFlattenReason { get; set; }
            public string PendingFlattenDetail { get; set; }
            public bool WasFlattenedManually { get; set; }

            public string EntryOrderName { get; set; }
            public string ExitOrderName { get; set; }
        }
    }
}