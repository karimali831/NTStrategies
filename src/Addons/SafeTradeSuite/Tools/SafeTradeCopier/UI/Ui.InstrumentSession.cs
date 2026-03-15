using System;
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
        private TabControl _instrumentTabs;
        private Border _btnAddInstrumentTab;
        private Point _instrumentTabDragStart;
        private InstrumentSession _draggingInstrumentSession;
        
        private bool _isInstrumentTabDragging;
        private Popup _instrumentTabInsertPopup;
        private Border _instrumentTabInsertMarker;

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

            NormalizeInstrumentSessions();

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
                Width = 4,
                Height = 30,
                CornerRadius = new CornerRadius(2),
                Background = PrimaryActionBrush(),
                BorderBrush = PrimaryActionBrush(),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Opacity = 1.0
            };

            _instrumentTabInsertPopup = new Popup
            {
                AllowsTransparency = true,
                Placement = PlacementMode.Absolute,
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
                SaveUiToActiveSession();
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

            var targetTab = GetInstrumentTabItemFromDropSource(e.OriginalSource as DependencyObject);

            if (targetTab == null)
            {
                ShowInstrumentTabInsertIndicatorAtEnd();
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            var targetSession = targetTab.Tag as InstrumentSession;
            if (targetSession == null)
            {
                HideInstrumentTabInsertIndicator();
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var insertAfter = ShouldInsertAfterTarget(targetTab, e, draggedSession);
            ShowInstrumentTabInsertIndicator(targetTab, insertAfter);

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

            var targetTab = GetInstrumentTabItemFromDropSource(e.OriginalSource as DependencyObject);

            if (targetTab == null)
            {
                MoveInstrumentSessionToEnd(draggedSession);
                e.Handled = true;
                return;
            }

            var targetSession = targetTab.Tag as InstrumentSession;
            if (targetSession == null)
            {
                e.Handled = true;
                return;
            }

            var insertAfter = ShouldInsertAfterTarget(targetTab, e, draggedSession);
            ReorderInstrumentSession(draggedSession, targetSession, insertAfter);
            e.Handled = true;
        }

        private void OnInstrumentTabsDragLeave(object sender, DragEventArgs e)
        {
            HideInstrumentTabInsertIndicator();
        }

        private void ShowInstrumentTabInsertIndicator(TabItem targetTab, bool insertAfter)
        {
            if (_instrumentTabs == null || targetTab == null || _instrumentTabInsertPopup == null)
                return;

            var targetTopLeft = targetTab.TranslatePoint(new Point(0, 0), _instrumentTabs);
            var x = insertAfter
                ? targetTopLeft.X + targetTab.ActualWidth
                : targetTopLeft.X;

            var topLeftOnScreen = _instrumentTabs.PointToScreen(new Point(0, 0));

            _instrumentTabInsertPopup.HorizontalOffset = topLeftOnScreen.X + x - 1.5;
            _instrumentTabInsertPopup.VerticalOffset = topLeftOnScreen.Y + 2;
            _instrumentTabInsertPopup.IsOpen = true;
        }

        private void ShowInstrumentTabInsertIndicatorAtEnd()
        {
            if (_instrumentTabs == null || _instrumentTabs.Items.Count == 0 || _instrumentTabInsertPopup == null)
                return;

            if (!(_instrumentTabs.Items[_instrumentTabs.Items.Count - 1] is TabItem lastTab))
                return;

            ShowInstrumentTabInsertIndicator(lastTab, insertAfter: true);
        }

        private void HideInstrumentTabInsertIndicator()
        {
            if (_instrumentTabInsertPopup != null)
                _instrumentTabInsertPopup.IsOpen = false;
        }

        private void MoveInstrumentSessionToEnd(InstrumentSession draggedSession)
        {
            if (draggedSession == null)
                return;

            var fromIndex = _instrumentSessions.IndexOf(draggedSession);
            if (fromIndex < 0)
                return;

            if (fromIndex == _instrumentSessions.Count - 1)
                return;

            _instrumentSessions.RemoveAt(fromIndex);
            _instrumentSessions.Add(draggedSession);
            _activeInstrumentSession = draggedSession;

            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
        }

        private bool ShouldInsertAfterTarget(TabItem targetTab, DragEventArgs e, InstrumentSession draggedSession)
        {
            if (targetTab == null || draggedSession == null || _instrumentTabs == null)
                return false;

            var targetSession = targetTab.Tag as InstrumentSession;
            if (targetSession == null)
                return false;

            var fromIndex = _instrumentSessions.IndexOf(draggedSession);
            var targetIndex = _instrumentSessions.IndexOf(targetSession);

            if (fromIndex < 0 || targetIndex < 0)
                return false;

            var posInTabs = e.GetPosition(_instrumentTabs);
            var targetLeft = targetTab.TranslatePoint(new Point(0, 0), _instrumentTabs).X;
            var targetMid = targetLeft + (targetTab.ActualWidth / 2.0);

            if (fromIndex < targetIndex)
                return posInTabs.X >= targetLeft + 6;

            if (fromIndex > targetIndex)
                return posInTabs.X >= targetMid;

            return false;
        }

        private static TabItem GetInstrumentTabItemFromDropSource(DependencyObject source)
        {
            var current = source;

            while (current != null)
            {
                if (current is TabItem tab && tab.Tag is InstrumentSession)
                    return tab;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
        
        private void ReorderInstrumentSession(InstrumentSession draggedSession, InstrumentSession targetSession, bool insertAfter)
        {
            if (draggedSession == null || targetSession == null)
                return;

            var fromIndex = _instrumentSessions.IndexOf(draggedSession);
            var targetIndex = _instrumentSessions.IndexOf(targetSession);

            if (fromIndex < 0 || targetIndex < 0 || fromIndex == targetIndex)
                return;

            SaveUiToActiveSession();

            _instrumentSessions.RemoveAt(fromIndex);

            if (fromIndex < targetIndex)
                targetIndex--;

            var insertIndex = insertAfter ? targetIndex + 1 : targetIndex;

            if (insertIndex < 0)
                insertIndex = 0;

            if (insertIndex > _instrumentSessions.Count)
                insertIndex = _instrumentSessions.Count;

            _instrumentSessions.Insert(insertIndex, draggedSession);
            _activeInstrumentSession = draggedSession;

            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
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
            if (session == null)
                return;

            if (ReferenceEquals(_activeInstrumentSession, session))
                return;

            SaveUiToActiveSession();
            _activeInstrumentSession = session;

            RefreshInstrumentSelectorItems();
            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
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
                    SaveUiToActiveSession(saveInstrumentName: false);
                    _activeInstrumentSession = existing;
                }

                if (refreshSelector)
                    RefreshInstrumentSelectorItems();

                RefreshInstrumentTabs();
                LoadActiveSessionToUi();
                return;
            }

            SaveUiToActiveSession(saveInstrumentName: false);

            var session = new InstrumentSession
            {
                InstrumentName = normalized,
                MasterAccount = _masterBox?.SelectedItem as Account,
                MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1),
                MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None"
            };

            _instrumentSessions.Add(session);
            _activeInstrumentSession = session;

            NormalizeInstrumentSessions();

            if (refreshSelector)
                RefreshInstrumentSelectorItems();

            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
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

            SaveUiToActiveSession();

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

            RefreshInstrumentSelectorItems();
            RefreshInstrumentTabs();
            LoadActiveSessionToUi();
            RenderFlattenAllButtonState();
        }

        private void SaveUiToActiveSession()
        {
            SaveUiToActiveSession(saveInstrumentName: true);
        }

        private void SaveUiToActiveSession(bool saveInstrumentName)
        {
            try
            {
                if (_activeInstrumentSession == null)
                    return;

                if (saveInstrumentName)
                {
                    var selectedInstrument = NormalizeInstrumentName(GetSelectedInstrumentName());
                    if (IsValidInstrumentName(selectedInstrument))
                        _activeInstrumentSession.InstrumentName = selectedInstrument;
                }

                _activeInstrumentSession.MasterAccount = _masterBox?.SelectedItem as Account;
                _activeInstrumentSession.MasterQty = ParseQtyOrDefault(_masterQtyBox?.Text, 1);
                _activeInstrumentSession.MasterAtm = (_masterAtmBox?.SelectedItem as string) ?? "None";

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

                    var atm = (r.AtmOverrideBox?.SelectedItem as string) ?? "(inherit master)";
                    _activeInstrumentSession.FollowerAtmOverrides[accName] = atm;
                }
            }
            catch (Exception ex)
            {
                LogUnhandled("SafeTradeCopierTool.SaveUiToActiveSession()", ex);
                throw;
            }
        }

        private void LoadActiveSessionToUi()
        {
            try
            {
                if (_activeInstrumentSession == null)
                    return;

                _suppressSessionUiEvents = true;
                try
                {
                    RefreshInstrumentSelectorItems();

                    var instrumentName = NormalizeInstrumentName(_activeInstrumentSession.InstrumentName);

                    if (!string.IsNullOrWhiteSpace(instrumentName) && !_instrumentSelector.Items.Contains(instrumentName))
                        _instrumentSelector.Items.Add(instrumentName);

                    _instrumentSelector.Text = instrumentName;
                    _instrumentSelector.SelectedItem = instrumentName;

                    var targetMaster = _activeInstrumentSession.MasterAccount;
                    if (targetMaster == null)
                        targetMaster = _masterBox.SelectedItem as Account ?? GetSelectableAccounts().FirstOrDefault(IsSimAccount) ?? GetSelectableAccounts().FirstOrDefault();

                    _masterBox.SelectedItem = targetMaster;

                    if (_activeInstrumentSession.MasterAccount == null)
                        _activeInstrumentSession.MasterAccount = targetMaster;

                    _masterQtyBox.Text =
                        (_activeInstrumentSession.MasterQty > 0 ? _activeInstrumentSession.MasterQty : 1).ToString();

                    _masterAtmBox.SelectedItem = _activeInstrumentSession.MasterAtm ?? "None";

                    foreach (var r in _followerRows)
                    {
                        if (r?.Account == null)
                            continue;

                        var accName = r.Account.Name;

                        r.EnabledCheck.IsChecked =
                            _activeInstrumentSession.FollowersEnabled.TryGetValue(accName, out var included) &&
                            included;

                        r.QtyOverrideBox.Text =
                            _activeInstrumentSession.FollowerQtyOverrides.TryGetValue(accName, out var qv)
                                ? qv.ToString()
                                : "";

                        var atm =
                            _activeInstrumentSession.FollowerAtmOverrides.TryGetValue(accName, out var av)
                                ? av
                                : "(inherit master)";

                        r.AtmOverrideBox.SelectedItem = atm;
                        RenderFollowerRowState(r);
                    }
                }
                finally
                {
                    _suppressSessionUiEvents = false;
                }

                ApplyConfigFromUi();
                RenderFollowerRowsState();
            }
            catch (Exception ex)
            {
                LogUnhandled("SafeTradeCopierTool.LoadActiveSessionToUi()", ex);
                throw;
            }
        }
    }
}