#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools
{
    public partial class SafeTradeCopierTool : IDisposable
    {
        private Button btnBuyMkt;
        private Button btnSellMkt;
        private TextBox qtyBox;
        private ComboBox atmBox;
        
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

            // ---------------- Header ----------------
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

            headerStateText = new TextBlock
            {
                Text = "READY: ✗   |   COPY: OFF",
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush
            };

            headerArea.Children.Add(header);
            headerArea.Children.Add(headerStateText);

            Grid.SetRow(headerArea, 0);
            root.Children.Add(headerArea);

            // ---------------- Master + Instrument ----------------
            var row1 = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 10) };

            masterBox = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
            instrBox = new TextBox { Height = 28, Text = "NQ 03-26", Margin = new Thickness(0, 0, 0, 6) };

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

            BuildFollowerCheckboxes(accounts);

            row2.Children.Add(followersScroll);

            Grid.SetRow(row2, 2);
            root.Children.Add(row2);

            // ---------------- Controls + Status ----------------
            var row3 = new StackPanel { Orientation = Orientation.Vertical };

            btnCopyOn = new Button
            {
                Content = "Trade Copier ON",
                Height = 44,
                Background = Brushes.DarkGreen,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 6, 0, 6)
            };

            btnCopyOff = new Button
            {
                Content = "Trade Copier OFF",
                Height = 44,
                Background = Brushes.Maroon,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var btnQuit = new Button
            {
                Content = "Close",
                Height = 44,
                Background = Brushes.DimGray,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            statusBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 160,
                Background = SystemColors.ControlLightBrush,
                Foreground = SystemColors.ControlTextBrush,
                Margin = new Thickness(0, 10, 0, 0)
            };

            // Engine -> UI events
            eng.OnStatus += (msg) =>
            {
                var disp = uiDispatcher ?? window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    statusBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
                    statusBox.ScrollToEnd();
                });
            };

            eng.OnReadyChanged += (ready, reason) =>
            {
                var disp = uiDispatcher ?? window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    readyState = ready;
                    readyReason = reason ?? "";
                    RenderHeader(eng.CopyEnabled);
                    RenderButtons(eng.CopyEnabled);
                });
            };

            eng.OnModeChanged += (armedIgnored, copyOn) =>
            {
                var disp = uiDispatcher ?? window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    RenderHeader(copyOn);
                    RenderButtons(copyOn);
                });
            };

            // Helpers
            List<Account> GetSelectedFollowers(Account master)
            {
                return followerCheckboxes
                    .Where(cb => cb.IsChecked == true)
                    .Select(cb => cb.Tag as Account)
                    .Where(a => a != null && master != null && !ReferenceEquals(a, master))
                    .ToList();
            }

            void ApplyCurrentConfig()
            {
                var master = masterBox.SelectedItem as Account;
                if (master == null)
                {
                    eng.ApplyConfig(null, new List<Account>(), instrBox.Text?.Trim());
                    return;
                }

                var followers = GetSelectedFollowers(master);
                eng.ApplyConfig(master, followers, instrBox.Text?.Trim());
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
                        cb.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        cb.Visibility = Visibility.Visible;
                    }
                }
            }

            void HookFollowerChangeHandlers()
            {
                foreach (var cb in followerCheckboxes)
                {
                    cb.Checked += (s, e) =>
                    {
                        ApplyCurrentConfig();
                        if (eng.CopyEnabled)
                            eng.SetCopyEnabled(true); // rewire immediately if needed
                    };

                    cb.Unchecked += (s, e) =>
                    {
                        ApplyCurrentConfig();
                        if (eng.CopyEnabled)
                            eng.SetCopyEnabled(true); // rewire immediately if needed
                    };
                }
            }
            
            // ---------------- Manual order controls (Master) ----------------
            var orderGrid = new Grid { Margin = new Thickness(0, 6, 0, 8) };
            orderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            orderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnBuyMkt = new Button
            {
                Content = "BUY MKT",
                Height = 34,
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.DimGray,
                Foreground = Brushes.White
            };

            btnSellMkt = new Button
            {
                Content = "SELL MKT",
                Height = 34,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.DimGray,
                Foreground = Brushes.White
            };

            Grid.SetColumn(btnBuyMkt, 0);
            Grid.SetColumn(btnSellMkt, 1);
            orderGrid.Children.Add(btnBuyMkt);
            orderGrid.Children.Add(btnSellMkt);

            var qtyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            qtyRow.Children.Add(new TextBlock
            {
                Text = "Order qty:",
                Width = 90,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SystemColors.WindowTextBrush
            });

            qtyBox = new TextBox
            {
                Height = 26,
                Width = 80,
                Text = "1",
                Margin = new Thickness(0, 0, 12, 0)
            };
            qtyRow.Children.Add(qtyBox);

            qtyRow.Children.Add(new TextBlock
            {
                Text = "ATM template:",
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = SystemColors.WindowTextBrush
            });

            atmBox = new ComboBox
            {
                Height = 26,
                MinWidth = 180
            };
            qtyRow.Children.Add(atmBox);

            // Add into row3 stack
            row3.Children.Add(orderGrid);
            row3.Children.Add(qtyRow);

            void RenderButtons(bool copyOn)
            {
                btnCopyOn.IsEnabled = !copyOn;
                btnCopyOff.IsEnabled = copyOn;
                btnCopyOn.Content = copyOn ? "COPY ON (active)" : "COPY ON";
            }

            void RenderHeader(bool copyOn)
            {
                var symbol = readyState ? "✓" : "✗";
                var readyLabel = readyState ? "READY" : $"NOT READY ({readyReason})";
                headerStateText.Text = $"{readyLabel}: {symbol}   |   COPY: {(copyOn ? "ON" : "OFF")}";
            }

            // Initial render
            ApplyMasterExclusion();
            HookFollowerChangeHandlers();
            ApplyCurrentConfig(); // will raise ready state

            RenderHeader(copyOn: false);
            RenderButtons(copyOn: false);

            // UI -> Engine actions
            btnCopyOn.Click += (s, e) =>
            {
                ApplyMasterExclusion();
                ApplyCurrentConfig();
                eng.SetCopyEnabled(true);
            };

            btnCopyOff.Click += (s, e) =>
            {
                eng.SetCopyEnabled(false);
            };

            btnQuit.Click += (s, e) =>
            {
                // true close + dispose (not hide)
                allowWindowClose = true;
                Dispose();
                // Dispose() will close the window if needed
                allowWindowClose = false;
            };

            masterBox.SelectionChanged += (s, e) =>
            {
                ApplyMasterExclusion();
                ApplyCurrentConfig();
                if (eng.CopyEnabled)
                    eng.SetCopyEnabled(true); // rewire to new master/followers
            };

            instrBox.TextChanged += (s, e) =>
            {
                ApplyCurrentConfig();
                if (eng.CopyEnabled)
                    eng.SetCopyEnabled(true); // rewire if needed (instrument matters)
            };

            row3.Children.Add(btnCopyOn);
            row3.Children.Add(btnCopyOff);
            row3.Children.Add(btnQuit);
            row3.Children.Add(new TextBlock { Text = "Status:", Foreground = SystemColors.WindowTextBrush });
            row3.Children.Add(statusBox);

            Grid.SetRow(row3, 3);
            root.Children.Add(row3);
            
            LoadAtmTemplatesInto(atmBox);
            WireOrderButtons(eng, instrBox);

            if (accounts.Count == 0)
                eng.Log("No accounts detected. Connect in Control Center first.");

            return root;

            // local function
            void BuildFollowerCheckboxes(List<Account> accs)
            {
                followersPanel.Children.Clear();
                followerCheckboxes.Clear();

                foreach (var acc in accs)
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
            }
        }
    }
}