using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static bool ShowConfirmDialog(
            Window owner,
            string title,
            string message,
            string okText = "Confirm",
            string cancelText = "Cancel")
        {
            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18),
                Foreground = WindowForegroundBrush(),
                LineHeight = 22
            };

            var okButton = new Button
            {
                Content = okText,
                Width = 120,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Background = Brushes.Firebrick,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = cancelText,
                Width = 120,
                Height = 34,
                IsCancel = true
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel
            {
                Margin = new Thickness(18)
            };
            panel.Children.Add(text);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title,
                Width = 460,
                Height = 220,
                MinHeight = 220,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Content = panel,
                Background = WindowBackgroundBrush(),
                Foreground = WindowForegroundBrush()
            };

            var confirmed = false;

            okButton.Click += (s, e) =>
            {
                confirmed = true;
                dialog.DialogResult = true;
                dialog.Close();
            };

            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            dialog.ShowDialog();
            return confirmed;
        }
    }
}