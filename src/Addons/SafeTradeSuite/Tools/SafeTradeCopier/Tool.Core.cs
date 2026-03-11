using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private readonly string _toolId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private static readonly object WindowGate = new object();
        private bool _allowWindowClose;
        private bool _isClosing;
        
        private static Window _window;
        private Dispatcher _uiDispatcher;
        private SafeCopierEngine _engine;
        private ComboBox _masterBox;
        private ComboBox _instrumentSelector;
        private StackPanel _followersPanel;
        private List<AccountSnap> _lastAccountsSnapshot = new List<AccountSnap>();

        public void Show()
        {
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] Show()");
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] WindowExists={_window != null}");

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

                _isClosing = false;
                _engine = new SafeCopierEngine();

                try
                {
                    _window = new Window
                    {
                        Title = "Safe Trade Copier (V2.1)",
                        Width = 900,
                        Height = 750,
                        MinWidth = 820,
                        MinHeight = 750,
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

        
        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;

            if (_allowWindowClose)
                return;

            // Treat the window X as a real close now
            _allowWindowClose = true;
            UnsubscribeUiAccountEvents(Account.All);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            lock (WindowGate)
            {
                CloseInternal(closeWindow: false);
            }
        }
        

        private void CloseInternal(bool closeWindow)
        {
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] CloseInternal(closeWindow={closeWindow})");
            
            _isClosing = true;
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
            _statusLabel = null;
            _statusBox = null;

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