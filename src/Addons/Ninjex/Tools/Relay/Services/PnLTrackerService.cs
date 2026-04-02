using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
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
        
    public partial class RelayTool
    {
        private readonly Dictionary<string, int> _uiNet = new Dictionary<string, int>(StringComparer.Ordinal);

        private void RenderPnlUi()
        {
            if (_isClosing)
                return;

            var display = _uiDispatcher ?? _window?.Dispatcher;
            if (display == null || display.HasShutdownStarted || display.HasShutdownFinished)
                return;

            display.InvokeAsync(() =>
            {
                if (_isClosing || _window == null)
                    return;

                var instr = GetInstrument();
                if (instr == null)
                    return;

                var totalR = 0.0;
                var totalU = 0.0;

                var followersR = 0.0;
                var followersU = 0.0;
                var openFollowerCount = 0;

                if (GetMasterAccount() is Account master)
                {
                    TryGetInstrumentUnrealized(master, instr, out var mu, out _);
                    _engine.TryGetRealizedPnl(master, out var mr);

                    totalR += mr;
                    totalU += mu;

                    if (_masterPnlText != null)
                        SetPnlText(_masterPnlText, "", mr, mu, shortened: false, master);

                    RenderLivePositionText(_masterPositionText, master);
                    RenderProgressBar(_masterPnlBar, _masterPnlBarStatusText, master);
                }

                foreach (var row in _followerRows)
                {
                    var acc = row?.Account;
                    if (acc == null)
                        continue;

                    TryGetInstrumentUnrealized(acc, instr, out var u, out var absQty);
                    _engine.TryGetRealizedPnl(acc, out var r);

                    totalR += r;
                    totalU += u;

                    followersR += r;
                    followersU += u;

                    if (absQty > 0)
                        openFollowerCount++;

                    if (row.PnlText != null)
                        SetPnlText(row.PnlText, "", r, u, shortened: true, acc);

                    RenderLivePositionText(row.Position, acc);
                    RenderProgressBar(row.PnlBar, row.PnlBarStatusText, acc);
                }

                if (_followersTotalPnlText != null)
                    SetPnlText(
                        _followersTotalPnlText, "Total", followersR, followersU, shortened: false, acc: null, openFollowerCount > 1);

                RenderFlattenEnablementUi();
                RenderBreakEvenEnablementUi();
            }, DispatcherPriority.Render);
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
                
        private void LogBarDiagnostics(Account account, Instrument instr, double unrealized, int qty, RelayEngine.ActiveBracketSpec spec)
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
            // NinjexRuntime.PrintLog(line);
        }
    }
}