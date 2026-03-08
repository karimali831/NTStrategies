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

            var firstInstrument =
                GetAvailableInstruments().FirstOrDefault()
                ?? GetSelectedInstrumentName()
                ?? "";

            var session = new InstrumentSession
            {
                InstrumentName = firstInstrument
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;
        }

        private List<string> GetAvailableInstruments()
        {
            var instruments = AddOns.GetChartInstruments();

            SafeTradeSuiteRuntime.PrintLog(
                instruments.Count == 0
                    ? $"Copier[{_toolId}] Available instruments: <none found>"
                    : $"Copier[{_toolId}] Available instruments: {string.Join(", ", instruments)}");

            return instruments;
        }

        private void RefreshInstrumentSelectorItems()
        {
            if (_instrumentSelector == null)
                return;

            var selected = _instrumentSelector.SelectedItem as string;
            var items = GetAvailableInstruments();

            _instrumentSelector.ItemsSource = null;
            _instrumentSelector.Items.Clear();

            foreach (var item in items)
                _instrumentSelector.Items.Add(item);

            if (!string.IsNullOrWhiteSpace(selected) && _instrumentSelector.Items.Contains(selected))
                _instrumentSelector.SelectedItem = selected;
            else if (_instrumentSelector.Items.Count > 0)
                _instrumentSelector.SelectedIndex = 0;
        }

        private void AddInstrumentSession(string instrumentName)
        {
            instrumentName = (instrumentName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(instrumentName))
                return;

            var existing = _instrumentSessions.FirstOrDefault(x =>
                string.Equals(x.InstrumentName, instrumentName, System.StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                _activeInstrumentSession = existing;
                RefreshInstrumentTabs();
                LoadActiveSessionToUi();
                return;
            }

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

        private void LogStatus(string message)
        {
            _engine?.Log(message);
        }
    }
}