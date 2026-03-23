using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private SafeTradeCopierUiState _persistedState = new SafeTradeCopierUiState();
        
        private void SavePersistentUiState()
        {
            try
            {
                _persistedState.Appearance.SimOnlyMode = _simOnlyMode;
                _persistedState.Appearance.ShowStatusBox = _showStatusBox;
                _persistedState.Appearance.ThemeMode = (int)_themeMode;

                _persistedState.BreakEven.BreakEvenMode = (int)_breakEvenMode;
                _persistedState.BreakEven.FreeTradeMinProfitPoints = _freeTradeMinProfitPoints;
                _persistedState.BreakEven.FreeTradePlusPoints = _freeTradePlusPoints;

                _persistedState.FollowerShield.Enabled = _followerGuardEnabled;
                _persistedState.FollowerShield.EntryFillTimeoutSeconds = _followerGuardEntryFillTimeoutSeconds;
                _persistedState.FollowerShield.DesyncGraceSeconds = _followerGuardDesyncGraceSeconds;
                _persistedState.FollowerShield.OnEntryReject = (int)_followerGuardOnEntryReject;
                _persistedState.FollowerShield.OnEntryTimeout = (int)_followerGuardOnEntryTimeout;
                _persistedState.FollowerShield.OnDesync = (int)_followerGuardOnDesync;

                _persistedState.Risk.MasterMaxDailyProfit = _masterMaxDailyProfit;
                _persistedState.Risk.MasterMaxDailyLoss = _masterMaxDailyLoss;
                _persistedState.Risk.AutoFlattenOnOrderReject = (int)_autoFlattenOnOrderReject;
                _persistedState.Risk.AutoFlattenMissingBracket = (int)_autoFlattenMissingBracket;

                _persistedState.Risk.FollowerUseMasterRisk =
                    new Dictionary<string, bool>(_followerUseMasterRisk, StringComparer.Ordinal);

                _persistedState.Risk.FollowerMaxDailyProfit =
                    new Dictionary<string, double>(_followerMaxDailyProfit, StringComparer.Ordinal);

                _persistedState.Risk.FollowerMaxDailyLoss =
                    new Dictionary<string, double>(_followerMaxDailyLoss, StringComparer.Ordinal);

                _persistedState.ActiveInstrumentName =
                    NormalizeInstrumentName(_activeInstrumentSession?.InstrumentName);

                _persistedState.ActiveMainMenuTab = _activeMainMenuTab.ToString();

                _persistedState.InstrumentSessions.Clear();
                foreach (var session in _instrumentSessions)
                {
                    if (session == null)
                        continue;

                    _persistedState.InstrumentSessions.Add(new InstrumentSessionState
                    {
                        InstrumentName = NormalizeInstrumentName(session.InstrumentName),
                        MasterAccountName = session.MasterAccount?.Name,
                        MasterQty = session.MasterQty,
                        MasterAtm = session.MasterAtm,
                        FollowersEnabled = new Dictionary<string, bool>(session.FollowersEnabled),
                        FollowerQtyOverrides = new Dictionary<string, int>(session.FollowerQtyOverrides),
                        FollowerAtmOverrides = new Dictionary<string, string>(session.FollowerAtmOverrides)
                    });
                }

                SafeTradeSuiteRuntime.SaveCopierUiState(_persistedState);
            }
            catch (Exception ex)
            {
                LogUnhandled("SavePersistentUiState()", ex);
            }
        }
        
        private void LoadPersistentUiState()
        {
            try
            {
                _persistedState =
                    SafeTradeSuiteRuntime.LoadCopierUiState<SafeTradeCopierUiState>()
                    ?? new SafeTradeCopierUiState();

                if (_persistedState.Appearance == null)
                    _persistedState.Appearance = new AppearanceSettings();

                if (_persistedState.BreakEven == null)
                    _persistedState.BreakEven = new BreakEvenSettings();

                if (_persistedState.Risk == null)
                    _persistedState.Risk = new RiskSettings();

                if (_persistedState.FollowerShield == null)
                    _persistedState.FollowerShield = new FollowerShieldSettings();

                if (_persistedState.InstrumentSessions == null)
                    _persistedState.InstrumentSessions = new List<InstrumentSessionState>();

                if (_persistedState.Risk.FollowerUseMasterRisk == null)
                    _persistedState.Risk.FollowerUseMasterRisk =
                        new Dictionary<string, bool>(StringComparer.Ordinal);

                if (_persistedState.Risk.FollowerMaxDailyProfit == null)
                    _persistedState.Risk.FollowerMaxDailyProfit =
                        new Dictionary<string, double>(StringComparer.Ordinal);

                if (_persistedState.Risk.FollowerMaxDailyLoss == null)
                    _persistedState.Risk.FollowerMaxDailyLoss =
                        new Dictionary<string, double>(StringComparer.Ordinal);

                _simOnlyMode = _persistedState.Appearance.SimOnlyMode;
                _showStatusBox = _persistedState.Appearance.ShowStatusBox;
                _themeMode = (ThemeMode)_persistedState.Appearance.ThemeMode;

                _breakEvenMode = (BreakEvenMode)_persistedState.BreakEven.BreakEvenMode;
                _freeTradeMinProfitPoints = _persistedState.BreakEven.FreeTradeMinProfitPoints;
                _freeTradePlusPoints = _persistedState.BreakEven.FreeTradePlusPoints;

                _followerGuardEnabled = _persistedState.FollowerShield.Enabled;
                _followerGuardEntryFillTimeoutSeconds =
                    _persistedState.FollowerShield.EntryFillTimeoutSeconds > 0
                        ? _persistedState.FollowerShield.EntryFillTimeoutSeconds
                        : 5;

                _followerGuardDesyncGraceSeconds =
                    _persistedState.FollowerShield.DesyncGraceSeconds > 0
                        ? _persistedState.FollowerShield.DesyncGraceSeconds
                        : 3;

                _followerGuardOnEntryReject =
                    Enum.IsDefined(typeof(GuardAction), _persistedState.FollowerShield.OnEntryReject)
                        ? (GuardAction)_persistedState.FollowerShield.OnEntryReject
                        : GuardAction.FlattenAndDisable;

                _followerGuardOnEntryTimeout =
                    Enum.IsDefined(typeof(GuardAction), _persistedState.FollowerShield.OnEntryTimeout)
                        ? (GuardAction)_persistedState.FollowerShield.OnEntryTimeout
                        : GuardAction.FlattenAndDisable;

                _followerGuardOnDesync =
                    Enum.IsDefined(typeof(GuardAction), _persistedState.FollowerShield.OnDesync)
                        ? (GuardAction)_persistedState.FollowerShield.OnDesync
                        : GuardAction.FlattenAndDisable;

                _masterMaxDailyProfit = Math.Max(0, _persistedState.Risk.MasterMaxDailyProfit);
                _masterMaxDailyLoss = Math.Max(0, _persistedState.Risk.MasterMaxDailyLoss);

                _autoFlattenOnOrderReject =
                    Enum.IsDefined(typeof(AutoFlattenProtectionScope), _persistedState.Risk.AutoFlattenOnOrderReject)
                        ? (AutoFlattenProtectionScope)_persistedState.Risk.AutoFlattenOnOrderReject
                        : AutoFlattenProtectionScope.Disabled;

                _autoFlattenMissingBracket =
                    Enum.IsDefined(typeof(AutoFlattenProtectionScope), _persistedState.Risk.AutoFlattenMissingBracket)
                        ? (AutoFlattenProtectionScope)_persistedState.Risk.AutoFlattenMissingBracket
                        : AutoFlattenProtectionScope.Disabled;

                _followerUseMasterRisk.Clear();
                foreach (var kv in _persistedState.Risk.FollowerUseMasterRisk)
                    _followerUseMasterRisk[kv.Key] = kv.Value;

                _followerMaxDailyProfit.Clear();
                foreach (var kv in _persistedState.Risk.FollowerMaxDailyProfit)
                    _followerMaxDailyProfit[kv.Key] = kv.Value;

                _followerMaxDailyLoss.Clear();
                foreach (var kv in _persistedState.Risk.FollowerMaxDailyLoss)
                    _followerMaxDailyLoss[kv.Key] = kv.Value;

                _instrumentSessions.Clear();
                var accounts = GetSelectableAccounts();

                foreach (var s in _persistedState.InstrumentSessions)
                {
                    if (s == null)
                        continue;

                    var masterAccount = accounts.FirstOrDefault(a =>
                        a != null &&
                        string.Equals(a.Name, s.MasterAccountName, StringComparison.Ordinal));

                    var session = new InstrumentSession
                    {
                        InstrumentName = NormalizeInstrumentName(s.InstrumentName),
                        MasterAccount = masterAccount,
                        MasterQty = s.MasterQty > 0 ? s.MasterQty : 1,
                        MasterAtm = string.IsNullOrWhiteSpace(s.MasterAtm) ? "None" : s.MasterAtm
                    };

                    foreach (var kv in s.FollowersEnabled)
                        session.FollowersEnabled[kv.Key] = kv.Value;

                    foreach (var kv in s.FollowerQtyOverrides)
                        session.FollowerQtyOverrides[kv.Key] = kv.Value;

                    foreach (var kv in s.FollowerAtmOverrides)
                        session.FollowerAtmOverrides[kv.Key] = kv.Value;

                    _instrumentSessions.Add(session);
                }

                NormalizeInstrumentSessions();

                if (!string.IsNullOrWhiteSpace(_persistedState.ActiveInstrumentName))
                {
                    _activeInstrumentSession = _instrumentSessions.FirstOrDefault(x =>
                        string.Equals(
                            NormalizeInstrumentName(x?.InstrumentName),
                            NormalizeInstrumentName(_persistedState.ActiveInstrumentName),
                            StringComparison.OrdinalIgnoreCase));
                }

                if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                    _activeInstrumentSession = _instrumentSessions[0];

                if (!string.IsNullOrWhiteSpace(_persistedState.ActiveMainMenuTab) &&
                    Enum.TryParse(_persistedState.ActiveMainMenuTab, out MainMenuTab parsedTab))
                {
                    _activeMainMenuTab = parsedTab;
                }
            }
            catch (Exception ex)
            {
                LogUnhandled("LoadPersistentUiState()", ex);
            }
        }
    }
}