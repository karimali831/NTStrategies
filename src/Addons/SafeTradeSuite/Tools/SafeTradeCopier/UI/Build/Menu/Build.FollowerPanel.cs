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
         private ScrollViewer _followersScrollViewer;
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

             var isRequested = ActiveSessionRequested();
 
             _btnCopyOn = CreateFormButton(
                 isRequested ? "Disarm" : "Arm",
                 height: SmallButtonHeight(),
                 tone: isRequested ? FormButtonTone.Warning : FormButtonTone.Success,
                 style: FormButtonStyle.Solid,
                 enabled: AreAllCheckedFollowersHealthy());
 
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
             
             _followersScrollViewer = CreateScrollbar(_followersPanel, canContentScroll: true, height: 240);
             followersStack.Children.Add(_followersScrollViewer);
 
             var followersFieldset = BuildFieldset("Followers", followersStack);
 
             root.Children.Add(followersFieldset);
 
             _btnCopyOn.Click += (s, e) =>
             {
                 if (ActiveSessionRequested())
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

             BuildFollowerRows(accounts);
             EnforceSimOnlyModeUi(accounts);
             LoadActiveSessionToUi();

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

             var requested = ActiveSessionRequested();

             _btnCopyOn.Content = requested ? "Disarm" : "Arm";

             var tone = requested ? FormButtonTone.Warning : FormButtonTone.Success;
             ApplyButtonTheme(_btnCopyOn, tone, FormButtonStyle.Solid, enabled: true);
         }
         
         private void RefreshFollowerBulkActionButtons()
         {
             if (_btnToggleAllFollowers == null)
                 return;

             _btnToggleAllFollowers.Content = ActiveSessionHasAllEnabledFollowers()
                 ? "Deselect All"
                 : "Select All";
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
 
             using (BeginSessionUiSuppression())
             {
                 foreach (var row in rows)
                     row.EnabledCheck.IsChecked = shouldCheck;
             }
 
             foreach (var row in rows)
                 RenderFollowerRowState(row);
 
             RefreshCopierStatusPanel();
             SaveUiToActiveSession("RenderFollowerPanel.ToggleFollowersSelection");
             ApplyConfigFromUi();
             RefreshFollowerBulkActionButtons();
         }
         
         private void ScrollFollowersToTop()
         {
             if (_followersScrollViewer == null)
                 return;

             _followersScrollViewer.ScrollToHome();
             _followersScrollViewer.ScrollToTop();
         }
     }
 }