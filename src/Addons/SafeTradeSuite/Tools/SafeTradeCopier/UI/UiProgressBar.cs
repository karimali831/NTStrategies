using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using NinjaTrader.Cbi;
using static System.Windows.Controls.Border;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
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

        private static readonly Effect GlowGreen = new DropShadowEffect
        {
            Color = Colors.LimeGreen,
            BlurRadius = 10,
            ShadowDepth = 0
        };

        private static readonly Effect GlowRed = new DropShadowEffect
        {
            Color = Colors.Red,
            BlurRadius = 10,
            ShadowDepth = 0
        };
        
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

            // subtle glow near extremes
            if (bar.Value > 90) bar.Effect = GlowGreen;
            else if (bar.Value < 10) bar.Effect = GlowRed;
            else bar.Effect = null;
        }
    

        private static void EnsureRoundedProgressBar(ProgressBar bar)
        {
            if (bar == null) return;

            // avoid re-templating repeatedly
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

            var outer = new FrameworkElementFactory(typeof(Border))
            {
                Name = "OuterBorder"
            };
            outer.SetValue(CornerRadiusProperty, new CornerRadius(6));
            outer.SetValue(BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            outer.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));

            // The fill "indicator"
            var indicator = new FrameworkElementFactory(typeof(Border))
            {
                Name = "PART_Indicator"
            };
            indicator.SetValue(CornerRadiusProperty, new CornerRadius(6));
            indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            indicator.SetValue(BackgroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            indicator.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            // ProgressBar uses this to size the indicator
            indicator.SetValue(FrameworkElement.HeightProperty, new TemplateBindingExtension(FrameworkElement.HeightProperty));
            indicator.SetValue(FrameworkElement.MarginProperty, new Thickness(0));

            grid.AppendChild(indicator);
            outer.AppendChild(grid);

            template.VisualTree = outer;
            return template;
        }
        
        
    }
}