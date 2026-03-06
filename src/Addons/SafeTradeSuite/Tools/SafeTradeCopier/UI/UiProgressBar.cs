using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public sealed class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var value = values != null && values.Length > 0 ? System.Convert.ToDouble(values[0]) : 0.0;
                var max   = values != null && values.Length > 1 ? System.Convert.ToDouble(values[1]) : 100.0;
                var width = values != null && values.Length > 2 ? System.Convert.ToDouble(values[2]) : 0.0;

                if (max <= 0 || width <= 0) return 0.0;

                var p = value / max;
                if (p < 0) p = 0;
                if (p > 1) p = 1;

                return p * width;
            }
            catch
            {
                return 0.0;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
    
    public partial class SafeTradeCopierTool
    {
        private static ControlTemplate _roundedProgressTemplate;
        
        private static readonly Brush GreenGrad = new LinearGradientBrush(
            Color.FromRgb(0, 180, 0),
            Color.FromRgb(0, 90, 0),
            90);

        private static readonly Brush RedGrad = new LinearGradientBrush(
            Color.FromRgb(220, 40, 40),
            Color.FromRgb(120, 0, 0),
            90);

        // private static readonly Effect GlowGreen = new DropShadowEffect
        // {
        //     Color = Colors.LimeGreen,
        //     BlurRadius = 10,
        //     ShadowDepth = 0
        // };
        //
        // private static readonly Effect GlowRed = new DropShadowEffect
        // {
        //     Color = Colors.Red,
        //     BlurRadius = 10,
        //     ShadowDepth = 0
        // };
        
        private static void SetBarValueAnimated(ProgressBar bar, double target)
        {
            if (bar == null) return;

            // clamp
            if (target < bar.Minimum) target = bar.Minimum;
            if (target > bar.Maximum) target = bar.Maximum;

            var anim = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            bar.BeginAnimation(RangeBase.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
        
        private static void RenderFlipBar(ProgressBar bar, double unrealized, int qty, int stopTicks, int targetTicks, Instrument instr)
        {
            if (bar == null) return;

            EnsureRoundedProgressBar(bar);

            if (instr == null || qty <= 0 || (stopTicks <= 0 && targetTicks <= 0))
            {
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

            var tickValue = GetTickValue(instr);
            if (tickValue <= 0)
            {
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

            var stopRisk = stopTicks > 0 ? stopTicks * tickValue * qty : 0.0;
            var targetRisk = targetTicks > 0 ? targetTicks * tickValue * qty : 0.0;

            bar.Visibility = Visibility.Visible;

            if (unrealized >= 0)
            {
                var denom = targetRisk > 0 ? targetRisk : 1.0;
                var p = Clamp01(unrealized / denom);
                bar.Foreground = GreenGrad;
                SetBarValueAnimated(bar, p * 100.0);
            }
            else
            {
                var denom = stopRisk > 0 ? stopRisk : 1.0;
                var used = Clamp01(Math.Abs(unrealized) / denom);
                var remaining = 1.0 - used;
                bar.Foreground = RedGrad;
                SetBarValueAnimated(bar, remaining * 100.0);
            }

            var v = unrealized >= 0
                ? Clamp01(unrealized / (targetRisk > 0 ? targetRisk : 1.0)) * 100.0
                : (1.0 - Clamp01(Math.Abs(unrealized) / (stopRisk > 0 ? stopRisk : 1.0))) * 100.0;

            // if (v > 90) bar.Effect = GlowGreen;
            // else if (v < 10) bar.Effect = GlowRed;
            // else bar.Effect = null;
        }
        
        private static void EnsureRoundedProgressBar(ProgressBar bar)
        {
            if (bar == null) return;

            if (bar.Tag as string == "STC_ROUNDED_PB") return;

            if (_roundedProgressTemplate == null)
                _roundedProgressTemplate = BuildRoundedProgressTemplate();

            bar.Template = _roundedProgressTemplate;
            bar.BorderThickness = new Thickness(0);
            bar.Background = new SolidColorBrush(Color.FromRgb(220, 220, 220)); // track
            bar.SnapsToDevicePixels = true;

            bar.Tag = "STC_ROUNDED_PB";
        }

        private static ControlTemplate BuildRoundedProgressTemplate()
        {
            var template = new ControlTemplate(typeof(ProgressBar));

            // Outer track container
            var outer = new FrameworkElementFactory(typeof(Border));
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            outer.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            outer.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            // Grid hosts the fill
            var grid = new FrameworkElementFactory(typeof(Grid));
            outer.AppendChild(grid);

            // Fill (rounded), clipped by outer border and width bound to Value/Maximum * ActualWidth
            var fill = new FrameworkElementFactory(typeof(Border));
            fill.Name = "PART_Indicator";
            fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            fill.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            fill.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            fill.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            fill.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            // Width binding: (Value/Maximum) * Outer.ActualWidth
            var mb = new MultiBinding { Converter = new ProgressToWidthConverter() };
            mb.Bindings.Add(new Binding("Value") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            mb.Bindings.Add(new Binding("Maximum") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            mb.Bindings.Add(new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            fill.SetBinding(FrameworkElement.WidthProperty, mb);

            grid.AppendChild(fill);

            template.VisualTree = outer;
            return template;
        }

        private static ControlTemplate BuildFillButtonTemplate()
        {
            // Uses ProgressBar.Foreground for fill brush (your gradient)
            var t = new ControlTemplate(typeof(RepeatButton));

            var b = new FrameworkElementFactory(typeof(Border));
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            b.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            b.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            b.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            t.VisualTree = b;
            return t;
        }

        private static ControlTemplate BuildTrackButtonTemplate()
        {
            // Remaining portion: transparent (outer border already has Background track color)
            var t = new ControlTemplate(typeof(RepeatButton));

            var b = new FrameworkElementFactory(typeof(Border));
            b.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            b.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            b.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            b.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            t.VisualTree = b;
            return t;
        }
    }
}