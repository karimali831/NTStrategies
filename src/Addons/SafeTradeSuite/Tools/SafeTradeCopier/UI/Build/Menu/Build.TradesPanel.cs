using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private Border _tradesPanelRoot;
        private StackPanel _tradesRowsHost;

        private UIElement BuildTradesPlaceholder()
        {
            _tradesRowsHost = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            content.Children.Add(BuildTradesHeaderRow());
            content.Children.Add(_tradesRowsHost);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            };

            _tradesPanelRoot = new Border
            {
                Background = SectionBackgroundBrush(),
                BorderBrush = SectionBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = scroll
            };

            RefreshTradesPanel();
            return _tradesPanelRoot;
        }

        private static Grid BuildTradesHeaderRow()
        {
            var g = CreateTableRowGrid(new Thickness(0, 0, 0, 6));
            g.Background = TableHeaderBrush();

            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });   // #
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });  // Instrument
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });   // Side
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });   // Qty
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });  // Account
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });  // Entry
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });  // Exit
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });   // PnL
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });  // Bracket
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });  // Outcome

            AddTradesHeaderCell(g, "#", 0);
            AddTradesHeaderCell(g, "Instrument", 1);
            AddTradesHeaderCell(g, "Side", 2);
            AddTradesHeaderCell(g, "Qty", 3);
            AddTradesHeaderCell(g, "Account", 4);
            AddTradesHeaderCell(g, "Entry", 5);
            AddTradesHeaderCell(g, "Exit", 6);
            AddTradesHeaderCell(g, "PnL", 7);
            AddTradesHeaderCell(g, "Bracket", 8);
            AddTradesHeaderCell(g, "Outcome", 9);

            return g;
        }

        private static void AddTradesHeaderCell(Grid g, string text, int col)
        {
            var tb = CreateTableHeader(text);
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }

        private void RefreshTradesPanel()
        {
            if (_tradesRowsHost == null)
                return;

            _tradesRowsHost.Children.Clear();

            if (_tradeHistory.Count == 0)
            {
                _tradesRowsHost.Children.Add(new TextBlock
                {
                    Text = "No completed trades yet.",
                    Margin = new Thickness(6, 8, 6, 8),
                    Foreground = MutedForegroundBrush()
                });
                return;
            }

            var ordered = _tradeHistory
                .OrderByDescending(x => x.IsMaster)
                .ThenByDescending(x => x.TradeNumber)
                .ToList();

            var rowIndex = 0;
            foreach (var t in ordered)
            {
                var row = CreateTableRowGrid(new Thickness(0, 0, 0, 4));
                row.Background = GetFollowerRowBackgroundBrush(rowIndex);

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

                AddTradesCell(row, t.TradeNumber.ToString(), 0);
                AddTradesCell(row, t.InstrumentName, 1);
                AddTradesCell(row, t.MarketPosition, 2);
                AddTradesCell(row, t.OrderQty.ToString(), 3);
                AddTradesCell(row, t.AccountName, 4);
                AddTradesCell(row, ToLocalTradeTime(t.EntryTimeUtc), 5);
                AddTradesCell(row, t.ExitTimeUtc.HasValue ? ToLocalTradeTime(t.ExitTimeUtc.Value) : "", 6);
                AddTradesCell(row, t.RealizedPnL.ToString("0.00"), 7, GetTradePnlBrush(t.RealizedPnL));
                AddTradesCell(row, t.BracketUsed, 8);
                AddTradesCell(row, t.Outcome, 9);

                _tradesRowsHost.Children.Add(row);
                rowIndex++;
            }
        }

        private static void AddTradesCell(Grid g, string text, int col, System.Windows.Media.Brush foreground = null)
        {
            var tb = CreateTableCell(text, foreground);
            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }

        private static string ToLocalTradeTime(DateTime utc)
        {
            var local = utc.Kind == System.DateTimeKind.Utc ? utc.ToLocalTime() : utc;
            return local.ToString("dd/MM HH:mm:ss");
        }

        private static System.Windows.Media.Brush GetTradePnlBrush(double pnl)
        {
            if (pnl > 0)
                return SuccessActionBrush();

            if (pnl < 0)
                return DangerActionBrush();

            return PrimaryTextBrush();
        }
    }
}