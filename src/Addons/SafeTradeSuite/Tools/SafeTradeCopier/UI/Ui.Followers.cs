using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Style _circularCheckBoxStyle;
        
         private void RebuildFollowersAndRewire(SafeCopierEngine eng, List<Account> accounts)
        {
            // preserve follower selections by account name (optional but nice)
            var selected = new HashSet<string>(
                _followerRows.Where(r => r?.EnabledCheck?.IsChecked == true && r.Account != null).Select(r => r.Account.Name),
                StringComparer.Ordinal);

            // rebuild rows (excludes current master)
            BuildFollowerRows(accounts);

            // restore selections
            foreach (var r in _followerRows)
            {
                if (r?.Account == null) continue;
                if (r.EnabledCheck == null) continue;
                r.EnabledCheck.IsChecked = selected.Contains(r.Account.Name);
            }

            // sim-only enforcement after rebuild
            EnforceSimOnlyModeUi(accounts);

            // reload ATMs into NEW combo instances
            foreach (var r in _followerRows)
                LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

            // rewire follower flatten button handlers (NEW button instances)
            WireFollowerFlattenButtons(eng);
            WireFollowerFreeTradeButtons(eng);

            // update engine config
            ApplyConfigFromUi();

            if (eng.CopyEnabled)
                eng.SetCopyEnabled(true);
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

                    var instr = GetInstrument();
                    if (instr == null)
                    {
                        eng.Log("Invalid instrument.");
                        return;
                    }
                    
                    if (r.PnlBar != null)
                        r.PnlBar.Tag = "ORDER_FILLED";
                    
                    eng.EnsureFlatInstrument(r.Account, instr);
                    eng.Log($"Flatten submitted -> {r.Account.Name} ({instr.FullName})");
                };
            }
        }
        
        private void WireFollowerFreeTradeButtons(SafeCopierEngine eng)
        {
            foreach (var r in _followerRows)
            {
                if (r?.FreeTradeBtn == null)
                    continue;

                r.FreeTradeBtn.Click += (s, e) =>
                {
                    if (eng == null || r.Account == null)
                        return;

                    var instr = GetInstrument();
                    if (instr == null)
                    {
                        eng.Log("Invalid instrument.");
                        return;
                    }

                    if (_freeTradeMinProfitPoints <= 0)
                    {
                        eng.Log("Free Trade disabled in Settings.");
                        return;
                    }

                    if (eng.CanUndoFreeTrade(r.Account, instr, out _))
                    {
                        if (eng.UndoFreeTrade(r.Account, instr))
                            eng.Log($"Free Trade undone -> {r.Account.Name} ({instr.FullName})");
                    }
                    else
                    {
                        if (eng.ApplyFreeTrade(r.Account, instr, _freeTradeMinProfitPoints))
                            eng.Log($"Free Trade applied -> {r.Account.Name} ({instr.FullName})");
                    }
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
                
                var rowGrid = new Grid
                {
                    Margin = new Thickness(2, 2, 2, 2),
                    Background = TableRowAltBrush()
                };
                
                FollowerHeaderColumnDefinitions(rowGrid);
                
                var statusDot = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = DotBorderBrush(),
                    Background = DotOffBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
              
                var enabled = new CheckBox
                {
                    Content = null,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = acc
                };

                ApplyCircularCheckBoxStyle(enabled);
                enabled.ToolTip = "Enable follower";
                
                var accountText = new TextBlock
                {
                    Text = acc.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
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
                    MinWidth = 125
                };

                var pnl = new TextBlock
                {
                    Text = "R $0.00   •   U $0.00",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.DimGray,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    Margin = new Thickness(6, 0, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                
                var pnlBar = new ProgressBar
                {
                    Height = 10,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0,
                    Width = 160,
                    Margin = new Thickness(6, 2, 6, 0),
                    Visibility = Visibility.Collapsed
                };
                EnsureRoundedProgressBar(pnlBar, alignRight: false);
                
                var pnlBarStatusText = new TextBlock
                {
                    Text = "",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(6, 0, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Visibility = Visibility.Collapsed
                };

                // Flatten
                var flatten = new Button
                {
                    Content = "❌",
                    Height = 24,
                    Width = 20,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderFlattenButtonState(flatten, enabled: false);
                
                // Free trade
                var freeTrade = new Button
                {
                    Content = "✓",
                    Height = 24,
                    Width = 20,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderFreeTradeButtonState(freeTrade, enabled: false, undoMode: false, "✔");
                
                var allow = !_simOnlyMode || IsSimAccount(acc);

                enabled.IsEnabled = allow;
                if (!allow) enabled.IsChecked = false;

                qtyBox.IsEnabled = allow;
                atmBox.IsEnabled = allow;

                // flatten stays disabled by default; PnL timer/net-position logic enables it when needed
                flatten.IsEnabled = false;

                var pnlStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin = new Thickness(0)
                };

                Grid.SetColumn(statusDot, 0);
                Grid.SetColumn(enabled, 1);
                Grid.SetColumn(accountText, 2);
                Grid.SetColumn(qtyBox, 3);
                Grid.SetColumn(atmBox, 4);
                Grid.SetColumn(pnlStack, 5);
                Grid.SetColumn(freeTrade, 6);
                Grid.SetColumn(flatten, 7);

                pnlStack.Children.Add(pnl);
                pnlStack.Children.Add(pnlBar);
                pnlStack.Children.Add(pnlBarStatusText);

                rowGrid.Children.Add(statusDot);
                rowGrid.Children.Add(enabled);
                rowGrid.Children.Add(accountText);
                rowGrid.Children.Add(qtyBox);
                rowGrid.Children.Add(atmBox);
                rowGrid.Children.Add(pnlStack);
                rowGrid.Children.Add(freeTrade);
                rowGrid.Children.Add(flatten);

                var row = new FollowerRow
                {
                    Account = acc,
                    StatusDot = statusDot,
                    EnabledCheck = enabled,
                    AccountText = accountText,
                    QtyOverrideBox = qtyBox,
                    AtmOverrideBox = atmBox,
                    PnlText = pnl,
                    PnlBar = pnlBar,
                    FlattenBtn = flatten,
                    FreeTradeBtn = freeTrade,
                    PnlBarStatusText = pnlBarStatusText,
                };
                
                // When user changes follower settings, we re-apply config (no re-arm UX)
                enabled.Checked += (s, e) =>
                {
                    if (_suppressSessionUiEvents) return;
                    RefreshStatusBar();
                    RenderFollowerRowState(row);
                    SaveUiToActiveSession();
                    ApplyConfigFromUi();
                };

                enabled.Unchecked += (s, e) =>
                {
                    if (_suppressSessionUiEvents) return;
                    RefreshStatusBar();
                    RenderFollowerRowState(row);
                    SaveUiToActiveSession();
                    ApplyConfigFromUi();
                };
                
                qtyBox.TextChanged += (s, e) =>
                {
                    if (_suppressSessionUiEvents) return;
                    SaveUiToActiveSession();
                    ApplyConfigFromUi();
                };

                atmBox.SelectionChanged += (s, e) =>
                {
                    if (_suppressSessionUiEvents) return;
                    SaveUiToActiveSession();
                    ApplyConfigFromUi();
                };

                RenderFollowerRowState(row);
                
                qtyBox.VerticalContentAlignment = VerticalAlignment.Center;
                atmBox.VerticalContentAlignment = VerticalAlignment.Center;
                flatten.VerticalAlignment = VerticalAlignment.Center;

                _followerRows.Add(row);
                _followersPanel.Children.Add(rowGrid);
            }
        }

        private static void FollowerHeaderColumnDefinitions(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });   // Status
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });   // On
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });  // Account
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  // Override Qty
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });  // Override ATM
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });  // PnL
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });   // BE
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // Flatten
        }
        
        private static Grid BuildFollowerHeaderRow()
        {
            var g = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4),
                Background = TableHeaderBrush()
            };

            FollowerHeaderColumnDefinitions(g);
            
            AddHeaderText(g, "Status", 0);
            AddHeaderText(g, "On", 1);
            AddHeaderText(g, "Account", 2);
            AddHeaderText(g, "Override Qty", 3);
            AddHeaderText(g, "Override ATM", 4);
            AddHeaderText(g, "PnL", 5);
            AddHeaderText(g, "Free Trade", 6);
            AddHeaderText(g, "Flatten", 7);

            return g;
        }

        private static void AddHeaderText(Grid g, string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }
        
        private static void ApplyCircularCheckBoxStyle(CheckBox cb)
        {
            if (cb == null) return;

            if (_circularCheckBoxStyle == null)
                _circularCheckBoxStyle = BuildCircularCheckBoxStyle();

            cb.Style = _circularCheckBoxStyle;
        }
        
         private static Style BuildCircularCheckBoxStyle()
        {
            var style = new Style(typeof(CheckBox));

            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 18.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 18.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));

            var template = new ControlTemplate(typeof(CheckBox));

            var root = new FrameworkElementFactory(typeof(Grid))
            {
                Name = "Root"
            };
            root.SetValue(FrameworkElement.WidthProperty, 18.0);
            root.SetValue(FrameworkElement.HeightProperty, 18.0);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var border = new FrameworkElementFactory(typeof(Border))
            {
                Name = "Dot"
            };
            border.SetValue(FrameworkElement.WidthProperty, 14.0);
            border.SetValue(FrameworkElement.HeightProperty, 14.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, DotBorderBrush());
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            root.AppendChild(border);
            template.VisualTree = root;

            var checkedTrigger = new Trigger
            {
                Property = ToggleButton.IsCheckedProperty,
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotConnectedOnBrush(), "Dot"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotConnectedOnBrush(), "Dot"));

            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Root"));

            template.Triggers.Add(checkedTrigger);
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
        
        private void RenderFollowerRowState(FollowerRow row)
        {
            if (row?.Account == null) return;

            var connState = GetUiConnectionState(row.Account);
            var connected = connState == UiConnectionState.Connected;
            var warning = connState == UiConnectionState.Warning;
            var disconnected = connState == UiConnectionState.Disconnected;

            var isChecked = row.EnabledCheck?.IsChecked == true;
            var isArmed = _engine?.CopyEnabled == true;

            if (row.StatusDot != null)
            {
                row.StatusDot.Visibility = Visibility.Visible;
                row.StatusDot.BorderBrush = DotBorderBrush();

                if (disconnected)
                {
                    row.StatusDot.Background = DotDisconnectedBrush();
                    row.StatusDot.ToolTip = "Disconnected";
                }
                else if (warning)
                {
                    row.StatusDot.Background = DotWarningBrush();
                    row.StatusDot.ToolTip = "Connecting or reconnecting";
                }
                else if (connected && isChecked && isArmed)
                {
                    row.StatusDot.Background = DotConnectedOnBrush();
                    row.StatusDot.ToolTip = "Connected";
                }
                else if (connected && isChecked && !isArmed)
                {
                    row.StatusDot.Background = DotWarningBrush();
                    row.StatusDot.ToolTip = "Disarmed";
                }
                else
                {
                    row.StatusDot.Background = DotOffBrush();
                    row.StatusDot.ToolTip = "Connected";
                }
            }

            var allowCheck = connected;

            if (row.EnabledCheck != null)
            {
                row.EnabledCheck.IsEnabled = allowCheck;
                row.EnabledCheck.Opacity = allowCheck ? 1.0 : 0.55;

                if (!allowCheck)
                    row.EnabledCheck.ToolTip = warning
                        ? "Connecting or reconnecting"
                        : "Disconnected";
                else if (isChecked && !isArmed)
                    row.EnabledCheck.ToolTip = "Selected but disarmed";
                else if (isChecked && isArmed)
                    row.EnabledCheck.ToolTip = "Selected and armed";
                else
                    row.EnabledCheck.ToolTip = "Click to enable follower";
            }

            var allowEdits = allowCheck && isChecked;

            if (row.QtyOverrideBox != null)
                row.QtyOverrideBox.IsEnabled = allowEdits;

            if (row.AtmOverrideBox != null)
                row.AtmOverrideBox.IsEnabled = allowEdits;
        }
        
        private void RenderFollowerRowsState()
        {
            foreach (var row in _followerRows)
                RenderFollowerRowState(row);
        }
    }
}