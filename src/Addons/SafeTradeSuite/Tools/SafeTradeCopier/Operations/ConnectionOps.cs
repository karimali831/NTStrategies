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
        
        private static string GetAccountConnectionLabel(Account acc)
        {
            if (acc == null)
                return "";

            var name = (acc.Connection?.Options?.Name ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            var text = acc.ConnectionStatus.ToString();
            return !string.IsNullOrWhiteSpace(text) ? text : "";
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
                RefreshCopierStatusPanel();
                RenderMasterSubmitButtonsState();

                _engine?.Log(
                    "Connection update: " +
                    $"Conn={e.Connection?.Options?.Name ?? "Unknown"} " +
                    $"Order={e.Status} Price={e.PriceStatus}"
                );
            }, DispatcherPriority.Background);
        }
        
        private void TryAutoRearmAfterReconnect()
        {
            if (_engine == null) return;
            if (_engine.IsRequested) return;
            if (!HasAnyCheckedFollowersHealthy()) return;

            if (ActiveSessionRequested())
                RequestArmed("Copy auto-rearmed after connection recovered.");
        }
        
        private void RequestArmed(string reason = null)
        {
            if (_engine == null || _activeInstrumentSession == null)
                return;

            if (!CanRequestArmedForActiveSession(out var blockReason))
            {
                _activeInstrumentSession.IsArmedRequested = false;
                RenderButtons();
                RefreshCopierStatusPanel();

                if (!string.IsNullOrWhiteSpace(blockReason))
                    _engine.Log(blockReason);

                return;
            }

            _activeInstrumentSession.IsArmedRequested = true;

            ApplyConfigFromUi();
            _engine.SetCopyEnabled(true);

            RenderButtons();
            RefreshCopierStatusPanel();

            if (!string.IsNullOrWhiteSpace(reason))
                _engine.Log(reason);
        }
        
        private void RequestDisarmed(string reason)
        {
            if (_engine == null)
                return;

            if (_activeInstrumentSession != null)
                _activeInstrumentSession.IsArmedRequested = false;

            _engine.SetCopyEnabled(false);

            if (!string.IsNullOrWhiteSpace(reason))
                _engine.Log(reason);

            RenderButtons();
            // RefreshInstrumentTabs();
            RefreshCopierStatusPanel();
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

            if (_engine.IsRequested)
            {
                RequestDisarmed("Copy disarmed: one or more selected followers lost connection.");
            }

            RenderFollowerRowsState();
            ApplyConfigFromUi();
        }
    }
}