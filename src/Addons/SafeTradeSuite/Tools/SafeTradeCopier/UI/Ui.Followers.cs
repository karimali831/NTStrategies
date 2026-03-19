using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            SafeTradeSuiteRuntime.PrintLog(
                $"[FOLLOWER REBUILD START] rows={_followerRows.Count}");

            var selected = new HashSet<string>(
                _followerRows
                    .Where(r => r?.EnabledCheck?.IsChecked == true && r.Account != null)
                    .Select(r => r.Account.Name),
                StringComparer.Ordinal);

            var qtyOverrides = _followerRows
                .Where(r => r?.Account != null)
                .ToDictionary(
                    r => r.Account.Name,
                    r => r.QtyOverrideBox?.Text,
                    StringComparer.Ordinal);

            var bracketSelections = _followerRows
                .Where(r => r?.Account != null)
                .ToDictionary(
                    r => r.Account.Name,
                    r => NormalizeAtm(r.AtmOverrideBox?.SelectedItem as string),
                    StringComparer.Ordinal);

            BuildFollowerRows(accounts);

            foreach (var r in _followerRows)
            {
                if (r?.Account == null)
                    continue;

                if (r.EnabledCheck != null)
                    r.EnabledCheck.IsChecked = selected.Contains(r.Account.Name);
            }

            EnforceSimOnlyModeUi(accounts);

            foreach (var r in _followerRows)
            {
                LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

                SafeTradeSuiteRuntime.PrintLog(
                    $"[FOLLOWER BRACKET LOAD] acc={r.Account?.Name} defaultSelected={r.AtmOverrideBox?.SelectedItem}");

                if (r?.Account == null)
                    continue;

                if (qtyOverrides.TryGetValue(r.Account.Name, out var qtyText) && r.QtyOverrideBox != null)
                    r.QtyOverrideBox.Text = qtyText;

                if (bracketSelections.TryGetValue(r.Account.Name, out var bracket) &&
                    r.AtmOverrideBox != null &&
                    r.AtmOverrideBox.Items.Contains(bracket))
                {
                    r.AtmOverrideBox.SelectedItem = bracket;
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[FOLLOWER BRACKET RESTORE] acc={r.Account.Name} restored={r.AtmOverrideBox.SelectedItem}");
                }
            }

            WireFollowerFlattenButtons(eng);
            WireFollowerFreeTradeButtons(eng);

            ApplyConfigFromUi();
            RefreshRiskFieldset();

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
                    
                    var hadOpenPosition = HasOpenInstrumentPosition(r.Account, instr);

                    if (!hadOpenPosition)
                    {
                        if (r.PnlBar != null)
                            r.PnlBar.Tag = null;

                        ClearBarOutcome(r.PnlBarStatusText, r.PnlBar);
                        eng.Log($"Flatten skipped -> {r.Account.Name} ({instr.FullName}) no open position.");
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

                    if (BreakEvenDisabled)
                    {
                        eng.Log("Break-even disabled in Settings.");
                        return;
                    }

                    if (eng.CanUndoFreeTrade(r.Account, instr, out _))
                    {
                        if (eng.UndoFreeTrade(r.Account, instr))
                            eng.Log($"Break-even undone -> {r.Account.Name} ({instr.FullName})");
                    }
                    else
                    {
                        if (eng.ApplyFreeTrade(r.Account, instr, _freeTradeMinProfitPoints, _freeTradePlusPoints))
                            eng.Log($"Break-even applied -> {r.Account.Name} ({instr.FullName})");
                    }
                };
            }
        }
        
        private void BuildFollowerRows(List<Account> accounts)
        {
            _followerRows.Clear();

            if (_followersPanel != null)
                _followersPanel.Children.Clear();

            var master = _masterBox?.SelectedItem as Account;
            var masterName = master?.Name ?? "";

            var rowIndex = 0;
            foreach (var acc in accounts)
            {
                if (acc == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(masterName) && acc.Name == masterName)
                    continue;

                if (_simOnlyMode && !IsSimAccount(acc))
                    continue;
                
                var rowGrid = new Grid
                {
                    Margin = new Thickness(2, 2, 2, 2),
                    Background = GetFollowerRowBackgroundBrush(rowIndex)
                };
                
                FollowerHeaderColumnDefinitions(rowGrid);
              
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
                    Foreground = WindowForegroundBrush()
                };

                var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);

                var qtyBox = CreateFormTextBox(masterQty.ToString(), width: 40);
                qtyBox.ToolTip = "Qty override (blank = inherit master)";

                var atmBox = CreateFormComboBox(width: 125);
                atmBox.ToolTip =
                    "(inherit master) = use master ATM, Follow Master Exit = no follower bracket, exit when master exits";

                var pnl = new TextBlock
                {
                    Text = "Unrealized $0.00",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = MutedForegroundBrush(),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    // Margin = new Thickness(10, 0, 10, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                
                var pnlBar = new ProgressBar
                {
                    Height = 10,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0,
                    Width = 100,
                    Margin = new Thickness(0, 2, 20, 2),
                    Visibility = Visibility.Collapsed
                };
                EnsureRoundedProgressBar(pnlBar, alignRight: false);
                
                var pnlBarStatusText = new TextBlock
                {
                    Text = "",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = MutedForegroundBrush(),
                    Margin = new Thickness(6, 0, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Visibility = Visibility.Collapsed
                };

                var flatten = CreateFormButton(
                    text: "❌",
                    tone: FormButtonTone.Danger,
                    style: FormButtonStyle.Outline);
                RenderFlattenButtonState(flatten, enabled: false);
                
                var freeTrade = CreateFormButton(
                    text: "✓",
                    tone: FormButtonTone.Primary,
                    style: FormButtonStyle.Outline);
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
                
                Grid.SetColumn(enabled, 0);
                Grid.SetColumn(accountText, 1);
                Grid.SetColumn(qtyBox, 2);
                Grid.SetColumn(atmBox, 3);
                Grid.SetColumn(pnlStack, 4);
                Grid.SetColumn(freeTrade, 5);
                Grid.SetColumn(flatten, 6);

                pnlStack.Children.Add(pnl);
                pnlStack.Children.Add(pnlBar);
                pnlStack.Children.Add(pnlBarStatusText);

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
                    RefreshCopierStatusPanel();
                    RenderFollowerRowState(row);
                    SaveUiToActiveSession();
                    ApplyConfigFromUi();
                    RefreshFollowerBulkActionButtons();
                };

                enabled.Unchecked += (s, e) =>
                {
                    if (_suppressSessionUiEvents) return;

                    var instr = GetInstrument();
                    if (HasOpenInstrumentPosition(row.Account, instr))
                    {
                        _suppressSessionUiEvents = true;
                        try
                        {
                            row.EnabledCheck.IsChecked = true;
                        }
                        finally
                        {
                            _suppressSessionUiEvents = false;
                        }

                        RenderFollowerRowState(row);
                        RefreshCopierStatusPanel();
                        RefreshFollowerBulkActionButtons();
                        SavePersistentUiState();
                        return;
                    }

                    RefreshCopierStatusPanel();
                    RenderFollowerRowState(row);
                    SaveUiToActiveSession();
                    ApplyConfigFromUi();
                    RefreshFollowerBulkActionButtons();
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
                _followersPanel?.Children.Add(rowGrid);

                rowIndex++;
            }
            
            RefreshFollowerBulkActionButtons();
        }

        private static Brush GetFollowerRowBackgroundBrush(int rowIndex)
        {
            if (IsDarkTheme())
            {
                return rowIndex % 2 == 0
                    ? new SolidColorBrush(Color.FromRgb(24, 24, 24))
                    : new SolidColorBrush(Color.FromRgb(29, 29, 29));
            }

            return rowIndex % 2 == 0
                ? new SolidColorBrush(Color.FromRgb(248, 248, 248))
                : new SolidColorBrush(Color.FromRgb(242, 242, 242));
        }
        
        private static void FollowerHeaderColumnDefinitions(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });  // On
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // Account
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });  // Override Qty
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); // Override ATM
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // PnL
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // BE
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Flatten
        }
        
        private static Grid BuildFollowerHeaderRow()
        {
            var g = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4),
                Background = IsDarkTheme()
                    ? new SolidColorBrush(Color.FromRgb(30, 30, 30))
                    : TableHeaderBrush()
            };

            FollowerHeaderColumnDefinitions(g);

            AddHeaderText(g, "On", 0, centered: true);
            AddHeaderText(g, "Account", 1, margin: new Thickness(6, 4, 6, 4));
            AddHeaderText(g, "Qty", 2, centered: true);
            AddHeaderText(g, "Bracket", 3, margin: new Thickness(14, 4, 6, 4));
            AddHeaderText(g, "PnL", 4, margin: new Thickness(6, 4, 6, 4));
            AddHeaderText(g, "Break-even", 5, centered: true);
            AddHeaderText(g, "Flatten", 6, centered: true);

            return g;
        }

        private static void AddHeaderText(
            Grid g,
            string text,
            int col,
            bool centered = false,
            Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = margin ?? new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = WindowForegroundBrush(),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
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
            style.Setters.Add(new Setter(Control.ForegroundProperty, DotOffBrush()));
            style.Setters.Add(new Setter(FrameworkElement.TagProperty, FollowerCheckVisualState.Off));

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

            var check = new FrameworkElementFactory(typeof(TextBlock))
            {
                Name = "CheckGlyph"
            };
            check.SetValue(TextBlock.TextProperty, "✓");
            check.SetValue(TextBlock.FontSizeProperty, 11.0);
            check.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            check.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            root.AppendChild(border);
            root.AppendChild(check);

            template.VisualTree = root;

            var offTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Off
            };
            offTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent, "Dot"));
            offTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotBorderBrush(), "Dot"));
            offTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "CheckGlyph"));

            var armedTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Armed
            };
            armedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotConnectedOnBrush(), "Dot"));
            armedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotConnectedOnBrush(), "Dot"));
            armedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));

            var warningTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Warning
            };
            warningTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotWarningBrush(), "Dot"));
            warningTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotWarningBrush(), "Dot"));
            warningTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));

            var disconnectedTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Disconnected
            };
            disconnectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotDisconnectedBrush(), "Dot"));
            disconnectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotDisconnectedBrush(), "Dot"));
            disconnectedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "CheckGlyph"));

            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Root"));

            template.Triggers.Add(offTrigger);
            template.Triggers.Add(armedTrigger);
            template.Triggers.Add(warningTrigger);
            template.Triggers.Add(disconnectedTrigger);
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
        
        private void RenderFollowerRowState(FollowerRow row)
        {
            if (row?.Account == null)
                return;

            var connState = GetUiConnectionState(row.Account);
            var connected = connState == UiConnectionState.Connected;
            var warning = connState == UiConnectionState.Warning;
            var disconnected = connState == UiConnectionState.Disconnected;

            var isChecked = row.EnabledCheck?.IsChecked == true;
            var isArmed = _engine?.CopyEnabled == true;
            
            var instr = GetInstrument();
            var hasOpenPosition = HasOpenInstrumentPosition(row.Account, instr);

            if (hasOpenPosition && row.EnabledCheck != null && row.EnabledCheck.IsChecked != true)
                row.EnabledCheck.IsChecked = true;

            if (row.EnabledCheck != null)
            {
                var allowCheckToggle = connected && !hasOpenPosition;

                row.EnabledCheck.IsEnabled = allowCheckToggle;
                row.EnabledCheck.Opacity = connected ? 1.0 : 0.55;

                if (disconnected)
                {
                    row.EnabledCheck.ToolTip = "Disconnected";
                    row.EnabledCheck.Foreground = DotDisconnectedBrush();
                }
                else if (warning)
                {
                    row.EnabledCheck.ToolTip = "Connecting or reconnecting";
                    row.EnabledCheck.Foreground = DotWarningBrush();
                }
                else if (hasOpenPosition)
                {
                    row.EnabledCheck.ToolTip = isArmed
                        ? "Enabled because an open position exists"
                        : "Enabled because an open position exists";
                    row.EnabledCheck.Foreground = isArmed ? DotConnectedOnBrush() : DotWarningBrush();
                }
                else if (isChecked && !isArmed)
                {
                    row.EnabledCheck.ToolTip = "Selected but disarmed";
                    row.EnabledCheck.Foreground = DotWarningBrush();
                }
                else if (isChecked)
                {
                    row.EnabledCheck.ToolTip = "Selected and armed";
                    row.EnabledCheck.Foreground = DotConnectedOnBrush();
                }
                else
                {
                    row.EnabledCheck.ToolTip = "Click to enable follower";
                    row.EnabledCheck.Foreground = DotOffBrush();
                }
            }

            var allowEdits = connected && (isChecked || hasOpenPosition);

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