using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private TextBox CreateOrderQtyBox(
            int? qty,
            string toolTip,
            bool allowBlank = false,
            bool transparentBg = false,
            Thickness? margin = null)
        {
            var qtyBox = CreateFormTextBox(qty.HasValue ? qty.Value.ToString() : "", width: 40);
            qtyBox.MaxLength = 2;
            
            if (margin.HasValue)
                qtyBox.Margin = margin.Value;

            qtyBox.ToolTip = toolTip ?? "Order quantity";

            qtyBox.PreviewTextInput += (s, e) =>
            {
                e.Handled = !char.IsDigit(e.Text, 0);
            };

            qtyBox.LostKeyboardFocus += (s, e) =>
            {
                var raw = (qtyBox.Text ?? "").Trim();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    qtyBox.Text = "";
                    ApplyConfigFromUi();
                    return;
                }

                if (!int.TryParse(raw, out var val))
                    val = 1;

                val = ClampQty(val);
                qtyBox.Text = val.ToString();
                ApplyConfigFromUi();
            };

            DataObject.AddPastingHandler(qtyBox, (s, e) =>
            {
                if (!e.DataObject.GetDataPresent(typeof(string)))
                {
                    e.CancelCommand();
                    return;
                }

                var text = ((string)e.DataObject.GetData(typeof(string)) ?? "").Trim();

                if (text.Length == 0)
                    return;

                if (!int.TryParse(text, out _))
                    e.CancelCommand();
            });

            return qtyBox;
        }

        private static int ClampQty(int value)
        {
            if (value < 1) return 1;
            if (value > 99) return 99;
            return value;
        }
    }
}