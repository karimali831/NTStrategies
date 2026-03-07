using System;
using System.Collections.Generic;
using System.Linq;
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
            return (_instrumentSelector?.SelectedItem as string ?? "").Trim();
        }

        private Instrument GetInstrument()
        {
            var instrName = GetSelectedInstrumentName();
            return string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);
        }
        
        private string GetInstrumentFullName()
        {
            var instr = GetInstrument();
            return instr?.FullName ?? "";
        }
        
        private void EnsureInitialInstrumentSession()
        {
            if (_instrumentSessions.Count > 0)
                return;

            var firstInstrument = GetAvailableInstruments().FirstOrDefault() ?? "";

            var session = new InstrumentSession
            {
                InstrumentName = firstInstrument
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;
        }

        private List<string> GetAvailableInstruments()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in _instrumentSessions)
            {
                var n = (s?.InstrumentName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(n))
                    names.Add(n);
            }

            foreach (var acc in Account.All)
            {
                if (acc?.Positions == null) continue;

                foreach (var p in acc.Positions)
                {
                    var n = p?.Instrument?.FullName ?? "";
                    if (!string.IsNullOrWhiteSpace(n))
                        names.Add(n);
                }
            }

            var current = (_instrumentSelector?.SelectedItem as string ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(current))
                names.Add(current);

            return names.OrderBy(x => x).ToList();
        }

        private void RefreshInstrumentSelectorItems()
        {
            if (_instrumentSelector == null) return;

            var selected = _instrumentSelector.SelectedItem as string;
            var items = GetAvailableInstruments();

            _instrumentSelector.ItemsSource = null;
            _instrumentSelector.Items.Clear();

            foreach (var item in items)
                _instrumentSelector.Items.Add(item);

            if (!string.IsNullOrWhiteSpace(selected) && _instrumentSelector.Items.Contains(selected))
                _instrumentSelector.SelectedItem = selected;
        }

        private void AddInstrumentSession(string instrumentName)
        {
            SaveUiToActiveSession();

            var session = new InstrumentSession
            {
                InstrumentName = instrumentName,
                MasterAccount = _masterBox?.SelectedItem as Account,
                MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1),
                MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None"
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;

            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
        }

        private void SwitchToSession(InstrumentSession session)
        {
            if (session == null) return;
            if (ReferenceEquals(_activeInstrumentSession, session)) return;

            SaveUiToActiveSession();
            _activeInstrumentSession = session;
            LoadActiveSessionToUi();
        }
    }
}