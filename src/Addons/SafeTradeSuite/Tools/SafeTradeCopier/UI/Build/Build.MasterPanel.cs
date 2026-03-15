using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

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
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Account
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Instrument label
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Instrument
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); // Add button
            
            var accountLbl = CreateFormLabel("Account", width: 50);

            _masterBox = CreateFormComboBox(width: 150, margin: new Thickness(0, 0, 12, 0));

            var instrumentLbl = CreateFormLabel("Instrument", width: 60, margin: new Thickness(0, 0, 6, 0));

            _instrumentSelector = CreateFormComboBox(width: 90, editable: true);
            _instrumentSelector.IsTextSearchEnabled = false;
            _instrumentSelector.StaysOpenOnEdit = true;
            
            _btnAddInstrumentTab = CreateFormIconAction(
                Geometry.Parse("M 0 5 L 10 5 M 5 0 L 5 10"),
                tone: FormButtonTone.Primary,
                width: 34,
                height: InputHeight(),
                toolTip: "Add instrument tab",
                onClick: () =>
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
                });
            
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
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _btnBuyMkt = CreateFormButton(
                text: "Buy",
                tone: FormButtonTone.Success,
                style: FormButtonStyle.Solid);

            _btnSellMkt = CreateFormButton(
                text: "Sell",
                tone: FormButtonTone.Danger,
                style: FormButtonStyle.Solid,
                margin: new Thickness(6, 0, 0, 0));

            _btnFreeTradeAll = CreateFormButton(
                text: "BE All",
                tone: FormButtonTone.Primary,
                style: FormButtonStyle.Outline,
                margin: new Thickness(6, 0, 0, 0));
            RenderFreeTradeButtonState(_btnFreeTradeAll, enabled: false, undoMode: false, "BE All");

            _btnFlattenAll = CreateFormButton(
                text: "Flatten All",
                tone: FormButtonTone.Danger,
                style: FormButtonStyle.Outline,
                margin: new Thickness(6, 0, 0, 0));
            ApplyButtonTheme(_btnFlattenAll, FormButtonTone.Danger, FormButtonStyle.Outline, enabled: false);
            
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
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  });
            
            var qtyLbl = CreateFormLabel("Order Qty", width: 60);
            var atmLbl = CreateFormLabel("Bracket", width: 50);

            _masterQtyBox = CreateFormTextBox("1", 40,  margin: new Thickness(0, 0, 20, 0));
            _masterAtmBox = CreateFormComboBox(width: 180);

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
                Text = "Master Unrealized $0.00",
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

            _btnMasterFreeTrade = CreateFormButton(
                text: "BE",
                tone: FormButtonTone.Primary,
                style: FormButtonStyle.Outline,
                width: 100,
                height: SmallButtonHeight(),
                margin: new Thickness(8, 0, 0, 0));

            _btnMasterFreeTrade.Click += (s, e) => FreeTradeMasterSelected(eng);
            
            Grid.SetColumn(_masterPnlText, 0);
            Grid.SetColumn(_btnMasterFreeTrade, 1);
            masterPnlTopRow.Children.Add(_masterPnlText);
            masterPnlTopRow.Children.Add(_btnMasterFreeTrade);

            masterStack.Children.Add(masterPnlTopRow);
            masterStack.Children.Add(masterPnlRow);
            masterStack.Children.Add(_masterPnlBarStatusText);

            var masterFieldset = BuildFieldset("Master", masterStack);
            Grid.SetColumn(masterFieldset, 0);
            root.Children.Add(masterFieldset);
            
            _masterBox.SelectionChanged += (s, e) =>
            {
                if (_suppressSessionUiEvents)
                    return;

                var latestAccounts = GetSelectableAccounts();

                SaveUiToActiveSession();
                RebuildFollowersAndRewire(eng, latestAccounts);
                LoadActiveSessionToUi();
                RefreshRiskFieldset();
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