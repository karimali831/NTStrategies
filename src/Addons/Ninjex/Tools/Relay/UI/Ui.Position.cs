using System;
using System.Windows;
using System.Windows.Controls;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.Tools.RelayTool
{
    public partial class RelayTool
    {
        private void OnUiPositionUpdate(object sender, PositionEventArgs e)
        {
            if (!(sender is Account acc)) return;
            if (e?.Position == null) return;

            NinjexRuntime.PrintLog(
                $"[UI POS EVT] acc={acc?.Name} instr={e.Position?.Instrument?.FullName} qty={e.Position?.Quantity} mp={e.Position?.MarketPosition}");

            var instrFull = e.Position.Instrument?.FullName ?? "";
            var key = $"{acc.Name}|{instrFull}";
            var qty = e.Position.Quantity;

            lock (_uiNet)
            {
                if (Math.Abs(qty) > 0)
                    _uiNet[key] = qty;
                else
                    _uiNet.Remove(key);
            }

            RenderPnlUi();
            // RenderMasterSubmitButtonsState();
            RenderFlattenEnablementUi();
            RenderBreakEvenEnablementUi();
            // InvalidatePositionsPanel();
        }
        
        private void RenderLivePositionText(TextBlock tb, Account acc)
        {
            if (tb == null)
                return;

            var isMasterAccount = acc == GetMasterAccount();
            var text = GetLivePosition(acc, isMasterAccount);

            tb.Text = text ?? "";
            tb.FontWeight = FontWeights.SemiBold;

            if (string.IsNullOrWhiteSpace(text))
            {
                tb.Foreground = MutedForegroundBrush();
                tb.ToolTip = null;
                return;
            }

            if (text.StartsWith("Long", StringComparison.OrdinalIgnoreCase))
            {
                // tb.Foreground = SuccessActionBrush();
                tb.ToolTip = "Live long position";
                return;
            }

            if (text.StartsWith("Short", StringComparison.OrdinalIgnoreCase))
            {
                // tb.Foreground = DangerActionBrush();
                tb.ToolTip = "Live short position";
                return;
            }

            tb.Foreground = WarningActionBrush();
            tb.ToolTip = "Live counter positions";
        }
    }
}