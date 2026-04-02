using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static ScrollViewer CreateScrollbar(
            StackPanel root,
            bool? canContentScroll = false,
            int? height = null)
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = canContentScroll ?? false,
                Content = root
            };

            if (height.HasValue)
                scroll.Height = height.Value;

            ApplyScrollViewerTheme(scroll);
            return scroll;
        }

        private static Style BuildThemedThumbStyle()
        {
            var style = new Style(typeof(Thumb));

            style.Setters.Add(new Setter(Control.BackgroundProperty, ScrollBarThumbBrush()));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildThemedThumbTemplate()));

            return style;
        }

        private static ControlTemplate BuildThemedThumbTemplate()
        {
            var template = new ControlTemplate(typeof(Thumb));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ThumbBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            template.VisualTree = border;

            var hoverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ScrollBarThumbHoverBrush()));

            var dragTrigger = new Trigger
            {
                Property = Thumb.IsDraggingProperty,
                Value = true
            };
            dragTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ScrollBarThumbHoverBrush()));

            template.Triggers.Add(hoverTrigger);
            template.Triggers.Add(dragTrigger);

            return template;
        }

        private static void ApplyScrollViewerTheme(ScrollViewer scrollViewer)
        {
            if (scrollViewer == null)
                return;

            scrollViewer.Background = Brushes.Transparent;
            scrollViewer.Resources[typeof(Thumb)] = BuildThemedThumbStyle();
        }

        private static Brush ScrollBarTrackBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(26, 26, 26))
                : new SolidColorBrush(Color.FromRgb(236, 236, 236));
        }

        private static Brush ScrollBarThumbBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(82, 82, 82))
                : new SolidColorBrush(Color.FromRgb(176, 176, 176));
        }

        private static Brush ScrollBarThumbHoverBrush()
        {
            return IsDarkTheme()
                ? new SolidColorBrush(Color.FromRgb(112, 112, 112))
                : new SolidColorBrush(Color.FromRgb(146, 146, 146));
        }
    }
}