using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        private static readonly Dictionary<ProgressBar, DispatcherTimer> BarHideTimers =
            new Dictionary<ProgressBar, DispatcherTimer>();
        private readonly Dictionary<string, string> _barDiagCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static ControlTemplate _roundedProgressTemplateLeft;
        private static ControlTemplate _roundedProgressTemplateRight;

        private void RenderProgressBar(ProgressBar bar, TextBlock statusText, Account account)
        {
            if (bar == null || statusText == null || account == null || _engine == null)
                return;

            var instr = GetInstrument();
            if (instr == null)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                bar.Tag = null;
                ClearBarOutcome(statusText, bar);
                return;
            }

            var hasBracket = _engine.TryGetActiveBracketSpec(account, instr, out var spec);

            var uTmp = 0.0;
            var qTmp = 0;
            var hasOpenPosition = false;

            if (hasBracket)
                hasOpenPosition = TryGetInstrumentUnrealized(account, instr, out uTmp, out qTmp);

            if (hasOpenPosition && spec != null)
            {
                ClearBarOutcome(statusText, bar);

                var qty = Math.Max(1, qTmp);

                LogBarDiagnostics(account, instr, uTmp, qty, spec);
                RenderFlipBar(bar, uTmp, qty, spec, instr);
                return;
            }

            var tag = bar.Tag as string ?? "";
            var canFinalizeNow =
                string.Equals(tag, "LIVE_NEG", StringComparison.Ordinal) ||
                string.Equals(tag, "LIVE_POS", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED_POS", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED_NEG", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED_NEUTRAL", StringComparison.Ordinal);

            if (canFinalizeNow)
            {
                FinalizeBarOutcomeFromTag(bar);
                ShowBarOutcome(bar, statusText);
                return;
            }

            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Visibility = Visibility.Collapsed;
            bar.Value = 0;

            var existingTag = bar.Tag as string;
            if (string.IsNullOrWhiteSpace(existingTag) ||
                (!existingTag.StartsWith("OUTCOME_DONE:", StringComparison.Ordinal) &&
                 !existingTag.StartsWith("OUTCOME_PENDING:", StringComparison.Ordinal)))
            {
                bar.Tag = null;
                ClearBarOutcome(statusText, bar);
            }
        }

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
            if (bar == null)
                return;

            if (target < bar.Minimum) target = bar.Minimum;
            if (target > bar.Maximum) target = bar.Maximum;

            // if bar hasn't measured yet, just set directly
            if (bar.ActualWidth <= 0)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Value = target;
                return;
            }

            var current = bar.Value;

            if (Math.Abs(current - target) < 0.01)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Value = target;
                return;
            }

            var anim = new DoubleAnimation
            {
                From = current,
                To = target,
                Duration = TimeSpan.FromMilliseconds(80)
            };

            bar.BeginAnimation(RangeBase.ValueProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
        
        private static void RenderFlipBar(
            ProgressBar bar,
            double unrealized,
            int qty,
            SafeCopierEngine.ActiveBracketSpec spec,
            Instrument instr)
        {
            if (bar == null)
                return;

            StopHideTimer(bar);

            if (instr == null || spec == null || qty <= 0)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

            var tickValue = GetTickValue(instr);
            if (tickValue <= 0 || spec.EntryPrice <= 0)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

            var stopRisk = 0.0;
            var targetRisk = 0.0;

            if (spec.CurrentStopPrice > 0)
                stopRisk = Math.Abs(spec.EntryPrice - spec.CurrentStopPrice) / instr.MasterInstrument.TickSize * tickValue * qty;

            if (spec.TargetPrice > 0)
                targetRisk = Math.Abs(spec.TargetPrice - spec.EntryPrice) / instr.MasterInstrument.TickSize * tickValue * qty;

            if (stopRisk <= 0 && targetRisk <= 0)
            {
                bar.BeginAnimation(RangeBase.ValueProperty, null);
                bar.Visibility = Visibility.Collapsed;
                bar.Value = 0;
                return;
            }

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
                bar.Foreground = RedGrad;
                bar.Tag = "LIVE_NEG";
                SetBarValueAnimated(bar, used * 100.0);
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
            {
                bar.Template = wanted;
                bar.ApplyTemplate();
            }

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
            if (bar == null || statusText == null)
                return;

            StopHideTimer(bar);

            var tag = (bar.Tag as string) ?? "";

            bar.BeginAnimation(RangeBase.ValueProperty, null);

            string outcomeCode;
            string outcomeText;
            Brush outcomeBrush;
            Brush fillBrush;
            bool alignRight;

            if (string.Equals(tag, "STOP_FILLED", StringComparison.Ordinal))
            {
                outcomeCode = "STOP_FILLED";
                outcomeText = "Stop Filled";
                outcomeBrush = Brushes.IndianRed;
                fillBrush = RedGrad;
                alignRight = true;
            }
            else if (string.Equals(tag, "TARGET_FILLED", StringComparison.Ordinal))
            {
                outcomeCode = "TARGET_FILLED";
                outcomeText = "Target Filled";
                outcomeBrush = Brushes.DarkGreen;
                fillBrush = GreenGrad;
                alignRight = false;
            }
            else
            {
                outcomeCode = "ORDER_FILLED";
                outcomeText = "Order Filled";

                if (string.Equals(tag, "ORDER_FILLED_POS", StringComparison.Ordinal))
                {
                    outcomeBrush = Brushes.DarkGreen;
                    fillBrush = GreenGrad;
                    alignRight = false;
                }
                else if (string.Equals(tag, "ORDER_FILLED_NEG", StringComparison.Ordinal))
                {
                    outcomeBrush = Brushes.IndianRed;
                    fillBrush = RedGrad;
                    alignRight = true;
                }
                else
                {
                    outcomeBrush = Brushes.SteelBlue;
                    fillBrush = GreenGrad;
                    alignRight = false;
                }
            }

            EnsureRoundedProgressBar(bar, alignRight: alignRight);
            bar.Foreground = fillBrush;
            bar.Value = 100;
            bar.Visibility = Visibility.Visible;

            statusText.Text = "";
            statusText.Visibility = Visibility.Collapsed;

            bar.Tag = "OUTCOME_PENDING:" + outcomeCode;

            var hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2000)
            };

            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                BarHideTimers.Remove(bar);

                bar.Visibility = Visibility.Collapsed;
                bar.Tag = "OUTCOME_DONE:" + outcomeCode;

                statusText.Text = outcomeText;
                statusText.Foreground = outcomeBrush;
                statusText.Visibility = Visibility.Visible;
            };

            BarHideTimers[bar] = hideTimer;
            hideTimer.Start();
        }

        private static void ClearBarOutcome(TextBlock statusText, ProgressBar bar = null)
        {
            if (bar != null)
            {
                StopHideTimer(bar);

                var tag = bar.Tag as string;
                if (!string.IsNullOrWhiteSpace(tag) &&
                    tag.StartsWith("OUTCOME_", StringComparison.Ordinal))
                {
                    bar.Tag = null;
                }
            }

            if (statusText == null)
                return;

            statusText.Text = "";
            statusText.Visibility = Visibility.Collapsed;
        }
        
        private static void StopHideTimer(ProgressBar bar)
        {
            if (bar == null)
                return;

            if (BarHideTimers.TryGetValue(bar, out var existing))
            {
                existing.Stop();
                BarHideTimers.Remove(bar);
            }
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
                    Foreground = MutedForegroundBrush(),
                    FontWeight = FontWeights.SemiBold
                });
            }
            
            tb.Inlines.Add(new System.Windows.Documents.Run("Unrealized ")
            {
                Foreground = MutedForegroundBrush(),
                FontWeight = FontWeights.SemiBold
            });

            tb.Inlines.Add(new System.Windows.Documents.Run(FmtUsd(unrealized))
            {
                Foreground = GetPnlValueBrush(unrealized),
                FontWeight = FontWeights.SemiBold
            });
        }
        
        private void LogBarDiagnostics(Account account, Instrument instr, double unrealized, int qty, SafeCopierEngine.ActiveBracketSpec spec)
        {
            if (account == null || instr == null || spec == null || qty <= 0)
                return;

            var tickValue = GetTickValue(instr);
            var tickSize = instr.MasterInstrument?.TickSize ?? 0.0;
            if (tickValue <= 0 || tickSize <= 0)
                return;

            var stopRisk = 0.0;
            var targetRisk = 0.0;

            if (spec.CurrentStopPrice > 0 && spec.EntryPrice > 0)
                stopRisk = Math.Abs(spec.EntryPrice - spec.CurrentStopPrice) / tickSize * tickValue * qty;

            if (spec.TargetPrice > 0 && spec.EntryPrice > 0)
                targetRisk = Math.Abs(spec.TargetPrice - spec.EntryPrice) / tickSize * tickValue * qty;

            string mode;
            double pct;
            string mathText;

            if (unrealized >= 0)
            {
                var denom = targetRisk > 0 ? targetRisk : 1.0;
                var raw = unrealized / denom;
                var clamped = Clamp01(raw);
                pct = clamped * 100.0;
                mode = "POS";
                mathText = $"pct = Clamp01({unrealized:0.00} / {denom:0.00}) * 100 = {pct:0.0}";
            }
            else
            {
                var denom = stopRisk > 0 ? stopRisk : 1.0;
                var rawUsed = Math.Abs(unrealized) / denom;
                var used = Clamp01(rawUsed);
                pct = used * 100.0;
                mode = "NEG";
                mathText = $"pct = Clamp01({Math.Abs(unrealized):0.00} / {denom:0.00}) * 100 = {pct:0.0}";
            }

            var key = account.Name + "|" + instr.FullName;
            var line =
                $"[BAR DIAG] acc={account.Name} instr={instr.FullName} " +
                $"u={unrealized:0.00} qty={qty} " +
                $"entry={spec.EntryPrice:0.00} stop={spec.CurrentStopPrice:0.00} target={spec.TargetPrice:0.00} " +
                $"tickValue={tickValue:0.00} stopRisk={stopRisk:0.00} targetRisk={targetRisk:0.00} " +
                $"mode={mode} {mathText}";

            if (_barDiagCache.TryGetValue(key, out var prev) && prev == line)
                return;

            _barDiagCache[key] = line;
            SafeTradeSuiteRuntime.PrintLog(line);
        }
    }
}