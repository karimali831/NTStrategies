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
        private readonly object _windowGate = new object();
        private bool _isClosing;

        private Window _window;
        private Dispatcher _uiDispatcher;
        private SafeCopierEngine _engine;
        private ComboBox _masterBox;
        private ComboBox _instrumentSelector;
        private StackPanel _followersPanel;
        private List<AccountSnap> _lastAccountsSnapshot = new List<AccountSnap>();
        
        public void Show()
        {
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] Show()");

            lock (_windowGate)
            {
                if (_window != null)
                {
                    if (!_window.IsLoaded ||
                        _window.Dispatcher.HasShutdownStarted ||
                        _window.Dispatcher.HasShutdownFinished)
                    {
                        TearDownEngine();
                        TearDownUiState();
                    }
                    
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
                        Width = 850,
                        Height = 730,
                        ResizeMode = ResizeMode.CanResize,
                        SizeToContent = SizeToContent.Manual,
                        Background = WindowBackgroundBrush(),
                        Foreground = WindowForegroundBrush(),
                        Content = SafeBuildUi(_engine),
                    };

                    _uiDispatcher = _window.Dispatcher;

                    _window.Closing += OnWindowClosing;
                    _window.Closed += OnWindowClosed;

                    HookConnectionStatusUpdates();
                    _window.Show();

                    _uiDispatcher.InvokeAsync(() =>
                    {
                        EnsureInitialInstrumentSession();
                        RefreshInstrumentSelectorItems();
                        RefreshInstrumentTabs();
                        LoadActiveSessionToUi();
                        RenderFlattenAllButtonState();
                    }, DispatcherPriority.Loaded);
                }
                catch (Exception ex)
                {
                    SafeTradeSuiteRuntime.PrintLog("SafeTradeCopierTool.Show() FAILED:");
                    SafeTradeSuiteRuntime.PrintLog(ex.ToString());

                    LogUnhandled("SafeTradeCopierTool.Show()", ex);
                    Dispose();
                    throw;
                }
            }
        }
        
        private void TearDownEngine()
        {
            UnhookConnectionStatusUpdates();

            if (_engine != null)
            {
                _engine.SetCopyEnabled(false);
                _engine.Dispose();
                _engine = null;
            }
        }

        private void TearDownUiState()
        {
            _window = null;
            _uiDispatcher = null;

            _masterBox = null;
            _instrumentSelector = null;
            _followersPanel = null;

            _btnBuyMkt = null;
            _btnSellMkt = null;
            _btnFlattenAll = null;
            _btnCopyOn = null;

            _statusBox = null;
            _masterQtyBox = null;
            _masterAtmBox = null;
            _masterPnlText = null;
            _totalPnlText = null;

            _instrumentTabs = null;
            _btnAddInstrumentTab = null;

            _activeInstrumentSession = null;
            _suppressSessionUiEvents = false;

            _followerRows.Clear();
            _lastAccountsSnapshot.Clear();
            _instrumentSessions.Clear();
        }

        public void Dispose()
        {
            lock (_windowGate)
            {
                _isClosing = true;

                UnsubscribeUiAccountEvents(Account.All);
                TearDownEngine();

                if (_window != null)
                {
                    var w = _window;
                    _window = null;

                    w.Closing -= OnWindowClosing;
                    w.Closed -= OnWindowClosed;

                    if (w.Dispatcher.CheckAccess())
                        w.Close();
                    else
                        w.Dispatcher.Invoke(() => w.Close());
                }

                TearDownUiState();
            }
        }
        
        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            lock (_windowGate)
            {
                _isClosing = true;
            }

            UnsubscribeUiAccountEvents(Account.All);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            lock (_windowGate)
            {
                TearDownEngine();
                TearDownUiState();
            }
        }
    }
}