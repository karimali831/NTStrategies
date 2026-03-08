using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private readonly string _toolId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static Window _window;
        private static readonly object WindowGate = new object();
        private Dispatcher _uiDispatcher;
        private bool _allowWindowClose;

        private SafeCopierEngine _engine;
        private ComboBox _masterBox;
        private ComboBox _instrumentSelector;
        private StackPanel _followersPanel;
        private List<AccountSnap> _lastAccountsSnapshot = new List<AccountSnap>();

        private DispatcherTimer _instrumentRefreshTimer;
        private List<string> _lastInstrumentSnapshot = new List<string>();

        public void Show()
        {
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] Show()");
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] WindowExists={(_window != null)}");

            lock (WindowGate)
            {
                // If window exists but is no longer usable, drop it and rebuild
                if (_window != null)
                {
                    if (!_window.IsLoaded || _window.Dispatcher.HasShutdownStarted ||
                        _window.Dispatcher.HasShutdownFinished)
                    {
                        CloseInternal(closeWindow: true);
                    }
                }

                if (_window != null)
                {
                    SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] Reusing existing window.");

                    if (!_window.IsVisible)
                        _window.Show();

                    if (_window.WindowState == WindowState.Minimized)
                        _window.WindowState = WindowState.Normal;

                    _window.Activate();
                    return;
                }

                _engine = new SafeCopierEngine();

                try
                {
                    _window = new Window
                    {
                        Title = "Safe Trade Copier (V2.1)",
                        Width = 900,
                        Height = 950,
                        MinWidth = 820,
                        MinHeight = 780,
                        ResizeMode = ResizeMode.CanResize,
                        SizeToContent = SizeToContent.Manual,
                        Background = SystemColors.WindowBrush,
                        Foreground = SystemColors.WindowTextBrush,
                        Content = BuildUi(_engine),
                    };

                    _uiDispatcher = _window.Dispatcher;
                    _window.Closing += OnWindowClosing;
                    _window.Closed += OnWindowClosed;

                    _window.Show();
                    SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] New window shown.");
                    HookConnectionStatusUpdates();

                    _uiDispatcher.InvokeAsync(() =>
                    {
                        EnsureInitialInstrumentSession();
                        StartInstrumentRefreshTimer();

                        RefreshInstrumentSelectorIfNeeded();

                        if (_activeInstrumentSession != null &&
                            string.IsNullOrWhiteSpace(_activeInstrumentSession.InstrumentName))
                        {
                            var first = GetSelectedInstrumentName();
                            if (!string.IsNullOrWhiteSpace(first))
                                _activeInstrumentSession.InstrumentName = first;
                        }

                        RefreshInstrumentTabs();
                        LoadActiveSessionToUi();
                        RenderFlattenAllButtonState();
                    }, DispatcherPriority.Loaded);
                }
                catch (Exception ex)
                {
                    LogUnhandled("SafeTradeCopierTool.Show()", ex);
                    CloseInternal(closeWindow: true);
                    throw;
                }
            }
        }

        public void Dispose()
        {
            // True teardown (used by Close button / tool registry dispose)
            lock (WindowGate)
            {
                CloseInternal(closeWindow: true);
            }
        }

        // If you still want to call HardClose() from UI code, keep it as a wrapper
        private void HardClose()
        {
            lock (WindowGate)
            {
                UnsubscribeUiAccountEvents(Account.All);
                CloseInternal(closeWindow: true);
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowWindowClose) return;

            // X button hides (keeps tool alive)
            e.Cancel = true;
            _window?.Hide();
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            // If it closes for real, ensure full cleanup
            lock (WindowGate)
            {
                CloseInternal(closeWindow: false); // already closed
            }
        }

        private void CloseInternal(bool closeWindow)
        {
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] CloseInternal(closeWindow={closeWindow})");
            UnhookConnectionStatusUpdates();

            if (_engine != null)
            {
                _engine.SetCopyEnabled(false);
                _engine.Dispose();
                _engine = null;
            }

            if (_instrumentRefreshTimer != null)
            {
                _instrumentRefreshTimer.Stop();
                _instrumentRefreshTimer.Tick -= OnInstrumentRefreshTimerTick;
                _instrumentRefreshTimer = null;
            }

            // Close window (optionally)
            if (closeWindow && _window != null)
            {
                var w = _window;

                // detach handlers to avoid re-entrancy weirdness
                w.Closing -= OnWindowClosing;
                w.Closed -= OnWindowClosed;

                _allowWindowClose = true;
                w.Close();
            }

            _window = null;
            _uiDispatcher = null;

            // Clear UI refs / state
            _masterBox = null;
            _followersPanel = null;

            _btnBuyMkt = null;
            _btnSellMkt = null;
            _btnFlattenAll = null;

            _btnCopyOn = null;

            _statusBox = null;
            _headerStateText = null;

            _masterQtyBox = null;
            _masterAtmBox = null;
            _masterPnlText = null;
            _totalPnlText = null;

            _followerRows.Clear();
            _lastAccountsSnapshot.Clear();

            _instrumentTabs = null;
            _btnAddInstrumentTab = null;
            _btnRemoveInstrumentTab = null;
            _activeInstrumentSession = null;
            _suppressSessionUiEvents = false;
            _instrumentSessions.Clear();
        }

        private void StartInstrumentRefreshTimer()
        {
            if (_uiDispatcher == null)
                return;

            if (_instrumentRefreshTimer != null)
                return;

            lock (WindowGate)
            {
                _instrumentRefreshTimer = new DispatcherTimer(
                    TimeSpan.FromSeconds(1),
                    DispatcherPriority.Background,
                    OnInstrumentRefreshTimerTick,
                    _uiDispatcher);
            }

            _instrumentRefreshTimer.Start();
            SafeTradeSuiteRuntime.PrintLog("Instrument refresh timer started.");
        }

        private void OnInstrumentRefreshTimerTick(object sender, EventArgs e)
        {
            try
            {
                RefreshInstrumentSelectorIfNeeded();
            }
            catch (Exception ex)
            {
                LogUnhandled("OnInstrumentRefreshTimerTick()", ex);
            }
        }

        private void RefreshInstrumentSelectorIfNeeded()
        {
            var latest = GetAvailableInstruments();

            var changed =
                latest.Count != _lastInstrumentSnapshot.Count ||
                !latest.SequenceEqual(_lastInstrumentSnapshot, StringComparer.OrdinalIgnoreCase);

            if (!changed)
                return;

            _lastInstrumentSnapshot = latest.ToList();

            SafeTradeSuiteRuntime.PrintLog(
                latest.Count == 0
                    ? $"Copier[{_toolId}] Instrument selector refresh -> <none>"
                    : $"Copier[{_toolId}] Instrument selector refresh -> {string.Join(", ", latest)}");

            var selected = GetSelectedInstrumentName();

            _suppressSessionUiEvents = true;
            try
            {
                _instrumentSelector.ItemsSource = null;
                _instrumentSelector.Items.Clear();

                foreach (var item in latest)
                    _instrumentSelector.Items.Add(item);

                if (!string.IsNullOrWhiteSpace(selected) && _instrumentSelector.Items.Contains(selected))
                {
                    _instrumentSelector.SelectedItem = selected;
                }
                else if (_activeInstrumentSession != null &&
                         !string.IsNullOrWhiteSpace(_activeInstrumentSession.InstrumentName) &&
                         _instrumentSelector.Items.Contains(_activeInstrumentSession.InstrumentName))
                {
                    _instrumentSelector.SelectedItem = _activeInstrumentSession.InstrumentName;
                }
                else if (_instrumentSelector.Items.Count > 0)
                {
                    _instrumentSelector.SelectedIndex = 0;

                    if (_activeInstrumentSession != null)
                        _activeInstrumentSession.InstrumentName = _instrumentSelector.SelectedItem as string ?? "";
                }
            }
            finally
            {
                _suppressSessionUiEvents = false;
            }

            RefreshInstrumentTabs();
            ApplyConfigFromUi();
            RenderFlattenEnablementUi();
            RenderFlattenAllButtonState();
        }
    }
}