using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static Style _circularCheckBoxStyle;
        
        private bool HasAnyCheckedFollowers()
        {
            return _followerRows.Any(r =>
                r?.Account != null &&
                r.EnabledCheck?.IsChecked == true);
        }

        private bool AllCheckedFollowersHealthy()
        {
            return _followerRows
                .Where(r => r?.Account != null && r.EnabledCheck?.IsChecked == true)
                .All(r => GetUiConnectionState(r.Account) == UiConnectionState.Connected);
        }
        
        private Grid BuildFollowerHeaderRow()
        {
            var g = new Grid
            {
                Margin = new Thickness(0, 0, 0, 4),
                Background = TableHeaderBrush()
            };

            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            AddHeaderText(g, " ", 0);
            AddHeaderText(g, "On", 1);
            AddHeaderText(g, "Account", 2);
            AddHeaderText(g, "Qty", 3);
            AddHeaderText(g, "ATM", 4);
            AddHeaderText(g, "PnL", 5);
            AddHeaderText(g, "Flatten", 6);

            return g;
        }

        private static void AddHeaderText(Grid g, string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text,
                Margin = new Thickness(6, 4, 6, 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = SystemColors.WindowTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(tb, col);
            g.Children.Add(tb);
        }
        
        private void ApplyCircularCheckBoxStyle(CheckBox cb)
        {
            if (cb == null) return;

            if (_circularCheckBoxStyle == null)
                _circularCheckBoxStyle = BuildCircularCheckBoxStyle();

            cb.Style = _circularCheckBoxStyle;
        }
        
         private Style BuildCircularCheckBoxStyle()
        {
            var style = new Style(typeof(CheckBox));

            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 18.0));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 18.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));

            var template = new ControlTemplate(typeof(CheckBox));

            var root = new FrameworkElementFactory(typeof(Grid))
            {
                Name = "Root"
            };
            root.SetValue(FrameworkElement.WidthProperty, 18.0);
            root.SetValue(FrameworkElement.HeightProperty, 18.0);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var border = new FrameworkElementFactory(typeof(Border))
            {
                Name = "Dot"
            };
            border.SetValue(FrameworkElement.WidthProperty, 14.0);
            border.SetValue(FrameworkElement.HeightProperty, 14.0);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, DotBorderBrush());
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            border.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            root.AppendChild(border);
            template.VisualTree = root;

            var checkedTrigger = new Trigger
            {
                Property = ToggleButton.IsCheckedProperty,
                Value = true
            };
            checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, DotConnectedOnBrush(), "Dot"));
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DotConnectedOnBrush(), "Dot"));

            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, "Root"));

            template.Triggers.Add(checkedTrigger);
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }
        
        private void RenderFollowerRowState(FollowerRow row)
        {
            if (row?.Account == null) return;

            var connState = GetUiConnectionState(row.Account);
            var connected = connState == UiConnectionState.Connected;
            var warning = connState == UiConnectionState.Warning;
            var disconnected = connState == UiConnectionState.Disconnected;

            if (row.StatusDot != null)
            {
                if (connected)
                {
                    // Healthy connection -> hide the separate status dot so we don't show "two checkboxes"
                    row.StatusDot.Visibility = Visibility.Collapsed;
                }
                else
                {
                    row.StatusDot.Visibility = Visibility.Visible;
                    row.StatusDot.BorderBrush = DotBorderBrush();

                    if (warning)
                        row.StatusDot.Background = DotWarningBrush();
                    else if (disconnected)
                        row.StatusDot.Background = DotDisconnectedBrush();
                    else
                        row.StatusDot.Background = DotOffBrush();
                }
            }

            var allowCheck = connected;

            if (row.EnabledCheck != null)
            {
                if (!allowCheck)
                    row.EnabledCheck.IsChecked = false;

                row.EnabledCheck.IsEnabled = allowCheck;
                row.EnabledCheck.Opacity = allowCheck ? 1.0 : 0.55;
            }

            var allowEdits = allowCheck && row.EnabledCheck?.IsChecked == true;

            if (row.QtyOverrideBox != null)
                row.QtyOverrideBox.IsEnabled = allowEdits;

            if (row.AtmOverrideBox != null)
                row.AtmOverrideBox.IsEnabled = allowEdits;
        }
        
        private void RenderFollowerRowsState()
        {
            foreach (var row in _followerRows)
                RenderFollowerRowState(row);
        }
    }
}