using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private Border _btnAddInstrumentTab;

        private void RenderInstrument(Grid root)
        {
            var instrumentStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var topRow = new Grid
            {
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _instrumentSelector = CreateFormComboBox(editable: true);
            _instrumentSelector.IsTextSearchEnabled = false;
            _instrumentSelector.StaysOpenOnEdit = true;
            _instrumentSelector.Foreground = InputForegroundBrush();
            _instrumentSelector.Background = InputBackgroundBrush();
            _instrumentSelector.HorizontalAlignment = HorizontalAlignment.Stretch;
            _instrumentSelector.Width = double.NaN;
            _instrumentSelector.Margin = new Thickness(0);
            
            _btnAddInstrumentTab = CreateFormIconAction(
                Geometry.Parse("M 0 5 L 10 5 M 5 0 L 5 10"),
                tone: FormButtonTone.Primary,
                width: 34,
                height: InputHeight(),
                toolTip: "Add instrument tab",
                onClick: () =>
                {
                    if (!CanAddSelectedInstrumentTab())
                        return;

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
                    UpdateAddInstrumentButtonState();
                    // RenderFlattenMasterButtonState();
                    // RenderFlattenAllButtonState();
                });
            _btnAddInstrumentTab.BorderThickness = new Thickness(0, 1, 1, 1);

            Grid.SetColumn(_instrumentSelector, 0);
            Grid.SetColumn(_btnAddInstrumentTab, 1);

            topRow.Children.Add(_instrumentSelector);
            topRow.Children.Add(_btnAddInstrumentTab);
            instrumentStack.Children.Add(topRow);

            var instrumentFieldset = BuildFieldset("Instrument", instrumentStack);
            if (instrumentFieldset is FrameworkElement fe)
                fe.HorizontalAlignment = HorizontalAlignment.Stretch;

            Grid.SetColumn(instrumentFieldset, 2);
            Grid.SetRow(instrumentFieldset, 0);

            root.Children.Add(instrumentFieldset);

            _instrumentSelector.SelectionChanged += (s, e) =>
            {
                if (SuppressSessionUiEvents)
                    return;

                var instrumentName = NormalizeInstrumentName(_instrumentSelector.SelectedItem as string);

                UpdateAddInstrumentButtonState();

                if (string.IsNullOrWhiteSpace(instrumentName))
                    return;

                if (!IsValidInstrumentName(instrumentName))
                    return;

                ActivateOrCreateInstrumentSession(instrumentName);

                if (_engine != null && _engine.IsRequested)
                    _engine.SetCopyEnabled(true);

                UpdateAddInstrumentButtonState();
            };

            _instrumentSelector.LostKeyboardFocus += (s, e) =>
            {
                if (SuppressSessionUiEvents)
                    return;

                var instrumentName = GetSelectedInstrumentName();

                UpdateAddInstrumentButtonState();

                if (string.IsNullOrWhiteSpace(instrumentName))
                    return;

                if (!IsValidInstrumentName(instrumentName))
                    return;

                ActivateOrCreateInstrumentSession(instrumentName, refreshSelector: true);
                UpdateAddInstrumentButtonState();
                // RenderFlattenMasterButtonState();
                // RenderFlattenAllButtonState();
            };

            _instrumentSelector.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
            {
                UpdateAddInstrumentButtonState();
            }));

            UpdateAddInstrumentButtonState();
        }

        private bool CanAddSelectedInstrumentTab()
        {
            var instrumentName = NormalizeInstrumentName(GetSelectedInstrumentName());
            if (string.IsNullOrWhiteSpace(instrumentName))
                return false;

            if (!IsValidInstrumentName(instrumentName))
                return false;

            return !_instrumentSessions.Any(x =>
                string.Equals(
                    NormalizeInstrumentName(x?.InstrumentName),
                    instrumentName,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateAddInstrumentButtonState()
        {
            if (_btnAddInstrumentTab == null)
                return;

            var enabled = CanAddSelectedInstrumentTab();

            SetFormIconActionEnabled(
                _btnAddInstrumentTab,
                enabled,
                FormButtonTone.Primary,
                enabledToolTip: "Add instrument tab",
                disabledToolTip: "Instrument tab already exists");
        }
    }
}