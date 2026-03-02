#region Using declarations
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        // Single instance window (prevents multiple instances in NT)
        private static Window window;
        private static readonly object windowGate = new object();

        private SafeCopierEngine engine;
        private Dispatcher uiDispatcher;
        private bool allowWindowClose;

        private ComboBox masterBox;
        private TextBox instrBox;

        private StackPanel followersPanel;
        private List<CheckBox> followerCheckboxes = new List<CheckBox>();

        private DispatcherTimer accountsTimer;
        private List<AccountSnap> lastAccountsSnapshot = new List<AccountSnap>();

        // UI state
        private TextBlock headerStateText;
        private TextBox statusBox;
        private Button btnCopyOn;
        private Button btnCopyOff;

        private bool readyState;
        private string readyReason = "";

        public void Show()
        {
            lock (windowGate)
            {
                // If window exists but is no longer usable, drop it
                if (window != null)
                {
                    if (!window.IsLoaded || window.Dispatcher.HasShutdownStarted || window.Dispatcher.HasShutdownFinished)
                    {
                        window = null;
                        uiDispatcher = null;
                    }
                }

                if (window != null)
                {
                    if (!window.IsVisible)
                        window.Show();

                    window.WindowState = WindowState.Normal;
                    window.Activate();

                    StartAccountsAutoRefresh();
                    return;
                }

                engine = new SafeCopierEngine();

                window = new Window
                {
                    Title = "Safe Trade Copier (v1)",
                    Width = 520,
                    Height = 620,
                    Background = SystemColors.WindowBrush,
                    Foreground = SystemColors.WindowTextBrush,
                    Content = BuildUi(engine),
                };

                uiDispatcher = window.Dispatcher;

                window.Closing += (s, e) =>
                {
                    if (allowWindowClose) return;

                    // Hide instead of closing
                    e.Cancel = true;
                    window.Hide();
                    StopAccountsAutoRefresh();
                };

                window.Closed += (s, e) =>
                {
                    StopAccountsAutoRefresh();

                    uiDispatcher = null;
                    window = null;

                    engine?.Dispose();
                    engine = null;
                };

                window.Show();
                StartAccountsAutoRefresh();
            }
        }

        public void Dispose()
        {
            StopAccountsAutoRefresh();

            if (engine != null)
            {
                engine.Dispose();
                engine = null;
            }

            lock (windowGate)
            {
                if (window != null)
                {
                    var w = window;
                    window = null;
                    uiDispatcher = null;

                    allowWindowClose = true;
                    w.Close();
                    allowWindowClose = false;
                }
            }
        }

        private sealed class AccountSnap
        {
            public readonly string Name;
            public readonly ConnectionStatus Status;
            public readonly string ConnName;

            public AccountSnap(Account a)
            {
                Name = a?.Name ?? "";
                Status = a != null ? a.ConnectionStatus : ConnectionStatus.Disconnected;
                ConnName = a?.Connection != null ? (a.Connection.Options?.Name ?? a.Connection.ToString()) : "";
            }

            public override bool Equals(object obj)
            {
                var o = obj as AccountSnap;
                if (o == null) return false;
                return string.Equals(Name, o.Name, StringComparison.Ordinal)
                       && Status == o.Status
                       && string.Equals(ConnName, o.ConnName, StringComparison.Ordinal);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var h = 17;
                    h = h * 31 + (Name?.GetHashCode() ?? 0);
                    h = h * 31 + Status.GetHashCode();
                    h = h * 31 + (ConnName?.GetHashCode() ?? 0);
                    return h;
                }
            }
        }
    }
}