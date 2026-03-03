#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
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
        private readonly List<CheckBox> _followerCheckboxes = new List<CheckBox>();

        private DispatcherTimer _accountsTimer;
        private List<AccountSnap> _lastAccountsSnapshot = new List<AccountSnap>();

        public void Show()
        {
            lock (WindowGate)
            {
                // If window exists but is no longer usable, drop it
                if (_window != null)
                {
                    if (!_window.IsLoaded || _window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
                    {
                        _window = null;
                        _uiDispatcher = null;
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

                _window = new Window
                {
                    Title = "Safe Trade Copier (v1)",
                    Width = 520,
                    Height = 620,
                    Background = SystemColors.WindowBrush,
                    Foreground = SystemColors.WindowTextBrush,
                    Content = BuildUi(_engine),
                };

                _uiDispatcher = _window.Dispatcher;

                _window.Closing += (s, e) =>
                {
                    if (_allowWindowClose) return;

                    // Hide instead of closing
                    e.Cancel = true;
                    _window.Hide();
                    StopAccountsAutoRefresh();
                };

                _window.Closed += (s, e) =>
                {
                    StopAccountsAutoRefresh();

                    _uiDispatcher = null;
                    _window = null;

                    _engine?.Dispose();
                    _engine = null;
                };

                StopPnLTimer();
                _window.Show();
                StartAccountsAutoRefresh();
            }
        }

        public void Dispose()
        {
            StopAccountsAutoRefresh();

            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }

            lock (WindowGate)
            {
                if (_window != null)
                {
                    var w = _window;
                    _window = null;
                    _uiDispatcher = null;

                    _allowWindowClose = true;
                    w.Close();
                    _allowWindowClose = false;
                }
            }
        }
    }
}