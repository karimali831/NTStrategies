using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static ComboBox CreateFormComboBox(double width = 120, Thickness? margin = null, bool editable = false)
        {
            var cb = new ComboBox
            {
                Width = width,
                Margin = margin ?? new Thickness(0),
                IsEditable = editable,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            ApplyInputChrome(cb);
            ApplyComboBoxTheme(cb);
            
            cb.Loaded += (s, e) =>
            {
                var tb = FindVisualChild<TextBox>(cb);
                if (tb != null)
                {
                    tb.Background = InputBackgroundBrush();
                    tb.Foreground = InputForegroundBrush();
                    tb.BorderThickness = new Thickness(0);
                }
            };
            
            return cb;
        }
    }
}