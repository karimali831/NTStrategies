using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.NinjaScript.AddOns.Ninjex.Licensing.Models;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private UIElement BuildLicensingPanel()
        {
            var licenseManager = NinjexRuntime.GetOrCreateLicenseManager();
            var state = licenseManager.State ?? new LicenseState();

            _licenseStatusText = BuildLicenseValueText(state.StatusText);
            _licenseFingerprintText = BuildLicenseValueText(state.Fingerprint ?? string.Empty);
            _licenseVersionText = BuildLicenseValueText(state.AddonVersion ?? string.Empty);

            var liveModeText = BuildLicenseValueText(state.CanUseLive ? "Enabled" : "Disabled");
            var simulationText = BuildLicenseValueText(state.CanUseSimulation ? "Enabled" : "Disabled");
            var tierText = BuildLicenseValueText(string.IsNullOrWhiteSpace(state.Tier) ? "None" : state.Tier);
            var maxMachinesText = BuildLicenseValueText(state.MaxMachines.ToString());
            var lastValidatedText = BuildLicenseValueText(FormatLicenseValidationTime(state.LastValidatedUtc));

            var stack = new StackPanel
            {
                Margin = new Thickness(0),
                Orientation = Orientation.Vertical
            };

            stack.Children.Add(BuildLicenseSection(
                "Current License",
                BuildLicenseGrid(
                    ("Status", _licenseStatusText),
                    ("Live Mode", liveModeText),
                    ("Simulation", simulationText),
                    ("Tier", tierText),
                    ("Max Machines", maxMachinesText),
                    ("Last Validated", lastValidatedText))));

            stack.Children.Add(BuildLicenseSection(
                "Machine",
                BuildLicenseGrid(
                    ("Machine Name", BuildLicenseValueText(state.MachineName ?? Environment.MachineName ?? string.Empty)),
                    ("Fingerprint", _licenseFingerprintText),
                    ("Addon Version", _licenseVersionText))));

            // var refreshButton = new Button
            // {
            //     Content = "Refresh License",
            //     MinWidth = 140,
            //     Height = 32,
            //     Margin = new Thickness(0, 12, 0, 0),
            //     Background = AccentBrush(),
            //     Foreground = Brushes.White,
            //     BorderBrush = AccentBrush(),
            //     HorizontalAlignment = HorizontalAlignment.Left
            // };

            var refreshButton = CreateFormButton("Refresh License");

            refreshButton.Click += async (s, e) =>
            {
                refreshButton.IsEnabled = false;

                try
                {
                    var manager = NinjexRuntime.GetOrCreateLicenseManager();
                    await manager.CheckAsync();
                }
                catch (Exception ex)
                {
                    NinjexRuntime.PrintLog("License refresh click failed: " + ex);
                }
                finally
                {
                    refreshButton.IsEnabled = true;

                    var refreshedState = NinjexRuntime.GetOrCreateLicenseManager().State;
                    ApplyLicenseSummaryToPanel(refreshedState, liveModeText, simulationText, tierText, maxMachinesText, lastValidatedText);
                }
            };

            stack.Children.Add(refreshButton);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                Content = stack
            };
        }

        private Border BuildLicenseSection(string title, UIElement content)
        {
            var titleText = new TextBlock
            {
                Text = title,
                Foreground = WindowForegroundBrush(),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var panel = new StackPanel();
            panel.Children.Add(titleText);
            panel.Children.Add(content);

            return new Border
            {
                Background = SectionBackgroundBrush(),
                BorderBrush = SectionBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = panel
            };
        }

        private Grid BuildLicenseGrid(params (string Label, TextBlock Value)[] rows)
        {
            var grid = new Grid();

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(170)
            });

            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            for (var i = 0; i < rows.Length; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                var label = new TextBlock
                {
                    Text = rows[i].Label,
                    Foreground = MutedForegroundBrush(),
                    Margin = new Thickness(0, 0, 16, 10),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var value = rows[i].Value;
                value.Margin = new Thickness(0, 0, 0, 10);

                Grid.SetRow(label, i);
                Grid.SetColumn(label, 0);

                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);

                grid.Children.Add(label);
                grid.Children.Add(value);
            }

            return grid;
        }

        private TextBlock BuildLicenseValueText(string text)
        {
            return new TextBlock
            {
                Text = text ?? string.Empty,
                Foreground = WindowForegroundBrush(),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void ApplyLicenseSummaryToPanel(
            LicenseState state,
            TextBlock liveModeText,
            TextBlock simulationText,
            TextBlock tierText,
            TextBlock maxMachinesText,
            TextBlock lastValidatedText)
        {
            if (state == null)
                return;

            if (_licenseStatusText != null)
                _licenseStatusText.Text = state.StatusText ?? string.Empty;

            if (_licenseFingerprintText != null)
                _licenseFingerprintText.Text = state.Fingerprint ?? string.Empty;

            if (_licenseVersionText != null)
                _licenseVersionText.Text = state.AddonVersion ?? string.Empty;

            if (liveModeText != null)
                liveModeText.Text = state.CanUseLive ? "Enabled" : "Disabled";

            if (simulationText != null)
                simulationText.Text = state.CanUseSimulation ? "Enabled" : "Disabled";

            if (tierText != null)
                tierText.Text = string.IsNullOrWhiteSpace(state.Tier) ? "None" : state.Tier;

            if (maxMachinesText != null)
                maxMachinesText.Text = state.MaxMachines.ToString();

            if (lastValidatedText != null)
                lastValidatedText.Text = FormatLicenseValidationTime(state.LastValidatedUtc);
        }

        private static string FormatLicenseValidationTime(DateTime? value)
        {
            if (!value.HasValue)
                return "Never";

            return value.Value.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss");
        }
    }
}