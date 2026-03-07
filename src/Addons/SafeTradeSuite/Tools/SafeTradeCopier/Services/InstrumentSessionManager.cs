using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
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

            AddSessionInstruments(names);
            AddPositionInstruments(names);
            AddCurrentSelectedInstrument(names);
            AddChartWindowInstruments(names);

            var result = names
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x)
                .ToList();

            _engine?.Log("Available instruments: " + string.Join(", ", result));

            return result;
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
        
        private void AddSessionInstruments(HashSet<string> names)
        {
            foreach (var s in _instrumentSessions)
            {
                var n = (s?.InstrumentName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(n))
                    names.Add(n);
            }
        }

        private void AddPositionInstruments(HashSet<string> names)
        {
            foreach (var acc in Account.All)
            {
                if (acc?.Positions == null)
                    continue;

                foreach (var p in acc.Positions)
                {
                    var n = p?.Instrument?.FullName ?? "";
                    if (!string.IsNullOrWhiteSpace(n))
                        names.Add(n);
                }
            }
        }

        private void AddCurrentSelectedInstrument(HashSet<string> names)
        {
            var current = (_instrumentSelector?.SelectedItem as string ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(current))
                names.Add(current);

            var active = (_activeInstrumentSession?.InstrumentName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(active))
                names.Add(active);
        }
        
        private void AddChartWindowInstruments(HashSet<string> names)
        {
            if (names == null)
                return;

            var dispatcher = _uiDispatcher ?? _window?.Dispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            try
            {
                if (dispatcher.CheckAccess())
                {
                    AddChartWindowInstrumentsOnUiThread(names);
                }
                else
                {
                    dispatcher.Invoke(() => AddChartWindowInstrumentsOnUiThread(names));
                }
            }
            catch (Exception ex)
            {
                _engine?.Log("AddChartWindowInstruments failed: " + ex.Message);
            }
        }
        
        private void AddChartWindowInstrumentsOnUiThread(HashSet<string> names)
        {
            var windows = Application.Current?.Windows;
            if (windows == null)
                return;

            foreach (Window win in windows)
            {
                if (win == null)
                    continue;

                TryAddChartInstrumentsFromVisual(win, names);
            }
        }
        
        private void TryAddChartInstrumentsFromVisual(DependencyObject root, HashSet<string> names)
        {
            if (root == null || names == null)
                return;

            try
            {
                var prop = root.GetType().GetProperty("Instrument");
                if (prop != null)
                {
                    var value = prop.GetValue(root, null);
                    if (value is Instrument instr && !string.IsNullOrWhiteSpace(instr.FullName))
                        names.Add(instr.FullName);
                }
            }
            catch
            {
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                TryAddChartInstrumentsFromVisual(child, names);
            }
        }
    }
}