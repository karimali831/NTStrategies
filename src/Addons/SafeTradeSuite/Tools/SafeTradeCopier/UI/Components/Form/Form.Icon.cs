using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
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
                // CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = toolTip
            };

            ApplyIconActionTheme(border, tone, isPressed: false, isHover: false);

            var path = new Path
            {
                Data = iconGeometry,
                Stroke = ActionForegroundBrush(),
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

            border.MouseEnter += (s, e) => ApplyIconActionTheme(border, tone, isPressed: false, isHover: true);
            border.MouseLeave += (s, e) => ApplyIconActionTheme(border, tone, isPressed: false, isHover: false);
            border.MouseLeftButtonDown += (s, e) =>
            {
                ApplyIconActionTheme(border, tone, isPressed: true, isHover: true);
                e.Handled = true;
            };
            border.MouseLeftButtonUp += (s, e) =>
            {
                ApplyIconActionTheme(border, tone, isPressed: false, isHover: true);
                onClick?.Invoke();
                e.Handled = true;
            };

            return border;
        }

        private static void ApplyIconActionTheme(Border border, FormButtonTone tone, bool isPressed, bool isHover)
        {
            if (border == null)
                return;

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
        }
    }
}