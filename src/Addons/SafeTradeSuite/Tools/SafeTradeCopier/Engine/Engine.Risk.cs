using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            private AutoFlattenProtectionScope _autoFlattenOnOrderReject = AutoFlattenProtectionScope.Disabled;
            private AutoFlattenProtectionScope _autoFlattenMissingBracket = AutoFlattenProtectionScope.Disabled;
            
            private void TriggerRiskProtectionFlatten(Account acc, Instrument instr, string reason)
            {
                if (acc == null || instr == null)
                    return;

                Log($"[RISK PROTECT] flatten -> acc={acc.Name} instr={instr.FullName} reason={reason}");

                if (_master != null && ReferenceEquals(acc, _master))
                {
                    lock (_gate)
                    {
                        _isRequested = false;
                        DisarmUnsafe_NoLock($"Master protection triggered: {reason}");
                        RaiseModeChanged_NoLock();
                        RaiseReady_NoLock(reasonOverride: reason);
                    }
                }
                else
                {
                    DisableFollower(acc, reason);
                }

                EnsureFlatInstrument(acc, instr, FlattenTriggerReason.RiskProtection, reason);
            }
            
            public void UpdateRiskProtectionSettings(
                AutoFlattenProtectionScope onOrderReject,
                AutoFlattenProtectionScope onMissingBracket)
            {
                lock (_gate)
                {
                    _autoFlattenOnOrderReject = onOrderReject;
                    _autoFlattenMissingBracket = onMissingBracket;
                }

                Log(
                    $"[RISK PROTECT] updated -> " +
                    $"onReject={onOrderReject}, missingBracket={onMissingBracket}");
            }
            
            private double GetEffectiveMaxDailyProfit(Account acc)
            {
                if (acc == null)
                    return 0;

                if (_configuredMaster != null && ReferenceEquals(acc, _configuredMaster))
                    return Math.Max(0, _configuredMasterMaxDailyProfit);

                var useMaster = true;
                if (_configuredFollowerUseMasterRisk != null &&
                    _configuredFollowerUseMasterRisk.TryGetValue(acc.Name, out var stored))
                {
                    useMaster = stored;
                }

                if (useMaster)
                    return Math.Max(0, _configuredMasterMaxDailyProfit);

                if (_configuredFollowerMaxDailyProfit != null &&
                    _configuredFollowerMaxDailyProfit.TryGetValue(acc.Name, out var val))
                {
                    return Math.Max(0, val);
                }

                return 0;
            }

            private double GetEffectiveMaxDailyLoss(Account acc)
            {
                if (acc == null)
                    return 0;

                if (_configuredMaster != null && ReferenceEquals(acc, _configuredMaster))
                    return Math.Max(0, _configuredMasterMaxDailyLoss);

                var useMaster = true;
                if (_configuredFollowerUseMasterRisk != null &&
                    _configuredFollowerUseMasterRisk.TryGetValue(acc.Name, out var stored))
                {
                    useMaster = stored;
                }

                if (useMaster)
                    return Math.Max(0, _configuredMasterMaxDailyLoss);

                if (_configuredFollowerMaxDailyLoss != null &&
                    _configuredFollowerMaxDailyLoss.TryGetValue(acc.Name, out var val))
                {
                    return Math.Max(0, val);
                }

                return 0;
            }
            
            internal bool CanEnterForRisk(Account acc, out string shortReason, out string fullReason)
            {
                shortReason = "";
                fullReason = "";

                if (acc == null)
                {
                    shortReason = "No account";
                    return false;
                }
                
                var maxProfit = GetEffectiveMaxDailyProfit(acc);
                var maxLoss = GetEffectiveMaxDailyLoss(acc);

                if (maxProfit <= 0 && maxLoss <= 0)
                {
                    shortReason = "";
                    return true;
                }

                if (!TryGetRealizedPnl(acc, out var realized))
                {
                    shortReason = "Unable to read realized PnL";
                    return false;
                }

                if (maxProfit > 0 && realized >= maxProfit)
                {
                    shortReason = "⚠ Daily profit lock hit";
                    fullReason = $"{shortReason} {FmtUsd(realized)} >= {FmtUsd(maxProfit)}";
                    return false;
                }
                
                if (maxLoss > 0 && realized <= -maxLoss)
                {
                    shortReason = "⚠ Daily loss lock hit";
                    fullReason = $"{shortReason}: {FmtUsd(realized)} <= -{FmtUsd(maxLoss)}";
                    return false;
                }

                return true;
            }
        }
    }
}