using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private static ComboBox CreateFormComboBox(
            double width = 120, 
            Thickness? margin = null, 
            bool editable = false)
        {
            var cb = new ComboBox
            {
                Width = width,
                Margin = margin ?? new Thickness(0),
                IsEditable = editable,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            ApplyInputChrome(cb);
            ApplyComboBoxTheme(cb);

            cb.Loaded += (s, e) => ApplyEditableComboTextTheme(cb);
            cb.SelectionChanged += (s, e) => ApplyEditableComboTextTheme(cb);
            cb.DropDownClosed += (s, e) => ApplyEditableComboTextTheme(cb);
            cb.IsEnabledChanged += (s, e) => ApplyEditableComboTextTheme(cb);

            return cb;
        }
        
        private static ControlTemplate BuildComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));

            var root = new FrameworkElementFactory(typeof(Grid));
            root.Name = "Root";
            root.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "OuterBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var innerGrid = new FrameworkElementFactory(typeof(Grid));

            var contentColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            contentColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));

            var arrowColumn = new FrameworkElementFactory(typeof(ColumnDefinition));
            arrowColumn.SetValue(ColumnDefinition.WidthProperty, new GridLength(30));

            innerGrid.AppendChild(contentColumn);
            innerGrid.AppendChild(arrowColumn);

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.Name = "ContentSite";
            contentPresenter.SetValue(Grid.ColumnProperty, 0);
            contentPresenter.SetValue(ContentPresenter.MarginProperty, new Thickness(8, 0, 4, 0));
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
            contentPresenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemTemplateProperty));
            contentPresenter.SetValue(ContentPresenter.ContentTemplateSelectorProperty, new TemplateBindingExtension(ItemsControl.ItemTemplateSelectorProperty));
            contentPresenter.SetValue(UIElement.IsHitTestVisibleProperty, false);

            var editableTextBox = new FrameworkElementFactory(typeof(TextBox));
            editableTextBox.Name = "PART_EditableTextBox";
            editableTextBox.SetValue(Grid.ColumnProperty, 0);
            editableTextBox.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 4, 0));
            editableTextBox.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            editableTextBox.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            editableTextBox.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            editableTextBox.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);

            var arrowHost = new FrameworkElementFactory(typeof(Border));
            arrowHost.Name = "ArrowHost";
            arrowHost.SetValue(Grid.ColumnProperty, 1);
            arrowHost.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            arrowHost.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            arrowHost.SetValue(UIElement.IsHitTestVisibleProperty, false);

            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.Name = "Arrow";
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0"));
            arrow.SetValue(Path.StrokeProperty, InputForegroundBrush());
            arrow.SetValue(Path.StrokeThicknessProperty, 2.0);
            arrow.SetValue(Path.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Path.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Path.StrokeLineJoinProperty, PenLineJoin.Round);
            arrow.SetValue(FrameworkElement.WidthProperty, 8.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 4.0);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            arrowHost.AppendChild(arrow);

            var toggleButton = new FrameworkElementFactory(typeof(ToggleButton));
            toggleButton.Name = "DropDownToggle";
            toggleButton.SetValue(Grid.ColumnSpanProperty, 2);
            toggleButton.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggleButton.SetValue(Control.BorderBrushProperty, Brushes.Transparent);
            toggleButton.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggleButton.SetValue(UIElement.FocusableProperty, false);
            toggleButton.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);

            toggleButton.SetBinding(
                ToggleButton.IsCheckedProperty,
                new Binding("IsDropDownOpen")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                    Mode = BindingMode.TwoWay
                });

            var toggleTemplate = new ControlTemplate(typeof(ToggleButton));
            var toggleRoot = new FrameworkElementFactory(typeof(Border));
            toggleRoot.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            toggleRoot.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            toggleRoot.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            toggleTemplate.VisualTree = toggleRoot;
            toggleButton.SetValue(Control.TemplateProperty, toggleTemplate);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetBinding(
                Popup.IsOpenProperty,
                new Binding("IsDropDownOpen")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                    Mode = BindingMode.TwoWay
                });
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.Name = "PopupBorder";
            popupBorder.SetValue(Border.BackgroundProperty, InputBackgroundBrush());
            popupBorder.SetValue(Border.BorderBrushProperty, ComboPopupBorderBrush());
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scrollViewer.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            scrollViewer.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            scrollViewer.AppendChild(itemsPresenter);
            popupBorder.AppendChild(scrollViewer);
            popup.AppendChild(popupBorder);

            innerGrid.AppendChild(toggleButton);
            innerGrid.AppendChild(contentPresenter);
            innerGrid.AppendChild(editableTextBox);
            innerGrid.AppendChild(arrowHost);

            border.AppendChild(innerGrid);
            root.AppendChild(border);
            root.AppendChild(popup);

            template.VisualTree = root;

            var editableTrigger = new Trigger
            {
                Property = ComboBox.IsEditableProperty,
                Value = true
            };
            editableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "PART_EditableTextBox"));
            editableTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Hidden, "ContentSite"));
            template.Triggers.Add(editableTrigger);

            var disabledTrigger = new Trigger
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(Border.BackgroundProperty, InputDisabledBackgroundBrush(), "OuterBorder"));
            disabledTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, DisabledBorderBrush(), "OuterBorder"));
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9, "Root"));
            template.Triggers.Add(disabledTrigger);

            return template;
        }
        
        private static void ApplyEditableComboTextTheme(ComboBox cb)
        {
          if (cb == null || !cb.IsEditable)
              return;

          cb.ApplyTemplate();

          if (cb.Template == null)
              return;

          var tb = cb.Template.FindName("PART_EditableTextBox", cb) as TextBox;
          if (tb == null)
              return;

          tb.Background = cb.IsEnabled
              ? InputBackgroundBrush()
              : InputDisabledBackgroundBrush();

          tb.Foreground = cb.IsEnabled
              ? InputForegroundBrush()
              : InputDisabledForegroundBrush();

          tb.CaretBrush = cb.IsEnabled
              ? InputForegroundBrush()
              : InputDisabledForegroundBrush();

          tb.BorderThickness = new Thickness(0);
        }
    }
}