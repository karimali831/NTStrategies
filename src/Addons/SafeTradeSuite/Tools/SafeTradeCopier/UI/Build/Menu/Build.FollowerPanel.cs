using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Button _btnCopyOn;
        private Button _btnToggleAllFollowers;
        private Button _btnToggleSimFollowers;
        private Button _btnToggleLiveFollowers;
        private StackPanel _followersBulkActionsPanel;
        private static readonly List<FollowerRow> _followerRows = new List<FollowerRow>();
        
        private void RenderFollowerPanel(SafeCopierEngine eng, Grid root)
        {
            var followersStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var followersTitleRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            followersTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            followersTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            followersTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var followersTitleText = new TextBlock
            {
                Text = "Followers",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = WindowForegroundBrush(),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Hidden
            };

            _followersBulkActionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            _btnToggleAllFollowers = CreateFormButton(
                text: "Select All",
                tone: FormButtonTone.Primary,
                style: FormButtonStyle.Outline,
                height: SmallButtonHeight());

            _btnToggleSimFollowers = CreateFormButton(
                text: "Select All Sim",
                tone: FormButtonTone.Primary,
                style: FormButtonStyle.Outline,
                height: SmallButtonHeight());

            _btnToggleLiveFollowers = CreateFormButton(
                text: "Select All Live",
                tone: FormButtonTone.Warning,
                style: FormButtonStyle.Outline,
                height: SmallButtonHeight(),
                margin: new Thickness(6, 0, 0, 0));

            _btnToggleAllFollowers.Click += (s, e) => ToggleFollowersSelection(x => true);
            _btnToggleSimFollowers.Click += (s, e) => ToggleFollowersSelection(r => IsSimAccount(r.Account));
            _btnToggleLiveFollowers.Click += (s, e) => ToggleFollowersSelection(r => !IsSimAccount(r.Account));

            _followersBulkActionsPanel.Children.Add(_btnToggleAllFollowers);
            _followersBulkActionsPanel.Children.Add(_btnToggleSimFollowers);
            _followersBulkActionsPanel.Children.Add(_btnToggleLiveFollowers);

            _btnCopyOn = CreateFormButton(
                eng.CopyEnabled ? "Armed" : "Disarmed",
                height: SmallButtonHeight(),
                tone: FormButtonTone.Danger,
                style: FormButtonStyle.Solid);

            Grid.SetColumn(followersTitleText, 0);
            Grid.SetColumn(_followersBulkActionsPanel, 1);
            Grid.SetColumn(_btnCopyOn, 2);

            followersTitleRow.Children.Add(followersTitleText);
            followersTitleRow.Children.Add(_followersBulkActionsPanel);
            followersTitleRow.Children.Add(_btnCopyOn);

            followersStack.Children.Add(followersTitleRow);
            followersStack.Children.Add(BuildFollowerHeaderRow());

            _followersPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            
            var followersScroll = CreateScrollbar(_followersPanel, canContentScroll: true, height: 240);
            followersStack.Children.Add(followersScroll);

            var followersFieldset = BuildFieldset("Followers", followersStack);

            root.Children.Add(followersFieldset);

            _btnCopyOn.Click += (s, e) =>
            {
                if (eng.CopyEnabled)
                {
                    RequestCopyDisabled(
                        manual: true,
                        allowAutoRearm: false,
                        reason: "Copy manually disabled.");
                    return;
                }

                _userManuallyDisarmed = false;
                _autoRearmPending = false;
                RequestCopyEnabled("Manual enable requested.");
            };
        }
        
        private UIElement BuildFollowersContent()
        {
            SafeTradeSuiteRuntime.PrintLog(
                $"[BUILD FOLLOWERS CONTENT] activeInstr={_activeInstrumentSession?.InstrumentName} rowsBefore={_followerRows.Count}");
            
            var host = new Grid();
            RenderFollowerPanel(_engine, host);

            var accounts = GetSelectableAccounts();
            BuildFollowerRows(accounts);

            foreach (var r in _followerRows)
                LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

            EnforceSimOnlyModeUi(accounts);
            LoadActiveSessionToUi();
            RenderFollowerRowsState();
            WireFollowerFlattenButtons(_engine);
            WireFollowerFreeTradeButtons(_engine);
            RefreshFollowerBulkActionButtons();

            return host;
        }

        private void RenderButtons(bool copyOn)
        {
            if (_btnCopyOn == null)
                return;
            
            _btnCopyOn.IsEnabled = true;
            _btnCopyOn.Content = copyOn ? "Armed" : "Disarmed";
            _btnCopyOn.Background = copyOn ? Brushes.DarkGreen : Brushes.Maroon;
            _btnCopyOn.BorderBrush = copyOn ? Brushes.DarkGreen : Brushes.Maroon;
        }
        
        private void RefreshFollowerBulkActionButtons()
        {
            if (_followersBulkActionsPanel == null)
                return;

            if (_btnToggleAllFollowers != null)
                _btnToggleAllFollowers.Visibility = _simOnlyMode ? Visibility.Visible : Visibility.Collapsed;

            if (_btnToggleSimFollowers != null)
                _btnToggleSimFollowers.Visibility = _simOnlyMode ? Visibility.Collapsed : Visibility.Visible;

            if (_btnToggleLiveFollowers != null)
                _btnToggleLiveFollowers.Visibility = _simOnlyMode ? Visibility.Collapsed : Visibility.Visible;

            if (_btnToggleAllFollowers != null)
                _btnToggleAllFollowers.Content = AreAllMatchingFollowersChecked(x => true)
                    ? "Deselect All"
                    : "Select All";

            if (_btnToggleSimFollowers != null)
                _btnToggleSimFollowers.Content = AreAllMatchingFollowersChecked(r => IsSimAccount(r.Account))
                    ? "Deselect All Sim"
                    : "Select All Sim";

            if (_btnToggleLiveFollowers != null)
                _btnToggleLiveFollowers.Content = AreAllMatchingFollowersChecked(r => !IsSimAccount(r.Account))
                    ? "Deselect All Live"
                    : "Select All Live";
        }

        private static bool AreAllMatchingFollowersChecked(Func<FollowerRow, bool> predicate)
        {
            var rows = _followerRows
                .Where(r => r?.Account != null)
                .Where(predicate)
                .Where(r => r.EnabledCheck != null && r.EnabledCheck.IsEnabled)
                .ToList();

            if (rows.Count == 0)
                return false;

            return rows.All(r => r.EnabledCheck.IsChecked == true);
        }

        private void ToggleFollowersSelection(Func<FollowerRow, bool> predicate)
        {
            var rows = _followerRows
                .Where(r => r?.Account != null)
                .Where(predicate)
                .Where(r => r.EnabledCheck != null && r.EnabledCheck.IsEnabled)
                .ToList();

            if (rows.Count == 0)
                return;

            var shouldCheck = rows.Any(r => r.EnabledCheck.IsChecked != true);

            _suppressSessionUiEvents = true;
            try
            {
                foreach (var row in rows)
                    row.EnabledCheck.IsChecked = shouldCheck;
            }
            finally
            {
                _suppressSessionUiEvents = false;
            }

            foreach (var row in rows)
                RenderFollowerRowState(row);

            RefreshCopierStatusPanel();
            SaveUiToActiveSession();
            ApplyConfigFromUi();
            RefreshFollowerBulkActionButtons();
        }
    }
}