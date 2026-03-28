using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void RebuildFollowersAndRewire(SafeCopierEngine eng, List<Account> accounts)
        {
            SafeTradeSuiteRuntime.PrintLog(
                $"[FOLLOWER REBUILD START] rows={_followerRows.Count}");

            BuildFollowerRows(accounts);
            WireFollowerFlattenButtons(eng);
            WireFollowerFreeTradeButtons(eng);
            EnforceSimOnlyModeUi(accounts);
            LoadActiveSessionToUi("RebuildFollowersAndRewire");
            RefreshRiskFieldset();
        }
        
        private void WireFollowerFlattenButtons(SafeCopierEngine eng)
        {
            foreach (var r in _followerRows)
            {
                if (r?.FlattenBtn == null) continue;

                r.FlattenBtn.Click += (s, e) =>
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[FOLLOWER FLATTEN CLICK] acc={r?.Account?.Name} instr={GetInstrument()?.FullName}");

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
        
        
        private void BuildFollowerRows(List<Account> accounts)
        {
            _followerRows.Clear();
            _followersPanel?.Children.Clear();

            var master = GetMasterAccount();
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

                var enabled = CreateCircularCheckBox();
                enabled.ToolTip = "Enable follower";
                
                var accountText = new TextBlock
                {
                    Text = acc.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                    Foreground = WindowForegroundBrush()
                };

                var masterQty = ParseQtyOrDefault(_masterQtyBox?.Text);
                var qtyBox = CreateOrderQtyBox(
                    null,
                    $"Blank = inherit master qty ({masterQty})", transparentBg: true);

                var atmBox = CreateFormComboBox(width: 125);
                atmBox.ToolTip =
                    "(inherit master) = use master ATM, Follow Master Exit = no follower bracket, exit when master exits";
                
                // Position
                var position = new TextBlock
                {
                    Text = "",
                    Foreground = MutedForegroundBrush(),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12
                };
                RenderLivePositionText(position, acc);

                // PnL Trade Tracker 
                var pnl = new TextBlock
                {
                    Text = "$0.00",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = MutedForegroundBrush(),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12
                };
                
                var pnlBar = new ProgressBar
                {
                    Height = 10,
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0,
                    Width = 100,
                    Margin = new Thickness(0, 2, 8, 2),
                    Visibility = Visibility.Collapsed
                };
                EnsureRoundedProgressBar(pnlBar, alignRight: false);
                
                var pnlBarStatusText = new TextBlock
                {
                    Text = "",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = MutedForegroundBrush(),
                    Margin = new Thickness(6, 0, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Visibility = Visibility.Collapsed
                };

                var flatten = CreateFormButton(
                    text: "❌",
                    tone: FormButtonTone.Flatten,
                    style: FormButtonStyle.Solid,
                    width: 60,
                    height: SmallButtonHeight());
                RenderFlattenFollowerButtonState(flatten, enabled: false);
                
                var freeTrade = CreateFormButton(
                    text: CheckIcon,
                    tone: FormButtonTone.Primary,
                    style: FormButtonStyle.Solid,
                    width: 60,
                    height: SmallButtonHeight());
                RenderFreeTradeButtonState(freeTrade, enabled: false, undoMode: false, CheckIcon, all: false);
                
                var allow = !_simOnlyMode || IsSimAccount(acc);

                enabled.IsEnabled = allow;
                if (!allow) enabled.IsChecked = false;

                qtyBox.IsEnabled = allow;
                atmBox.IsEnabled = allow;

                // flatten stays disabled by default; PnL timer/net-position logic enables it when needed
                flatten.IsEnabled = false;

                var pnlHost = new Grid
                {
                    VerticalAlignment = VerticalAlignment.Center
                };

                var pnlStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0)
                };

                pnl.VerticalAlignment = VerticalAlignment.Center;
                pnl.HorizontalAlignment = HorizontalAlignment.Left;

                pnlBar.VerticalAlignment = VerticalAlignment.Center;
                pnlBarStatusText.VerticalAlignment = VerticalAlignment.Center;
                
                // Guard
                var guardText = new TextBlock
                {
                    Text = "",
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = WarningActionBrush(),
                    ToolTip = null
                };
                
                Grid.SetColumn(enabled, 0);
                Grid.SetColumn(accountText, 1);
                Grid.SetColumn(qtyBox, 2);
                Grid.SetColumn(atmBox, 3);
                Grid.SetColumn(position, 4);
                Grid.SetColumn(pnlHost, 5);
                Grid.SetColumn(freeTrade, 6);
                Grid.SetColumn(flatten, 7);
                Grid.SetColumn(guardText, 8);

                pnlStack.Children.Add(pnl);
                pnlStack.Children.Add(pnlBar);
                pnlStack.Children.Add(pnlBarStatusText);

                pnlHost.Children.Add(pnlStack);

                rowGrid.Children.Add(enabled);
                rowGrid.Children.Add(accountText);
                rowGrid.Children.Add(qtyBox);
                rowGrid.Children.Add(atmBox);
                rowGrid.Children.Add(position);
                rowGrid.Children.Add(pnlHost);
                rowGrid.Children.Add(freeTrade);
                rowGrid.Children.Add(flatten);
                rowGrid.Children.Add(guardText);

                var row = new FollowerRow
                {
                    Account = acc,
                    EnabledCheck = enabled,
                    QtyOverrideBox = qtyBox,
                    BracketOverrideBox = atmBox,
                    Position = position,
                    PnlText = pnl,
                    PnlBar = pnlBar,
                    FlattenBtn = flatten,
                    FreeTradeBtn = freeTrade,
                    PnlBarStatusText = pnlBarStatusText,
                    GuardText = guardText
                };
                
                RenderLivePositionText(row.Position, row.Account);
                RenderFollowerRowState(row);
                
                // When user changes follower settings, we re-apply config (no re-arm UX)
                enabled.Checked += (s, e) =>
                {
                    HandleFollowerEnabledChanged(
                        row,
                        isChecked: true,
                        source: "BuildFollowerRows.enabled.Checked");
                };

                enabled.Unchecked += (s, e) =>
                {
                    HandleFollowerEnabledChanged(
                        row,
                        isChecked: false,
                        source: "BuildFollowerRows.enabled.Unchecked");
                };
                
                qtyBox.TextChanged += (s, e) =>
                {
                    if (SuppressSessionUiEvents) return;

                    RenderQtyBoxState(qtyBox, ParseQtyOrDefault(_masterQtyBox?.Text));
                    SaveUiToActiveSession("BuildFollowerRows.qtyBox.TextChanged");
                    ApplyConfigFromUi();
                };

                atmBox.SelectionChanged += (s, e) =>
                {
                    if (SuppressSessionUiEvents) return;
                    SaveUiToActiveSession("BuildFollowerRows.atmBox.SelectionChanged");
                    ApplyConfigFromUi();
                };
                
                qtyBox.VerticalContentAlignment = VerticalAlignment.Center;
                position.HorizontalAlignment =  HorizontalAlignment.Center;
                position.VerticalAlignment =  VerticalAlignment.Center;
                atmBox.VerticalContentAlignment = VerticalAlignment.Center;
                flatten.VerticalAlignment = VerticalAlignment.Center;

                _followerRows.Add(row);
                _followersPanel?.Children.Add(rowGrid);

                rowIndex++;
            }
            
            RefreshFollowerBulkActionButtons();
        }
        
        private void HandleFollowerEnabledChanged(FollowerRow row, bool isChecked, string source)
        {
            if (row?.Account == null || row.EnabledCheck == null)
                return;

            if (SuppressSessionUiEvents || !row.EnabledCheck.IsLoaded || _activeInstrumentSession == null)
                return;

            var instr = GetInstrument();

            if (!isChecked && instr != null && HasOpenInstrumentPosition(row.Account, instr))
            {
                SetFollowerChecked(row, true, source + ".revertOpenPosition");
                RenderFollowerRowState(row);
                RefreshCopierStatusPanel();
                RefreshFollowerBulkActionButtons();
                return;
            }

            RenderFollowerRowState(row);

            SaveUiToActiveSession(source);
            ApplyConfigFromUi();
            SyncEngineRequestedStateForActiveSession();
            
            RefreshCopierStatusPanel();
            RenderButtons();
            RefreshFollowerBulkActionButtons();
        }
        
        private void SetFollowerChecked(FollowerRow row, bool isChecked, string source)
        {
            if (row?.EnabledCheck == null)
                return;

            var before = row.EnabledCheck.IsChecked == true;
            if (before == isChecked)
                return;

            SafeTradeSuiteRuntime.PrintLog(
                $"[SET FOLLOWER CHECK] source={source} acc={row.Account?.Name} before={before} after={isChecked} suppress={SuppressSessionUiEvents}");

            row.EnabledCheck.IsChecked = isChecked;
        }
        
        private void RenderQtyBoxState(TextBox qtyBox, int masterQty)
        {
            if (qtyBox == null)
                return;

            var isBlank = string.IsNullOrWhiteSpace(qtyBox.Text);

            qtyBox.ToolTip = isBlank
                ? $"Blank = inherit master qty ({masterQty})"
                : "Follower qty override";

            qtyBox.Background = isBlank
                ? InputDisabledBackgroundBrush()
                : InputBackgroundBrush();
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
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); // Bracket
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Position
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) }); // PnL
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // BE
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); // Flatten
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Guard
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
            AddHeaderText(g, "Account", 1);
            AddHeaderText(g, "Qty", 2, centered: true);
            AddHeaderText(g, "Bracket", 3);
            AddHeaderText(g, "Position", 4, centered: true);
            AddHeaderText(g, "PnL", 5);
            AddHeaderText(g, "Break-even", 6, centered: true);
            AddHeaderText(g, "Flatten", 7, centered: true);
            AddHeaderText(g, "Shield", 8, centered: true);

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
        
        private void RenderFollowerGuardState(FollowerRow row)
        {
            if (row?.Account == null || row.GuardText == null || _engine == null)
                return;

            row.GuardText.Visibility = Visibility.Visible;
            row.GuardText.FontSize = 14;
            row.GuardText.FontWeight = FontWeights.Bold;

            _engine.TryGetFollowerGuardState(row.Account, out var state);

            var shieldEnabled = _followerGuardEnabled;

            if (!shieldEnabled)
            {
                row.GuardText.Text = "🛡";
                row.GuardText.Foreground = WarningActionBrush();
                row.GuardText.ToolTip = "Shield disabled in Settings (no protection active)";
                return;
            }

            if (state == null)
            {
                row.GuardText.Text = "🛡";
                row.GuardText.Foreground = SuccessActionBrush();
                row.GuardText.ToolTip = "Shield healthy";
                return;
            }

            if (state.IsGuardDisabled)
            {
                row.GuardText.Text = "🛡";
                row.GuardText.Foreground = DangerActionBrush();
                row.GuardText.ToolTip = string.IsNullOrWhiteSpace(state.LastGuardReason)
                    ? "Follower disabled by shield"
                    : state.LastGuardReason;
                return;
            }

            if (state.EntryWorking)
            {
                row.GuardText.Text = "🛡";
                row.GuardText.Foreground = WarningActionBrush();
                row.GuardText.ToolTip = state.PendingEntryTimeUtc != null
                    ? $"Follower entry pending since {state.PendingEntryTimeUtc.Value:HH:mm:ss} UTC"
                    : "Follower entry pending.";
                return;
            }

            if (state.DesyncDetectedAtUtc != null)
            {
                row.GuardText.Text = "🛡";
                row.GuardText.Foreground = WarningActionBrush();
                row.GuardText.ToolTip = $"Follower desync under observation since {state.DesyncDetectedAtUtc.Value:HH:mm:ss} UTC";
                return;
            }

            row.GuardText.Text = "🛡";
            row.GuardText.Foreground = SuccessActionBrush();
            row.GuardText.ToolTip = "Shield healthy";
        }
        
        private void RenderFollowerRowState(FollowerRow row)
        {
            if (row?.Account == null)
                return;

            if (row.Position != null)
                RenderLivePositionText(row.Position, row.Account);

            var connState = GetUiConnectionState(row.Account);
            var connected = connState == UiConnectionState.Connected;
            var warning = connState == UiConnectionState.Warning;
            var disconnected = connState == UiConnectionState.Disconnected;

            var instr = GetInstrument();
            var hasOpenPosition = HasOpenInstrumentPosition(row.Account, instr);

            if (hasOpenPosition && row.EnabledCheck != null && row.EnabledCheck.IsChecked != true)
                SetFollowerChecked(row, true, "RenderFollowerRowState.hasOpenPosition");

            var isChecked = row.EnabledCheck?.IsChecked == true;
            var isArmed = _engine?.IsRequested == true;

            if (row.EnabledCheck != null)
            {
                var accountLocked = _engine?.TryGetAccountLockReason(row.Account, out _, out _) ?? false;
                var allowCheckToggle = connected && !hasOpenPosition && !accountLocked;

                row.EnabledCheck.IsEnabled = allowCheckToggle;
                row.EnabledCheck.Opacity = connected ? 1.0 : 0.55;

                if (disconnected)
                {
                    row.EnabledCheck.ToolTip = "Disconnected";
                    row.EnabledCheck.Tag = FollowerCheckVisualState.Disconnected;
                }
                else if (warning)
                {
                    row.EnabledCheck.ToolTip = "Connecting or reconnecting";
                    row.EnabledCheck.Tag = FollowerCheckVisualState.Warning;
                }
                else if (hasOpenPosition)
                {
                    row.EnabledCheck.ToolTip = isArmed
                        ? "Enabled because an open position exists"
                        : "Enabled because an open position exists while copier is disarmed";

                    row.EnabledCheck.Tag = isArmed
                        ? FollowerCheckVisualState.Armed
                        : FollowerCheckVisualState.Warning;
                }
                else if (isChecked && !isArmed)
                {
                    row.EnabledCheck.ToolTip = "Selected but disarmed";
                    row.EnabledCheck.Tag = FollowerCheckVisualState.Warning;
                }
                else if (isChecked)
                {
                    row.EnabledCheck.ToolTip = "Selected and armed";
                    row.EnabledCheck.Tag = FollowerCheckVisualState.Armed;
                }
                else
                {
                    row.EnabledCheck.ToolTip = "Click to enable follower";
                    row.EnabledCheck.Tag = FollowerCheckVisualState.Off;
                }
            }

            var allowEdits = connected && (isChecked || hasOpenPosition);

            if (row.QtyOverrideBox != null)
                row.QtyOverrideBox.IsEnabled = allowEdits;

            if (row.BracketOverrideBox != null)
                row.BracketOverrideBox.IsEnabled = allowEdits;
            
            RenderFollowerGuardState(row);
        }
        
        private void RenderFollowerRowsState()
        {
            foreach (var row in _followerRows)
                RenderFollowerRowState(row);
            
            RenderPnlUi();
        }
    }
}