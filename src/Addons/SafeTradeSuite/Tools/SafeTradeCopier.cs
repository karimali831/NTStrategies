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

using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public sealed class SafeTradeCopierTool : IDisposable
    {
        private Window window;
        private SafeCopierEngine engine;

        private bool allowWindowClose;

        public void Show()
        {
            if (window != null)
            {
                if (!window.IsVisible)
                    window.Show();

                window.WindowState = WindowState.Normal;
                window.Activate();
                return;
            }

            engine = new SafeCopierEngine();

            window = new Window
            {
                Title = "Safe Trade Copier (v1)",
                Width = 520,
                Height = 520,
                Content = BuildUi(engine),
                Background = Brushes.DimGray,
                Foreground = Brushes.White
            };

            window.Closing += (s, e) =>
            {
                if (allowWindowClose) return;
                e.Cancel = true;
                window.Hide();
            };

            window.Show();
        }

        private UIElement BuildUi(SafeCopierEngine eng)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock
            {
                Text = "Execution-based copier with circuit-breaker",
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var row1 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            var masterBox = new ComboBox { Height = 26, Margin = new Thickness(0, 0, 0, 6) };
            var instrBox = new TextBox { Height = 26, Text = "NQ 03-26", Margin = new Thickness(0, 0, 0, 6) };

            var accounts = Account.All.ToList();
            masterBox.ItemsSource = accounts;
            masterBox.DisplayMemberPath = "Name";
            masterBox.SelectedItem = accounts.FirstOrDefault();

            row1.Children.Add(new TextBlock { Text = "Master account:" });
            row1.Children.Add(masterBox);
            row1.Children.Add(new TextBlock { Text = "Instrument (exact NT name):" });
            row1.Children.Add(instrBox);

            Grid.SetRow(row1, 1);
            root.Children.Add(row1);

            var row2 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            var followerList = new ListBox
            {
                Height = 140,
                SelectionMode = SelectionMode.Multiple,
                ItemsSource = accounts,
                DisplayMemberPath = "Name"
            };

            row2.Children.Add(new TextBlock { Text = "Followers (multi-select):" });
            row2.Children.Add(followerList);

            Grid.SetRow(row2, 2);
            root.Children.Add(row2);

            var row3 = new StackPanel { Orientation = Orientation.Vertical };

            var btnArm = new Button { Content = "ARM (required)", Height = 34, Background = Brushes.SteelBlue, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) };
            var btnOn = new Button { Content = "COPY ON", Height = 34, Background = Brushes.DarkGreen, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) };
            var btnOff = new Button { Content = "DISARM (panic)", Height = 34, Background = Brushes.Maroon, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 6) };

            var btnQuit = new Button
            {
                Content = "QUIT (close & dispose)",
                Height = 34,
                Background = Brushes.DarkSlateGray,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            btnQuit.Click += (s, e) =>
            {
                eng.Disarm("Quit");
                eng.Dispose(); 

                engine = null;

                allowWindowClose = true;
                window.Close(); 
                window = null;
                allowWindowClose = false;
            };

            var maxQtyBox = new TextBox { Height = 26, Text = "2", Margin = new Thickness(0, 6, 0, 6) };
            var staggerMsBox = new TextBox { Height = 26, Text = "125", Margin = new Thickness(0, 0, 0, 6) };
            var longOnly = new CheckBox { Content = "Long-only (never create shorts)", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };

            var status = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 120,
                Background = Brushes.Black,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 10, 0, 0)
            };

            eng.OnStatus += (msg) =>
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null) return;

                disp.Invoke(() =>
                {
                    status.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
                    status.ScrollToEnd();
                });
            };

            btnArm.Click += (s, e) =>
            {
                var master = masterBox.SelectedItem as Account;
                var followers = followerList.SelectedItems.Cast<Account>().Where(a => a != null).ToList();

                if (master == null)
                {
                    eng.Log("Select a master account.");
                    return;
                }

                if (followers.Count == 0)
                {
                    eng.Log("Select at least one follower.");
                    return;
                }

                var maxQty = ParseInt(maxQtyBox.Text, 2);
                var staggerMs = ParseInt(staggerMsBox.Text, 125);

                eng.Configure(
                    masterAccount: master,
                    followerAccounts: followers,
                    instrumentName: instrBox.Text?.Trim(),
                    maxAbsQtyPerFollower: Math.Max(1, maxQty),
                    staggerMsPerFollower: Math.Max(0, staggerMs),
                    longOnly: longOnly.IsChecked == true
                );

                eng.Arm();
            };

            btnOn.Click += (s, e) => eng.EnableCopying();
            btnOff.Click += (s, e) => eng.Disarm("Manual DISARM");

            row3.Children.Add(new TextBlock { Text = "Max abs qty per follower (hard cap):" });
            row3.Children.Add(maxQtyBox);
            row3.Children.Add(new TextBlock { Text = "Stagger ms per follower:" });
            row3.Children.Add(staggerMsBox);
            row3.Children.Add(longOnly);
            row3.Children.Add(btnArm);
            row3.Children.Add(btnOn);
            row3.Children.Add(btnOff);
            row3.Children.Add(btnQuit);
            row3.Children.Add(new TextBlock { Text = "Status:" });
            row3.Children.Add(status);

            Grid.SetRow(row3, 3);
            root.Children.Add(row3);

            return root;
        }

        private int ParseInt(string s, int fallback)
        {
            if (int.TryParse(s, out var v)) return v;
            return fallback;
        }

        public void Dispose()
        {
            engine?.Dispose();
            engine = null;

            if (window != null)
            {
                allowWindowClose = true;
                window.Close();
                allowWindowClose = false;
                window = null;
            }
        }
    }

    internal sealed class SafeCopierEngine : IDisposable
    {
        private Account master;
        private List<Account> followers = new List<Account>();
        private string instrumentName;
        private Instrument instrument;
        private int maxAbsQtyPerFollower = 2;
        private int staggerMsPerFollower = 125;
        private bool longOnly = true;

        private volatile bool armed;
        private volatile bool copyEnabled;
        private readonly object gate = new object();

        private readonly ConcurrentDictionary<string, long> seen = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentQueue<long> copiedTicks = new ConcurrentQueue<long>();
        private int maxCopiesPer2Sec = 20;

        private readonly SemaphoreSlim submitLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource cts = new CancellationTokenSource();

        public event Action<string> OnStatus;

        public void Configure(Account masterAccount, List<Account> followerAccounts, string instrumentName,
            int maxAbsQtyPerFollower, int staggerMsPerFollower, bool longOnly)
        {
            Disarm("Reconfigure");

            master = masterAccount;
            followers = followerAccounts?.Where(a => a != null).Distinct().ToList() ?? new List<Account>();
            this.instrumentName = instrumentName ?? "";
            this.maxAbsQtyPerFollower = maxAbsQtyPerFollower;
            this.staggerMsPerFollower = staggerMsPerFollower;
            this.longOnly = longOnly;

            instrument = string.IsNullOrWhiteSpace(this.instrumentName)
                ? null
                : Instrument.GetInstrument(this.instrumentName);

            Log($"Configured. Master={master?.Name}, Followers={followers.Count}, Instr='{this.instrumentName}', MaxQty={this.maxAbsQtyPerFollower}, StaggerMs={this.staggerMsPerFollower}, LongOnly={this.longOnly}");
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
                armed = true;
                copyEnabled = false;

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
            Log("COPY ON.");
        }

        public void Disarm(string reason)
        {
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

                cts.Cancel();
                cts.Dispose(); 
                cts = new CancellationTokenSource();

                seen.Clear();
                while (copiedTicks.TryDequeue(out _)) { }

                if (!string.IsNullOrWhiteSpace(reason))
                    Log($"DISARMED: {reason}");
            }
        }

        private void OnMasterExecution(object sender, ExecutionEventArgs e)
        {
            try
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

                var masterNet = GetNetPosition(master, instrument);
                if (longOnly && masterNet < 0)
                    masterNet = 0;

                if (!AllowCopyNow())
                {
                    Disarm("Circuit breaker: too many copied orders in short window");
                    return;
                }

                _ = Task.Run(async () =>
                {
                    await submitLock.WaitAsync(cts.Token).ConfigureAwait(false);
                    try
                    {
                        await CopyToFollowers(execId, masterNet, cts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        submitLock.Release();
                    }
                }, cts.Token);
            }
            catch (Exception ex)
            {
                Disarm("Exception in OnMasterExecution: " + ex.Message);
            }
        }

        private async Task CopyToFollowers(string execId, int masterTargetNet, CancellationToken token)
        {
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

                if (longOnly)
                {
                    var desiredNet = followerNet + delta;
                    if (desiredNet < 0)
                        delta = -followerNet;
                }

                if (delta == 0) continue;

                if (Math.Abs(delta) > maxAbsQtyPerFollower)
                    delta = Math.Sign(delta) * maxAbsQtyPerFollower;

                var key = $"{execId}|{f.Name}|{instrument.FullName}";
                if (!seen.TryAdd(key, DateTime.UtcNow.Ticks))
                    continue;

                var action = delta > 0 ? OrderAction.Buy : OrderAction.SellShort;
                var qty = Math.Abs(delta);

                if (longOnly && action == OrderAction.SellShort)
                    continue;

                var name = $"STC:{execId}";
                var ord = f.CreateOrder(instrument, action, OrderType.Market, OrderEntry.Manual, TimeInForce.Day,
                    qty, 0, 0, string.Empty, name, DateTime.MaxValue, null);

                if (qty <= 0 || qty > maxAbsQtyPerFollower)
                {
                    Disarm($"Safety stop: computed qty={qty} for follower={f.Name}");
                    return;
                }

                Log($"Copy -> {f.Name}: target={masterTargetNet}, followerNet={followerNet}, delta={delta}, action={action}, qty={qty}");

                f.Submit(new[] { ord });
                RecordCopy();

                if (staggerMsPerFollower > 0)
                    await Task.Delay(staggerMsPerFollower, token).ConfigureAwait(false);
            }
        }

        private void OnFollowerOrderUpdate(object sender, OrderEventArgs e)
        {
            try
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

                    var lower = msg.ToLowerInvariant();

                    if (lower.Contains("rate limit") ||
                        lower.Contains("liquidation") ||
                        lower.Contains("max") ||
                        lower.Contains("risk") ||
                        lower.Contains("position") ||
                        lower.Contains("order quantity"))
                    {
                        Disarm($"Circuit breaker: copied order REJECTED on {e.Order.Account?.Name}. Msg={msg}");
                        return;
                    }

                    Disarm($"Circuit breaker: copied order REJECTED on {e.Order.Account?.Name}.");
                }
            }
            catch
            {
                Disarm("Exception in follower OrderUpdate monitor");
            }
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

            return copiedTicks.Count <= maxCopiesPer2Sec;
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