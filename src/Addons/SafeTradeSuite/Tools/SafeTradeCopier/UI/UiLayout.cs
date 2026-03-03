using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBlock _headerStateText;
        private TextBox _statusBox;
        private Button _btnCopyOn;
        private Button _btnCopyOff;

        private bool _readyState;
        private string _readyReason = "";
        
        // ---- V2 UI controls ----
        private TextBox _masterQtyBox;
        private ComboBox _masterAtmBox;
        private TextBlock _masterPnlText;
        private TextBlock _totalPnlText;

        private Button _btnBuyMkt;
        private Button _btnSellMkt;
        private Button _btnFlattenAll;
        private CheckBox _chkSimOnly;
        private bool _simOnlyMode = true; // default checked

        // follower rows
        private sealed class FollowerRow
        {
            public Account Account;

            // UI controls (current)
            public CheckBox EnabledCheck;
            public TextBox QtyOverrideBox;
            public ComboBox AtmOverrideBox;
            public TextBlock PnlText;
            public Button FlattenBtn;

            // ---- Compatibility wrappers (expected by Accounts.cs) ----
            public string AccountName => Account?.Name ?? "";
            public CheckBox IncludeCheck => EnabledCheck;

            // We are not using a dedicated override checkbox in the UI.
            // Accounts.cs will treat "override enabled" as: qty filled OR atm chosen.
            public CheckBox OverrideCheck => null;
            public TextBox QtyBox => QtyOverrideBox;
            public ComboBox AtmBox => AtmOverrideBox;
        }

        private readonly List<FollowerRow> _followerRows = new List<FollowerRow>();
        
        private UIElement BuildUi(SafeCopierEngine eng)
        {
            var root = new Grid
            {
                Margin = new Thickness(12),
                Background = SystemColors.WindowBrush
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // totals pnl
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // master
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // followers
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // copier buttons + status

            // ---------------- Header ----------------
            var headerArea = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 8) };

            // var header = new TextBlock
            // {
            //     Text = "Safe Trade Copier (v2)",
            //     FontSize = 16,
            //     Margin = new Thickness(0, 0, 0, 4),
            //     Foreground = SystemColors.WindowTextBrush
            // };
            
            _chkSimOnly = new CheckBox
            {
                Content = "Simulation Mode (Sim / Playback only)",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = SystemColors.WindowTextBrush
            };
            
            var accounts = GetSelectableAccounts();
            
            SubscribeUiAccountEvents(accounts);

            _chkSimOnly.Checked += (s, e) =>
            {
                _simOnlyMode = true;
                EnforceSimOnlyModeUi(accounts);
                ApplyConfigFromUi();
            };

            _chkSimOnly.Unchecked += (s, e) =>
            {
                _simOnlyMode = false;
                EnforceSimOnlyModeUi(accounts);
                ApplyConfigFromUi();
            };

            headerArea.Children.Add(_chkSimOnly);

            _headerStateText = new TextBlock
            {
                Text = "READY: ✗   |   COPY: OFF",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush
            };

            // headerArea.Children.Add(header);
            headerArea.Children.Add(_headerStateText);

            Grid.SetRow(headerArea, 0);
            root.Children.Add(headerArea);

            // ---------------- Totals PnL ----------------
            _totalPnlText = new TextBlock
            {
                Text = "TOTAL PnL  R: 0.00  U: 0.00",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(_totalPnlText, 1);
            root.Children.Add(_totalPnlText);

            // ---------------- Master group ----------------
            var masterBorder = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = SystemColors.ActiveBorderBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = SystemColors.WindowBrush
            };

            var masterStack = new StackPanel { Orientation = Orientation.Vertical };

            var masterTitle = new TextBlock
            {
                Text = "Master",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                Margin = new Thickness(0, 0, 0, 6)
            };

            _masterBox = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 0, 6) };
            _instrBox = new TextBox { Height = 28, Text = "NQ 03-26", Margin = new Thickness(0, 0, 0, 8) };

            // Order buttons row (Buy / Sell / Flatten All)
            var orderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnBuyMkt = new Button
            {
                Content = "Buy Market",
                Height = 36,
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.DodgerBlue,
                Foreground = Brushes.White
            };
            
            _btnSellMkt = new Button
            {
                Content = "Sell Market",
                Height = 36,
                Margin = new Thickness(6, 0, 6, 0),
                Background = Brushes.DodgerBlue,
                Foreground = Brushes.White
            };
            
            _btnFlattenAll = new Button
            {
                Content = "Flatten All",
                Height = 36,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Maroon,
                Foreground = Brushes.White
            };
            
            _btnFlattenAll.Click += (s, e) => FlattenAllSelected(eng);
            _btnBuyMkt.Click += (s, e) => SubmitMasterMarket(eng, isBuy: true);
            _btnSellMkt.Click += (s, e) => SubmitMasterMarket(eng, isBuy: false);

            Grid.SetColumn(_btnBuyMkt, 0);
            Grid.SetColumn(_btnSellMkt, 1);
            Grid.SetColumn(_btnFlattenAll, 2);
            orderRow.Children.Add(_btnBuyMkt);
            orderRow.Children.Add(_btnSellMkt);
            orderRow.Children.Add(_btnFlattenAll);

            // Master qty + ATM row
            var qaRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var qtyLbl = new TextBlock
            {
                Text = "Order qty:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = SystemColors.WindowTextBrush
            };
            _masterQtyBox = new TextBox { Height = 26, Text = "1", Margin = new Thickness(0, 0, 14, 0) };

            var atmLbl = new TextBlock
            {
                Text = "ATM:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = SystemColors.WindowTextBrush
            };
            _masterAtmBox = new ComboBox { Height = 26, MinWidth = 180 };

            Grid.SetColumn(qtyLbl, 0);
            Grid.SetColumn(_masterQtyBox, 1);
            Grid.SetColumn(atmLbl, 2);
            Grid.SetColumn(_masterAtmBox, 3);

            qaRow.Children.Add(qtyLbl);
            qaRow.Children.Add(_masterQtyBox);
            qaRow.Children.Add(atmLbl);
            qaRow.Children.Add(_masterAtmBox);

            _masterPnlText = new TextBlock
            {
                Text = "Master PnL  R: 0.00  U: 0.00",
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = SystemColors.WindowTextBrush,
                FontWeight = FontWeights.SemiBold
            };

            masterStack.Children.Add(masterTitle);
            masterStack.Children.Add(new TextBlock { Text = "Master account:", Foreground = SystemColors.WindowTextBrush });
            masterStack.Children.Add(_masterBox);
            masterStack.Children.Add(new TextBlock { Text = "Instrument:", Foreground = SystemColors.WindowTextBrush });
            masterStack.Children.Add(_instrBox);
            masterStack.Children.Add(orderRow);
            masterStack.Children.Add(qaRow);
            masterStack.Children.Add(_masterPnlText);

            masterBorder.Child = masterStack;
            Grid.SetRow(masterBorder, 2);
            root.Children.Add(masterBorder);

            // ---------------- Followers ----------------
            var followersBorder = new Border
            {
                BorderThickness = new Thickness(1),
                BorderBrush = SystemColors.ActiveBorderBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Background = SystemColors.WindowBrush
            };

            var followersStack = new StackPanel { Orientation = Orientation.Vertical };

            followersStack.Children.Add(new TextBlock
            {
                Text = "Followers (override qty/ATM per account)",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _followersPanel = new StackPanel { Orientation = Orientation.Vertical };
            var followersScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 220,
                Content = _followersPanel,
                Background = SystemColors.ControlLightBrush
            };
            followersStack.Children.Add(followersScroll);

            followersBorder.Child = followersStack;
            Grid.SetRow(followersBorder, 3);
            root.Children.Add(followersBorder);

            // ---------------- Copier buttons + Status ----------------
            var bottom = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 10, 0, 0) };

            _btnCopyOn = new Button
            {
                Content = "COPY ON",
                Height = 44,
                Background = Brushes.DarkGreen,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _btnCopyOff = new Button
            {
                Content = "COPY OFF",
                Height = 44,
                Background = Brushes.Maroon,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var btnClose = new Button
            {
                Content = "Close",
                Height = 44,
                Background = Brushes.DimGray,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 6)
            };

            _statusBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 140,
                Background = SystemColors.ControlLightBrush,
                Foreground = SystemColors.ControlTextBrush
            };

            bottom.Children.Add(_btnCopyOn);
            bottom.Children.Add(_btnCopyOff);
            bottom.Children.Add(btnClose);
            bottom.Children.Add(new TextBlock { Text = "Status:", Foreground = SystemColors.WindowTextBrush });
            bottom.Children.Add(_statusBox);

            Grid.SetRow(bottom, 4);
            root.Children.Add(bottom);

            // ---------------- Hook engine events ----------------
            eng.OnStatus += (msg) =>
            {
                var disp = _uiDispatcher ?? _window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    _statusBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
                    _statusBox.ScrollToEnd();
                });
            };

            eng.OnReadyChanged += (ready, reason) =>
            {
                var disp = _uiDispatcher ?? _window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    _readyState = ready;
                    _readyReason = reason ?? "";
                    RenderHeader(eng.CopyEnabled);
                    RenderButtons(eng.CopyEnabled);
                });
            };

            eng.OnModeChanged += (armedIgnored, copyOn) =>
            {
                var disp = _uiDispatcher ?? _window?.Dispatcher;
                if (disp == null) return;

                disp.InvokeAsync(() =>
                {
                    RenderHeader(copyOn);
                    RenderButtons(copyOn);
                });
            };

            void RenderButtons(bool copyOn)
            {
                _btnCopyOn.IsEnabled = !copyOn;
                _btnCopyOff.IsEnabled = copyOn;
                _btnCopyOn.Content = copyOn ? "COPY ON (active)" : "COPY ON";
            }

            void RenderHeader(bool copyOn)
            {
                var symbol = _readyState ? "✓" : "✗";
                var readyLabel = _readyState ? "READY" : $"NOT READY ({_readyReason})";
                _headerStateText.Text = $"{readyLabel}: {symbol}   |   COPY: {(copyOn ? "ON" : "OFF")}";
            }

            // ---------------- Populate accounts + followers ----------------
            _masterBox.ItemsSource = accounts;
            _masterBox.DisplayMemberPath = "Name";
            _masterBox.SelectedItem = accounts.FirstOrDefault();

            BuildFollowerRows(accounts);
            EnforceSimOnlyModeUi(accounts);

            // ATMs
            LoadAtmTemplatesInto(_masterAtmBox, includeInherit: false);
            foreach (var r in _followerRows)
                LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

            // ---------------- UI -> Engine wiring ----------------
            void ApplyAndMaybeRewire()
            {
                ApplyConfigFromUi();
                if (eng.CopyEnabled)
                    eng.SetCopyEnabled(true);
            }

            _masterBox.SelectionChanged += (s, e) =>
            {
                RebuildFollowersAndRewire(eng, accounts);
            };
            _masterBox.DropDownOpened += (s, e) => UpdateMasterComboItemEnablement();
            _instrBox.TextChanged += (s, e) => ApplyAndMaybeRewire();
            _masterQtyBox.TextChanged += (s, e) => ApplyAndMaybeRewire();
            _masterAtmBox.SelectionChanged += (s, e) => ApplyAndMaybeRewire();

            _btnCopyOn.Click += (s, e) =>
            {
                ApplyConfigFromUi();
                eng.SetCopyEnabled(true);
            };

            _btnCopyOff.Click += (s, e) => eng.SetCopyEnabled(false);

            btnClose.Click += (s, e) =>
            {
                HardClose();
            };
            
            WireFollowerFlattenButtons(eng);

            // initial config
            ApplyConfigFromUi();
            RenderHeader(copyOn: false);
            RenderButtons(copyOn: false);
            
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };
        }
        
        private void WireFollowerFlattenButtons(SafeCopierEngine eng)
        {
            foreach (var r in _followerRows)
            {
                if (r?.FlattenBtn == null) continue;

                r.FlattenBtn.Click += (s, e) =>
                {
                    if (eng == null) return;
                    if (r.Account == null) return;

                    var instrName = (_instrBox?.Text ?? "").Trim();
                    var instr = string.IsNullOrWhiteSpace(instrName) ? null : Instrument.GetInstrument(instrName);
                    if (instr == null)
                    {
                        eng.Log("Invalid instrument (must match NT instrument exactly).");
                        return;
                    }

                    eng.EnsureFlatInstrument(r.Account, instr);
                    eng.Log($"Flatten submitted -> {r.Account.Name} ({instr.FullName})");
                };
            }
        }
        
        private void BuildFollowerRows(List<Account> accounts)
        {
            _followerRows.Clear();
            _followersPanel.Children.Clear();

            var master = _masterBox?.SelectedItem as Account;
            var masterName = master?.Name ?? "";

            foreach (var acc in accounts)
            {
                if (!string.IsNullOrWhiteSpace(masterName) && acc.Name == masterName)
                    continue;
                
                var rowGrid = new Grid { Margin = new Thickness(2, 2, 2, 2) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // checkbox
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });                    // qty
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });                   // atm
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });                   // pnl
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });                    // flatten
                
                var enabled = new CheckBox
                {
                    Content = acc.Name,
                    Tag = acc,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = SystemColors.ControlTextBrush
                };

                var qtyBox = new TextBox
                {
                    Height = 24,
                    Margin = new Thickness(6, 0, 6, 0),
                    ToolTip = "Qty override (blank = inherit master)"
                };

                var atmBox = new ComboBox
                {
                    Height = 24,
                    Margin = new Thickness(6, 0, 6, 0),
                    MinWidth = 160
                };

                var pnl = new TextBlock
                {
                    Text = "R: 0.00  U: 0.00",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = SystemColors.ControlTextBrush,
                    Margin = new Thickness(6, 0, 6, 0)
                };

                var flatten = new Button
                {
                    Content = "Flatten",
                    Height = 24,
                    Background = Brushes.Maroon,
                    Foreground = Brushes.White,
                    IsEnabled = false
                };
                
                var allow = !_simOnlyMode || IsSimAccount(acc);

                enabled.IsEnabled = allow;
                if (!allow) enabled.IsChecked = false;

                qtyBox.IsEnabled = allow;
                atmBox.IsEnabled = allow;

                // flatten stays disabled by default; PnL timer/net-position logic enables it when needed
                flatten.IsEnabled = false;

                Grid.SetColumn(enabled, 0);
                Grid.SetColumn(qtyBox, 1);
                Grid.SetColumn(atmBox, 2);
                Grid.SetColumn(pnl, 3);
                Grid.SetColumn(flatten, 4);

                rowGrid.Children.Add(enabled);
                rowGrid.Children.Add(qtyBox);
                rowGrid.Children.Add(atmBox);
                rowGrid.Children.Add(pnl);
                rowGrid.Children.Add(flatten);

                var row = new FollowerRow
                {
                    Account = acc,
                    EnabledCheck = enabled,
                    QtyOverrideBox = qtyBox,
                    AtmOverrideBox = atmBox,
                    PnlText = pnl,
                    FlattenBtn = flatten
                };

                // When user changes follower settings, we re-apply config (no re-arm UX)
                enabled.Checked += (s, e) => ApplyConfigFromUi();
                enabled.Unchecked += (s, e) => ApplyConfigFromUi();
                qtyBox.TextChanged += (s, e) => ApplyConfigFromUi();
                atmBox.SelectionChanged += (s, e) => ApplyConfigFromUi();

                _followerRows.Add(row);
                _followersPanel.Children.Add(rowGrid);
            }
        }
    }
}