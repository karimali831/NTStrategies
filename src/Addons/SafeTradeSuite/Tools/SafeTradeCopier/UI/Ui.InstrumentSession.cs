﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NinjaTrader.Cbi;
using Mouse = System.Windows.Input.Mouse;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _isLoadingSessionUi;
        private TabControl _instrumentTabs;
        private Point _instrumentTabDragStart;
        private InstrumentSession _draggingInstrumentSession;
        private bool _isInstrumentTabDragging;
        private Popup _instrumentTabInsertPopup;
        private Border _instrumentTabInsertMarker;
        
        private static string UiInstrumentKey(Account acc, Instrument instr)
        {
            return (acc?.Name ?? "") + "|" + (instr?.FullName ?? "");
        }

        private void RefreshInstrumentTabs()
        {
            if (_instrumentTabs == null)
                return;

            EnsureInstrumentTabDragUi();

            _instrumentTabs.AllowDrop = true;
            _instrumentTabs.Drop -= OnInstrumentTabsDrop;
            _instrumentTabs.Drop += OnInstrumentTabsDrop;
            _instrumentTabs.DragOver -= OnInstrumentTabsDragOver;
            _instrumentTabs.DragOver += OnInstrumentTabsDragOver;
            _instrumentTabs.DragLeave -= OnInstrumentTabsDragLeave;
            _instrumentTabs.DragLeave += OnInstrumentTabsDragLeave;
            
            LogInstrumentSessions("RefreshInstrumentTabs.start");
            
            NormalizeInstrumentSessions();

            LogInstrumentSessions("RefreshInstrumentTabs.afterNormalize");

            _instrumentTabs.SelectionChanged -= OnInstrumentTabsSelectionChanged;

            try
            {
                _instrumentTabs.Items.Clear();

                foreach (var session in _instrumentSessions)
                    _instrumentTabs.Items.Add(BuildInstrumentTabItem(session));

                EnsureActiveInstrumentSession();
                SelectActiveInstrumentTab();
            }
            finally
            {
                _instrumentTabs.SelectionChanged += OnInstrumentTabsSelectionChanged;
            }
        }

        private void EnsureInstrumentTabDragUi()
        {
            if (_instrumentTabInsertPopup != null)
                return;

            _instrumentTabInsertMarker = new Border
            {
                Width = 2,
                Height = 22,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromRgb(54, 137, 230)),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsHitTestVisible = false,
                Opacity = 0.95,
                SnapsToDevicePixels = true
            };

            _instrumentTabInsertPopup = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Relative,
                PlacementTarget = _instrumentTabs,
                StaysOpen = true,
                IsHitTestVisible = false,
                Child = _instrumentTabInsertMarker
            };
        }

        private void EnsureActiveInstrumentSession()
        {
            if (_activeInstrumentSession == null && _instrumentSessions.Count > 0)
                _activeInstrumentSession = _instrumentSessions[0];
        }

        private void SelectActiveInstrumentTab()
        {
            if (_instrumentTabs == null || _activeInstrumentSession == null)
                return;

            foreach (var obj in _instrumentTabs.Items)
            {
                if (obj is TabItem tab && ReferenceEquals(tab.Tag, _activeInstrumentSession))
                {
                    _instrumentTabs.SelectedItem = tab;
                    break;
                }
            }
        }
        
        private void OnInstrumentTabHeaderMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsInstrumentTabCloseButtonSource(e.OriginalSource as DependencyObject))
            {
                _draggingInstrumentSession = null;
                _isInstrumentTabDragging = false;
                HideInstrumentTabInsertIndicator();
                return;
            }

            _instrumentTabDragStart = sender is IInputElement sourceElement
                ? Mouse.GetPosition(sourceElement)
                : new Point(0, 0);

            if (sender is FrameworkElement fe && fe.Tag is InstrumentSession session)
                _draggingInstrumentSession = session;
            else
                _draggingInstrumentSession = null;

            _isInstrumentTabDragging = false;
        }

        private static bool IsInstrumentTabCloseButtonSource(DependencyObject source)
        {
            var current = source;

            while (current != null)
            {
                if (current is Button btn)
                {
                    var tip = (btn.ToolTip as string ?? "").Trim();
                    if (string.Equals(tip, "Remove instrument", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void OnInstrumentTabHeaderMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
                return;

            if (_draggingInstrumentSession == null)
                return;

            var currentPos = sender is IInputElement sourceElement
                ? Mouse.GetPosition(sourceElement)
                : new Point(0, 0);

            var diff = _instrumentTabDragStart - currentPos;

            if (!_isInstrumentTabDragging)
            {
                if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                _isInstrumentTabDragging = true;
            }

            var dragSession = _draggingInstrumentSession;

            DragDrop.DoDragDrop(
                _instrumentTabs,
                new DataObject(typeof(InstrumentSession), dragSession),
                DragDropEffects.Move);
            
            _draggingInstrumentSession = null;
            _isInstrumentTabDragging = false;
            
            HideInstrumentTabInsertIndicator();
        }

        private void OnInstrumentTabsDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(InstrumentSession)))
            {
                HideInstrumentTabInsertIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var draggedSession = e.Data.GetData(typeof(InstrumentSession)) as InstrumentSession;
            if (draggedSession == null)
            {
                HideInstrumentTabInsertIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var insertIndex = GetInsertIndexFromDrag(e, draggedSession);
            if (insertIndex == null)
            {
                HideInstrumentTabInsertIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (WouldBeNoOpDrop(draggedSession, insertIndex.Value))
            {
                HideInstrumentTabInsertIndicator();
            }
            
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnInstrumentTabsDrop(object sender, DragEventArgs e)
        {
            HideInstrumentTabInsertIndicator();

            if (!e.Data.GetDataPresent(typeof(InstrumentSession)))
                return;

            var draggedSession = e.Data.GetData(typeof(InstrumentSession)) as InstrumentSession;
            if (draggedSession == null)
                return;

            var insertIndex = GetInsertIndexFromDrag(e, draggedSession);
            if (insertIndex == null)
            {
                e.Handled = true;
                return;
            }

            if (WouldBeNoOpDrop(draggedSession, insertIndex.Value))
            {
                e.Handled = true;
                return;
            }

            MoveInstrumentSessionToInsertIndex(draggedSession, insertIndex.Value);
            e.Handled = true;
        }
        
        private int? GetInsertIndexFromDrag(DragEventArgs e, InstrumentSession draggedSession)
        {
            if (_instrumentTabs == null || draggedSession == null)
                return null;

            var tabs = _instrumentTabs.Items
                .OfType<TabItem>()
                .Where(t => t?.Tag is InstrumentSession)
                .ToList();

            if (tabs.Count == 0)
                return 0;

            var mouseX = e.GetPosition(_instrumentTabs).X;
            const double deadBand = 3.0;

            for (var i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                var left = tab.TranslatePoint(new Point(0, 0), _instrumentTabs).X;
                var mid = left + (tab.ActualWidth / 2.0);

                if (mouseX < mid - deadBand)
                    return i;

                if (mouseX <= mid + deadBand)
                    return null;
            }

            // dragging beyond all tabs = insert at end
            return tabs.Count;
        }

        private bool WouldBeNoOpDrop(InstrumentSession draggedSession, int insertIndex)
        {
            if (draggedSession == null)
                return true;

            var fromIndex = _instrumentSessions.IndexOf(draggedSession);
            if (fromIndex < 0)
                return true;

            var normalizedInsertIndex = insertIndex;

            if (normalizedInsertIndex < 0)
                normalizedInsertIndex = 0;

            if (normalizedInsertIndex > _instrumentSessions.Count)
                normalizedInsertIndex = _instrumentSessions.Count;

            // convert insert index to final index after removal
            if (fromIndex < normalizedInsertIndex)
                normalizedInsertIndex--;

            return normalizedInsertIndex == fromIndex;
        }

        private void MoveInstrumentSessionToInsertIndex(InstrumentSession draggedSession, int insertIndex)
        {
            if (draggedSession == null)
                return;

            var fromIndex = _instrumentSessions.IndexOf(draggedSession);
            if (fromIndex < 0)
                return;

            if (insertIndex < 0)
                insertIndex = 0;

            if (insertIndex > _instrumentSessions.Count)
                insertIndex = _instrumentSessions.Count;

            SaveUiToActiveSession("MoveInstrumentSessionToInsertIndex");

            _instrumentSessions.RemoveAt(fromIndex);

            if (fromIndex < insertIndex)
                insertIndex--;

            if (insertIndex < 0)
                insertIndex = 0;

            if (insertIndex > _instrumentSessions.Count)
                insertIndex = _instrumentSessions.Count;

            _instrumentSessions.Insert(insertIndex, draggedSession);
            _activeInstrumentSession = draggedSession;

            LogInstrumentSessions("MoveInstrumentSessionToInsertIndex.afterReorder");

            SafeTradeSuiteRuntime.SaveInstrumentOrder(
                _instrumentSessions.Select(x => x?.InstrumentName));

            LogSavedInstrumentOrder("MoveInstrumentSessionToInsertIndex.afterSaveOrder");

            SavePersistentUiState();

            RefreshInstrumentTabs();
            LoadActiveSessionToUi("MoveInstrumentSessionToInsertIndex");
        }

        private void OnInstrumentTabsDragLeave(object sender, DragEventArgs e)
        {
            HideInstrumentTabInsertIndicator();
        }

        private void HideInstrumentTabInsertIndicator()
        {
            if (_instrumentTabInsertPopup != null)
                _instrumentTabInsertPopup.IsOpen = false;
        }

        private void OnInstrumentTabCloseClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (sender is Button btn && btn.Tag is InstrumentSession session)
                RemoveInstrumentSession(session);
        }

        private void OnInstrumentTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_instrumentTabs?.SelectedItem is TabItem tab && tab.Tag is InstrumentSession session)
                SwitchToSession(session);
        }

        private void SwitchToSession(InstrumentSession session)
        {
            SafeTradeSuiteRuntime.PrintLog(
                $"[SWITCH SESSION] from={_activeInstrumentSession?.InstrumentName} to={session?.InstrumentName}");

            if (session == null)
                return;

            if (ReferenceEquals(_activeInstrumentSession, session))
                return;

            SaveUiToActiveSession("SwitchToSession");
            _activeInstrumentSession = session;

            SafeTradeSuiteRuntime.PrintLog(
                $"[SWITCH SESSION MASTER] sessionMaster={_activeInstrumentSession?.MasterAccount?.Name ?? "null"}");

            RefreshInstrumentSelectorItems();
            RefreshInstrumentTabs();

            var latestAccounts = GetSelectableAccounts();
            RebuildFollowersAndRewire(_engine, latestAccounts);
        }

        private void ActivateOrCreateInstrumentSession(string instrumentName, bool refreshSelector = true)
        {
            var normalized = NormalizeInstrumentName(instrumentName);
            if (!IsValidInstrumentName(normalized))
                return;

            RememberInstrument(normalized);

            _instrumentSessions.RemoveAll(x =>
                x != null &&
                string.IsNullOrWhiteSpace(NormalizeInstrumentName(x.InstrumentName)));

            var existing = _instrumentSessions.FirstOrDefault(x =>
                string.Equals(
                    NormalizeInstrumentName(x?.InstrumentName),
                    normalized,
                    StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!ReferenceEquals(_activeInstrumentSession, existing))
                {
                    SaveUiToActiveSession(saveInstrumentName: false, "ActivateOrCreateInstrumentSession (1)");
                    _activeInstrumentSession = existing;
                }

                if (refreshSelector)
                    RefreshInstrumentSelectorItems();

                RefreshInstrumentTabs();
                LoadActiveSessionToUi("");
                return;
            }

            SaveUiToActiveSession(saveInstrumentName: false, "ActivateOrCreateInstrumentSession (2)");

            var session = new InstrumentSession
            {
                InstrumentName = normalized,
                MasterAccount = GetMasterAccount(),
                MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text),
                MasterAtm = _masterBracketBox?.SelectedItem as string ?? "None"
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;

            NormalizeInstrumentSessions();

            if (refreshSelector)
                RefreshInstrumentSelectorItems();

            SafeTradeSuiteRuntime.SaveInstrumentOrder(
                _instrumentSessions.Select(x => x?.InstrumentName));
            
            RefreshInstrumentTabs();
            LoadActiveSessionToUi("ActivateOrCreateInstrumentSession");
        }

        private void RemoveInstrumentSession(InstrumentSession sessionToRemove)
        {
            if (sessionToRemove == null)
                return;

            if (_instrumentSessions.Count <= 1)
            {
                ShowFriendlyError("Cannot remove tab", "At least one instrument tab must remain open.");
                return;
            }

            SaveUiToActiveSession("RemoveInstrumentSession");

            var instrumentToMaybeForget = NormalizeInstrumentName(sessionToRemove.InstrumentName);
            var idx = _instrumentSessions.IndexOf(sessionToRemove);
            if (idx < 0)
                return;

            _instrumentSessions.RemoveAt(idx);

            if (!IsInstrumentUsedByAnySession(instrumentToMaybeForget, sessionToRemove))
                ForgetInstrument(instrumentToMaybeForget);

            if (_instrumentSessions.Count > 0)
            {
                var nextIdx = Math.Min(idx, _instrumentSessions.Count - 1);
                _activeInstrumentSession = _instrumentSessions[nextIdx];
            }
            else
            {
                _activeInstrumentSession = null;
            }
            
            SafeTradeSuiteRuntime.SaveInstrumentOrder(
                _instrumentSessions.Select(x => x?.InstrumentName));
            
            RefreshInstrumentSelectorItems();
            RefreshInstrumentTabs();
            LoadActiveSessionToUi("RemoveInstrumentSession");
        }

        private void SaveUiToActiveSession(string source)
        {
            SaveUiToActiveSession(saveInstrumentName: true, source: source);
        }

        private void SaveUiToActiveSession(bool saveInstrumentName, string source)
        {
            if (VerboseSessionLogging)
            {
                SafeTradeSuiteRuntime.PrintLog(
                    $"[SAVE SESSION TARGET] source={source} instr={_activeInstrumentSession?.InstrumentName} " +
                    $"sessionHash={_activeInstrumentSession?.GetHashCode() ?? 0}");
            }

            try
            {
                if (_activeInstrumentSession == null)
                    return;

                if (VerboseSessionLogging)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[SAVE SESSION START] source={source ?? "unknown"} " +
                        $"instr={_activeInstrumentSession?.InstrumentName} " +
                        $"uiChecked={DiagCheckedFollowerCount()} uiMap={DiagCheckedFollowers()} " +
                        $"sessionCheckedBefore={DiagSessionCheckedCount()} sessionMapBefore={DiagSessionFollowersMap()}");
                }

                if (saveInstrumentName)
                {
                    var selectedInstrument = NormalizeInstrumentName(GetSelectedInstrumentName());
                    if (IsValidInstrumentName(selectedInstrument))
                        _activeInstrumentSession.InstrumentName = selectedInstrument;
                }

                _activeInstrumentSession.MasterAccount = _masterBox?.SelectedItem as Account;
                _activeInstrumentSession.MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text);
                _activeInstrumentSession.MasterAtm = _masterBracketBox?.SelectedItem as string ?? "None";

                _activeInstrumentSession.FollowersEnabled.Clear();
                _activeInstrumentSession.FollowerQtyOverrides.Clear();
                _activeInstrumentSession.FollowerAtmOverrides.Clear();

                foreach (var r in _followerRows)
                {
                    if (r?.Account == null)
                        continue;

                    var accName = r.Account.Name;

                    _activeInstrumentSession.FollowersEnabled[accName] = r.EnabledCheck?.IsChecked == true;

                    var qtyText = (r.QtyOverrideBox?.Text ?? "").Trim();
                    if (int.TryParse(qtyText, out var qv) && qv > 0)
                        _activeInstrumentSession.FollowerQtyOverrides[accName] = qv;

                    var atm = r.BracketOverrideBox?.SelectedItem as string ?? "Inherit Master";
                    _activeInstrumentSession.FollowerAtmOverrides[accName] = atm;
                }

                if (VerboseSessionLogging)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[SAVE SESSION END] source={source ?? "unknown"} " +
                        $"instr={_activeInstrumentSession?.InstrumentName} " +
                        $"sessionCheckedAfter={DiagSessionCheckedCount()} sessionMapAfter={DiagSessionFollowersMap()}");
                }
            }
            catch (Exception ex)
            {
                LogUnhandled("SafeTradeCopierTool.SaveUiToActiveSession()", ex);
                throw;
            }
        }

        private void LoadActiveSessionToUi(string source)
        {
            if (VerboseSessionLogging)
            {
                SafeTradeSuiteRuntime.PrintLog(
                    $"[LOAD SESSION CALL] source={source} instr={_activeInstrumentSession?.InstrumentName}");
                
                SafeTradeSuiteRuntime.PrintLog(
                    $"[LOAD SESSION TARGET] instr={_activeInstrumentSession?.InstrumentName} " +
                    $"sessionHash={_activeInstrumentSession?.GetHashCode() ?? 0}");
            }

            _isLoadingSessionUi = true;

            try
            {
                if (_activeInstrumentSession == null)
                    return;

                if (VerboseSessionLogging)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[LOAD SESSION START] instr={_activeInstrumentSession?.InstrumentName} " +
                        $"sessionCheckedBefore={DiagSessionCheckedCount()} sessionMapBefore={DiagSessionFollowersMap()}");
                }

                using (BeginSessionUiSuppression())
                {
                    RefreshInstrumentSelectorItems();

                    var instrumentName = NormalizeInstrumentName(_activeInstrumentSession.InstrumentName);

                    if (!string.IsNullOrWhiteSpace(instrumentName) &&
                        !_instrumentSelector.Items.Contains(instrumentName))
                        _instrumentSelector.Items.Add(instrumentName);

                    _instrumentSelector.Text = instrumentName;
                    _instrumentSelector.SelectedItem = instrumentName;

                    var targetMaster = _activeInstrumentSession.MasterAccount
                       ?? GetSelectableAccounts().FirstOrDefault(IsSimAccount)
                       ?? GetSelectableAccounts().FirstOrDefault();

                    _masterBox.SelectedItem = targetMaster;

                    if (_activeInstrumentSession.MasterAccount == null)
                        _activeInstrumentSession.MasterAccount = targetMaster;

                    _masterQtyBox.Text =
                        (_activeInstrumentSession.MasterQty > 0 ? _activeInstrumentSession.MasterQty : 1).ToString();

                    LoadMasterAtmTemplatesIntoSuppress(_masterBracketBox);

                    foreach (var r in _followerRows)
                    {
                        if (r?.Account == null)
                            continue;

                        var accName = r.Account.Name;

                        var included =
                            _activeInstrumentSession.FollowersEnabled.TryGetValue(accName, out var savedIncluded) &&
                            savedIncluded;

                        SetFollowerChecked(r, included, "LoadActiveSessionToUi.restoreFollower");

                        if (VerboseSessionLogging)
                        {
                            SafeTradeSuiteRuntime.PrintLog(
                                $"[RESTORE FOLLOWER] instr={_activeInstrumentSession?.InstrumentName} " +
                                $"acc={accName} saved={included} uiAfter={(r.EnabledCheck?.IsChecked == true ? 1 : 0)}");
                        }

                        r.QtyOverrideBox.Text =
                            _activeInstrumentSession.FollowerQtyOverrides.TryGetValue(accName, out var qv)
                                ? qv.ToString()
                                : "";

                        LoadFollowerAtmTemplatesIntoSuppress(r.BracketOverrideBox, accName);
                        RenderFollowerRowState(r);
                    }

                    if (VerboseSessionLogging)
                    {
                        SafeTradeSuiteRuntime.PrintLog(
                            $"[LOAD SESSION END] instr={_activeInstrumentSession?.InstrumentName} " +
                            $"uiCheckedAfter={DiagCheckedFollowerCount()} uiMapAfter={DiagCheckedFollowers()}");
                    }
                }

                RenderFollowerRowsState();

                if (VerboseSessionLogging)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[POST RENDER ROWS] instr={_activeInstrumentSession?.InstrumentName} " +
                        $"uiChecked={DiagCheckedFollowerCount()} uiMap={DiagCheckedFollowers()}");
                }

                ApplyConfigFromUi();

                if (VerboseSessionLogging)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[POST APPLY CONFIG] instr={_activeInstrumentSession?.InstrumentName} " +
                        $"uiChecked={DiagCheckedFollowerCount()} uiMap={DiagCheckedFollowers()}");
                }

                SyncEngineRequestedStateForActiveSession();

                if (VerboseSessionLogging)
                {
                    SafeTradeSuiteRuntime.PrintLog(
                        $"[POST SET REQUESTED] instr={_activeInstrumentSession?.InstrumentName} " +
                        $"requested={_activeInstrumentSession.IsArmedRequested} " +
                        $"engineRequested={_engine?.IsRequested} armed={_engine?.Armed}");
                }

                RenderCopierButton();
                RefreshFollowerBulkActionButtons();
                RefreshCopierStatusPanel();
                ScrollFollowersToTop();
            }
            catch (Exception ex)
            {
                LogUnhandled("SafeTradeCopierTool.LoadActiveSessionToUi()", ex);
                throw;
            }
            finally
            {
                _isLoadingSessionUi = false;
            }
        }
        
        private void SyncEngineRequestedStateForActiveSession()
        {
            if (_engine == null || _activeInstrumentSession == null)
                return;

            var requested = _activeInstrumentSession.IsArmedRequested;

            if (!requested)
            {
                if (_engine.IsRequested)
                    _engine.SetCopyEnabled(false);

                return;
            }

            if (CanRequestArmedForActiveSession(out _))
            {
                if (!_engine.IsRequested || !_engine.Armed)
                    _engine.SetCopyEnabled(true);

                return;
            }

            if (_engine.IsRequested)
                _engine.SetCopyEnabled(false);
        }
    }
}