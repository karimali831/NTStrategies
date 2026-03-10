using System.Linq;
using System.Windows.Threading;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.AddOns.SafeTradeSuite.Tools.SafeTradeCopier
{
    public partial class SafeTradeCopierTool
    {
        private enum UiConnectionState
        {
            Connected,
            Warning,
            Disconnected
        }
        
        private bool _connectionStatusHooked;
        
        private static UiConnectionState GetUiConnectionState(Account acc)
        {
            if (acc?.Connection == null)
                return UiConnectionState.Disconnected;

            var status = acc.ConnectionStatus;

            if (status == ConnectionStatus.Connected)
                return UiConnectionState.Connected;

            if (status == ConnectionStatus.ConnectionLost
                || status == ConnectionStatus.Connecting)
                return UiConnectionState.Warning;

            return UiConnectionState.Disconnected;
        }

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
                HandleFollowerConnectionSafety();
                TryAutoRearmAfterReconnect();
                RenderFlattenEnablementUi();

                _engine?.Log(
                    $"Connection update: " +
                    $"Conn={(e.Connection?.Options?.Name ?? "Unknown")} " +
                    $"Order={e.Status} Price={e.PriceStatus}"
                );
            }, DispatcherPriority.Background);
        }
        
        private void TryAutoRearmAfterReconnect()
        {
            if (_engine == null) return;
            if (_engine.CopyEnabled) return;
            if (_userManuallyDisarmed) return;
            if (!_autoRearmPending) return;
            if (!HasAnyCheckedFollowers()) return;
            if (!AllCheckedFollowersHealthy()) return;

            ApplyConfigFromUi();
            _engine.SetCopyEnabled(true);
            _autoRearmPending = false;

            _engine.Log("Copy auto-rearmed after connection recovered.");
        }
        
        private void HandleFollowerConnectionSafety()
        {
            if (_engine == null) return;

            var anySelectedBad = _followerRows.Any(r =>
            {
                if (r?.Account == null) return false;
                if (r.EnabledCheck?.IsChecked != true) return false;

                var state = GetUiConnectionState(r.Account);
                return state != UiConnectionState.Connected;
            });

            if (!anySelectedBad)
                return;

            if (_engine.CopyEnabled)
            {
                _autoRearmPending = true;
                _userManuallyDisarmed = false;

                _engine.SetCopyEnabled(false);
                _engine.Log("Copy disarmed: one or more selected followers lost connection.");
            }

            RenderFollowerRowsState();
            ApplyConfigFromUi();
        }
    }
}