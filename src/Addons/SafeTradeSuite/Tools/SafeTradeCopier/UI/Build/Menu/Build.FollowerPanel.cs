using System;
 using System.Collections.Generic;
 using System.Linq;
 using System.Windows;
 using System.Windows.Controls;
 
 namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
 {
     public partial class SafeTradeCopierTool
     {
         private Button _btnCopyOn;
         private Button _btnToggleAllFollowers;
         private StackPanel _followersBulkActionsPanel;
         private readonly List<FollowerRow> _followerRows = new List<FollowerRow>();
         
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
             
             _btnToggleAllFollowers.Click += (s, e) => ToggleFollowersSelection(x => true);
             _followersBulkActionsPanel.Children.Add(_btnToggleAllFollowers);
 
             var isRequested = _activeInstrumentSession?.IsArmedRequested == true;
 
             _btnCopyOn = CreateFormButton(
                 isRequested ? "Disarm" : "Arm",
                 height: SmallButtonHeight(),
                 tone: isRequested ? FormButtonTone.Warning : FormButtonTone.Success,
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
                 if (_activeInstrumentSession?.IsArmedRequested == true)
                 {
                     RequestDisarmed("Copy manually disabled.");
                     return;
                 }
 
                 RequestArmed("Manual enable requested.");
             };
         }
         
         private UIElement BuildFollowersContent()
         {
             SafeTradeSuiteRuntime.PrintLog(
                 $"[BUILD FOLLOWERS CONTENT] activeInstr={_activeInstrumentSession?.InstrumentName} rowsBefore={_followerRows.Count}");
     
             var host = new Grid();
             RenderFollowerPanel(_engine, host);
 
             var accounts = GetSelectableAccounts();
             
             _suppressSessionUiEvents = true;
             try
             {
                 BuildFollowerRows(accounts);
             
                 foreach (var r in _followerRows)
                     LoadAtmTemplatesInto(r.BracketOverrideBox, includeInherit: true);
             
                 EnforceSimOnlyModeUi(accounts);
                 LoadActiveSessionToUi();
             }
             finally
             {
                 _suppressSessionUiEvents = false;
             }
             
             RenderFollowerRowsState();
             WireFollowerFlattenButtons(_engine);
             WireFollowerFreeTradeButtons(_engine);
             RefreshFollowerBulkActionButtons();
             RefreshCopierStatusPanel();
 
             return host;
         }
 
         private void RenderButtons()
         {
             if (_btnCopyOn == null)
                 return;
 
             var isRequested = _activeInstrumentSession?.IsArmedRequested == true;
 
             _btnCopyOn.Content = isRequested ? "Disarm" : "Arm";
 
             var tone = isRequested ? FormButtonTone.Warning : FormButtonTone.Success;
             ApplyButtonTheme(_btnCopyOn, tone, FormButtonStyle.Solid, enabled: true);
         }
         
         private void RefreshFollowerBulkActionButtons()
         {
             if (_followersBulkActionsPanel == null)
                 return;
 
             if (_btnToggleAllFollowers != null)
                 _btnToggleAllFollowers.Content = AreAllMatchingFollowersChecked(x => true)
                     ? "Deselect All"
                     : "Select All";
         }
 
         private bool AreAllMatchingFollowersChecked(Func<FollowerRow, bool> predicate)
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
             SaveUiToActiveSession("RenderFollowerPanel.ToggleFollowersSelection");
             ApplyConfigFromUi();
             RefreshFollowerBulkActionButtons();
         }
     }
 }