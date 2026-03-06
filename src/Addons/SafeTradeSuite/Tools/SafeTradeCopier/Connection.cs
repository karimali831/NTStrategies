using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private bool _connectionStatusHooked;

        private void HookConnectionStatusUpdates()
        {
            if (_connectionStatusHooked) return;

            Connection.ConnectionStatusUpdate += OnGlobalConnectionStatusUpdate;
            _connectionStatusHooked = true;
        }

        private void UnhookConnectionStatusUpdates()
        {
            if (!_connectionStatusHooked) return;

            Connection.ConnectionStatusUpdate -= OnGlobalConnectionStatusUpdate;
            _connectionStatusHooked = false;
        }

        private void OnGlobalConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
        {
            var display = _uiDispatcher ?? _window?.Dispatcher;

            display?.InvokeAsync(() =>
            {
                RefreshAccountsUi();

                _engine?.Log(
                    $"Connection update: " +
                    $"Conn={(e.Connection?.Options?.Name ?? "Unknown")} " +
                    $"Order={e.Status} Price={e.PriceStatus}"
                );
            }, DispatcherPriority.Background);
        }
    }
}