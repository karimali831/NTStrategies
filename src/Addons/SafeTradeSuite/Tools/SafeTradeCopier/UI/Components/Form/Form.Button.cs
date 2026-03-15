using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private enum FormButtonTone
        {
            Neutral,
            Primary,
            Success,
            Danger,
            Warning
        }

        private enum FormButtonStyle
        {
            Solid,
            Outline
        }

        private static Button CreateFormButton(
            string text,
            FormButtonTone tone = FormButtonTone.Neutral,
            FormButtonStyle style = FormButtonStyle.Solid,
            double? width = null,
            double? height = null,
            Thickness? margin = null,
            bool bold = true)
        {
            var btn = new Button
            {
                Content = text ?? "",
                Width = width ?? double.NaN,
                Height = height ?? MainButtonHeight(),
                Margin = margin ?? new Thickness(0),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 0, 2, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            ApplyButtonTheme(btn, tone, style, enabled: true);
            return btn;
        }

        private static void ApplyButtonTheme(Button btn, FormButtonTone tone, FormButtonStyle style, bool enabled)
        {
            if (btn == null)
                return;

            btn.IsEnabled = enabled;
            btn.Opacity = enabled ? 1.0 : 0.65;

            if (!enabled)
            {
                btn.Background = DisabledBackgroundBrush();
                btn.BorderBrush = DisabledBackgroundBrush();
                btn.Foreground = DisabledForegroundBrush();
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

            if (style == FormButtonStyle.Outline)
            {
                btn.Background = Brushes.Transparent;
                btn.BorderBrush = accent;
                btn.Foreground = accent;
            }
            else
            {
                btn.Background = accent;
                btn.BorderBrush = accent;
                btn.Foreground = ActionForegroundBrush();
            }
        }
    }
}