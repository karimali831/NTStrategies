using System;
using System.Linq;
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

            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); // Account
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Conn
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Open Pos
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });  // Net Qty
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Realized
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Unrealized
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); // Lock
            _positionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) }); // Mode

            BuildPositionsHeaderRow(_positionsGrid);
            InvalidatePositionsPanel();

            stack.Children.Add(_positionsGrid);

            var positionsFieldset = BuildFieldset("Positions", stack);
            Grid.SetColumn(positionsFieldset, 2);
            root.Children.Add(positionsFieldset);
        }

        private void BuildPositionsHeaderRow(Grid g)
        {
            g.RowDefinitions.Clear();
            g.Children.Clear();

            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddPositionsHeaderText(g, "Account", 0);
            AddPositionsHeaderText(g, "Conn", 1);
            AddPositionsHeaderText(g, "Open", 2);
            AddPositionsHeaderText(g, "Net", 3);
            AddPositionsHeaderText(g, "Realized", 4);
            AddPositionsHeaderText(g, "Unrealized", 5);
            AddPositionsHeaderText(g, "Lock", 6);
            AddPositionsHeaderText(g, "Mode", 7);
        }

        private static void AddPositionsHeaderText(Grid g, string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetRow(tb, 0);
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
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
                var netQty = instr == null ? 0 : GetNetPosition(acc, instr);

                var connText = GetAccountConnectionLabel(acc);
                if (string.IsNullOrWhiteSpace(connText))
                    connText = acc.ConnectionStatus == ConnectionStatus.Connected ? "Connected" : acc.ConnectionStatus.ToString();

                var lockText = "OK";
                if (_engine != null && !_engine.CanEnterForRisk(acc, out var riskReason))
                    lockText = string.IsNullOrWhiteSpace(riskReason) ? "Locked" : riskReason;

                var modeText = GetAccountModeText(acc, master);

                AddPositionsCell(_positionsGrid, acc.Name, rowIndex, 0, Brushes.Black);
                AddPositionsCell(_positionsGrid, connText, rowIndex, 1,
                    acc.ConnectionStatus == ConnectionStatus.Connected ? Brushes.DarkGreen : Brushes.Firebrick);
                AddPositionsCell(_positionsGrid, openCount.ToString(), rowIndex, 2, Brushes.Black);
                AddPositionsCell(_positionsGrid, netQty.ToString(), rowIndex, 3, Brushes.Black);
                AddPositionsCell(_positionsGrid, FmtUsd(realized), rowIndex, 4, GetPnlValueBrush(realized));
                AddPositionsCell(_positionsGrid, FmtUsd(unrealized), rowIndex, 5, GetPnlValueBrush(unrealized));
                AddPositionsCell(_positionsGrid, lockText, rowIndex, 6,
                    string.Equals(lockText, "OK", StringComparison.OrdinalIgnoreCase) ? Brushes.DarkGreen : Brushes.Firebrick);
                AddPositionsCell(_positionsGrid, modeText, rowIndex, 7, Brushes.DimGray);

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
            if (acc == null)
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

        private string GetAccountModeText(Account acc, Account master)
        {
            if (acc == null)
                return "";

            if (master != null && ReferenceEquals(acc, master))
                return "Master";

            if (_followerRows == null || _followerRows.Count == 0)
                return "";

            var row = _followerRows.FirstOrDefault(x => x?.Account != null && x.Account.Name == acc.Name);
            if (row == null || row.EnabledCheck?.IsChecked != true)
                return "Not selected";

            var bracket = NormalizeAtm(row.AtmOverrideBox?.SelectedItem as string);

            if (string.Equals(bracket, "(follow master exit)", StringComparison.OrdinalIgnoreCase))
                return "Follow master exit";

            if (string.Equals(bracket, "None", StringComparison.OrdinalIgnoreCase))
                return "Entry only";

            if (string.Equals(bracket, "(inherit master)", StringComparison.OrdinalIgnoreCase))
                return "Inherit master";

            return "Own bracket";
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