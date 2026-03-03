#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        // Single instance window (prevents multiple instances in NT)
        private static Window _window;
        private static readonly object WindowGate = new object();

        private SafeCopierEngine _engine;
        private Dispatcher _uiDispatcher;
        private bool _allowWindowClose;

        private ComboBox _masterBox;
        private TextBox _instrBox;

        private StackPanel _followersPanel;

        private DispatcherTimer _accountsTimer;
        private List<AccountSnap> _lastAccountsSnapshot = new List<AccountSnap>();

        public void Show()
        {
            lock (WindowGate)
            {
                // If window exists but is no longer usable, drop it and rebuild
                if (_window != null)
                {
                    if (!_window.IsLoaded || _window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
                    {
                        CloseInternal(closeWindow: true);
                    }
                }

                if (_window != null)
                {
                    if (!_window.IsVisible)
                        _window.Show();

                    _window.WindowState = WindowState.Normal;
                    _window.Activate();

                    StartAccountsAutoRefresh();
                    return;
                }

                _engine = new SafeCopierEngine();

                try
                {
                    _window = new Window
                    {
                        Title = "Safe Trade Copier (v2)",
                        Width = 520,
                        Height = 620,
                        Background = SystemColors.WindowBrush,
                        Foreground = SystemColors.WindowTextBrush,
                        Content = BuildUi(_engine),
                    };

                    _uiDispatcher = _window.Dispatcher;

                    _window.Closing += OnWindowClosing;
                    _window.Closed += OnWindowClosed;

                    _window.Show();
                    StartAccountsAutoRefresh();
                }
                catch
                {
                    // If BuildUi throws or window creation fails, ensure we don't leave a half-created tool around
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
                CloseInternal(closeWindow: true);
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowWindowClose) return;

            // X button hides (keeps tool alive)
            e.Cancel = true;

            try { _window?.Hide(); } catch { }

            StopAccountsAutoRefresh();
            StopPnLTimer();
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
            // Stop UI timers first (prevents Tick from touching disposed UI/engine)
            StopAccountsAutoRefresh();
            StopPnLTimer();

            // Turn off copier + dispose engine
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
                try { w.Close(); } catch { }
                _allowWindowClose = false;
            }

            _uiDispatcher = null;
            _window = null;

            // Clear UI refs / state
            _masterBox = null;
            _instrBox = null;
            _followersPanel = null;

            _btnBuyMkt = null;
            _btnSellMkt = null;
            _btnFlattenAll = null;

            _btnCopyOn = null;
            _btnCopyOff = null;

            _statusBox = null;
            _headerStateText = null;

            _masterQtyBox = null;
            _masterAtmBox = null;
            _masterPnlText = null;
            _totalPnlText = null;

            _followerRows.Clear();
            _lastAccountsSnapshot.Clear();
        }
    }
}