using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private RelayToolUiState _persistedState = new RelayToolUiState();

        private void EnsurePersistedStateDefaults()
        {
            if (_persistedState == null)
                _persistedState = new RelayToolUiState();

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
            {
                _persistedState.Risk.FollowerUseMasterRisk =
                    new Dictionary<string, bool>(StringComparer.Ordinal);
            }

            if (_persistedState.Risk.FollowerMaxDailyProfit == null)
            {
                _persistedState.Risk.FollowerMaxDailyProfit =
                    new Dictionary<string, double>(StringComparer.Ordinal);
            }

            if (_persistedState.Risk.FollowerMaxDailyLoss == null)
            {
                _persistedState.Risk.FollowerMaxDailyLoss =
                    new Dictionary<string, double>(StringComparer.Ordinal);
            }

            if (_persistedState.TradeHistory == null)
                _persistedState.TradeHistory = new List<TradeHistoryItemState>();
        }

        private void SavePersistentUiState()
        {
            try
            {
                EnsurePersistedStateDefaults();

                // Persist user preference only. Do not persist effective enforced mode.
                _persistedState.Appearance.SimOnlyMode = _userSimOnlyMode;
                _persistedState.Appearance.ShowStatusBox = _showStatusBox;
                _persistedState.Appearance.ThemeMode = _themeMode;

                _persistedState.BreakEven.Mode = _breakEvenMode;
                _persistedState.BreakEven.MinProfitPoints = _freeTradeMinProfitPoints;
                _persistedState.BreakEven.PlusPoints = _freeTradePlusPoints;

                SaveRiskSettingsToState();
                SaveFollowerGuardSettingsToState();
                SaveTradeHistoryToState();

                _persistedState.ActiveInstrumentName = NormalizeInstrumentName(_activeInstrumentSession?.InstrumentName);
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

                LogInstrumentSessions("SavePersistentUiState.beforeSave");
                LogSavedInstrumentOrder("SavePersistentUiState.beforeSave");

                NinjexRuntime.SaveRelayUiState(_persistedState);
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
                _persistedState = NinjexRuntime.LoadRelayUiState<RelayToolUiState>()
                                  ?? new RelayToolUiState();

                EnsurePersistedStateDefaults();

                // Load preference only. Effective mode is recomputed from license + preference.
                _userSimOnlyMode = _persistedState.Appearance.SimOnlyMode;
                _showStatusBox = _persistedState.Appearance.ShowStatusBox;
                _themeMode = _persistedState.Appearance.ThemeMode;

                _breakEvenMode = _persistedState.BreakEven.Mode;
                _freeTradeMinProfitPoints = _persistedState.BreakEven.MinProfitPoints;
                _freeTradePlusPoints = _persistedState.BreakEven.PlusPoints;

                LoadRiskSettingsFromState();
                LoadFollowerGuardSettingsFromState();
                LoadTradeHistoryFromState();

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
                    Enum.TryParse(_persistedState.ActiveMainMenuTab, true, out MainMenuTab parsedTab))
                {
                    _activeMainMenuTab = parsedTab;
                }
                else
                {
                    _activeMainMenuTab = MainMenuTab.Copier;
                }

                RecomputeEffectiveSimMode(rebuildUiBindings: false);

                LogInstrumentSessions("LoadPersistentUiState.end");
                LogSavedInstrumentOrder("LoadPersistentUiState.end");
            }
            catch (Exception ex)
            {
                LogUnhandled("LoadPersistentUiState()", ex);
            }
        }
    }
}