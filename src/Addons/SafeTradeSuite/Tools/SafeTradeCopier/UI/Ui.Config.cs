using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private CopierUiConfig _lastAppliedConfig;
        private double _masterMaxDailyProfit;
        private double _masterMaxDailyLoss;

        private readonly Dictionary<string, bool> _followerUseMasterRisk = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _followerMaxDailyProfit = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _followerMaxDailyLoss = new Dictionary<string, double>(StringComparer.Ordinal);
        
        private void ApplyConfigFromUi()
        {
            if (_engine == null)
                return;

            var config = BuildConfigFromUi();
            if (config == null)
                return;

            if (AreConfigsEqual(_lastAppliedConfig, config))
                return;

            _engine.ApplyConfig(
                masterAccount: config.MasterAccount,
                followerAccounts: config.Followers,
                instrName: config.InstrumentName,
                masterBracket: config.MasterAtm,
                followerQtyOverridesByAccountName: config.FollowerQtyOverrides,
                followerAtmOverridesByAccountName: config.FollowerAtmOverrides,
                masterMaxDailyProfit: config.MasterMaxDailyProfit,
                masterMaxDailyLoss: config.MasterMaxDailyLoss,
                followerUseMasterRiskByAccountName: config.FollowerUseMasterRisk,
                followerMaxDailyProfitByAccountName: config.FollowerMaxDailyProfit,
                followerMaxDailyLossByAccountName: config.FollowerMaxDailyLoss,
                breakEvenMode: _breakEvenMode,
                freeTradeMinProfitPoints: _freeTradeMinProfitPoints,
                freeTradePlusPoints: _freeTradePlusPoints
            );

            _lastAppliedConfig = config;
            SavePersistentUiState();
        }
        
        private CopierUiConfig BuildConfigFromUi()
        {
            var master = GetMasterAccount();
            var instr = NormalizeInstrumentName(
                (_instrumentSelector?.SelectedItem as string) ??
                _instrumentSelector?.Text ??
                "");
            var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text);
            var masterAtm = (_masterBracketBox?.SelectedItem as string) ?? _masterBracketBox?.Text ?? "None";
            
            if (string.IsNullOrWhiteSpace(masterAtm))
                masterAtm = "None";

            masterAtm = NormalizeAtm(masterAtm);

            if (_simOnlyMode && master != null && !IsSimAccount(master))
            {
                var accounts = GetSelectableAccounts();
                var firstSim = accounts.FirstOrDefault(IsSimAccount);
                _masterBox.SelectedItem = firstSim;
                master = firstSim;
            }

            var followers = new List<Account>();
            var qtyOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
            var atmOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
            var useMasterRisk = new Dictionary<string, bool>(StringComparer.Ordinal);
            var followerMaxProfit = new Dictionary<string, double>(StringComparer.Ordinal);
            var followerMaxLoss = new Dictionary<string, double>(StringComparer.Ordinal);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null)
                    continue;

                var enabled = r.EnabledCheck?.IsChecked == true;
                if (!enabled)
                    continue;

                if (_simOnlyMode && !IsSimAccount(r.Account))
                {
                    r.EnabledCheck.IsChecked = false;
                    continue;
                }

                followers.Add(r.Account);

                var qText = (r.QtyOverrideBox?.Text ?? "").Trim();
                if (int.TryParse(qText, out var qv) && qv > 0)
                    qtyOverrides[r.Account.Name] = qv;

                var aText = r.BracketOverrideBox?.SelectedItem as string ?? "Inherit Master";
                aText = NormalizeAtm(aText);

                if (string.IsNullOrWhiteSpace(aText))
                    aText = "Inherit Master";

                atmOverrides[r.Account.Name] = aText;

                var useMaster = true;
                if (_followerUseMasterRisk.TryGetValue(r.Account.Name, out var storedUseMaster))
                    useMaster = storedUseMaster;

                useMasterRisk[r.Account.Name] = useMaster;

                if (_followerMaxDailyProfit.TryGetValue(r.Account.Name, out var fp))
                    followerMaxProfit[r.Account.Name] = fp;

                if (_followerMaxDailyLoss.TryGetValue(r.Account.Name, out var fl))
                    followerMaxLoss[r.Account.Name] = fl;
            }

            return new CopierUiConfig
            {
                MasterAccount = master,
                InstrumentName = instr,
                MasterQty = masterQty,
                MasterAtm = masterAtm,
                Followers = followers,
                FollowerQtyOverrides = qtyOverrides,
                FollowerAtmOverrides = atmOverrides,
                BreakEvenMode = _breakEvenMode,
                FreeTradeMinProfitPoints = _freeTradeMinProfitPoints,
                FreeTradePlusPoints = _freeTradePlusPoints,
                MasterMaxDailyProfit = _masterMaxDailyProfit,
                MasterMaxDailyLoss = _masterMaxDailyLoss,
                FollowerUseMasterRisk = useMasterRisk,
                FollowerMaxDailyProfit = followerMaxProfit,
                FollowerMaxDailyLoss = followerMaxLoss
            };
        }
        
        private static bool AreConfigsEqual(CopierUiConfig a, CopierUiConfig b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null)
                return false;

            if (!ReferenceEquals(a.MasterAccount, b.MasterAccount))
                return false;

            if (!string.Equals(a.InstrumentName ?? "", b.InstrumentName ?? "", StringComparison.Ordinal))
                return false;

            if (a.MasterQty != b.MasterQty)
                return false;

            if (!string.Equals(a.MasterAtm ?? "", b.MasterAtm ?? "", StringComparison.Ordinal))
                return false;

            if (a.BreakEvenMode != b.BreakEvenMode)
                return false;

            if (Math.Abs(a.FreeTradeMinProfitPoints - b.FreeTradeMinProfitPoints) > 0.0000001)
                return false;

            if (Math.Abs(a.FreeTradePlusPoints - b.FreeTradePlusPoints) > 0.0000001)
                return false;
            
            if (a.Followers.Count != b.Followers.Count)
                return false;

            for (var i = 0; i < a.Followers.Count; i++)
            {
                if (!ReferenceEquals(a.Followers[i], b.Followers[i]))
                    return false;
            }

            if (a.FollowerQtyOverrides.Count != b.FollowerQtyOverrides.Count)
                return false;

            foreach (var kv in a.FollowerQtyOverrides)
            {
                if (!b.FollowerQtyOverrides.TryGetValue(kv.Key, out var val))
                    return false;

                if (val != kv.Value)
                    return false;
            }

            if (a.FollowerAtmOverrides.Count != b.FollowerAtmOverrides.Count)
                return false;

            foreach (var kv in a.FollowerAtmOverrides)
            {
                if (!b.FollowerAtmOverrides.TryGetValue(kv.Key, out var val))
                    return false;

                if (!string.Equals(val ?? "", kv.Value ?? "", StringComparison.Ordinal))
                    return false;
            }
            
            // Risk
            if (Math.Abs(a.MasterMaxDailyProfit - b.MasterMaxDailyProfit) > 0.0000001)
                return false;

            if (Math.Abs(a.MasterMaxDailyLoss - b.MasterMaxDailyLoss) > 0.0000001)
                return false;

            if (a.FollowerUseMasterRisk.Count != b.FollowerUseMasterRisk.Count)
                return false;

            foreach (var kv in a.FollowerUseMasterRisk)
            {
                if (!b.FollowerUseMasterRisk.TryGetValue(kv.Key, out var val))
                    return false;

                if (val != kv.Value)
                    return false;
            }

            if (a.FollowerMaxDailyProfit.Count != b.FollowerMaxDailyProfit.Count)
                return false;

            foreach (var kv in a.FollowerMaxDailyProfit)
            {
                if (!b.FollowerMaxDailyProfit.TryGetValue(kv.Key, out var val))
                    return false;

                if (Math.Abs(val - kv.Value) > 0.0000001)
                    return false;
            }

            if (a.FollowerMaxDailyLoss.Count != b.FollowerMaxDailyLoss.Count)
                return false;

            foreach (var kv in a.FollowerMaxDailyLoss)
            {
                if (!b.FollowerMaxDailyLoss.TryGetValue(kv.Key, out var val))
                    return false;

                if (Math.Abs(val - kv.Value) > 0.0000001)
                    return false;
            }

            return true;
        }
    }
}