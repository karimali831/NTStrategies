using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static TextBox CreateFormTextBox(string text = "", double width = 120, Thickness? margin = null)
        {
            var tb = new TextBox
            {
                Width = width,
                Margin = margin ?? new Thickness(0),
                Text = text ?? "",
                VerticalContentAlignment = VerticalAlignment.Center
            };

            ApplyInputChrome(tb);
            return tb;
        }
    }
}