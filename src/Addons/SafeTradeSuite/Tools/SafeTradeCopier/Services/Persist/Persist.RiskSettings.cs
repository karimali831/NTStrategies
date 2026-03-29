using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void LoadRiskSettingsFromState()
        {
            var risk = _persistedState?.Risk ?? new RiskSettings();

            _masterMaxDailyProfit = Math.Max(0, risk.MasterMaxDailyProfit);
            _masterMaxDailyLoss = Math.Max(0, risk.MasterMaxDailyLoss);

            _autoFlattenOnOrderReject =
                Enum.IsDefined(typeof(AutoFlattenProtectionScope), risk.AutoFlattenOnOrderReject)
                    ? risk.AutoFlattenOnOrderReject
                    : AutoFlattenProtectionScope.Disabled;

            _autoFlattenMissingBracket =
                Enum.IsDefined(typeof(AutoFlattenProtectionScope), risk.AutoFlattenMissingBracket)
                    ? risk.AutoFlattenMissingBracket
                    : AutoFlattenProtectionScope.Disabled;

            _followerUseMasterRisk.Clear();
            if (risk.FollowerUseMasterRisk != null)
            {
                foreach (var kv in risk.FollowerUseMasterRisk)
                    _followerUseMasterRisk[kv.Key] = kv.Value;
            }

            _followerMaxDailyProfit.Clear();
            if (risk.FollowerMaxDailyProfit != null)
            {
                foreach (var kv in risk.FollowerMaxDailyProfit)
                    _followerMaxDailyProfit[kv.Key] = kv.Value;
            }

            _followerMaxDailyLoss.Clear();
            if (risk.FollowerMaxDailyLoss != null)
            {
                foreach (var kv in risk.FollowerMaxDailyLoss)
                    _followerMaxDailyLoss[kv.Key] = kv.Value;
            }
        }
        
        private void SaveRiskSettingsToState()
        {
            EnsurePersistedStateDefaults();
            
            _persistedState.Risk.MasterMaxDailyProfit = _masterMaxDailyProfit;
            _persistedState.Risk.MasterMaxDailyLoss = _masterMaxDailyLoss;
            _persistedState.Risk.AutoFlattenOnOrderReject = _autoFlattenOnOrderReject;
            _persistedState.Risk.AutoFlattenMissingBracket = _autoFlattenMissingBracket;

            _persistedState.Risk.FollowerUseMasterRisk =
                new Dictionary<string, bool>(_followerUseMasterRisk, StringComparer.Ordinal);

            _persistedState.Risk.FollowerMaxDailyProfit =
                new Dictionary<string, double>(_followerMaxDailyProfit, StringComparer.Ordinal);

            _persistedState.Risk.FollowerMaxDailyLoss =
                new Dictionary<string, double>(_followerMaxDailyLoss, StringComparer.Ordinal);
        }
    }
}