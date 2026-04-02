using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private readonly string _toolId = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly object _windowGate = new object();
        private bool _isClosing;

        private Window _window;
        private Dispatcher _uiDispatcher;
        private static RelayEngine _engine;
        private ComboBox _masterBox;
        private ComboBox _instrumentSelector;
        private StackPanel _followersPanel;
        private List<AccountSnap> _lastAccountsSnapshot = new List<AccountSnap>();

        private TabItem _licenseTab;
        private TextBlock _licenseStatusText;
        private TextBlock _licenseFingerprintText;
        private TextBlock _licenseVersionText;

        public void Show()
        {
            NinjexRuntime.PrintLog($"Relay[{_toolId}] Show()");

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
                }

                if (_window != null)
                {
                    if (!_window.IsVisible)
                        _window.Show();

                    if (_window.WindowState == WindowState.Minimized)
                        _window.WindowState = WindowState.Normal;

                    _window.Activate();

                    if (Application.Current != null)
                    {
                        Application.Current.DispatcherUnhandledException += (s, e) =>
                        {
                            LogUnhandled("Application.DispatcherUnhandledException", e.Exception);
                        };
                    }

                    if (_window.Dispatcher != null)
                    {
                        _window.Dispatcher.UnhandledException += (s, e) =>
                        {
                            LogUnhandled("Window.Dispatcher.UnhandledException", e.Exception);
                        };
                    }

                    AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    {
                        LogUnhandled("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject as Exception);
                    };

                    return;
                }

                try
                {
                    CreateAndShowWindow();
                }
                catch (Exception ex)
                {
                    NinjexRuntime.PrintLog("RelayTool.Show() FAILED:");
                    NinjexRuntime.PrintLog(ex.ToString());

                    LogUnhandled("RelayTool.Show()", ex);
                    Dispose();
                    throw;
                }
            }
        }

        private void CreateAndShowWindow(
            double? left = null,
            double? top = null,
            WindowState windowState = WindowState.Normal)
        {
            _isClosing = false;
            _engine = new RelayEngine(this);

            LoadPersistentUiState();
            _lastAppliedConfig = null;

            _window = new Window
            {
                Title = "Ninjex Relay",
                Width = 820,
                Height = 720,
                ResizeMode = ResizeMode.CanResize,
                SizeToContent = SizeToContent.Manual,
                Background = WindowBackgroundBrush(),
                Foreground = WindowForegroundBrush(),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                Content = new Grid
                {
                    Margin = new Thickness(3),
                    Children =
                    {
                        BuildChromedWindowContent(_engine)
                    }
                }
            };

            if (left.HasValue)
                _window.Left = left.Value;

            if (top.HasValue)
                _window.Top = top.Value;

            WindowChrome.SetWindowChrome(_window, new WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(8),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
                NonClientFrameEdges = NonClientFrameEdges.None
            });

            _uiDispatcher = _window.Dispatcher;

            _window.Dispatcher.UnhandledException += (s, e) =>
            {
                LogUnhandled("Dispatcher.UnhandledException", e.Exception);
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogUnhandled("AppDomain.CurrentDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            _window.Closing += OnWindowClosing;
            _window.Closed += OnWindowClosed;
            _window.StateChanged += (s, e) => UpdateWindowCaptionButtons();

            HookConnectionStatusUpdates();

            var licenseManager = NinjexRuntime.GetOrCreateLicenseManager();
            licenseManager.LicenseStateChanged += OnLicenseStateChanged;

            _window.Show();
            _window.WindowState = windowState;

            ApplyLicenseState(licenseManager.State);

            _uiDispatcher.InvokeAsync(() =>
            {
                EnsureInitialInstrumentSession();
                RefreshInstrumentSelectorItems();
                RefreshInstrumentTabs();

                ApplyConfigFromUiAndRefresh();
                Rehydrate();
            }, DispatcherPriority.Loaded);
        }

        private void CloseCurrentWindowForRebuild()
        {
            if (_window == null)
                return;

            var w = _window;
            _window = null;

            w.Closing -= OnWindowClosing;
            w.Closed -= OnWindowClosed;

            if (w.Dispatcher.CheckAccess())
                w.Close();
            else
                w.Dispatcher.Invoke(() => w.Close());
        }

        private void ReopenWindowForThemeChange()
        {
            var left = _window?.Left;
            var top = _window?.Top;
            var state = _window?.WindowState ?? WindowState.Normal;

            lock (_windowGate)
            {
                _isClosing = true;

                UnsubscribeUiAccountEvents(Account.All);
                TearDownEngine();
                CloseCurrentWindowForRebuild();
                TearDownUiState();

                CreateAndShowWindow(left, top, state);
            }
        }

        private void Rehydrate()
        {
            ApplyConfigFromUiAndRefresh();

            _engine?.RehydrateActiveBracketsFromLiveOrders();
            EnsureEnabledFollowersAndAutoRearmForOpenPositions();

            RenderFollowerRowsState();
            RefreshRelayStatusPanel();
            RenderFlattenEnablementUi();
            RenderBreakEvenEnablementUi();
            RenderPnlUi();
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
            var licenseManager = NinjexRuntime.GetOrCreateLicenseManager();
            licenseManager.LicenseStateChanged -= OnLicenseStateChanged;

            _window = null;
            _uiDispatcher = null;
            _btnWindowMinimize = null;
            _btnWindowMaximize = null;
            _btnWindowClose = null;
            _windowTitleBar = null;

            _masterBox = null;
            _instrumentSelector = null;
            _followersPanel = null;

            _btnBuyMkt = null;
            _btnSellMkt = null;
            _btnFlattenAll = null;
            _btnCopyOn = null;

            _statusBox = null;
            _masterQtyBox = null;
            _masterBracketBox = null;
            _masterPnlText = null;
            _followersTotalPnlText = null;

            _instrumentTabs = null;
            _btnAddInstrumentTab = null;
            _activeInstrumentSession = null;

            _followerRows.Clear();
            _lastAccountsSnapshot.Clear();
            _instrumentSessions.Clear();

            _lastAppliedConfig = null;

            _licensingContent = null;
            _licenseTab = null;
            _licenseStatusText = null;
            _licenseFingerprintText = null;
            _licenseVersionText = null;
        }

        public void Dispose()
        {
            lock (_windowGate)
            {
                _isClosing = true;

                UnsubscribeUiAccountEvents(Account.All);
                TearDownEngine();
                CloseCurrentWindowForRebuild();
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