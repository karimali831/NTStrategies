using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBox _masterQtyBox;
        private ComboBox _masterAtmBox;
        private TextBlock _masterPnlText;
        private ProgressBar _masterPnlBar;
        private Button _btnBuyMkt;
        private Button _btnSellMkt;
        private Button _btnFlattenAll;
        private TextBlock _masterPnlBarStatusText;
        private Button _btnFreeTradeAll;
        private Button _btnMasterFreeTrade;
        
        private void RenderMasterPanel(SafeCopierEngine eng, Grid root)
        {
            var masterStack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            var masterTopRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Account label
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); // Account
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Instrument label
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Instrument
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Add button

            var accountLbl = new TextBlock
            {
                Text = "Account",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = SystemColors.WindowTextBrush,
                FontWeight = FontWeights.SemiBold
            };

            _masterBox = new ComboBox
            {
                Height = 30,
                Margin = new Thickness(0, 0, 14, 0),
            };

            var instrumentLbl = new TextBlock
            {
                Text = "Instrument",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = SystemColors.WindowTextBrush,
                FontWeight = FontWeights.SemiBold
            };

            _instrumentSelector = new ComboBox
            {
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsEditable = true,
                IsTextSearchEnabled = false,
                StaysOpenOnEdit = true
            };

            _btnAddInstrumentTab = new Button
            {
                Content = "+",
                Width = 34,
                Height = 30,
                ToolTip = "Add instrument tab",
                Background = Brushes.DodgerBlue,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderBrush = Brushes.DodgerBlue
            };

            Grid.SetColumn(accountLbl, 0);
            Grid.SetColumn(_masterBox, 1);
            Grid.SetColumn(instrumentLbl, 2);
            Grid.SetColumn(_instrumentSelector, 3);
            Grid.SetColumn(_btnAddInstrumentTab, 4);

            masterTopRow.Children.Add(accountLbl);
            masterTopRow.Children.Add(_masterBox);
            masterTopRow.Children.Add(instrumentLbl);
            masterTopRow.Children.Add(_instrumentSelector);
            masterTopRow.Children.Add(_btnAddInstrumentTab);

            var orderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnBuyMkt = new Button
            {
                Content = "Buy Market",
                Height = 36,
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.ForestGreen,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };

            _btnSellMkt = new Button
            {
                Content = "Sell Market",
                Height = 36,
                Margin = new Thickness(6, 0, 6, 0),
                Background = Brushes.Firebrick,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            
            _btnFreeTradeAll = new Button
            {
                Content = "Free Trade All",
                Height = 36,
                Margin = new Thickness(6, 0, 6, 0),
                Background = Brushes.SteelBlue,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            RenderFreeTradeButtonState(_btnFreeTradeAll, enabled: false, undoMode: false, "Free Trade All");
            
            _btnFlattenAll = new Button
            {
                Content = "Flatten All",
                Height = 36,
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Maroon,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                IsEnabled = false,
                Opacity = 0.6
            };
            
            _btnFreeTradeAll.Click += (s, e) => FreeTradeAllSelected(eng);
            _btnFlattenAll.Click += (s, e) => FlattenAllSelected(eng);
            _btnBuyMkt.Click += (s, e) => SubmitMasterMarket(eng, isBuy: true);
            _btnSellMkt.Click += (s, e) => SubmitMasterMarket(eng, isBuy: false);

            Grid.SetColumn(_btnBuyMkt, 0);
            Grid.SetColumn(_btnSellMkt, 1);
            Grid.SetColumn(_btnFreeTradeAll, 2);
            Grid.SetColumn(_btnFlattenAll, 3);

            orderRow.Children.Add(_btnBuyMkt);
            orderRow.Children.Add(_btnSellMkt);
            orderRow.Children.Add(_btnFreeTradeAll);
            orderRow.Children.Add(_btnFlattenAll);

            var qaRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var qtyLbl = new TextBlock
            {
                Text = "Order qty:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = SystemColors.WindowTextBrush
            };

            _masterQtyBox = new TextBox
            {
                Height = 26,
                Text = "1",
                Margin = new Thickness(0, 0, 14, 0)
            };

            var atmLbl = new TextBlock
            {
                Text = "ATM:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = SystemColors.WindowTextBrush
            };

            _masterAtmBox = new ComboBox
            {
                Height = 26,
                MinWidth = 180
            };

            Grid.SetColumn(qtyLbl, 0);
            Grid.SetColumn(_masterQtyBox, 1);
            Grid.SetColumn(atmLbl, 2);
            Grid.SetColumn(_masterAtmBox, 3);

            qaRow.Children.Add(qtyLbl);
            qaRow.Children.Add(_masterQtyBox);
            qaRow.Children.Add(atmLbl);
            qaRow.Children.Add(_masterAtmBox);

            _masterPnlText = new TextBlock
            {
                Text = "Master   R $0.00   •   U $0.00",
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = Brushes.DimGray,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            };

            var masterPnlRow = new Grid
            {
                Margin = new Thickness(0, 6, 0, 0)
            };
            masterPnlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            masterPnlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });

            _masterPnlBar = new ProgressBar
            {
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Width = 150,
                Margin = new Thickness(0, 0, 12, 0),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            EnsureRoundedProgressBar(_masterPnlBar, alignRight: false);

            Grid.SetColumn(_masterPnlBar, 0);
            masterPnlRow.Children.Add(_masterPnlBar);

            _masterPnlBarStatusText = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed
            };

            masterStack.Children.Add(masterTopRow);
            masterStack.Children.Add(orderRow);
            masterStack.Children.Add(qaRow);
            var masterPnlTopRow = new Grid
            {
                Margin = new Thickness(0, 6, 0, 0)
            };
            masterPnlTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            masterPnlTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _btnMasterFreeTrade = new Button
            {
                Content = "Free Trade",
                Height = 24,
                MinWidth = 90,
                Margin = new Thickness(8, 0, 0, 0),
                Background = Brushes.SteelBlue,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };

            _btnMasterFreeTrade.Click += (s, e) => FreeTradeMasterSelected(eng);

            Grid.SetColumn(_masterPnlText, 0);
            Grid.SetColumn(_btnMasterFreeTrade, 1);
            masterPnlTopRow.Children.Add(_masterPnlText);
            masterPnlTopRow.Children.Add(_btnMasterFreeTrade);

            masterStack.Children.Add(masterPnlTopRow);
            masterStack.Children.Add(masterPnlRow);
            masterStack.Children.Add(_masterPnlBarStatusText);

            var masterFieldset = BuildFieldset("Master", masterStack);
            Grid.SetRow(masterFieldset, 3);
            root.Children.Add(masterFieldset);

            _masterBox.SelectionChanged += (s, e) =>
            {
                if (_suppressSessionUiEvents)
                    return;

                var latestAccounts = GetSelectableAccounts();

                SaveUiToActiveSession();
                RebuildFollowersAndRewire(eng, latestAccounts);
                LoadActiveSessionToUi();
                RenderFlattenAllButtonState();
            };

            _masterBox.DropDownOpened += (s, e) => UpdateMasterComboItemEnablement();

            _instrumentSelector.SelectionChanged += (s, e) =>
            {
                if (_suppressSessionUiEvents)
                    return;

                var instrumentName = NormalizeInstrumentName(_instrumentSelector.SelectedItem as string);
                if (string.IsNullOrWhiteSpace(instrumentName))
                    return;

                if (!IsValidInstrumentName(instrumentName))
                    return;

                ActivateOrCreateInstrumentSession(instrumentName);

                if (eng.CopyEnabled)
                    eng.SetCopyEnabled(true);

                RenderFlattenAllButtonState();
            };

            _btnAddInstrumentTab.Click += (s, e) =>
            {
                var typed = GetSelectedInstrumentName();

                if (string.IsNullOrWhiteSpace(typed))
                {
                    ShowFriendlyError("Instrument required", "Please type or select an instrument first.");
                    return;
                }

                if (!IsValidInstrumentName(typed))
                {
                    ShowFriendlyError("Invalid instrument", "Please enter a valid NinjaTrader instrument, for example: NQ 03-26");
                    return;
                }

                ActivateOrCreateInstrumentSession(typed, refreshSelector: true);
                RenderFlattenAllButtonState();
            };

            _instrumentSelector.LostKeyboardFocus += (s, e) =>
            {
                if (_suppressSessionUiEvents)
                    return;

                var instrumentName = GetSelectedInstrumentName();
                if (string.IsNullOrWhiteSpace(instrumentName))
                    return;

                if (!IsValidInstrumentName(instrumentName))
                    return;

                ActivateOrCreateInstrumentSession(instrumentName, refreshSelector: true);
                RenderFlattenAllButtonState();
            };

            void ApplyAndMaybeRewire()
            {
                if (_suppressSessionUiEvents)
                    return;

                SaveUiToActiveSession();
                ApplyConfigFromUi();

                if (eng.CopyEnabled)
                    eng.SetCopyEnabled(true);
            }

            _masterQtyBox.TextChanged += (s, e) => ApplyAndMaybeRewire();
            _masterAtmBox.SelectionChanged += (s, e) => ApplyAndMaybeRewire();
        }
    }
}