using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _autoRearmPending;
        private bool _userManuallyDisarmed;
        private bool _simOnlyMode = true;
        
        
        private UIElement BuildUi(SafeCopierEngine eng)
        {
            SafeTradeSuiteRuntime.PrintLog($"Copier[{_toolId}] BuildUi()");
            
            try
            {
                var accounts = GetSelectableAccounts();
                SubscribeUiAccountEvents(accounts);
                
                var root = new Grid
                {
                    Margin = new Thickness(12),
                    Background = SystemColors.WindowBrush
                };

                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // menu
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status bar
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // totals pnl
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // top panels
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // followers
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // footer/status


                // ---------------- Header ----------------
                RenderHeader(root);
                
                // ---------------- Top panels: Master + Positions ----------------
                var topPanelsGrid = new Grid
                {
                    Margin = new Thickness(0, 0, 0, 0)
                };

                topPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

                topPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(12)
                });

                topPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

                Grid.SetRow(topPanelsGrid, 4);
                root.Children.Add(topPanelsGrid);

                RenderMasterPanel(eng, topPanelsGrid);
                RenderPositionsPanel(topPanelsGrid);
                
                // ---------------- Followers ----------------
                RenderFollowerPanel(eng, root);

                // ---------------- Copier buttons + Status ----------------
                RenderFooter(root);

                // ---------------- Hook engine events ----------------
                eng.OnStatus += msg =>
                {
                    if (_isClosing || _window == null)
                        return;
                    
                    var display = _uiDispatcher ?? _window?.Dispatcher;
                    if (display == null) return;

                    display.InvokeAsync(() =>
                    {
                        _statusBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}\n");
                        _statusBox.ScrollToEnd();
                    });
                };

                eng.OnReadyChanged += (ready, reason) =>
                {
                    if (_isClosing || _window == null)
                        return;
                    
                    var display = _uiDispatcher ?? _window?.Dispatcher;
                    if (display == null) return;

                    display.InvokeAsync(() =>
                    {
                        RefreshStatusBar();
                        RenderButtons(eng.CopyEnabled);
                        RenderFollowerRowsState();
                        RenderMasterSubmitButtonsState();
                    }, DispatcherPriority.Background);
                };

                eng.OnModeChanged += (armedIgnored, copyOn) =>
                {
                    if (_isClosing || _window == null)
                        return;
                    
                    var display = _uiDispatcher ?? _window?.Dispatcher;
                    if (display == null) return;

                    display.InvokeAsync(() =>
                    {
                        RefreshStatusBar();
                        RenderButtons(copyOn);
                        RenderFollowerRowsState();
                        RenderMasterSubmitButtonsState();
                    }, DispatcherPriority.Background);
                };
                
                // ---------------- Populate accounts + followers ----------------
                _masterBox.ItemsSource = accounts;
                _masterBox.DisplayMemberPath = "Name";

                // ✅ choose initial master correctly before building followers
                Account initialMaster = null;

                if (_simOnlyMode)
                {
                    initialMaster =
                        accounts.FirstOrDefault(a =>
                            a != null &&
                            IsSimAccount(a) &&
                            string.Equals(GetAccountConnectionLabel(a), "Playback", StringComparison.OrdinalIgnoreCase) &&
                            GetUiConnectionState(a) == UiConnectionState.Connected)
                        ?? accounts.FirstOrDefault(a =>
                            a != null &&
                            IsSimAccount(a) &&
                            GetUiConnectionState(a) == UiConnectionState.Connected)
                        ?? accounts.FirstOrDefault(IsSimAccount);
                }

                if (initialMaster == null)
                    initialMaster = accounts.FirstOrDefault();

                _masterBox.SelectedItem = initialMaster;
                
                BuildFollowerRows(accounts);
                EnforceSimOnlyModeUi(accounts);
                RenderFollowerRowsState();
                WireFollowerFlattenButtons(eng);
                WireFollowerFreeTradeButtons(eng);

                // ATMs
                LoadAtmTemplatesInto(_masterAtmBox, includeInherit: false);
                foreach (var r in _followerRows)
                    LoadAtmTemplatesInto(r.AtmOverrideBox, includeInherit: true);

                // ---------------- UI -> Engine wiring ----------------
                RenderButtons(copyOn: eng.CopyEnabled);
                RenderFlattenAllButtonState();
                EnsureInitialInstrumentSession();
                RefreshInstrumentSelectorItems();
                RefreshInstrumentTabs();
                LoadActiveSessionToUi();
                RefreshStatusBar();
                
                return new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = root
                };
            
            }
            catch (Exception ex)
            {
                LogUnhandled("BuildUi()", ex);
                throw;
            }
        }
    }
}