using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public sealed class GeneralSettings
        {
            public bool SimulationOnly { get; set; } = true;
            public RelayExitMode ExitMode { get; set; } = RelayExitMode.IndependentOnly;
        }

        public sealed class CopierSettings
        {
            public GeneralSettings General { get; set; }
            public GlobalRiskSettings GlobalRisk { get; set; }
            public List<AccountRiskSettings> Accounts { get; set; }
        }
        
        public sealed class GlobalRiskSettings
        {
            public bool EnableMaxDailyLoss { get; set; }
            public double MaxDailyLoss { get; set; }

            public bool EnableMaxDailyProfit { get; set; }
            public double MaxDailyProfit { get; set; }

            public bool IsLocked { get; set; }
            public string LockReason { get; set; } = "";
        }
        
        public sealed class AccountRiskSettings
        {
            public string AccountName { get; set; } = "";

            public bool EnableMaxDailyLoss { get; set; }
            public double MaxDailyLoss { get; set; }

            public bool EnableMaxDailyProfit { get; set; }
            public double MaxDailyProfit { get; set; }

            public bool EnableMaxTrades { get; set; }
            public int MaxTrades { get; set; }

            public bool IsLocked { get; set; }
            public string LockReason { get; set; } = "";
        }
    }
}