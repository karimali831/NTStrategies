using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private enum FormButtonTone
        {
            Neutral,
            Primary,
            Success,
            Danger,
            Warning,
            Flatten
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
            bool bold = true,
            double? fontSize = null,
            bool? enabled = true
            )
        {
            var btn = new Button
            {
                Content = text ?? "",
                Width = width ?? 100,
                MinWidth = 0,
                Height = height ?? MainButtonHeight(),
                Margin = margin ?? new Thickness(0),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 0, 2, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = fontSize ?? 13
            };

            ApplyButtonTheme(btn, tone, style, enabled ?? true);
            return btn;
        }

        private static void ApplyButtonTheme(Button btn, FormButtonTone tone, FormButtonStyle style, bool enabled)
        {
            if (btn == null)
                return;

            btn.IsEnabled = enabled;
            btn.Opacity = enabled ? 1.0 : 0.65;
            btn.Focusable = false;
            btn.FocusVisualStyle = null;

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
                case FormButtonTone.Flatten:
                    accent = FlattenActionBrush();
                    break;
                default:
                    accent = OutlineNeutralBorderBrush();
                    break;
            }
            
            btn.BorderBrush = accent;
            var isSolidStyle = style == FormButtonStyle.Solid;
            var isFlattenBtn = tone == FormButtonTone.Flatten;

            var effectiveStyle = style;
            if (IsDarkTheme() && isSolidStyle && !isFlattenBtn)
                effectiveStyle = FormButtonStyle.Outline;

            if (!enabled)
            {
                if (isFlattenBtn)
                {
                    btn.Foreground = Brushes.White;
                    btn.Background = Brushes.Gray;
                    return;
                }
                
                btn.Foreground = DisabledForegroundBrush();
                btn.BorderBrush = IsDarkTheme() ? DisabledBorderBrush() : DisabledBackgroundBrush();
                btn.Background = IsDarkTheme() ? Brushes.Transparent : InputDisabledBackgroundBrush();
                
                return;
            }

            if (effectiveStyle == FormButtonStyle.Outline)
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = accent;
            }
            else
            {
                btn.Background = accent;
                btn.Foreground = ActionForegroundBrush();
            }
        }
    }
}