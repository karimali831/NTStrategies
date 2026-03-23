using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private void OnUiPositionUpdate(object sender, PositionEventArgs e)
        {
            if (!(sender is Account acc)) return;
            if (e?.Position == null) return;
            
            SafeTradeSuiteRuntime.PrintLog(
                $"[UI POS EVT] acc={acc?.Name} instr={e.Position?.Instrument?.FullName} qty={e.Position?.Quantity} mp={e.Position?.MarketPosition}");

            var instrFull = e.Position.Instrument?.FullName ?? "";
            var key = $"{acc.Name}|{instrFull}";
            var qty = e.Position.Quantity;

            lock (_uiNet)
                _uiNet[key] = qty;

            InvalidatePositionsPanel();
        }
    }
}