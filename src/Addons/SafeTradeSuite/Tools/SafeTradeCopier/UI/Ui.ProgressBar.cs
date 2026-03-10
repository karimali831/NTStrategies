using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        private static ControlTemplate _roundedProgressTemplateLeft;
        private static ControlTemplate _roundedProgressTemplateRight;

        private static readonly Brush GreenGrad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0, 180, 0), 0.0),
                new GradientStop(Color.FromRgb(0, 90, 0), 1.0)
            }
        };

        private static readonly Brush RedGrad = new LinearGradientBrush
        {
            StartPoint = new Point(1, 0.5),
            EndPoint = new Point(0, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(220, 40, 40), 0.0),
                new GradientStop(Color.FromRgb(120, 0, 0), 1.0)
            }
        };
        
        private static void SetBarValueAnimated(ProgressBar bar, double target)
        {
            if (bar == null) return;

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
                EnsureRoundedProgressBar(bar, alignRight: false);

                var denom = targetRisk > 0 ? targetRisk : 1.0;
                var p = Clamp01(unrealized / denom);
                bar.Foreground = GreenGrad;
                bar.Tag = "LIVE_POS";
                SetBarValueAnimated(bar, p * 100.0);
            }
            else
            {
                EnsureRoundedProgressBar(bar, alignRight: true);

                var denom = stopRisk > 0 ? stopRisk : 1.0;
                var used = Clamp01(Math.Abs(unrealized) / denom);
                var remaining = 1.0 - used;
                bar.Foreground = RedGrad;
                bar.Tag = "LIVE_NEG";
                SetBarValueAnimated(bar, remaining * 100.0);
            }
        }
        
        private static double Clamp01(double x)
        {
            if (x < 0) return 0;
            if (x > 1) return 1;
            return x;
        }
        
        private static double GetTickValue(Instrument instr)
        {
            if (instr?.MasterInstrument == null) return 0.0;
            var tickSize = instr.MasterInstrument.TickSize;
            var pointValue = instr.MasterInstrument.PointValue;
            if (tickSize <= 0 || pointValue <= 0) return 0.0;
            return tickSize * pointValue;
        }
        
        private static void EnsureRoundedProgressBar(ProgressBar bar, bool alignRight)
        {
            if (bar == null) return;

            if (_roundedProgressTemplateLeft == null)
                _roundedProgressTemplateLeft = BuildRoundedProgressTemplate(HorizontalAlignment.Left);

            if (_roundedProgressTemplateRight == null)
                _roundedProgressTemplateRight = BuildRoundedProgressTemplate(HorizontalAlignment.Right);

            var wanted = alignRight ? _roundedProgressTemplateRight : _roundedProgressTemplateLeft;
            if (!ReferenceEquals(bar.Template, wanted))
                bar.Template = wanted;

            bar.BorderThickness = new Thickness(0);
            bar.Background = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            bar.SnapsToDevicePixels = true;
        }
        
        private static void FinalizeBarOutcomeFromTag(ProgressBar bar)
        {
            if (bar == null) return;

            var tag = bar.Tag as string;

            if (string.Equals(tag, "LIVE_NEG", StringComparison.Ordinal))
                bar.Tag = "STOP_FILLED";
            else if (string.Equals(tag, "LIVE_POS", StringComparison.Ordinal))
                bar.Tag = "TARGET_FILLED";
        }
        
        private static void ShowBarOutcome(ProgressBar bar, TextBlock statusText)
        {
            if (bar == null || statusText == null) return;

            var tag = (bar.Tag as string) ?? "";

            bar.Visibility = Visibility.Visible;

            // stop any previous animation so final state renders exactly
            bar.BeginAnimation(RangeBase.ValueProperty, null);

            if (string.Equals(tag, "STOP_FILLED", StringComparison.Ordinal))
            {
                EnsureRoundedProgressBar(bar, alignRight: true);
                bar.Foreground = RedGrad;
                bar.Value = 0;
                statusText.Text = "Stop Filled";
                statusText.Foreground = Brushes.IndianRed;
            }
            else if (string.Equals(tag, "ORDER_FILLED", StringComparison.Ordinal))
            {
                EnsureRoundedProgressBar(bar, alignRight: false);
                bar.Foreground = GreenGrad;
                bar.Value = 100;
                statusText.Text = "Order Filled";
                statusText.Foreground = Brushes.SteelBlue;
            }
            else
            {
                EnsureRoundedProgressBar(bar, alignRight: false);
                bar.Foreground = GreenGrad;
                bar.Value = 100;
                statusText.Text = "Target Filled";
                statusText.Foreground = Brushes.DarkGreen;
            }

            statusText.Visibility = Visibility.Visible;
        }

        private static void ClearBarOutcome(TextBlock statusText)
        {
            if (statusText == null) return;
            statusText.Text = "";
            statusText.Visibility = Visibility.Collapsed;
        }

        private static ControlTemplate BuildRoundedProgressTemplate(HorizontalAlignment alignment)
        {
            var template = new ControlTemplate(typeof(ProgressBar));

            var outer = new FrameworkElementFactory(typeof(Border));
            outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            outer.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            outer.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            outer.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var grid = new FrameworkElementFactory(typeof(Grid));
            outer.AppendChild(grid);

            var fill = new FrameworkElementFactory(typeof(Border))
            {
                Name = "PART_Indicator"
            };
            fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            fill.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            fill.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, alignment);
            fill.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
            fill.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var mb = new MultiBinding { Converter = new ProgressToWidthConverter() };
            mb.Bindings.Add(new Binding("Value") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            mb.Bindings.Add(new Binding("Maximum") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            mb.Bindings.Add(new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            fill.SetBinding(FrameworkElement.WidthProperty, mb);

            grid.AppendChild(fill);

            template.VisualTree = outer;
            return template;
        }
        
        private static Brush GetPnlValueBrush(double value)
        {
            if (value > 0.009)
                return Brushes.DarkGreen;

            if (value < -0.009)
                return Brushes.Firebrick;

            return Brushes.DimGray;
        }

        private static void SetPnlText(TextBlock tb, string prefix, double realized, double unrealized, bool shortened)
        {
            if (tb == null)
                return;

            tb.Inlines.Clear();

            if (!shortened)
            {
                tb.Inlines.Add(new System.Windows.Documents.Run(prefix + "  ")
                {
                    Foreground = Brushes.DimGray,
                    FontWeight = FontWeights.SemiBold
                });
            }

            tb.Inlines.Add(new System.Windows.Documents.Run("R ")
            {
                Foreground = Brushes.DimGray,
                FontWeight = FontWeights.SemiBold
            });

            tb.Inlines.Add(new System.Windows.Documents.Run(FmtUsd(realized))
            {
                Foreground = GetPnlValueBrush(realized),
                FontWeight = FontWeights.SemiBold
            });

            tb.Inlines.Add(new System.Windows.Documents.Run("   •   ")
            {
                Foreground = Brushes.DimGray,
                FontWeight = FontWeights.SemiBold
            });

            tb.Inlines.Add(new System.Windows.Documents.Run("U ")
            {
                Foreground = Brushes.DimGray,
                FontWeight = FontWeights.SemiBold
            });

            tb.Inlines.Add(new System.Windows.Documents.Run(FmtUsd(unrealized))
            {
                Foreground = GetPnlValueBrush(unrealized),
                FontWeight = FontWeights.SemiBold
            });
        }
    }
}