using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void RenderInstrument(Grid root)
        {
            var instrumentStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0)
            };

            var topRow = new Grid
            {
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // combo
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); // add
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); // remove

            _instrumentSelector = CreateFormComboBox(editable: true);
            _instrumentSelector.IsTextSearchEnabled = false;
            _instrumentSelector.StaysOpenOnEdit = true;
            _instrumentSelector.Foreground = InputForegroundBrush();
            _instrumentSelector.Background = InputBackgroundBrush();
            _instrumentSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
            _instrumentSelector.Width = double.NaN;
            _instrumentSelector.Margin = new Thickness(0, 0, 6, 0);

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

            _btnRemoveInstrumentTab = CreateFormIconAction(
                Geometry.Parse("M 0 0 L 10 10 M 10 0 L 0 10"),
                tone: FormButtonTone.Danger,
                width: 34,
                height: InputHeight(),
                toolTip: "Remove instrument tab",
                onClick: () =>
                {
                    if (_activeInstrumentSession != null)
                        RemoveInstrumentSession(_activeInstrumentSession);
                });

            Grid.SetColumn(_instrumentSelector, 0);
            Grid.SetColumn(_btnAddInstrumentTab, 1);
            Grid.SetColumn(_btnRemoveInstrumentTab, 2);

            topRow.Children.Add(_instrumentSelector);
            topRow.Children.Add(_btnAddInstrumentTab);
            topRow.Children.Add(_btnRemoveInstrumentTab);

            instrumentStack.Children.Add(topRow);

            var instrumentFieldset = BuildFieldset("Instrument", instrumentStack);

            Grid.SetColumn(instrumentFieldset, 2);
            Grid.SetRow(instrumentFieldset, 0);

            root.Children.Add(instrumentFieldset);

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

                if (_engine != null && _engine.CopyEnabled)
                    _engine.SetCopyEnabled(true);

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
        }
    }
}