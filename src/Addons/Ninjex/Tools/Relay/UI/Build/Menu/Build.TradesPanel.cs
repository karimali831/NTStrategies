using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private Border _tradesPanelRoot;
        private StackPanel _tradesRowsHost;
        private bool _hasTrades;

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

            content.Children.Add(BuildTradesToolbar());
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

            RequestTradesUiRefresh();
            return _tradesPanelRoot;
        }
        
        private UIElement BuildTradesToolbar()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new TextBlock
            {
                Text = "Completed trades history",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = PrimaryTextBrush(),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 8, 0)
            };

            var clearBtn = CreateFormButton(
                text: "Clear Trades",
                width: 115,
                tone: FormButtonTone.Danger,
                style: FormButtonStyle.Solid,
                // enabled: _hasTrades,
                margin: new Thickness(0, 0, 5, 0),
                height: SmallButtonHeight(),
                bold: true);

            clearBtn.Click += (s, e) => ClearTradesHistory();

            Grid.SetColumn(info, 0);
            Grid.SetColumn(clearBtn, 1);

            grid.Children.Add(info);
            grid.Children.Add(clearBtn);

            return grid;
        }
        
        private void ClearTradesHistory()
        {
            var confirmed = ShowConfirmDialog(
                _window,
                "Clear trades history",
                "This will permanently remove all rows from the trades table, including any currently tracked active trades.\n\nThis action cannot be undone.",
                okText: "Delete all",
                cancelText: "Cancel");

            if (!confirmed)
                return;

            lock (_tradeGate)
            {
                _tradeHistory.Clear();
                _activeTrades.Clear();
            }

            RequestTradesUiRefresh();
            SavePersistentUiState();
            _engine?.Log("Trades history cleared.");
        }

        private static Grid BuildTradesHeaderRow()
        {
            var g = CreateTableRowGrid(new Thickness(0, 0, 0, 6));
            g.Background = TableHeaderBrush();

            CreateColumnDefinitions(g);
            AddTradesHeaderCell(g, "#", 0);
            AddTradesHeaderCell(g, "Instrument", 1);
            AddTradesHeaderCell(g, "Side", 2);
            AddTradesHeaderCell(g, "Qty", 3);
            AddTradesHeaderCell(g, "Account", 4);
            AddTradesHeaderCell(g, "Entry", 5);
            AddTradesHeaderCell(g, "Exit", 6);
            AddTradesHeaderCell(g, "Profit", 7);
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

        private static void CreateColumnDefinitions(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });  // #
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Instrument
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // Side
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });  // Qty
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Account
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // Entry
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // Exit
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Profit
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Bracket
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // Outcome
        }

        private void RefreshTradesPanel()
        {
            if (_tradesRowsHost == null)
                return;

            _tradesRowsHost.Children.Clear();

            System.Collections.Generic.List<TradeHistoryItemState> ordered;
            lock (_tradeGate)
            {
                if (_tradeHistory.Count == 0)
                {
                    _hasTrades = false;
                    _tradesRowsHost.Children.Add(new TextBlock
                    {
                        Text = "No completed trades yet.",
                        Margin = new Thickness(6, 8, 6, 8),
                        Foreground = MutedForegroundBrush()
                    });
                    return;
                }

                _hasTrades = true;
                ordered = _tradeHistory
                    .OrderByDescending(x => x.IsMaster)
                    .ThenByDescending(x => x.TradeNumber)
                    .ToList();
            }

            var rowIndex = 0;
            foreach (var t in ordered)
            {
                var row = CreateTableRowGrid(new Thickness(0, 0, 0, 4));
                row.Background = GetFollowerRowBackgroundBrush(rowIndex);

                CreateColumnDefinitions(row);
                AddTradesCell(row, t.TradeNumber.ToString(), 0);
                AddTradesCell(row, t.InstrumentName, 1);
                AddTradesCell(row, t.MarketPosition, 2);
                AddTradesCell(row, t.OrderQty.ToString(), 3);
                AddTradesCell(row, t.AccountName, 4);
                AddTradesCell(row, ToLocalTradeTime(t.EntryTimeUtc), 5);
                AddTradesCell(row, t.ExitTimeUtc.HasValue ? ToLocalTradeTime(t.ExitTimeUtc.Value) : "", 6);
                AddTradesCell(row, FmtUsd(t.RealizedPnL), 7, GetTradePnlBrush(t.RealizedPnL));
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