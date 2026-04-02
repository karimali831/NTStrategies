using System.Windows;
using System.Windows.Controls;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static void ApplyInputChrome(Control c)
        {
            if (c == null)
                return;

            c.Height = InputHeight();

            c.Background = c.IsEnabled
                ? InputBackgroundBrush()
                : InputDisabledBackgroundBrush();

            c.Foreground = c.IsEnabled
                ? InputForegroundBrush()
                : InputDisabledForegroundBrush();

            c.BorderBrush = InputBorderBrush();
            c.BorderThickness = ControlBorderThickness();
            c.Padding = new Thickness(8, 0, 8, 0);
        }
        
        private static void ApplyComboBoxTheme(ComboBox cb)
        {
            if (cb == null)
                return;

            cb.Background = cb.IsEnabled
                ? InputBackgroundBrush()
                : InputDisabledBackgroundBrush();

            cb.Foreground = cb.IsEnabled
                ? InputForegroundBrush()
                : InputDisabledForegroundBrush();

            cb.BorderBrush = InputBorderBrush();

            var itemStyle = new Style(typeof(ComboBoxItem));

            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, InputBackgroundBrush()));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, InputForegroundBrush()));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));

            var highlightTrigger = new Trigger
            {
                Property = ComboBoxItem.IsHighlightedProperty,
                Value = true
            };
            highlightTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ComboItemHoverBackgroundBrush()));
            highlightTrigger.Setters.Add(new Setter(Control.ForegroundProperty, WindowForegroundBrush()));

            var selectedTrigger = new Trigger
            {
                Property = ListBoxItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ComboItemSelectedBackgroundBrush()));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, WindowForegroundBrush()));

            itemStyle.Triggers.Add(highlightTrigger);
            itemStyle.Triggers.Add(selectedTrigger);

            cb.ItemContainerStyle = itemStyle;

            var style = new Style(typeof(ComboBox), cb.Style);
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildComboBoxTemplate()));
            cb.Style = style;
        }

        private static void ApplyCardChrome(Border border)
        {
            if (border == null)
                return;

            border.Background = CardBackgroundBrush();
            border.BorderBrush = CardBorderBrush();
            border.BorderThickness = new Thickness(1);
            border.CornerRadius = new CornerRadius(8);
            border.Effect = SoftShadow();
        }
    }
}