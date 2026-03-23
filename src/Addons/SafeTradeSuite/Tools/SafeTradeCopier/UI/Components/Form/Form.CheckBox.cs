using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Style _circularCheckBoxStyle;
        
        private static CheckBox CreateCheckBox(
            string text = null,
            bool isChecked = false,
            HorizontalAlignment? horizontalAlignment = null,
            Thickness? margin = null)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = margin ?? new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WindowForegroundBrush()
            };

            if (horizontalAlignment.HasValue)
                checkBox.HorizontalAlignment = horizontalAlignment.Value;

            return checkBox;
        }
        
        private static CheckBox CreateCircularCheckBox(Thickness? margin = null)
        {
            var checkbox = new CheckBox
            {
                Content = null,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            ApplyCircularCheckBoxStyle(checkbox);
            return checkbox;
        }
        
        private static void ApplyCircularCheckBoxStyle(CheckBox cb)
        {
            if (cb == null) return;

            if (_circularCheckBoxStyle == null)
                _circularCheckBoxStyle = BuildCircularCheckBoxStyle();

            cb.Style = _circularCheckBoxStyle;
        }
        
        private static Style BuildCircularCheckBoxStyle()
        {
            var style = new Style(typeof(CheckBox));

            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 18.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 18.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, DotOffBrush()));
            style.Setters.Add(new Setter(FrameworkElement.TagProperty, FollowerCheckVisualState.Off));

            var template = new ControlTemplate(typeof(CheckBox));

            var root = new FrameworkElementFactory(typeof(Grid))
            {
                Name = "Root"
            };
            root.SetValue(FrameworkElement.WidthProperty, 18.0);
            root.SetValue(FrameworkElement.HeightProperty, 18.0);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var border = new FrameworkElementFactory(typeof(Border))
            {
                Name = "Dot"
            };
            border.SetValue(FrameworkElement.WidthProperty, 14.0);
            border.SetValue(FrameworkElement.HeightProperty, 14.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.ForegroundProperty));

            var check = new FrameworkElementFactory(typeof(TextBlock))
            {
                Name = "CheckGlyph"
            };
            check.SetValue(TextBlock.TextProperty, CheckGlyph);
            check.SetValue(TextBlock.FontSizeProperty, 11.0);
            check.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            check.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            check.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            root.AppendChild(border);
            root.AppendChild(check);

            template.VisualTree = root;

            var offTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Off
            };
            offTrigger.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent, "Dot"));
            offTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotBorderBrush(), "Dot"));
            offTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "CheckGlyph"));

            var armedTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Armed
            };
            armedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotConnectedOnBrush(), "Dot"));
            armedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotConnectedOnBrush(), "Dot"));
            armedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));

            var warningTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Warning
            };
            warningTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotWarningBrush(), "Dot"));
            warningTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotWarningBrush(), "Dot"));
            warningTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckGlyph"));

            var disconnectedTrigger = new Trigger
            {
                Property = FrameworkElement.TagProperty,
                Value = FollowerCheckVisualState.Disconnected
            };
            disconnectedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotDisconnectedBrush(), "Dot"));
            disconnectedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotDisconnectedBrush(), "Dot"));
            disconnectedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "CheckGlyph"));

            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Root"));

            template.Triggers.Add(offTrigger);
            template.Triggers.Add(armedTrigger);
            template.Triggers.Add(warningTrigger);
            template.Triggers.Add(disconnectedTrigger);
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
        
        // private CheckBox CreateFormCheckBox2(
        //     string text,
        //     bool isChecked = false,
        //     Thickness? margin = null)
        // {
        //     var checkBox = new CheckBox
        //     {
        //         Content = text,
        //         IsChecked = isChecked,
        //         Margin = margin ?? new Thickness(0),
        //         Foreground = WindowForegroundBrush(),
        //         VerticalAlignment = VerticalAlignment.Center,
        //         VerticalContentAlignment = VerticalAlignment.Center,
        //         FontSize = 12
        //     };
        //
        //     var borderBrush = IsDarkTheme()
        //         ? new SolidColorBrush(Color.FromRgb(92, 92, 92))
        //         : new SolidColorBrush(Color.FromRgb(150, 150, 150));
        //
        //     var hoverBrush = IsDarkTheme()
        //         ? new SolidColorBrush(Color.FromRgb(36, 36, 36))
        //         : new SolidColorBrush(Color.FromRgb(240, 246, 252));
        //
        //     var boxBackground = IsDarkTheme()
        //         ? new SolidColorBrush(Color.FromRgb(18, 18, 18))
        //         : Brushes.White;
        //
        //     var checkFill = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        //
        //     var template = new ControlTemplate(typeof(CheckBox));
        //
        //     var root = new FrameworkElementFactory(typeof(Border));
        //     root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        //
        //     var stack = new FrameworkElementFactory(typeof(StackPanel));
        //     stack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        //     stack.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        //
        //     var boxOuter = new FrameworkElementFactory(typeof(Border))
        //     {
        //         Name = "BoxOuter"
        //     };
        //     boxOuter.SetValue(FrameworkElement.WidthProperty, 16.0);
        //     boxOuter.SetValue(FrameworkElement.HeightProperty, 16.0);
        //     boxOuter.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        //     boxOuter.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        //     boxOuter.SetValue(Border.BorderBrushProperty, borderBrush);
        //     boxOuter.SetValue(Border.BackgroundProperty, boxBackground);
        //     boxOuter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        //     boxOuter.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        //
        //     var checkMark = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path))
        //     {
        //         Name = "CheckMark"
        //     };
        //
        //     // smaller + centered geometry
        //     checkMark.SetValue(System.Windows.Shapes.Path.DataProperty,
        //         Geometry.Parse("M 4 8 L 7 11 L 12 5"));
        //
        //     checkMark.SetValue(System.Windows.Shapes.Shape.StrokeProperty, ThemeColor());
        //     checkMark.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.8);
        //     checkMark.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        //     checkMark.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        //     checkMark.SetValue(System.Windows.Shapes.Shape.StrokeLineJoinProperty, PenLineJoin.Round);
        //
        //     // 👇 key: center it properly inside the 16x16 box
        //     checkMark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        //     checkMark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        //     checkMark.SetValue(UIElement.RenderTransformProperty, new TranslateTransform(0, -0.5));
        //     checkMark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        //     boxOuter.AppendChild(checkMark);
        //
        //     var content = new FrameworkElementFactory(typeof(ContentPresenter));
        //     content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        //     content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        //     content.SetValue(TextElement.ForegroundProperty, WindowForegroundBrush());
        //
        //     stack.AppendChild(boxOuter);
        //     stack.AppendChild(content);
        //     root.AppendChild(stack);
        //
        //     template.VisualTree = root;
        //
        //     var checkedTrigger = new Trigger
        //     {
        //         Property = ToggleButton.IsCheckedProperty,
        //         Value = true
        //     };
        //     checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
        //     checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, ThemeColor(), "BoxOuter"));
        //     checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
        //         IsDarkTheme()
        //             ? new SolidColorBrush(Color.FromRgb(32, 24, 18))
        //             : new SolidColorBrush(Color.FromRgb(255, 245, 240)),
        //         "BoxOuter"));
        //     template.Triggers.Add(checkedTrigger);
        //
        //     var mouseOverTrigger = new Trigger
        //     {
        //         Property = UIElement.IsMouseOverProperty,
        //         Value = true
        //     };
        //     mouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "BoxOuter"));
        //     template.Triggers.Add(mouseOverTrigger);
        //
        //     var disabledTrigger = new Trigger
        //     {
        //         Property = UIElement.IsEnabledProperty,
        //         Value = false
        //     };
        //     disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6));
        //     template.Triggers.Add(disabledTrigger);
        //
        //     checkBox.Template = template;
        //
        //     return checkBox;
        // }
    }
}