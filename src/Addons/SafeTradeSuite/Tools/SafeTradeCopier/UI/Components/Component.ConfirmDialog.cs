using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static bool ShowConfirmDialog(Window owner, string title, string message, string okText = "Confirm", string cancelText = "Cancel")
        {
            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16),
                Foreground = WindowForegroundBrush()
            };

            var okButton = new Button
            {
                Content = okText,
                Width = 100,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = Brushes.Firebrick,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = cancelText,
                Width = 100,
                Height = 30,
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
                Margin = new Thickness(16)
            };
            panel.Children.Add(text);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                Content = panel,
                Background = WindowBackgroundBrush()
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