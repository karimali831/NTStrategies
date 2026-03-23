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
        
        private UIElement SafeBuildUi(SafeCopierEngine engine)
        {
            try
            {
                return BuildUi(engine);
            }
            catch (Exception ex)
            {
                SafeTradeSuiteRuntime.PrintLog("BuildUi FAILED:");
                SafeTradeSuiteRuntime.PrintLog(ex.ToString());
                throw;
            }
        }
        
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
                    Background = WindowBackgroundBrush()
                };

                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // top menu tabs
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // top panels
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // main content
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // instrument tabs

                // ---------------- Top menu ----------------
                var topMenuBar = BuildTopMenuBar(eng);
                Grid.SetRow(topMenuBar, 0);
                root.Children.Add(topMenuBar);
                
                // ---------------- Master + Instrument/Status ----------------
                var topPanelsGrid = new Grid
                {
                    Margin = new Thickness(0)
                };

                topPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(13, GridUnitType.Star)
                });
                topPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(12)
                });
                topPanelsGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(7, GridUnitType.Star)
                });

                topPanelsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                topPanelsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(topPanelsGrid, 1);
                root.Children.Add(topPanelsGrid);

                RenderMasterPanel(eng, topPanelsGrid);
                RenderInstrument(topPanelsGrid);
                RenderCopierStatusPanel(topPanelsGrid);
                
                // -- Tab Menu Panels --
                _statusBox = RenderStatusBox();
                _mainContentHost = new ContentControl
                {
                    Margin = new Thickness(0)
                };

                Grid.SetRow(_mainContentHost, 2);
                root.Children.Add(_mainContentHost);

                // ---------------- Instrument Tabs ----------------
                RenderInstrumentTabs(root);

                // ---------------- Hook engine events ----------------
                eng.OnStatus += msg =>
                {
                    if (_isClosing || _window == null)
                        return;
                    
                    var display = _uiDispatcher ?? _window?.Dispatcher;
                    if (display == null) return;

                    display.InvokeAsync(() =>
                    {
                        var line = $"{DateTime.Now:HH:mm:ss}  {msg}\n";

                        _statusBox?.AppendText(line);
                        _statusBox?.ScrollToEnd();

                        _diagWindowTextBox?.AppendText(line);
                        _diagWindowTextBox?.ScrollToEnd();
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
                        RefreshCopierStatusPanel();
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
                        RefreshCopierStatusPanel();
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

                // ATMs
                LoadAtmTemplatesInto(_masterAtmBox, includeInherit: false);

                // ---------------- UI -> Engine wiring ----------------
                RenderButtons(copyOn: eng.CopyEnabled);
                RenderFlattenAllButtonState();
                RefreshCopierStatusPanel();
                
                _topPanelsGrid = topPanelsGrid;
                RefreshMainMenuTabs();
                RefreshMainMenuContent();
                
                return root;
            
            }
            catch (Exception ex)
            {
                LogUnhandled("BuildUi()", ex);
                throw;
            }
        }
        
        private void RenderMasterSubmitButtonsState()
        {
            var pending = _engine != null && _engine.IsMasterSubmitInFlight();

            if (_btnBuyMkt != null)
                _btnBuyMkt.IsEnabled = !pending;

            if (_btnSellMkt != null)
                _btnSellMkt.IsEnabled = !pending;
        }
    }
}