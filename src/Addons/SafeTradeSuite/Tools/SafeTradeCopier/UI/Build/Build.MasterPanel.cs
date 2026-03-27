using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private TextBox _masterQtyBox;
        private ComboBox _masterBracketBox;
        private TextBlock _masterPnlText;
        private ProgressBar _masterPnlBar;
        private Button _btnBuyMkt;
        private Button _btnSellMkt;
        private Button _btnFlattenAll;
        private TextBlock _masterPnlBarStatusText;
        private Button _btnFreeTradeAll;
        private Button _btnMasterFreeTrade;
        private Button _btnFlattenMaster;
        private TextBlock _masterPositionText;
        
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
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Account combo
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Qty label
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Qty box
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Bracket label
            masterTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Bracket combo

            var accountLbl = CreateFormLabel("Account", width: 55);

            _masterBox = CreateFormComboBox(width: 140, margin: new Thickness(0, 0, 12, 0));

            var qtyLbl = CreateFormLabel("Qty", width: 28);
            _masterQtyBox = CreateOrderQtyBox(1, "Order quantity", margin: new Thickness(0, 0, 12, 0));

            var atmLbl = CreateFormLabel("Bracket", width: 50);
            _masterBracketBox = CreateFormComboBox(width: 140);

            Grid.SetColumn(accountLbl, 0);
            Grid.SetColumn(_masterBox, 1);
            Grid.SetColumn(qtyLbl, 2);
            Grid.SetColumn(_masterQtyBox, 3);
            Grid.SetColumn(atmLbl, 4);
            Grid.SetColumn(_masterBracketBox, 5);

            masterTopRow.Children.Add(accountLbl);
            masterTopRow.Children.Add(_masterBox);
            masterTopRow.Children.Add(qtyLbl);
            masterTopRow.Children.Add(_masterQtyBox);
            masterTopRow.Children.Add(atmLbl);
            masterTopRow.Children.Add(_masterBracketBox);

            var orderRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            orderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Copier controls
            _btnBuyMkt = CreateFormButton(
                text: "Buy Market",
                width: 115,
                tone: FormButtonTone.Success,
                style: FormButtonStyle.Solid,
                bold: true);

            _btnSellMkt = CreateFormButton(
                text: "Sell Market",
                width: 115,
                tone: FormButtonTone.Danger,
                style: FormButtonStyle.Solid,
                margin: new Thickness(6, 0, 0, 0),
                bold: true);

            _btnFreeTradeAll = CreateFormButton(
                text: "Break-even All",
                width: 115,
                tone: FormButtonTone.Primary,
                style: FormButtonStyle.Solid,
                margin: new Thickness(6, 0, 0, 0),
                bold: true);
            RenderFreeTradeButtonState(_btnFreeTradeAll, enabled: false, undoMode: false, "Break-even All", all: true);

            _btnFlattenAll = CreateFormButton(
                text: "Flatten All",
                width: 115,
                tone: FormButtonTone.Flatten,
                style: FormButtonStyle.Solid,
                margin: new Thickness(6, 0, 0, 0),
                bold: true);
            RenderFlattenAllButtonState();
            
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
            
            masterStack.Children.Add(masterTopRow);
            masterStack.Children.Add(orderRow);
            
           // Master controls
            var masterInfoGrid = new Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };

            masterInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            masterInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  });
            masterInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto  });
            masterInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _masterPositionText = new TextBlock
            {
                Text = "",
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = MutedForegroundBrush(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _masterPnlText = new TextBlock
            {
                Text = "",
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = MutedForegroundBrush(),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _btnMasterFreeTrade = CreateFormButton(
                text: "Break-even",
                tone: FormButtonTone.Primary,
                style: FormButtonStyle.Outline,
                height: SmallButtonHeight(),
                margin: new Thickness(0),
                width: null);

            _btnMasterFreeTrade.HorizontalAlignment = HorizontalAlignment.Right;
            _btnMasterFreeTrade.Click += (s, e) => FreeTradeMasterSelected(eng);

            _btnFlattenMaster = CreateFormButton(
                text: "Flatten",
                tone: FormButtonTone.Flatten,
                style: FormButtonStyle.Solid,
                height: SmallButtonHeight(),
                enabled: false,
                margin: new Thickness(8, 0, 0, 0),
                width: null);

            _btnFlattenMaster.HorizontalAlignment = HorizontalAlignment.Right;
            RenderFlattenMasterButtonState();

            _btnFlattenMaster.Click += (s, e) =>
            {
                var instr = GetInstrument();
                var master = GetMasterAccount();
                if (eng == null || master == null || instr == null)
                    return;

                eng.EnsureFlatInstrument(master, instr);
            };

            Grid.SetColumn(_masterPositionText, 0);
            Grid.SetColumn(_masterPnlText, 1);
            Grid.SetColumn(_btnMasterFreeTrade, 2);
            Grid.SetColumn(_btnFlattenMaster, 3);

            masterInfoGrid.Children.Add(_masterPositionText);
            masterInfoGrid.Children.Add(_masterPnlText);
            masterInfoGrid.Children.Add(_btnMasterFreeTrade);
            masterInfoGrid.Children.Add(_btnFlattenMaster);

            _masterPnlBar = new ProgressBar
            {
                Height = 10,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Width = 100,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            EnsureRoundedProgressBar(_masterPnlBar, alignRight: false);

            _masterPnlBarStatusText = new TextBlock
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = MutedForegroundBrush(),
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed
            };

            masterStack.Children.Add(masterInfoGrid);
            masterStack.Children.Add(_masterPnlBar);
            masterStack.Children.Add(_masterPnlBarStatusText);

            var masterFieldset = BuildFieldset("Master", masterStack);
            Grid.SetColumn(masterFieldset, 0);
            Grid.SetRow(masterFieldset, 0);
            Grid.SetRowSpan(masterFieldset, 2);
            root.Children.Add(masterFieldset);

            _masterBox.SelectionChanged += (s, e) =>
            {
                if (_suppressSessionUiEvents || _isLoadingSessionUi)
                    return;

                var latestAccounts = GetSelectableAccounts();

                SaveUiToActiveSession("RenderMasterPanel._masterBox.SelectionChanged");
                RebuildFollowersAndRewire(eng, latestAccounts);
                LoadActiveSessionToUi();
                RefreshRiskFieldset();
            };
            
            void ApplyAndMaybeRewire()
            {
                if (_suppressSessionUiEvents || _isLoadingSessionUi)
                    return;

                var latestAccounts = GetSelectableAccounts();

                RebuildFollowersAndRewire(eng, latestAccounts);
                SaveUiToActiveSession("RenderMasterPanel.ApplyAndMaybeRewire");
                ApplyConfigFromUi();

                if (_activeInstrumentSession?.IsArmedRequested == true)
                    eng.SetCopyEnabled(true);
            }

            _masterQtyBox.TextChanged += (s, e) =>
            {
                if (_suppressSessionUiEvents || _isLoadingSessionUi)
                    return;

                SaveUiToActiveSession("RenderMasterPanel._masterQtyBox.TextChanged ");
                ApplyAndMaybeRewire();
            };

            _masterBracketBox.SelectionChanged += (s, e) =>
            {
                if (_suppressSessionUiEvents || _isLoadingSessionUi)
                    return;

                SaveUiToActiveSession("RenderMasterPanel._masterBracketBox.SelectionChanged");
                ApplyConfigFromUi();
                SavePersistentUiState();
                RefreshFollowerBulkActionButtons();
            };
        }
    }
}