using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static TextBox CreateFormTextBox(
            string text = "",
            double width = 120,
            double? height = null,
            Brush foreground = null,
            bool? isEnabled = true,
            Thickness? margin = null)
        {
            var tb = new TextBox
            {
                Width = width,
                Margin = margin ?? new Thickness(0),
                Text = text ?? "",
                VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled =  isEnabled ?? true,
            };

            ApplyInputChrome(tb);

            tb.IsEnabledChanged += (s, e) => ApplyInputChrome(tb);

            return tb;
        }
    }
}