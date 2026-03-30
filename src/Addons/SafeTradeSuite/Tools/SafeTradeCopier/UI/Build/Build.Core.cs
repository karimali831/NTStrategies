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
                _lastAccountsSnapshot = accounts.Select(a => new AccountSnap(a)).ToList();
                
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
                        RenderCopierButton();
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
                        RenderCopierButton();
                        RenderFollowerRowsState();
                        RenderMasterSubmitButtonsState();
                    }, DispatcherPriority.Background);
                };
                
                // ---------------- Populate accounts + followers ----------------
                _masterBox.ItemsSource = accounts;
                _masterBox.DisplayMemberPath = "Name";

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

                using (BeginSessionUiSuppression())
                {
                    _masterBox.SelectedItem = initialMaster;
                    LoadMasterAtmTemplatesIntoSuppress(_masterBracketBox);
                }

                // ---------------- UI -> Engine wiring ----------------
                RenderCopierButton();
                RefreshCopierStatusPanel();
                
                _topPanelsGrid = topPanelsGrid;
                _copierContent = null;
                _settingsContent = null;
                RefreshMainMenuTabs();
                RefreshMainMenuContent();
                RenderMasterSubmitButtonsState();
                
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
            if (_engine == null)
                return;

            var pending = _engine.IsMasterSubmitInFlight();
            var masterConnected = IsMasterConnected(out var masterAcc);
            var masterLocked = _engine.TryGetAccountLockReason(masterAcc, out var lockShortReason, out var lockFullReason);

            var instr = GetInstrument();
            var hasOpenPosition = masterAcc != null && instr != null && HasOpenInstrumentPosition(masterAcc, instr);

            var enabled = !pending && masterConnected && !masterLocked && !hasOpenPosition;

            if (_btnBuyMkt != null)
            {
                _btnBuyMkt.IsEnabled = enabled;
                _btnBuyMkt.ToolTip = !masterConnected
                    ? "Master account is disconnected"
                    : masterLocked
                        ? lockShortReason
                        : hasOpenPosition
                            ? $"Master already has an open position on {instr?.FullName ?? "this instrument"}"
                            : "Submit a market buy on the master account";

                ApplyButtonTheme(_btnBuyMkt, FormButtonTone.Success, FormButtonStyle.Solid, enabled);
            }

            if (_btnSellMkt != null)
            {
                _btnSellMkt.IsEnabled = enabled;
                _btnSellMkt.ToolTip = !masterConnected
                    ? "Master account is disconnected"
                    : masterLocked
                        ? lockFullReason
                        : hasOpenPosition
                            ? $"Master already has an open position on {instr?.FullName ?? "this instrument"}"
                            : "Submit a market sell on the master account";

                ApplyButtonTheme(_btnSellMkt, FormButtonTone.Danger, FormButtonStyle.Solid, enabled);
            }
        }
    }
}