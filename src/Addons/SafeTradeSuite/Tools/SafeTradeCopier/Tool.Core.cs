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
                    _uiDispatcher.InvokeAsync(() =>
                    {
                        EnsureInitialInstrumentSession();
                        // EnsureStartupInstrumentSelection();
                        RefreshInstrumentSelectorItems();
                        RefreshInstrumentTabs();
                        LoadActiveSessionToUi();
                        RenderFlattenAllButtonState();
                    }, DispatcherPriority.Loaded);
                    
                    _window.Closing += OnWindowClosing;
                    _window.Closed += OnWindowClosed;

                    _window.Show();
                    HookConnectionStatusUpdates();
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
            if (_allowWindowClose)
                return;

            e.Cancel = true;
            HardClose();
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
            _allowWindowClose = false;

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
            _activeInstrumentSession = null;
            _suppressSessionUiEvents = false;
            _instrumentSessions.Clear();
        }
    }
}