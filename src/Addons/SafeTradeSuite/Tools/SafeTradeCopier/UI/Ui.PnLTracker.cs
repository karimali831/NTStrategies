using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
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

            _engine.TryGetActiveBracketSpec(account, instr, out var spec);

            var hasOpenPosition = TryGetInstrumentUnrealized(account, instr, out var uTmp, out var qTmp);
            var hasLiveBracket = spec != null && _engine.HasWorkingBracketOrders(account, instr);

            if (hasOpenPosition && hasLiveBracket)
            {
                ClearBarOutcome(statusText, bar);

                var qty = Math.Max(1, qTmp);

                LogBarDiagnostics(account, instr, uTmp, qty, spec);
                RenderFlipBar(bar, uTmp, qty, spec, instr);
                return;
            }

            var tag = bar.Tag as string ?? "";

            if (tag.StartsWith("OUTCOME_SHOWING:", StringComparison.Ordinal))
                return;

            if (tag.StartsWith("LIVE_", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED_POS", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED_NEG", StringComparison.Ordinal) ||
                string.Equals(tag, "ORDER_FILLED_NEUTRAL", StringComparison.Ordinal))
            {
                FinalizeBarOutcomeFromTag(bar);
                ShowBarOutcome(bar, statusText);
                return;
            }

            if (tag.StartsWith("OUTCOME_DONE:", StringComparison.Ordinal))
            {
                bar.Tag = null;
                ClearBarOutcome(statusText, bar);
                return;
            }

            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Visibility = Visibility.Collapsed;
            bar.Value = 0;
            bar.Tag = null;
            ClearBarOutcome(statusText, bar);
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
        
        private static void ShowBarOutcome(ProgressBar bar, TextBlock statusText)
        {
            if (bar == null || statusText == null)
                return;

            StopHideTimer(bar);
            var tag = bar.Tag as string ?? "";

            string outcomeCode;
            string outcomeText;
            Brush outcomeBrush;

            if (string.Equals(tag, "STOP_FILLED", StringComparison.Ordinal))
            {
                outcomeCode = "STOP_FILLED";
                outcomeText = "Stop Filled";
                outcomeBrush = Brushes.IndianRed;
            }
            else if (string.Equals(tag, "TARGET_FILLED", StringComparison.Ordinal))
            {
                outcomeCode = "TARGET_FILLED";
                outcomeText = "Target Filled";
                outcomeBrush = Brushes.DarkGreen;
            }
            else
            {
                outcomeCode = "ORDER_FILLED";
                outcomeText = "Order Filled";

                if (string.Equals(tag, "ORDER_FILLED_POS", StringComparison.Ordinal))
                    outcomeBrush = Brushes.DarkGreen;
                else if (string.Equals(tag, "ORDER_FILLED_NEG", StringComparison.Ordinal))
                    outcomeBrush = Brushes.IndianRed;
                else
                    outcomeBrush = Brushes.SteelBlue;
            }

            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Visibility = Visibility.Collapsed;
            bar.Value = 0;

            statusText.Text = outcomeText;
            statusText.Foreground = outcomeBrush;
            statusText.Visibility = Visibility.Visible;

            bar.Tag = "OUTCOME_SHOWING:" + outcomeCode;

            var hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2000)
            };

            hideTimer.Tick += (s, e) =>
            {
                hideTimer.Stop();
                BarHideTimers.Remove(bar);

                statusText.Text = "";
                statusText.Visibility = Visibility.Collapsed;

                bar.Tag = "OUTCOME_DONE:" + outcomeCode;
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
                
                tb.Inlines.Add(new System.Windows.Documents.Run("Unrealized ")
                {
                    Foreground = MutedForegroundBrush(),
                    FontWeight = FontWeights.SemiBold
                });
            }

            tb.Inlines.Add(new System.Windows.Documents.Run(FmtUsd(unrealized))
            {
                Foreground = GetPnlValueBrush(unrealized),
                FontWeight = FontWeights.SemiBold
            });
        }
    }
}