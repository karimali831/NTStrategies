using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private static Border CreateFormIconAction(
            Geometry iconGeometry,
            FormButtonTone tone = FormButtonTone.Primary,
            double width = 34,
            double height = 30,
            Thickness? margin = null,
            string toolTip = null,
            Action onClick = null)
        {
            var border = new Border
            {
                Width = width,
                Height = height,
                Margin = margin ?? new Thickness(0),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = toolTip,
                Tag = tone
            };

            var path = new Path
            {
                Data = iconGeometry,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Width = 10,
                Height = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            border.Child = new Grid
            {
                Children = { path }
            };

            ApplyIconActionTheme(border, tone, isPressed: false, isHover: false, isEnabled: true);

            border.MouseEnter += (s, e) =>
            {
                if (!border.IsHitTestVisible)
                    return;

                ApplyIconActionTheme(border, tone, isPressed: false, isHover: true, isEnabled: true);
            };

            border.MouseLeave += (s, e) =>
            {
                var enabled = border.IsHitTestVisible;
                ApplyIconActionTheme(border, tone, isPressed: false, isHover: false, isEnabled: enabled);
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                if (!border.IsHitTestVisible)
                    return;

                ApplyIconActionTheme(border, tone, isPressed: true, isHover: true, isEnabled: true);
                e.Handled = true;
            };

            border.MouseLeftButtonUp += (s, e) =>
            {
                if (!border.IsHitTestVisible)
                    return;

                ApplyIconActionTheme(border, tone, isPressed: false, isHover: true, isEnabled: true);
                onClick?.Invoke();
                e.Handled = true;
            };

            return border;
        }

        private static void ApplyIconActionTheme(Border border, FormButtonTone tone, bool isPressed, bool isHover, bool isEnabled)
        {
            if (border == null)
                return;

            var path = ((border.Child as Grid)?.Children.Count ?? 0) > 0
                ? (border.Child as Grid)?.Children[0] as Path
                : null;

            if (!isEnabled)
            {
                border.Background = InputDisabledBackgroundBrush();
                border.BorderBrush = OutlineNeutralBorderBrush();
                border.Opacity = 1.0;

                if (path != null)
                    path.Stroke = MutedForegroundBrush();

                return;
            }

            Brush accent;
            switch (tone)
            {
                case FormButtonTone.Primary:
                    accent = PrimaryActionBrush();
                    break;
                case FormButtonTone.Success:
                    accent = SuccessActionBrush();
                    break;
                case FormButtonTone.Danger:
                    accent = DangerActionBrush();
                    break;
                case FormButtonTone.Warning:
                    accent = WarningActionBrush();
                    break;
                default:
                    accent = OutlineNeutralBorderBrush();
                    break;
            }

            border.Background = accent;
            border.BorderBrush = accent;
            border.Opacity = isPressed ? 0.82 : isHover ? 0.92 : 1.0;

            if (path != null)
                path.Stroke = ActionForegroundBrush();
        }
        
        private static void SetFormIconActionEnabled(
            Border border,
            bool isEnabled,
            FormButtonTone tone,
            string enabledToolTip = null,
            string disabledToolTip = null)
        {
            if (border == null)
                return;

            border.IsHitTestVisible = isEnabled;
            border.Cursor = isEnabled ? Cursors.Hand : Cursors.Arrow;
            border.ToolTip = isEnabled ? enabledToolTip : disabledToolTip;

            ApplyIconActionTheme(
                border,
                tone,
                isPressed: false,
                isHover: false,
                isEnabled: isEnabled);
        }
    }
}