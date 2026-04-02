using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        public sealed class CopierRiskEvaluator
        {
            public RiskEvaluationResult Evaluate(
                CopierSettings settings,
                IReadOnlyList<AccountDailyRuntime> accountsRuntime)
            {
                // global first
                // then per-account
                return new RiskEvaluationResult();
            }
            
            public sealed class RiskEvaluationResult
            {
                public bool GlobalLockTriggered { get; set; }
                public string GlobalLockReason { get; set; } = "";
                public List<AccountRiskAction> AccountActions { get; set; }
            }

            public sealed class AccountRiskAction
            {
                public string AccountName { get; set; } = "";
                public bool LockAccount { get; set; }
                public bool FlattenAccount { get; set; }
                public string Reason { get; set; } = "";
            }
        }
    }
}