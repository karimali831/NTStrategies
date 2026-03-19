using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private sealed class MainMenuTabItem
        {
            public MainMenuTab Key { get; set; }
            public Border Border { get; set; }
            public TextBlock Text { get; set; }
            public Path Icon { get; set; }
        }

        private ContentControl _mainContentHost;
        private Grid _topPanelsGrid;
        private Border _mainMenuTabsWrapper;
        private StackPanel _mainMenuTabsPanel;
        private readonly List<MainMenuTabItem> _mainMenuTabItems = new List<MainMenuTabItem>();
        private MainMenuTab _activeMainMenuTab = MainMenuTab.Copier;
        private Window _diagWindow;
        private TextBox _diagWindowTextBox;

        private Border BuildMainMenuTabs()
        {
            _mainMenuTabsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            _mainMenuTabsWrapper = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 8),
                Child = _mainMenuTabsPanel
            };

            RebuildMainMenuTabs();
            return _mainMenuTabsWrapper;
        }

        private void BuildMenuTabs()
        {
            _mainMenuTabItems.Clear();
            _mainMenuTabsPanel.Children.Clear();
            
            AddMainMenuTab(MainMenuTab.Copier, "Copier", CreateCopierIcon());
            AddMainMenuTab(MainMenuTab.Positions, "Positions", CreateGridIcon());
            AddMainMenuTab(MainMenuTab.Trades, "Trades", CreateListIcon());
            AddMainMenuTab(MainMenuTab.Settings, "Settings", CreateCogIcon());

            if (_showStatusBox)
                AddMainMenuTab(MainMenuTab.Diag, "Diag", CreateListIcon());
            
            RefreshMainMenuTabs();
        }

        private void AddMainMenuTab(MainMenuTab key, string title, Geometry iconGeometry)
        {
            var icon = new Path
            {
                Data = iconGeometry,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = MutedForegroundBrush()
            };

            var text = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = MutedForegroundBrush(),
                FontWeight = FontWeights.Normal
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            content.Children.Add(icon);
            content.Children.Add(text);

            var border = new Border
            {
                Background = TabBackgroundBrush(),
                BorderBrush = SectionBorderBrush(),
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(6, 6, 0, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 6, 0),
                Child = content,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (key == MainMenuTab.Diag)
                {
                    OpenDiagWindow();
                    return;
                }

                _activeMainMenuTab = key;
                RefreshMainMenuTabs();
                RefreshMainMenuContent();
            };

            _mainMenuTabsPanel.Children.Add(border);

            _mainMenuTabItems.Add(new MainMenuTabItem
            {
                Key = key,
                Border = border,
                Text = text,
                Icon = icon
            });
        }
        
        private void RebuildMainMenuTabs()
        {
            if (_mainMenuTabsPanel == null)
                return;

            // if user hid Diag while it was active, move back to Copier
            if (!_showStatusBox && _activeMainMenuTab == MainMenuTab.Diag)
                _activeMainMenuTab = MainMenuTab.Copier;
            
            BuildMenuTabs();
        }

        private void RefreshMainMenuTabs()
        {
            foreach (var item in _mainMenuTabItems)
            {
                var active = item.Key == _activeMainMenuTab;

                item.Border.Background = active
                    ? TabSelectedBackgroundBrush()
                    : TabBackgroundBrush();

                item.Border.BorderBrush = active
                    ? TabSelectedBorderBrush()
                    : SectionBorderBrush();

                item.Text.Foreground = active
                    ? WindowForegroundBrush()
                    : MutedForegroundBrush();

                item.Text.FontWeight = active
                    ? FontWeights.SemiBold
                    : FontWeights.Normal;

                item.Icon.Fill = active
                    ? WindowForegroundBrush()
                    : MutedForegroundBrush();
            }
        }
        
        private void RefreshMainMenuContent()
        {
            if (_mainContentHost == null || _topPanelsGrid == null)
                return;

            switch (_activeMainMenuTab)
            {
                case MainMenuTab.Copier:
                    _topPanelsGrid.Visibility = Visibility.Visible;
                    _mainContentHost.Content = BuildFollowersContent();
                    break;

                case MainMenuTab.Positions:
                    _topPanelsGrid.Visibility = Visibility.Visible;
                    _mainContentHost.Content = BuildPositionsContent();
                    break;

                case MainMenuTab.Trades:
                    _topPanelsGrid.Visibility = Visibility.Collapsed;
                    _mainContentHost.Content = BuildTradesPlaceholder();
                    break;

                case MainMenuTab.Settings:
                    _topPanelsGrid.Visibility = Visibility.Collapsed;
                    _mainContentHost.Content = BuildSettingsContent();
                    break;
            }
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

        private UIElement BuildPositionsContent()
        {
            var host = new Grid();

            _positionsGrid = null;
            RenderPositionsPanel(host);
            
            return host;
        }

        private UIElement BuildTradesPlaceholder()
        {
            return new Border
            {
                Background = SectionBackgroundBrush(),
                BorderBrush = SectionBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = new TextBlock
                {
                    Text = "Trades panel coming soon.",
                    Foreground = WindowForegroundBrush()
                }
            };
        }

        private UIElement BuildSettingsContent()
        {
            var root = new StackPanel
            {
                Margin = new Thickness(0)
            };

            var generalFieldset = RenderGeneralFieldset();
            var beFieldset = RenderBreakEvenFieldset();

            _riskFieldsetHost = new ContentControl
            {
                Content = RenderRiskFieldset()
            };

            root.Children.Add(generalFieldset);
            root.Children.Add(beFieldset);
            root.Children.Add(_riskFieldsetHost);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };
        }

        private void OpenDiagWindow()
        {
            if (!_showStatusBox)
                return;

            if (_diagWindow != null)
            {
                _diagWindow.Activate();
                return;
            }

            _diagWindowTextBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = SectionBackgroundBrush(),
                Foreground = WindowForegroundBrush(),
                Text = _statusBox?.Text ?? ""
            };

            _diagWindow = new Window
            {
                Title = "Safe Trade Copier Diagnostics",
                Width = 700,
                Height = 420,
                Owner = _window,
                Background = WindowBackgroundBrush(),
                Foreground = WindowForegroundBrush(),
                Content = _diagWindowTextBox
            };

            _diagWindow.Closed += (s, e) =>
            {
                _diagWindow = null;
                _diagWindowTextBox = null;
            };

            _diagWindow.Show();
        }

        private static Geometry CreateCopierIcon()
        {
            return Geometry.Parse("M3,3 H13 V13 H3 Z M6,6 H16 V16 H6 Z");
        }
        
        private static Geometry CreateUsersIcon()
        {
            return Geometry.Parse("F1 M 4,8 A 2,2 0 1 1 4,4 A 2,2 0 1 1 4,8 M 8.5,7.5 A 1.5,1.5 0 1 1 8.5,4.5 A 1.5,1.5 0 1 1 8.5,7.5 M 1,11 C 1,9.5 3,9 4,9 C 5,9 7,9.5 7,11 M 6.5,11 C 6.7,10 7.8,9.3 9,9.3 C 10,9.3 11,9.8 11,11");
        }

        private static Geometry CreateGridIcon()
        {
            return Geometry.Parse("F1 M 1,1 L 5,1 L 5,5 L 1,5 Z M 7,1 L 11,1 L 11,5 L 7,5 Z M 1,7 L 5,7 L 5,11 L 1,11 Z M 7,7 L 11,7 L 11,11 L 7,11 Z");
        }

        private static Geometry CreateListIcon()
        {
            return Geometry.Parse("F1 M 1,2 L 3,2 L 3,4 L 1,4 Z M 4.5,2 L 11,2 L 11,4 L 4.5,4 Z M 1,5.5 L 3,5.5 L 3,7.5 L 1,7.5 Z M 4.5,5.5 L 11,5.5 L 11,7.5 L 4.5,7.5 Z M 1,9 L 3,9 L 3,11 L 1,11 Z M 4.5,9 L 11,9 L 11,11 L 4.5,11 Z");
        }

        private static Geometry CreateCogIcon()
        {
            return Geometry.Parse("F1 M 6,1.5 L 7,1.5 L 7.3,2.7 C 7.7,2.8 8.1,3 8.4,3.2 L 9.5,2.6 L 10.2,3.3 L 9.6,4.4 C 9.8,4.7 10,5.1 10.1,5.5 L 11.3,5.8 L 11.3,6.8 L 10.1,7.1 C 10,7.5 9.8,7.9 9.6,8.2 L 10.2,9.3 L 9.5,10 L 8.4,9.4 C 8.1,9.6 7.7,9.8 7.3,9.9 L 7,11.1 L 6,11.1 L 5.7,9.9 C 5.3,9.8 4.9,9.6 4.6,9.4 L 3.5,10 L 2.8,9.3 L 3.4,8.2 C 3.2,7.9 3,7.5 2.9,7.1 L 1.7,6.8 L 1.7,5.8 L 2.9,5.5 C 3,5.1 3.2,4.7 3.4,4.4 L 2.8,3.3 L 3.5,2.6 L 4.6,3.2 C 4.9,3 5.3,2.8 5.7,2.7 Z M 6.5,4.2 A 1.8,1.8 0 1 1 6.5,7.8 A 1.8,1.8 0 1 1 6.5,4.2");
        }
    }
}