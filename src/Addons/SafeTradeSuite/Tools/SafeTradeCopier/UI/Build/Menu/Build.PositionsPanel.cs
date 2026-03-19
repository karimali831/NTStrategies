using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Grid _positionsGrid;
        private bool _positionsRefreshPending;

        private void RenderPositionsPanel(Grid root)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            _positionsGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 0)
            };

            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  }); // Account
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  });  // Conn
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  });  // Open Pos
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Realized
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Lock

            BuildPositionsHeaderRow(_positionsGrid);
            InvalidatePositionsPanel();

            stack.Children.Add(_positionsGrid);

            var positionsFieldset = BuildFieldset("Positions", stack);
            root.Children.Add(positionsFieldset);
        }

        private static void BuildPositionsHeaderRow(Grid g)
        {
            g.RowDefinitions.Clear();
            g.Children.Clear();

            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var headerBg = new Border
            {
                Background = IsDarkTheme()
                    ? new SolidColorBrush(Color.FromRgb(32, 32, 32))
                    : new SolidColorBrush(Color.FromRgb(245, 245, 245))
            };

            Grid.SetRow(headerBg, 0);
            Grid.SetColumn(headerBg, 0);
            Grid.SetColumnSpan(headerBg, g.ColumnDefinitions.Count);
            Panel.SetZIndex(headerBg, -1);
            g.Children.Add(headerBg);

            AddPositionsHeaderText(g, "Account", 0);
            AddPositionsHeaderText(g, "Conn", 1);
            AddPositionsHeaderText(g, "Open", 2);
            AddPositionsHeaderText(g, "Realized", 3);
            AddPositionsHeaderText(g, "Lock", 4);
        }

        private static void AddPositionsHeaderText(Grid g, string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = WindowForegroundBrush(),
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(tb, 0);
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }

        private static Brush PositionsRowBackgroundBrush(int rowIndex)
        {
            if (IsDarkTheme())
                return rowIndex % 2 == 0
                    ? new SolidColorBrush(Color.FromRgb(28, 28, 28))
                    : new SolidColorBrush(Color.FromRgb(34, 34, 34));

            return rowIndex % 2 == 0
                ? new SolidColorBrush(Color.FromRgb(250, 250, 250))
                : new SolidColorBrush(Color.FromRgb(242, 242, 242));
        }
        
        private static void AddPositionsRowBackground(Grid g, int row, Brush background)
        {
            if (g == null)
                return;

            var rect = new Border
            {
                Background = background,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };

            Grid.SetRow(rect, row);
            Grid.SetColumn(rect, 0);
            Grid.SetColumnSpan(rect, g.ColumnDefinitions.Count);
            Panel.SetZIndex(rect, -1);
            g.Children.Add(rect);
        }
        
        private void RefreshPositionsPanel()
        {
            if (_positionsGrid == null)
                return;

            var accounts = GetSelectableAccounts();
            var instr = GetInstrument();
            var master = _masterBox?.SelectedItem as Account;

            BuildPositionsHeaderRow(_positionsGrid);

            var rowIndex = 1;
            foreach (var acc in accounts)
            {
                if (acc == null)
                    continue;
                
                AddPositionsRowBackground(_positionsGrid, rowIndex, PositionsRowBackgroundBrush(rowIndex));

                _positionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var realized = 0.0;
                var unrealized = 0.0;

                lock (_uiPnl)
                {
                    if (_uiPnl.TryGetValue(acc.Name, out var snap))
                    {
                        realized = snap.r;
                        unrealized = snap.u;
                    }
                }

                var openCount = GetOpenPositionCount(acc);
   
                var connText = GetAccountConnectionLabel(acc);
                if (string.IsNullOrWhiteSpace(connText))
                    connText = acc.ConnectionStatus == ConnectionStatus.Connected ? "Connected" : acc.ConnectionStatus.ToString();

                var lockText = "OK";
                if (_engine != null && !_engine.CanEnterForRisk(acc, out var riskReason))
                    lockText = string.IsNullOrWhiteSpace(riskReason) ? "Locked" : riskReason;

      
                AddPositionsCell(_positionsGrid, acc.Name, rowIndex, 0, WindowForegroundBrush());
                AddPositionsCell(_positionsGrid, connText, rowIndex, 1,
                    acc.ConnectionStatus == ConnectionStatus.Connected ? SuccessActionBrush() : DangerActionBrush());
                AddPositionsCell(_positionsGrid, openCount.ToString(), rowIndex, 2, WindowForegroundBrush());
                AddPositionsCell(_positionsGrid, FmtUsd(realized), rowIndex, 3, GetPnlValueBrush(realized));
                AddPositionsCell(_positionsGrid, lockText, rowIndex, 4,
                    string.Equals(lockText, "OK", StringComparison.OrdinalIgnoreCase) ? SuccessActionBrush() : DangerActionBrush());

                rowIndex++;
            }
        }

        private static void AddPositionsCell(Grid g, string text, int row, int col, Brush foreground)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                Margin = new Thickness(6, 4, 6, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = foreground,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }

        private static int GetOpenPositionCount(Account acc)
        {
            if (acc?.Positions == null)
                return 0;

            var count = 0;

            foreach (var p in acc.Positions)
            {
                if (p == null)
                    continue;

                var qty = Math.Abs((int)Math.Round((double)p.Quantity, MidpointRounding.AwayFromZero));
                if (qty > 0)
                    count++;
            }

            return count;
        }
        
        private void InvalidatePositionsPanel()
        {
            if (_positionsRefreshPending)
                return;

            InvalidateUi(
                RefreshPositionsPanel,
                () => _positionsRefreshPending = true,
                () => _positionsRefreshPending = false
            );
        }
    }
}