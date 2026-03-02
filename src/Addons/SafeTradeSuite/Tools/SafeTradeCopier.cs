#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public sealed class SafeTradeCopierTool : IDisposable
    {
        private Window window;
        private SafeCopierEngine engine;
        private Dispatcher uiDispatcher;
        private bool allowWindowClose;
        
        private ComboBox masterBox;
        private StackPanel followersPanel;
        private List<CheckBox> followerCheckboxes;

        private DispatcherTimer accountsTimer;
        private List<Account> lastAccountsSnapshot = new List<Account>();

        public void Show()
        {
            // If the window object exists but is no longer usable, drop it and rebuild
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
                Content = BuildUi(engine),
                Background = SystemColors.WindowBrush,
                Foreground = SystemColors.WindowTextBrush
            };

            uiDispatcher = window.Dispatcher;

            window.Closing += (s, e) =>
            {
                if (allowWindowClose) return;
                e.Cancel = true;
                window.Hide();
                StopAccountsAutoRefresh();
            };

            // Important: if the window DOES close, clear refs so Show() rebuilds safely
            window.Closed += (s, e) =>
            {
                uiDispatcher = null;
                window = null;

                engine?.Dispose();
                engine = null;
            };

            window.Show();
            StartAccountsAutoRefresh();
        }

        private UIElement BuildUi(SafeCopierEngine eng)
        {
            var root = new Grid
            {
                Margin = new Thickness(12),
                Background = SystemColors.WindowBrush
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // master + instr
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // followers
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // controls + status

            var headerArea = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var header = new TextBlock
            {
                Text = "Execution-based copier with circuit-breaker",
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = SystemColors.WindowTextBrush
            };

            var modeText = new TextBlock
            {
                Text = "ARMED: OFF   |   COPY: OFF",
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush
            };

            headerArea.Children.Add(header);
            headerArea.Children.Add(modeText);

            Grid.SetRow(headerArea, 0);
            root.Children.Add(headerArea);

            // ---------------- Master + Instrument ----------------
            var row1 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            masterBox = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
            var instrBox = new TextBox { Height = 28, Text = "NQ 03-26", Margin = new Thickness(0, 0, 0, 6) };

            var accounts = GetSelectableAccounts();
            masterBox.ItemsSource = accounts;
            masterBox.DisplayMemberPath = "Name";
            masterBox.SelectedItem = accounts.FirstOrDefault();

            row1.Children.Add(new TextBlock { Text = "Master account:", Foreground = SystemColors.WindowTextBrush });
            row1.Children.Add(masterBox);
            row1.Children.Add(new TextBlock { Text = "Instrument:", Foreground = SystemColors.WindowTextBrush });
            row1.Children.Add(instrBox);

            Grid.SetRow(row1, 1);
            root.Children.Add(row1);

            // ---------------- Followers (checkbox list) ----------------
            var row2 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            row2.Children.Add(new TextBlock { Text = "Copy accounts:", Foreground = SystemColors.WindowTextBrush });

            followersPanel = new StackPanel { Orientation = Orientation.Vertical };
            var followersScroll = new ScrollViewer
            {
                Height = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = followersPanel,
                Background = SystemColors.ControlLightBrush
            };

            followerCheckboxes = new List<CheckBox>();

            foreach (var acc in accounts)
            {
                var cb = new CheckBox
                {
                    Content = acc.Name,
                    Tag = acc,
                    Margin = new Thickness(6, 3, 6, 3),
                    Foreground = SystemColors.ControlTextBrush
                };

                followerCheckboxes.Add(cb);
                followersPanel.Children.Add(cb);
            }

            void ApplyMasterExclusion()
            {
                var master = masterBox.SelectedItem as Account;

                foreach (var cb in followerCheckboxes)
                {
                    var acc = cb.Tag as Account;
                    var isMaster = (master != null && acc != null && ReferenceEquals(acc, master));

                    if (isMaster)
                    {
                        cb.IsChecked = false;
                        cb.Visibility = Visibility.Collapsed; // hide master from follower list
                    }
                    else
                    {
                        cb.Visibility = Visibility.Visible;
                    }
                }
            }

            masterBox.SelectionChanged += (s, e) => ApplyMasterExclusion();
            ApplyMasterExclusion();

            row2.Children.Add(followersScroll);

            Grid.SetRow(row2, 2);
            root.Children.Add(row2);

            // ---------------- Controls + Status ----------------
            var row3 = new StackPanel { Orientation = Orientation.Vertical };

            var btnArm = new Button
            {
                Content = "ARM (required)",
                Height = 40,
                Background = Brushes.SteelBlue,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 6, 0, 6)
            };

            var btnOn = new Button
            {
                Content = "COPY ON",
                Height = 40,
                Background = Brushes.DarkGreen,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var btnOff = new Button
            {
                Content = "DISARM (panic)",
                Height = 40,
                Background = Brushes.Maroon,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var btnQuit = new Button
            {
                Content = "QUIT (close & dispose)",
                Height = 40,
                Background = Brushes.DimGray,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };
            
            void UpdateModeUi(bool isArmed, bool isCopyOn)
            {
                modeText.Text = $"ARMED: {(isArmed ? "ON" : "OFF")}   |   COPY: {(isCopyOn ? "ON" : "OFF")}";

                btnArm.IsEnabled = !isArmed;
                btnOn.IsEnabled = isArmed && !isCopyOn;
                btnOff.IsEnabled = isArmed;

                // Optional: make Copy button show current intent
                btnOn.Content = isCopyOn ? "COPY ON (active)" : "COPY ON";
            }

            UpdateModeUi(isArmed: false, isCopyOn: false);

            eng.OnModeChanged += (a, c) =>
            {
                var disp = uiDispatcher ?? window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() => UpdateModeUi(a, c));
            };

            var status = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 160,
                Background = SystemColors.ControlLightBrush,
                Foreground = SystemColors.ControlTextBrush,
                Margin = new Thickness(0, 10, 0, 0)
            };

            eng.OnStatus += (msg) =>
            {
                var disp = uiDispatcher ?? window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    status.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
                    status.ScrollToEnd();
                });
            };

            btnArm.Click += (s, e) =>
            {
                if (!(masterBox.SelectedItem is Account master))
                {
                    eng.Log("Select a master account (must be Connected).");
                    return;
                }

                var followers = followerCheckboxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag as Account)
                    .Where(a => a != null && !ReferenceEquals(a, master))
                    .ToList();

                if (followers.Count == 0)
                {
                    eng.Log("Select at least one follower (Connected).");
                    return;
                }

                eng.Configure(
                    masterAccount: master,
                    followerAccounts: followers,
                    instrumentName: instrBox.Text?.Trim()
                );

                eng.Arm();
            };

            btnOn.Click += (s, e) => eng.EnableCopying();
            btnOff.Click += (s, e) => eng.Disarm("Manual DISARM");

            btnQuit.Click += (s, e) =>
            {
                allowWindowClose = true;
                window.Close();
                allowWindowClose = false;
            };

            row3.Children.Add(btnArm);
            row3.Children.Add(btnOn);
            row3.Children.Add(btnOff);
            row3.Children.Add(btnQuit);
            row3.Children.Add(new TextBlock { Text = "Status:", Foreground = SystemColors.WindowTextBrush });
            row3.Children.Add(status);

            Grid.SetRow(row3, 3);
            root.Children.Add(row3);

            // If no accounts, make it obvious
            if (accounts.Count == 0)
                eng.Log("No Connected accounts detected. Connect in Control Center first.");

            return root;
        }

        // private List<Account> GetSelectableAccounts()
        // {
        //     // Only Connected accounts (filters out dead/liquidated/closed)
        //     // Playback accounts will only appear if connected (i.e., typically Market Replay)
        //     return Account.All
        //         .Where(a => a != null && a.ConnectionStatus == ConnectionStatus.Connected)
        //         .OrderBy(a => a.Name)
        //         .ToList();
        // }

        private List<Account> GetSelectableAccounts()
        {
            return Account.All
                .Where(a => a?.Connection != null)
                .OrderBy(a => a.Name)
                .ToList();
        }
        
        private void StartAccountsAutoRefresh()
        {
            if (accountsTimer != null) return;

            accountsTimer = new DispatcherTimer(DispatcherPriority.Background, uiDispatcher ?? Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            
            accountsTimer.Tick += (s, e) => RefreshAccountsUi();
            accountsTimer.Start();

            RefreshAccountsUi(); // immediate first run
        }

        private void StopAccountsAutoRefresh()
        {
            if (accountsTimer == null) return;

            accountsTimer.Stop();
            accountsTimer = null;
        }

        private void RefreshAccountsUi()
        {
            if (window == null) return;
            if (masterBox == null) return;
            if (followersPanel == null) return;
            if (followerCheckboxes == null) return;

            // Preserve current selections
            var prevMasterName = (masterBox.SelectedItem as Account)?.Name;
            var prevChecked = new HashSet<string>(
                followerCheckboxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => (cb.Tag as Account)?.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
            );

            // Recompute accounts
            var accounts = GetSelectableAccounts();

            // If nothing materially changed, skip UI rebuild
            if (SameAccountList(lastAccountsSnapshot, accounts))
                return;

            lastAccountsSnapshot = accounts;

            // Update master list
            masterBox.ItemsSource = accounts;
            masterBox.DisplayMemberPath = "Name";

            var newMaster = !string.IsNullOrWhiteSpace(prevMasterName)
                ? accounts.FirstOrDefault(a => a.Name == prevMasterName)
                : null;

            masterBox.SelectedItem = newMaster ?? accounts.FirstOrDefault();

            // Rebuild followers checkboxes (excluding master)
            var master = masterBox.SelectedItem as Account;
            var masterName = master?.Name;

            followersPanel.Children.Clear();
            followerCheckboxes.Clear();

            foreach (var acc in accounts)
            {
                if (!string.IsNullOrWhiteSpace(masterName) && acc.Name == masterName)
                    continue;

                var cb = new CheckBox
                {
                    Content = acc.Name,
                    Tag = acc,
                    Margin = new Thickness(6, 3, 6, 3),
                    Foreground = SystemColors.ControlTextBrush,
                    IsChecked = prevChecked.Contains(acc.Name)
                };

                followerCheckboxes.Add(cb);
                followersPanel.Children.Add(cb);
            }

            // If master changes manually, we should also keep followers excluding it.
            masterBox.SelectionChanged -= MasterBox_SelectionChanged;
            masterBox.SelectionChanged += MasterBox_SelectionChanged;
        }

        private void MasterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // When master changes, immediately refresh the followers list
            RefreshAccountsUi();
        }

        private bool SameAccountList(List<Account> a, List<Account> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            for (var i = 0; i < a.Count; i++)
            {
                // comparing by Name is stable enough for UI purposes
                if (!string.Equals(a[i]?.Name, b[i]?.Name, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        public void Dispose()
        {
            // Stop UI timers first (prevents ticks during shutdown)
            StopAccountsAutoRefresh();

            // Stop engine next (prevents background callbacks)
            if (engine != null)
            {
                engine.Dispose();
                engine = null;
            }

            // Close window last
            if (window != null)
            {
                var w = window;          // capture local
                window = null;           // important: prevent Show() from seeing a closing window
                uiDispatcher = null;

                allowWindowClose = true;
                w.Close();
                allowWindowClose = false;
            }
        }
    }

    internal sealed class SafeCopierEngine : IDisposable
    {
        private Account master;
        private List<Account> followers = new List<Account>();
        private string instrumentName;
        private Instrument instrument;

        // Shadow net position for master (avoids stale Account.Positions during ExecutionUpdate)
        private int masterNetShadow;
        private bool masterNetShadowInit;

        private volatile bool armed;
        private volatile bool copyEnabled;
        private readonly object gate = new object();

        private readonly ConcurrentDictionary<string, long> seen = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentQueue<long> copiedTicks = new ConcurrentQueue<long>();

        // Safety defaults (not exposed to UI)
        private const int MaxAbsQtyPerFollower = 2;
        private const int MaxCopiesPer2Sec = 20;
        private const int StaggerMsPerFollower = 125;

        private readonly SemaphoreSlim submitLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource cts = new CancellationTokenSource();

        public event Action<string> OnStatus;
        public event Action<bool, bool> OnModeChanged; 

        public void Configure(Account masterAccount, List<Account> followerAccounts, string instrumentName)
        {
            Disarm("Reconfigure");

            master = masterAccount;
            followers = followerAccounts?.Where(a => a != null).Distinct().ToList() ?? new List<Account>();
            this.instrumentName = instrumentName ?? "";

            instrument = string.IsNullOrWhiteSpace(this.instrumentName)
                ? null
                : Instrument.GetInstrument(this.instrumentName);

            Log($"Configured. Master={master?.Name}, Followers={followers.Count}, Instr='{this.instrumentName}'");
        }
        
        private void RaiseModeChanged()
        {
            OnModeChanged?.Invoke(armed, copyEnabled);
        }

        public void Arm()
        {
            if (master == null || followers.Count == 0)
            {
                Log("Cannot ARM: missing master/followers.");
                return;
            }

            if (instrument == null)
            {
                Log("Cannot ARM: invalid instrument name (must match NT instrument exactly).");
                return;
            }

            lock (gate)
            {
                if (armed) return;

                // Safety: ignore any follower that isn't connected at arm time
                followers = followers
                    .Where(a => a != null && a.ConnectionStatus == ConnectionStatus.Connected)
                    .Distinct()
                    .ToList();

                if (followers.Count == 0)
                {
                    Log("Cannot ARM: no Connected followers.");
                    return;
                }
                
                masterNetShadow = GetNetPosition(master, instrument);
                masterNetShadowInit = true;

                armed = true;
                copyEnabled = false;

                RaiseModeChanged();

                master.ExecutionUpdate += OnMasterExecution;

                foreach (var f in followers)
                    f.OrderUpdate += OnFollowerOrderUpdate;

                Log("ARMED. Copying is OFF until you press COPY ON.");
            }
        }

        public void EnableCopying()
        {
            lock (gate)
            {
                if (!armed)
                {
                    Log("Press ARM first.");
                    return;
                }
            }

            copyEnabled = true;
            RaiseModeChanged();
            Log("COPY ON.");
        }

        public void Disarm(string reason)
        {
            CancellationTokenSource oldCts;

            lock (gate)
            {
                copyEnabled = false;

                if (armed)
                {
                    if (master != null)
                        master.ExecutionUpdate -= OnMasterExecution;

                    foreach (var f in followers)
                        f.OrderUpdate -= OnFollowerOrderUpdate;
                }

                armed = false;
                masterNetShadowInit = false;
                masterNetShadow = 0;
                
                RaiseModeChanged();
                // Swap FIRST so no other thread can observe a disposed CTS in 'cts'
                oldCts = cts;
                cts = new CancellationTokenSource();

                seen.Clear();
                while (copiedTicks.TryDequeue(out _)) { }

                if (!string.IsNullOrWhiteSpace(reason))
                    Log($"DISARMED: {reason}");
            }

            // Cancel/Dispose outside lock
            oldCts.Cancel();
            oldCts.Dispose();
        }

        private void OnMasterExecution(object sender, ExecutionEventArgs e)
        {
            if (!armed || !copyEnabled) return;
            if (e?.Execution == null) return;
            if (master == null || e.Execution.Account != master) return;
            if (instrument == null) return;

            if (e.Execution.Instrument == null || e.Execution.Instrument.FullName != instrument.FullName)
                return;

            var execId = e.Execution.ExecutionId ?? "";
            if (string.IsNullOrWhiteSpace(execId))
                execId = $"{e.Execution.Time.Ticks}_{e.Execution.Price}_{e.Execution.Quantity}_{e.Execution.MarketPosition}";

            if (!AllowCopyNow())
            {
                Disarm("Circuit breaker: too many copied orders in short window");
                return;
            }

            if (!masterNetShadowInit)
            {
                masterNetShadow = GetNetPosition(master, instrument);
                masterNetShadowInit = true;
            }

            // Update shadow net using *this execution* (so entry is seen immediately)
            masterNetShadow += SignedQtyFromExecution(e.Execution);

            var masterTargetNet = masterNetShadow;

            // Capture a stable token source/token for this work item
            var localCts = cts;
            var token = localCts.Token;

            Task.Run(async () =>
            {
                await submitLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await CopyToFollowers(execId, masterTargetNet, token).ConfigureAwait(false);
                }
                finally
                {
                    submitLock.Release();
                }
            }, token);
        }

        private async Task CopyToFollowers(string execId, int masterTargetNet, CancellationToken token)
        {
            // occasional cleanup
            if (seen.Count > 5000)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-30).Ticks;
                foreach (var kv in seen.ToArray())
                {
                    if (kv.Value < cutoff)
                        seen.TryRemove(kv.Key, out _);
                }
            }

            foreach (var f in followers)
            {
                if (token.IsCancellationRequested) return;
                if (f == null) continue;
                if (f == master) continue;

                if (f.ConnectionStatus != ConnectionStatus.Connected)
                {
                    Disarm($"Follower {f.Name} not Connected (status={f.ConnectionStatus}).");
                    return;
                }

                var followerNet = GetNetPosition(f, instrument);
                var delta = masterTargetNet - followerNet;

                if (delta == 0) continue;

                if (Math.Abs(delta) > MaxAbsQtyPerFollower)
                    delta = Math.Sign(delta) * MaxAbsQtyPerFollower;

                var key = $"{execId}|{f.Name}|{instrument.FullName}";
                if (!seen.TryAdd(key, DateTime.UtcNow.Ticks))
                    continue;

                var action = delta > 0 ? OrderAction.Buy : OrderAction.SellShort;
                var qty = Math.Abs(delta);

                if (qty <= 0 || qty > MaxAbsQtyPerFollower)
                {
                    Disarm($"Safety stop: computed qty={qty} for follower={f.Name}");
                    return;
                }

                Log($"Copy -> {f.Name}: target={masterTargetNet}, followerNet={followerNet}, delta={delta}, action={action}, qty={qty}");

                var ord = f.CreateOrder(instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Day,
                    qty, 0, 0, string.Empty, $"STC:{execId}", DateTime.MaxValue, null);

                f.Submit(new[] { ord });
                RecordCopy();

                if (StaggerMsPerFollower > 0)
                    await Task.Delay(StaggerMsPerFollower, token).ConfigureAwait(false);
            }
        }

        private void OnFollowerOrderUpdate(object sender, OrderEventArgs e)
        {
            if (!armed) return;
            if (e?.Order == null) return;

            if (string.IsNullOrWhiteSpace(e.Order.Name) || !e.Order.Name.StartsWith("STC:", StringComparison.Ordinal))
                return;

            if (e.Order.OrderState == OrderState.Rejected)
            {
                var msg =
                    $"Error={e.Error} " +
                    $"State={e.Order.OrderState} " +
                    $"Action={e.Order.OrderAction} " +
                    $"Qty={e.Order.Quantity} " +
                    $"Name={e.Order.Name}";

                Disarm($"Circuit breaker: copied order REJECTED on {e.Order.Account?.Name}. Msg={msg}");
            }
        }
        
        private int SignedQtyFromExecution(Execution exec)
        {
            if (exec == null) return 0;

            var qty = (int)Math.Round((double)exec.Quantity, MidpointRounding.AwayFromZero);
            if (qty == 0) return 0;

            var action = exec.Order?.OrderAction ?? OrderAction.Buy;

            // Buy / BuyToCover increases net, Sell / SellShort decreases net
            if (action == OrderAction.Buy || action == OrderAction.BuyToCover)
                return Math.Abs(qty);

            if (action == OrderAction.Sell || action == OrderAction.SellShort)
                return -Math.Abs(qty);

            return 0;
        }

        private int GetNetPosition(Account acc, Instrument instr)
        {
            foreach (var p in acc.Positions)
            {
                if (p?.Instrument == null) continue;
                if (p.Instrument.FullName != instr.FullName) continue;

                var qty = (int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero);
                if (p.MarketPosition == MarketPosition.Short)
                    qty = -Math.Abs(qty);
                else if (p.MarketPosition == MarketPosition.Long)
                    qty = Math.Abs(qty);
                else
                    qty = 0;

                return qty;
            }

            return 0;
        }

        private bool AllowCopyNow()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-2).Ticks;
            while (copiedTicks.TryPeek(out var t) && t < cutoff)
                copiedTicks.TryDequeue(out _);

            return copiedTicks.Count <= MaxCopiesPer2Sec;
        }

        private void RecordCopy()
        {
            copiedTicks.Enqueue(DateTime.UtcNow.Ticks);
        }

        public void Log(string msg) => OnStatus?.Invoke(msg);

        public void Dispose()
        {
            Disarm("Dispose");
            submitLock.Dispose();
            cts.Dispose();
        }
    }
}