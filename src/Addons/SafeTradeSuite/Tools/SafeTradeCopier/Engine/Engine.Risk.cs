using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        public partial class SafeCopierEngine
        {
            // Risk-settings
            private double _masterMaxDailyProfit;
            private double _masterMaxDailyLoss;

            private readonly Dictionary<string, bool> _followerUseMasterRisk = new Dictionary<string, bool>(StringComparer.Ordinal);
            private readonly Dictionary<string, double> _followerMaxDailyProfit = new Dictionary<string, double>(StringComparer.Ordinal);
            private readonly Dictionary<string, double> _followerMaxDailyLoss = new Dictionary<string, double>(StringComparer.Ordinal);
            
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

            private bool TryGetRealizedPnl(Account acc, out double realized)
            {
                realized = 0;

                if (acc == null)
                    return false;

                try
                {
                    realized = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            internal bool CanEnterForRisk(Account acc, out string reason)
            {
                reason = "";

                if (acc == null)
                {
                    reason = "No account";
                    return false;
                }

                if (!TryGetRealizedPnl(acc, out var realized))
                {
                    reason = "Unable to read realized PnL";
                    return false;
                }

                var maxProfit = GetEffectiveMaxDailyProfit(acc);
                if (maxProfit > 0 && realized >= maxProfit)
                {
                    reason = $"Daily profit lock hit ({realized:0.00} >= {maxProfit:0.00})";
                    return false;
                }

                var maxLoss = GetEffectiveMaxDailyLoss(acc);
                if (maxLoss > 0 && realized <= -maxLoss)
                {
                    reason = $"Daily loss lock hit ({realized:0.00} <= -{maxLoss:0.00})";
                    return false;
                }

                return true;
            }
        }
    }
}