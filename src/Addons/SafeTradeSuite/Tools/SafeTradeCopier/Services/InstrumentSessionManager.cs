using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private readonly List<InstrumentSession> _instrumentSessions = new List<InstrumentSession>();
        private InstrumentSession _activeInstrumentSession;
        private bool _suppressSessionUiEvents;

        private string GetSelectedInstrumentName()
        {
            if (_instrumentSelector == null)
                return "";

            var text = NormalizeInstrumentName(_instrumentSelector.Text);
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            return NormalizeInstrumentName(_instrumentSelector.SelectedItem as string);
        }

        private static string NormalizeInstrumentName(string instrumentName)
        {
            return (instrumentName ?? "").Trim().ToUpperInvariant();
        }

        private static bool IsValidInstrumentName(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return false;

            try
            {
                return Instrument.GetInstrument(n) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void RememberInstrument(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (!IsValidInstrumentName(n))
                return;

            SafeTradeSuiteRuntime.RememberInstrument(n);
        }

        private static void ForgetInstrument(string instrumentName)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return;

            SafeTradeSuiteRuntime.ForgetInstrument(n);
        }

        private bool IsInstrumentUsedByAnySession(string instrumentName, InstrumentSession excludeSession = null)
        {
            var n = NormalizeInstrumentName(instrumentName);
            if (string.IsNullOrWhiteSpace(n))
                return false;

            return _instrumentSessions.Any(x =>
                !ReferenceEquals(x, excludeSession) &&
                string.Equals(NormalizeInstrumentName(x?.InstrumentName), n, System.StringComparison.OrdinalIgnoreCase));
        }

        private Instrument GetInstrument()
        {
            var instrName = GetSelectedInstrumentName();
            if (string.IsNullOrWhiteSpace(instrName))
                return null;

            return Instrument.GetInstrument(instrName);
        }

        private string GetInstrumentFullName()
        {
            var instr = GetInstrument();
            return instr?.FullName ?? "";
        }

        private List<string> GetAvailableInstruments()
        {
            var ordered = new List<string>();

            void AddIfValid(string value)
            {
                var n = NormalizeInstrumentName(value);
                if (!IsValidInstrumentName(n))
                    return;

                if (ordered.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                    return;

                ordered.Add(n);
            }

            foreach (var n in SafeTradeSuiteRuntime.GetSavedInstrumentsSnapshot())
                AddIfValid(n);

            foreach (var s in _instrumentSessions)
                AddIfValid(s?.InstrumentName);

            AddIfValid(GetSelectedInstrumentName());

            return ordered;
        }
        
        private void NormalizeInstrumentSessions()
        {
            var activeName = NormalizeInstrumentName(_activeInstrumentSession?.InstrumentName);

            var deduped = _instrumentSessions
                .Where(x => x != null)
                .GroupBy(x => NormalizeInstrumentName(x.InstrumentName), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            _instrumentSessions.Clear();
            _instrumentSessions.AddRange(deduped);

            if (_instrumentSessions.Count == 0)
            {
                _activeInstrumentSession = null;
                return;
            }

            if (!string.IsNullOrWhiteSpace(activeName))
            {
                _activeInstrumentSession = _instrumentSessions.FirstOrDefault(x =>
                   string.Equals(
                       NormalizeInstrumentName(x?.InstrumentName),
                       activeName,
                       StringComparison.OrdinalIgnoreCase))
               ?? _instrumentSessions[0];
            }
            else
            {
                _activeInstrumentSession = _instrumentSessions[0];
            }
        }

        private void EnsureInitialInstrumentSession()
        {
            var available = GetAvailableInstruments();

            if (_instrumentSessions.Count == 0)
            {
                foreach (var instrument in available)
                {
                    var normalized = NormalizeInstrumentName(instrument);
                    if (!IsValidInstrumentName(normalized))
                        continue;

                    if (_instrumentSessions.Any(x =>
                            string.Equals(
                                NormalizeInstrumentName(x?.InstrumentName),
                                normalized,
                                StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _instrumentSessions.Add(new InstrumentSession
                    {
                        InstrumentName = normalized
                    });
                }

                if (_instrumentSessions.Count == 0)
                {
                    _instrumentSessions.Add(new InstrumentSession
                    {
                        InstrumentName = ""
                    });

                    LogStatus("No saved instruments yet. Type an instrument such as NQ 03-26.");
                }
            }

            if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                _activeInstrumentSession = _instrumentSessions[0];

            _engine?.RehydrateActiveBracketsFromLiveOrders();
        }
        
        private void RefreshInstrumentSelectorItems()
        {
            if (_instrumentSelector == null)
                return;

            var selected = NormalizeInstrumentName(
                _activeInstrumentSession?.InstrumentName ?? GetSelectedInstrumentName());

            var items = GetAvailableInstruments();

            _suppressSessionUiEvents = true;
            try
            {
                _instrumentSelector.ItemsSource = null;
                _instrumentSelector.Items.Clear();

                foreach (var item in items)
                    _instrumentSelector.Items.Add(item);

                if (IsValidInstrumentName(selected))
                {
                    if (!_instrumentSelector.Items.Contains(selected))
                        _instrumentSelector.Items.Add(selected);

                    _instrumentSelector.SelectedItem = selected;
                    _instrumentSelector.Text = selected;
                }
                else if (_instrumentSelector.Items.Count > 0)
                {
                    _instrumentSelector.SelectedIndex = 0;
                    _instrumentSelector.Text = _instrumentSelector.SelectedItem as string ?? "";
                }
                else
                {
                    _instrumentSelector.SelectedItem = null;
                    _instrumentSelector.Text = "";
                }
            }
            finally
            {
                _suppressSessionUiEvents = false;
            }
        }
        
        private bool SessionHasEnabledFollowers(InstrumentSession session)
        {
            if (session?.FollowersEnabled == null)
                return false;

            return session.FollowersEnabled.Any(x => x.Value);
        }

        private bool SessionHasOpenPosition(InstrumentSession session)
        {
            if (session == null)
                return false;

            var instrName = NormalizeInstrumentName(session.InstrumentName);
            if (string.IsNullOrWhiteSpace(instrName))
                return false;

            var instr = Instrument.GetInstrument(instrName);
            if (instr == null)
                return false;

            if (session.MasterAccount != null && HasOpenInstrumentPosition(session.MasterAccount, instr))
                return true;

            if (session.FollowersEnabled == null)
                return false;

            foreach (var kvp in session.FollowersEnabled)
            {
                var account = Account.All.FirstOrDefault(a =>
                    a != null &&
                    string.Equals(a.Name, kvp.Key, StringComparison.Ordinal));

                if (account != null && HasOpenInstrumentPosition(account, instr))
                    return true;
            }

            return false;
        }

        private bool SessionIsLiveArmed(InstrumentSession session)
        {
            return ReferenceEquals(session, _activeInstrumentSession)
                   && _engine?.IsRequested == true
                   && _engine.Armed;
        }
        
        private InstrumentTabDotState GetInstrumentTabDotState(InstrumentSession session)
        {
            if (session == null)
                return InstrumentTabDotState.None;

            if (ReferenceEquals(session, _activeInstrumentSession))
                return InstrumentTabDotState.None;

            var hasOpenPosition = SessionHasOpenPosition(session);
            var isLiveArmed = SessionIsLiveArmed(session);
            var hasEnabledFollowers = SessionHasEnabledFollowers(session);

            if (hasOpenPosition || isLiveArmed)
                return InstrumentTabDotState.Green;

            if (hasEnabledFollowers)
                return InstrumentTabDotState.Yellow;

            return InstrumentTabDotState.None;
        }

        private void ShowFriendlyError(string title, string message)
        {
            try
            {
                if (_window != null)
                    MessageBox.Show(_window, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
                LogStatus($"{title}: {message}");
            }
        }

        private void LogStatus(string message)
        {
            _engine?.Log(message);
        }
    }
}