using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void SavePersistentUiState()
        {
            try
            {
                var state = new SafeTradeCopierUiState
                {
                    SimOnlyMode = _simOnlyMode,
                    ShowStatusBox = _showStatusBox,
                    ThemeMode = (int)_themeMode,
                    BreakEvenMode = (int)_breakEvenMode,
                    FreeTradeMinProfitPoints = _freeTradeMinProfitPoints,
                    FreeTradePlusPoints = _freeTradePlusPoints,
                    ActiveInstrumentName = NormalizeInstrumentName(_activeInstrumentSession?.InstrumentName),
                    ActiveMainMenuTab = _activeMainMenuTab.ToString()
                };

                foreach (var session in _instrumentSessions)
                {
                    if (session == null)
                        continue;

                    state.InstrumentSessions.Add(new InstrumentSessionState
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

                SafeTradeSuiteRuntime.SaveCopierUiState(state);
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
                var state = SafeTradeSuiteRuntime.LoadCopierUiState<SafeTradeCopierUiState>();
                if (state == null)
                    return;

                _simOnlyMode = state.SimOnlyMode;
                _showStatusBox = state.ShowStatusBox;
                _themeMode = (ThemeMode)state.ThemeMode;
                _breakEvenMode = (BreakEvenMode)state.BreakEvenMode;
                _freeTradeMinProfitPoints = state.FreeTradeMinProfitPoints;
                _freeTradePlusPoints = state.FreeTradePlusPoints;

                _instrumentSessions.Clear();

                var accounts = GetSelectableAccounts();

                foreach (var s in state.InstrumentSessions)
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

                if (!string.IsNullOrWhiteSpace(state.ActiveInstrumentName))
                {
                    _activeInstrumentSession = _instrumentSessions.FirstOrDefault(x =>
                        string.Equals(
                            NormalizeInstrumentName(x?.InstrumentName),
                            NormalizeInstrumentName(state.ActiveInstrumentName),
                            StringComparison.OrdinalIgnoreCase));
                }

                if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                    _activeInstrumentSession = _instrumentSessions[0];

                if (!string.IsNullOrWhiteSpace(state.ActiveMainMenuTab) &&
                    Enum.TryParse(state.ActiveMainMenuTab, out MainMenuTab parsedTab))
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